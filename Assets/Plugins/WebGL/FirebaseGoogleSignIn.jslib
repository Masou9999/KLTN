// Google sign-in for the WebGL build. Browsers cannot run the desktop loopback OAuth flow,
// so a Firebase JS SDK popup returns the Google ID token, which C# then sends through the
// same Firebase REST -> UGS pipeline as desktop. C# owns the session; the JS session is
// signed out right after the token is handed over.
mergeInto(LibraryManager.library, {
  $KltnFirebase: {
    version: "10.12.2",
    loading: null,

    load: function () {
      if (KltnFirebase.loading) {
        return KltnFirebase.loading;
      }

      function inject(src) {
        return new Promise(function (resolve, reject) {
          var script = document.createElement("script");
          script.src = src;
          script.async = false;
          script.onload = resolve;
          script.onerror = function () {
            reject(new Error("Could not load " + src));
          };
          document.head.appendChild(script);
        });
      }

      var base = "https://www.gstatic.com/firebasejs/" + KltnFirebase.version + "/";
      KltnFirebase.loading = (typeof firebase !== "undefined" && firebase.auth)
        ? Promise.resolve()
        : inject(base + "firebase-app-compat.js").then(function () {
            return inject(base + "firebase-auth-compat.js");
          });
      KltnFirebase.loading.catch(function () {
        KltnFirebase.loading = null;
      });
      return KltnFirebase.loading;
    },
  },

  KltnFirebasePreload__deps: ["$KltnFirebase"],
  KltnFirebasePreload: function () {
    // Loading early keeps the popup inside the click's user-activation window.
    KltnFirebase.load().catch(function (error) {
      console.warn("[GoogleAuth] " + error.message);
    });
  },

  KltnGoogleSignIn__deps: ["$KltnFirebase"],
  KltnGoogleSignIn: function (apiKeyPtr, authDomainPtr, projectIdPtr, targetPtr, successPtr, failurePtr) {
    var apiKey = UTF8ToString(apiKeyPtr);
    var authDomain = UTF8ToString(authDomainPtr);
    var projectId = UTF8ToString(projectIdPtr);
    var target = UTF8ToString(targetPtr);
    var success = UTF8ToString(successPtr);
    var failure = UTF8ToString(failurePtr);

    function fail(code, message) {
      SendMessage(target, failure, JSON.stringify({ code: code || "unknown", message: message || "" }));
    }

    KltnFirebase.load()
      .then(function () {
        var app = firebase.apps.length
          ? firebase.app()
          : firebase.initializeApp({ apiKey: apiKey, authDomain: authDomain, projectId: projectId });
        var auth = firebase.auth(app);
        var provider = new firebase.auth.GoogleAuthProvider();
        provider.setCustomParameters({ prompt: "select_account" });

        return auth.signInWithPopup(provider).then(function (result) {
          var idToken = result && result.credential && result.credential.idToken;

          if (!idToken) {
            fail("no-id-token", "Google did not return an ID token.");
            return;
          }

          SendMessage(target, success, idToken);
          return auth.signOut();
        });
      })
      .catch(function (error) {
        fail(error && error.code, error && error.message ? error.message : String(error));
      });
  },
});
