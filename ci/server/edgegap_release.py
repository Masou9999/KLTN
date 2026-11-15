"""Single-version Edgegap release. Uses Python's standard library only.

No API writes unless --apply is supplied. Credentials are read from the environment;
neither request bodies nor raw API errors (which can contain secrets) are logged.
"""
import argparse
import copy
import json
import os
from pathlib import Path
import re
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode
from urllib.request import Request, urlopen

READY_KEY = "KLTN_RELEASE_READY"
SHA_TAG = re.compile(r"sha-[0-9a-f]{12}\Z")


class ApiError(RuntimeError):
    def __init__(self, method, path, status):
        self.status = status
        super().__init__(f"Edgegap {method} {path.split('?')[0]} returned HTTP {status}")


class Edgegap:
    def __init__(self, token):
        self.token = token if token.lower().startswith("token ") else "token " + token

    def request(self, method, path, body=None):
        request = Request("https://api.edgegap.com" + path, method=method,
                          data=None if body is None else json.dumps(body).encode(),
                          headers={"Authorization": self.token, "Content-Type": "application/json"})
        # Never retry a mutation implicitly: a timeout may mean it already succeeded.
        try:
            with urlopen(request, timeout=45) as response:
                raw = response.read()
                result = json.loads(raw) if raw else {}
                if isinstance(result, dict) and result.get("success") is False:
                    raise RuntimeError(f"Edgegap rejected {method} {path.split('?')[0]}")
                return result
        except HTTPError as error:
            raise ApiError(method, path, error.code) from None
        except (URLError, TimeoutError):
            raise RuntimeError(f"Edgegap network failure during {method}; inspect dashboard before retrying.") from None


def segment(value):
    return quote(value, safe="")


def items(api, path, key):
    result = []
    page = 1
    while True:
        data = api.request("GET", path + "?" + urlencode({"page": page, "limit": 100}))
        if not isinstance(data.get(key), list):
            raise RuntimeError(f"Unexpected Edgegap list response ({key}).")
        result.extend(data[key])
        pagination = data.get("pagination", {})
        if not pagination.get("has_next", False):
            return result
        next_page = pagination.get("next_page_number")
        if not isinstance(next_page, int) or next_page <= page:
            raise RuntimeError("Invalid Edgegap pagination.")
        page = next_page


def versions(api, app):
    return items(api, f"/v1/app/{segment(app)}/versions", "versions")


def status(api, deployment_id):
    try:
        return api.request("GET", f"/v1/status/{segment(deployment_id)}")
    except ApiError as error:
        if error.status == 404:
            return None
        raise


def app_deployments(api, app):
    # The list response does not include app_name; inspect status before any stop.
    result = []
    for deployment in items(api, "/v1/deployments", "data"):
        details = status(api, deployment["request_id"])
        if details and details.get("app_name") == app:
            result.append(details)
    return result


def wait_for(check, description, timeout=600, sleep=time.sleep, clock=time.monotonic):
    deadline = clock() + timeout
    while True:
        if check():
            return
        if clock() >= deadline:
            raise RuntimeError(f"Timed out: {description}")
        sleep(5)


def stop(api, deployment_id):
    try:
        api.request("DELETE", f"/v1/stop/{segment(deployment_id)}")
    except ApiError as error:
        if error.status != 404:
            raise


def stopped(api, deployment_id):
    details = status(api, deployment_id)
    return details is None or details.get("current_status") == "Status.TERMINATED"


def marker(envs, ready):
    return [env for env in envs if env["key"] != READY_KEY] + [
        {"key": READY_KEY, "value": "true" if ready else "false", "is_hidden": False}]


def make_payload(config, version, repository, username, password):
    if not SHA_TAG.fullmatch(version):
        raise ValueError("Version must be sha- followed by 12 lowercase hex characters.")
    if not re.fullmatch(r"registry\.edgegap\.com/[a-z0-9][a-z0-9._/-]+", repository):
        raise ValueError("Repository must be registry.edgegap.com/<project>/<image>.")
    payload = copy.deepcopy(config)
    if payload.get("req_cpu", 0) < 256 or payload.get("req_memory", 0) < 256:
        raise ValueError("Invalid CPU/memory configuration.")
    if not any(p.get("name") == "gameport" and p.get("port") == 7777
               and p.get("protocol") == "UDP" for p in payload.get("ports", [])):
        raise ValueError("KLTN requires the gameport mapping UDP 7777.")
    if any(env.get("is_hidden") for env in payload.get("envs", [])):
        raise ValueError("Do not commit secrets in version.json; allocator injects runtime secrets.")
    registry, image = repository.split("/", 1)
    payload.update(name=version, docker_repository=registry, docker_image=image,
                   docker_tag=version, private_username=username, private_token=password,
                   is_active=True, verify_image=True)
    payload["envs"] = marker(payload.get("envs", []), False)
    return payload


def release(api, app, payload, registry_image, apply=False, timeout=600):
    app_path = f"/v1/app/{segment(app)}"
    api.request("GET", app_path)  # Fail authentication/app lookup before mutation.
    old = versions(api, app)
    if len(old) > 1:
        raise RuntimeError("Expected zero or one version. Review/remove extra versions manually first.")
    image_path = f"/v1/container-registry/images/{segment(registry_image)}/tags"
    tags = api.request("GET", image_path).get("data")
    if not isinstance(tags, list) or not any(t.get("tag") == payload["docker_tag"] for t in tags):
        raise RuntimeError("New image tag is not present in the configured Edgegap registry image.")
    # Always resolve deployment ownership during preflight as well.
    deployments = app_deployments(api, app)
    print(f"Plan: {app}, {len(old)} old version(s), {len(deployments)} deployment(s) to stop, new={payload['name']}")
    for previous in old:
        print(f"Previous version: {previous['name']}; image: "
              f"{previous.get('docker_repository')}/{previous.get('docker_image')}:{previous.get('docker_tag')}")
    if not apply:
        print("Read-only preflight complete. No resources changed.")
        return

    version_path = app_path + "/version/" + segment(payload["name"])
    smoke_id = None
    try:
        # The allocator checks both the app flag and KLTN_RELEASE_READY before
        # reserving a reusable server or creating a new deployment.
        api.request("PATCH", app_path, {"is_active": False})
        requested_stops = set()

        def drain():
            active = app_deployments(api, app)
            for deployment in active:
                deployment_id = deployment["request_id"]
                if deployment_id not in requested_stops:
                    stop(api, deployment_id)
                    requested_stops.add(deployment_id)
            return not active

        wait_for(drain, "old deployments stopping", timeout)
        # Re-read after disabling, and refuse unexpected dashboard interference.
        current = versions(api, app)
        if [v["name"] for v in current] != [v["name"] for v in old]:
            raise RuntimeError("Versions changed during release; refusing to delete unexpected resources.")
        for previous in old:
            api.request("DELETE", app_path + "/version/" + segment(previous["name"]))
        wait_for(lambda: not versions(api, app), "old version deletion", timeout)
        api.request("POST", app_path + "/version", payload)
        wait_for(lambda: [v["name"] for v in versions(api, app)] == [payload["name"]],
                 "new version creation", timeout)
        api.request("PATCH", app_path, {"is_active": True})
        # Marker remains false throughout the test, so normal matchmaking is gated.
        smoke = api.request("POST", "/v2/deployments", {
            "application": app, "version": payload["name"],
            "users": [{"user_type": "geo_coordinates", "user_data": {"latitude": 10.8231, "longitude": 106.6297}}],
            "environment_variables": [{"key": "MATCH_REPORTER_STUB", "value": "1", "is_hidden": False}],
            "tags": ["kltn-ci-smoke"],
        })
        smoke_id = smoke["request_id"]

        def ready():
            details = status(api, smoke_id)
            if not details or details.get("error") or details.get("current_status") == "Status.TERMINATED":
                raise RuntimeError("Smoke deployment failed or terminated.")
            port = details.get("ports", {}).get("gameport", {})
            return (details.get("running") is True and details.get("current_status") == "Status.READY"
                    and port.get("internal") == 7777 and port.get("protocol", "").upper() == "UDP"
                    and bool(port.get("external")))

        wait_for(ready, "smoke deployment readiness and UDP mapping", timeout)
        stop(api, smoke_id)
        wait_for(lambda: stopped(api, smoke_id), "smoke deployment stopping", timeout)
        smoke_id = None
        # A fresh allocation now sees the sole, tested SHA version.
        api.request("PATCH", version_path, {"envs": marker(payload["envs"], True)})
        def published():
            detail = api.request("GET", version_path)
            return (detail.get("is_active") is True
                    and any(env.get("key") == READY_KEY and env.get("value") == "true"
                            for env in detail.get("envs", [])))
        wait_for(published, "release marker publication", timeout)
        print(f"Released {app}/{payload['name']}; smoke deployment stopped, matchmaking enabled.")
    except Exception:
        # Fail closed, including ambiguous mutation timeouts. Do not remove old images.
        try:
            api.request("PATCH", app_path, {"is_active": False})
            for deployment in app_deployments(api, app):
                stop(api, deployment["request_id"])
        except Exception:
            print("WARNING: automatic containment failed; check app/deployments in Edgegap dashboard.", file=sys.stderr)
        print("Release failed; app left disabled where possible. Old registry images retained. See recovery guide.", file=sys.stderr)
        raise

    # Only delete the previous version's exact tag in the same image repository.
    # Never bulk-delete a shared registry or remove aliases belonging to other builds.
    for previous in old:
        if (previous.get("docker_repository") == payload["docker_repository"]
                and previous.get("docker_image") == payload["docker_image"]
                and previous.get("docker_tag") != payload["docker_tag"]):
            try:
                api.request("DELETE", image_path + "/" + segment(previous["docker_tag"]))
            except Exception:
                print("WARNING: release succeeded, but previous image tag cleanup failed; clean it manually.", file=sys.stderr)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--config", default=str(Path(__file__).with_name("version.json")))
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    names = ("EDGEGAP_API_TOKEN", "EDGEGAP_APP_NAME", "EDGEGAP_IMAGE_REPOSITORY",
             "EDGEGAP_REGISTRY_IMAGE_NAME", "EDGEGAP_REGISTRY_USERNAME", "EDGEGAP_REGISTRY_PASSWORD")
    env = {name: os.environ.get(name, "").strip() for name in names}
    if any(not value for value in env.values()):
        raise ValueError("Missing configuration: " + ", ".join(k for k, v in env.items() if not v))
    payload = make_payload(json.loads(Path(args.config).read_text(encoding="utf-8")), args.version,
                           env["EDGEGAP_IMAGE_REPOSITORY"], env["EDGEGAP_REGISTRY_USERNAME"],
                           env["EDGEGAP_REGISTRY_PASSWORD"])
    release(Edgegap(env["EDGEGAP_API_TOKEN"]), env["EDGEGAP_APP_NAME"], payload,
            env["EDGEGAP_REGISTRY_IMAGE_NAME"], args.apply)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, ValueError, KeyError, OSError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
