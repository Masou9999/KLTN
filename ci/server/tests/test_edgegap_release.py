import copy
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

MODULE = Path(__file__).resolve().parents[1] / "edgegap_release.py"
spec = importlib.util.spec_from_file_location("edgegap_release", MODULE)
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)

OLD = "sha-111111111111"
NEW = "sha-222222222222"


class FakeApi:
    """Stateful service double: rejects creating a second version and models stopping."""
    def __init__(self):
        self.calls = []
        self.active = True
        self.versions = [{"name": OLD, "docker_tag": OLD,
                          "docker_repository": "registry.edgegap.com", "docker_image": "project/kltn"}]
        self.tags = [OLD, NEW]
        self.deployments = {
            "old-game": {"request_id": "old-game", "app_name": "kltn"},
            "foreign-game": {"request_id": "foreign-game", "app_name": "another-app"},
        }
        self.fail_create = False
        self.fail_smoke = False
        self.fail_cleanup = False

    def request(self, method, path, body=None):
        self.calls.append((method, path, copy.deepcopy(body)))
        path = path.split("?")[0]
        if path == "/v1/app/kltn":
            if method == "PATCH":
                self.active = body["is_active"]
            return {"is_active": self.active}
        if path == "/v1/app/kltn/versions":
            return {"versions": copy.deepcopy(self.versions)}
        if path.endswith("/tags"):
            return {"data": [{"tag": tag} for tag in self.tags]}
        if "/tags/" in path and method == "DELETE":
            if self.fail_cleanup:
                raise RuntimeError("cleanup failed")
            self.tags.remove(path.rsplit("/", 1)[1])
            return {}
        if path == "/v1/deployments":
            return {"data": [{"request_id": key} for key in self.deployments]}
        if path.startswith("/v1/status/"):
            return self.deployments.get(path.rsplit("/", 1)[1])
        if path.startswith("/v1/stop/"):
            self.deployments.pop(path.rsplit("/", 1)[1], None)
            return {}
        if path == "/v1/app/kltn/version" and method == "POST":
            if self.fail_create:
                raise RuntimeError("create failed")
            assert not self.versions, "Must delete old version before creating new one"
            self.versions.append(copy.deepcopy(body))
            return {}
        if path.startswith("/v1/app/kltn/version/"):
            if method == "GET":
                return copy.deepcopy(self.versions[0])
            if method == "DELETE":
                self.versions = []
            elif method == "PATCH":
                self.versions[0].update(copy.deepcopy(body))
            return {}
        if path == "/v2/deployments":
            assert self.active
            assert self.versions[0]["envs"][-1]["value"] == "false"
            self.deployments["smoke"] = {
                "request_id": "smoke", "app_name": "kltn", "running": True,
                "current_status": "Status.READY", "error": self.fail_smoke,
                "ports": {"gameport": {"internal": 7777, "external": 30000, "protocol": "TCP", "tls_upgrade": True}},
            }
            return {"request_id": "smoke"}
        raise AssertionError((method, path))


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.api = FakeApi()
        config = {"req_cpu": 1536, "req_memory": 3072,
                  "ports": [{"name": "gameport", "port": 7777, "protocol": "WS", "tls_upgrade": True}]}
        self.payload = release.make_payload(config, NEW, "registry.edgegap.com/project/kltn", "user", "secret")
        # Exercise production wait loop without spending wall-clock time on simulated stops.
        self.wait_patch = patch.object(release, "wait_for", side_effect=self.fast_wait)
        self.wait_patch.start()
        self.addCleanup(self.wait_patch.stop)

    @staticmethod
    def fast_wait(check, description, timeout=600):
        for _ in range(5):
            if check():
                return
        raise RuntimeError("Timed out: " + description)

    def run_release(self, apply=True):
        release.release(self.api, "kltn", self.payload, "project/kltn", apply)

    def test_success_keeps_exactly_one_version_and_stops_smoke(self):
        self.run_release()
        self.assertEqual([v["name"] for v in self.api.versions], [NEW])
        self.assertEqual(self.api.versions[0]["envs"][-1]["value"], "true")
        self.assertTrue(self.api.active)
        self.assertEqual(list(self.api.deployments), ["foreign-game"])
        self.assertEqual(self.api.tags, [NEW])
        writes = [(m, p) for m, p, _ in self.api.calls if m != "GET"]
        self.assertLess(writes.index(("DELETE", "/v1/stop/smoke")),
                        writes.index(("PATCH", "/v1/app/kltn/version/" + NEW)))

    def test_preflight_is_read_only(self):
        self.run_release(apply=False)
        self.assertTrue(all(m == "GET" for m, _, _ in self.api.calls))

    def test_missing_image_never_disables_or_deletes_old_version(self):
        self.api.tags = [OLD]
        with self.assertRaisesRegex(RuntimeError, "not present"):
            self.run_release()
        self.assertTrue(all(m == "GET" for m, _, _ in self.api.calls))
        self.assertEqual(self.api.versions[0]["name"], OLD)

    def test_two_versions_require_manual_review(self):
        self.api.versions.append({"name": "unexpected"})
        with self.assertRaisesRegex(RuntimeError, "zero or one"):
            self.run_release()
        self.assertTrue(all(m == "GET" for m, _, _ in self.api.calls))

    def test_create_failure_disables_app_and_preserves_old_image(self):
        self.api.fail_create = True
        with self.assertRaisesRegex(RuntimeError, "create failed"):
            self.run_release()
        self.assertFalse(self.api.active)
        self.assertIn(OLD, self.api.tags)

    def test_smoke_failure_never_publishes_marker_and_preserves_old_image(self):
        self.api.fail_smoke = True
        with self.assertRaisesRegex(RuntimeError, "Smoke deployment failed"):
            self.run_release()
        self.assertFalse(self.api.active)
        self.assertEqual(self.api.versions[0]["envs"][-1]["value"], "false")
        self.assertIn(OLD, self.api.tags)
        self.assertNotIn("smoke", self.api.deployments)

    def test_cleanup_failure_does_not_take_down_successful_release(self):
        self.api.fail_cleanup = True
        self.run_release()
        self.assertTrue(self.api.active)
        self.assertEqual(self.api.versions[0]["envs"][-1]["value"], "true")

    def test_recovery_same_tag_does_not_delete_current_image(self):
        self.api.versions = [dict(self.payload)]
        self.run_release()
        self.assertIn(NEW, self.api.tags)

    def test_cleanup_never_deletes_other_repository(self):
        self.api.versions[0]["docker_image"] = "other/image"
        self.run_release()
        self.assertIn(OLD, self.api.tags)

    def test_rejects_wrong_port(self):
        with self.assertRaisesRegex(ValueError, "WS 7777"):
            release.make_payload({"req_cpu": 1536, "req_memory": 3072}, NEW,
                                 "registry.edgegap.com/project/kltn", "user", "secret")

    def test_list_pagination(self):
        class Pages:
            def request(self, method, path):
                if "page=1" in path:
                    return {"versions": [{"name": "a"}], "pagination": {"has_next": True, "next_page_number": 2}}
                return {"versions": [{"name": "b"}], "pagination": {"has_next": False}}
        self.assertEqual([v["name"] for v in release.versions(Pages(), "kltn")], ["a", "b"])


class TimeoutTests(unittest.TestCase):
    def test_registry_404_falls_back_to_relative_image_and_uses_it_for_cleanup(self):
        class ScopedApi(FakeApi):
            def request(self, method, path, body=None):
                if path == '/v1/container-registry/images/project%2Fkltn/tags':
                    self.calls.append((method, path, body))
                    raise release.ApiError(method, path, 404)
                return super().request(method, path, body)
        api = ScopedApi()
        payload = release.make_payload({"req_cpu": 1536, "req_memory": 3072,
            "ports": [{"name": "gameport", "port": 7777, "protocol": "WS", "tls_upgrade": True}]},
            NEW, "registry.edgegap.com/project/kltn", "user", "secret")
        with patch.object(release, "wait_for", side_effect=ReleaseTests.fast_wait):
            release.release(api, 'kltn', payload, 'project/kltn', apply=True)
        self.assertIn(('DELETE', '/v1/container-registry/images/kltn/tags/' + OLD, None), api.calls)

    def test_registry_does_not_fallback_on_auth_or_server_errors(self):
        for code in (401, 403, 500):
            class BrokenApi:
                calls = 0
                def request(self, method, path):
                    self.calls += 1
                    raise release.ApiError(method, path, code)
            api = BrokenApi()
            with self.assertRaises(release.ApiError):
                release.registry_tags(api, 'project/kltn', 'project/kltn')
            self.assertEqual(api.calls, 1)

    def test_registry_rejects_unrelated_image(self):
        api = FakeApi()
        with self.assertRaises(ValueError):
            release.registry_tags(api, 'other/image', 'project/kltn')
        self.assertEqual(api.calls, [])

    def test_registry_missing_image_fails_without_mutation(self):
        class MissingApi:
            def request(self, method, path):
                self.assert_read(method)
                raise release.ApiError(method, path, 404)
            @staticmethod
            def assert_read(method):
                assert method == 'GET'
        with self.assertRaisesRegex(RuntimeError, 'current version was not changed'):
            release.registry_tags(MissingApi(), 'project/kltn', 'project/kltn')

    def test_wait_is_bounded(self):
        times = iter([0, 0, 10])
        with self.assertRaisesRegex(RuntimeError, "Timed out"):
            release.wait_for(lambda: False, "test", timeout=5, sleep=lambda _: None,
                             clock=lambda: next(times))


if __name__ == "__main__":
    unittest.main()
