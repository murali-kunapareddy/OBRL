// OBRL idle sign-out. Activity (scrolling, typing, clicking, turning pages) keeps the session alive;
// after the Admin's idle limit with no activity the member is signed out and the page leaves the screen.
// A notice one minute before gives the chance to stay signed in.
(function () {
    "use strict";
    var meta = document.querySelector('meta[name="obrl-idle-minutes"]');
    var csrf = document.querySelector('meta[name="csrf-token"]');
    if (!meta || !csrf) return;

    var limitMs = parseFloat(meta.content) * 60000;
    if (!(limitMs > 0)) return;
    var warnMs = Math.max(limitMs - 60000, limitMs * 0.8);
    // The server renews the cookie once half the limit has passed, so a ping every third keeps it fresh.
    var pingEveryMs = Math.max(30000, limitMs / 3);

    var lastActivity = Date.now();
    var lastPing = Date.now();
    var notice = null;

    function post(url) {
        return fetch(url, { method: "POST", headers: { "X-CSRF-TOKEN": csrf.content }, credentials: "same-origin" });
    }

    function activity() {
        lastActivity = Date.now();
        if (notice) { notice.remove(); notice = null; }
        if (Date.now() - lastPing >= pingEveryMs) {
            lastPing = Date.now();
            post("/session/keepalive").then(function (res) {
                // Signed out meanwhile (elsewhere, or the session ended): show the login page.
                if (res.redirected || res.status === 400 || res.status === 401) window.location.reload();
            }).catch(function () { /* offline: try again on the next activity */ });
        }
    }

    ["pointerdown", "keydown", "wheel", "touchstart", "scroll"].forEach(function (evt) {
        window.addEventListener(evt, activity, { passive: true, capture: true });
    });
    var lastMove = 0;
    window.addEventListener("pointermove", function () {
        var now = Date.now();
        if (now - lastMove > 5000) { lastMove = now; activity(); }
    }, { passive: true });
    // The reader announces page turns and section changes, which may not involve the window's own events.
    document.addEventListener("obrl:activity", activity);

    function showNotice() {
        notice = document.createElement("div");
        notice.className = "idle-notice";
        notice.setAttribute("role", "alertdialog");
        notice.setAttribute("aria-live", "assertive");
        notice.innerHTML = '<span>You will be signed out in a minute because nothing has happened for a while.</span> ';
        var button = document.createElement("button");
        button.type = "button";
        button.className = "btn btn-sm btn-primary";
        button.textContent = "Stay signed in";
        button.addEventListener("click", function () { lastPing = 0; activity(); });
        notice.appendChild(button);
        document.body.appendChild(notice);
        button.focus();
    }

    setInterval(function () {
        var idle = Date.now() - lastActivity;
        if (idle >= limitMs) {
            post("/session/idle").finally(function () {
                window.location.href = "/Identity/Account/Login?reason=idle&ReturnUrl=" + encodeURIComponent(location.pathname + location.search);
            });
        } else if (idle >= warnMs && !notice) {
            showNotice();
        }
    }, 5000);
})();
