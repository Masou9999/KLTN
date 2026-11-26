// Browser clipboard for the WebGL build. GUIUtility.systemCopyBuffer only updates Unity's
// internal buffer on WebGL, so the room code never reached the system clipboard.
// Browsers allow clipboard writes only during a user gesture, while Unity handles a
// button click a frame later. C# therefore "arms" the copy on pointer-down and the write
// runs inside the browser's own pointerup event. The result is reported with SendMessage.
mergeInto(LibraryManager.library, {
  $KltnClipboard: {
    armed: null,

    write: function (text, target, method) {
      function report(ok) {
        try {
          SendMessage(target, method, ok ? "1" : "0");
        } catch (e) {
          // The scene may have changed before an async write finished.
        }
      }

      function fallback() {
        var ok = false;
        try {
          var area = document.createElement("textarea");
          area.value = text;
          area.setAttribute("readonly", "");
          area.style.position = "fixed";
          area.style.top = "0";
          area.style.left = "0";
          area.style.opacity = "0";
          document.body.appendChild(area);
          area.select();
          area.setSelectionRange(0, text.length);
          ok = document.execCommand("copy");
          document.body.removeChild(area);
        } catch (e) {
          ok = false;
        }
        // Return keyboard focus to the game canvas after the hidden textarea.
        var canvas = document.querySelector("canvas");
        if (canvas) canvas.focus();
        report(ok);
      }

      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(text).then(function () { report(true); }, fallback);
      } else {
        fallback();
      }
    },
  },

  /// Copies on the next pointerup (inside the browser's user gesture).
  KltnClipboardArm__deps: ["$KltnClipboard"],
  KltnClipboardArm: function (textPtr, targetPtr, methodPtr) {
    var text = UTF8ToString(textPtr);
    var target = UTF8ToString(targetPtr);
    var method = UTF8ToString(methodPtr);
    if (KltnClipboard.armed) {
      document.removeEventListener("pointerup", KltnClipboard.armed, true);
    }
    KltnClipboard.armed = function () {
      document.removeEventListener("pointerup", KltnClipboard.armed, true);
      KltnClipboard.armed = null;
      KltnClipboard.write(text, target, method);
    };
    document.addEventListener("pointerup", KltnClipboard.armed, true);
  },

  /// Copies immediately (keyboard activation); may be refused without a recent gesture.
  KltnClipboardCopy__deps: ["$KltnClipboard"],
  KltnClipboardCopy: function (textPtr, targetPtr, methodPtr) {
    KltnClipboard.write(UTF8ToString(textPtr), UTF8ToString(targetPtr),
      UTF8ToString(methodPtr));
  },
});
