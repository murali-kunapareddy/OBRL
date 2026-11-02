// OBRL site-wide behaviour: theme switch, 2FA QR code, confirm prompts.
(function () {
    "use strict";

    var csrf = document.querySelector('meta[name="csrf-token"]');
    window.obrl = window.obrl || {};
    window.obrl.csrf = csrf ? csrf.content : "";

    // ----- Theme switch -----
    function applyTheme(choice) {
        var theme = choice === "auto"
            ? (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")
            : choice;
        var root = document.documentElement;
        root.setAttribute("data-obrl-theme", theme);
        root.setAttribute("data-obrl-theme-choice", choice);
        root.setAttribute("data-bs-theme", theme === "dark" ? "dark" : "light");
        document.querySelectorAll("[data-theme-choice]").forEach(function (b) {
            b.classList.toggle("active", b.getAttribute("data-theme-choice") === choice);
        });
    }

    document.querySelectorAll("[data-theme-choice]").forEach(function (button) {
        button.addEventListener("click", function () {
            var choice = button.getAttribute("data-theme-choice");
            applyTheme(choice);
            fetch("/Profile/Theme", {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded", "X-CSRF-TOKEN": window.obrl.csrf },
                body: "theme=" + encodeURIComponent(choice),
            });
        });
    });
    applyTheme(document.documentElement.getAttribute("data-obrl-theme-choice") || "auto");

    // ----- Two-factor setup: draw the QR code Identity's page asks for -----
    var qrData = document.getElementById("qrCodeData");
    var qrTarget = document.getElementById("qrCode");
    if (qrData && qrTarget && window.qrcode) {
        var qr = window.qrcode(0, "M");
        qr.addData(qrData.getAttribute("data-url"));
        qr.make();
        qrTarget.innerHTML = qr.createSvgTag({ cellSize: 4, margin: 2, scalable: true });
    }

    // ----- Selects that submit their form when changed -----
    document.querySelectorAll("select[data-autosubmit]").forEach(function (sel) {
        sel.addEventListener("change", function () { sel.form.submit(); });
    });

    // ----- Confirm before destructive actions -----
    document.addEventListener("submit", function (e) {
        var msg = e.target.getAttribute && e.target.getAttribute("data-confirm");
        if (msg && !window.confirm(msg)) e.preventDefault();
    });
})();
