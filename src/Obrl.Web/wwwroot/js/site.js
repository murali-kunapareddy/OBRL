// OBRL site-wide behaviour: theme switch, 2FA QR code, confirm prompts.
(function () {
    "use strict";

    var csrf = document.querySelector('meta[name="csrf-token"]');
    window.obrl = window.obrl || {};
    window.obrl.csrf = csrf ? csrf.content : "";

    // ----- Interface text in the member's language (see Views/Shared/_ScriptText.cshtml) -----
    var textEl = document.getElementById("obrl-text");
    var text = {};
    try { text = textEl ? JSON.parse(textEl.textContent) : {}; } catch (e) { /* keep English */ }
    // obrl.t("Page {0}", 5): the translation of an English text, with {0}, {1}... filled in.
    window.obrl.t = function (key) {
        var args = arguments;
        return (text[key] || key).replace(/\{(\d+)\}/g, function (m, i) { return args[+i + 1] !== undefined ? args[+i + 1] : m; });
    };

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

    // ----- Book cover choice on the upload form -----
    var coverPage = document.getElementById("CoverPage");
    var coverImage = document.getElementById("CoverImage");
    function pick(id) { var r = document.getElementById(id); if (r) r.checked = true; }
    function markTile(page) {
        document.querySelectorAll("[data-cover-page]").forEach(function (t) {
            t.classList.toggle("selected", t.getAttribute("data-cover-page") === String(page));
        });
    }
    if (coverImage) coverImage.addEventListener("change", function () { if (coverImage.files.length) pick("cover-custom"); });
    if (coverPage) coverPage.addEventListener("input", function () { pick("cover-page"); markTile(coverPage.value); });
    document.querySelectorAll("[data-cover-page]").forEach(function (tile) {
        tile.addEventListener("click", function () {
            var page = tile.getAttribute("data-cover-page");
            if (coverPage) coverPage.value = page;
            pick("cover-page");
            markTile(page);
        });
    });
    document.querySelectorAll("[data-cover-more]").forEach(function (button) {
        button.addEventListener("click", function () {
            var step = +button.getAttribute("data-cover-more");
            var hidden = document.querySelectorAll("[data-cover-page][hidden]");
            for (var i = 0; i < hidden.length && i < step; i++) hidden[i].hidden = false;
            if (hidden.length <= step) button.remove();
        });
    });

    // ----- Phones: the side menu slides in from the left -----
    var shell = document.querySelector(".app-shell");
    function setMenu(open) {
        if (!shell) return;
        shell.classList.toggle("menu-open", open);
        document.querySelectorAll("[data-menu-open]").forEach(function (b) { b.setAttribute("aria-expanded", open ? "true" : "false"); });
        document.body.classList.toggle("overflow-hidden", open);
        if (open) { var first = document.querySelector("#sidebar a, #sidebar button"); if (first) first.focus(); }
    }
    document.querySelectorAll("[data-menu-open]").forEach(function (b) { b.addEventListener("click", function () { setMenu(true); }); });
    document.querySelectorAll("[data-menu-close]").forEach(function (b) { b.addEventListener("click", function () { setMenu(false); }); });
    document.addEventListener("keydown", function (e) { if (e.key === "Escape" && shell && shell.classList.contains("menu-open")) setMenu(false); });
    window.matchMedia("(min-width: 992px)").addEventListener("change", function (m) { if (m.matches) setMenu(false); });

    // ----- Panels that start folded on phones (<details data-collapse-small>), and remember the member's choice -----
    var small = window.matchMedia("(max-width: 991.98px)").matches;
    document.querySelectorAll("details[data-collapse-small], details[data-remember]").forEach(function (d) {
        var key = d.getAttribute("data-remember");
        var saved = null;
        try { saved = key ? localStorage.getItem(key) : null; } catch (e) { /* private mode */ }
        if (saved !== null) d.open = saved === "1";
        else if (small && d.hasAttribute("data-collapse-small")) d.open = false;
        if (key) d.addEventListener("toggle", function () { try { localStorage.setItem(key, d.open ? "1" : "0"); } catch (e) { } });
    });

    // ----- Filter a long list as you type (<input data-filter-list="#list">, rows carry data-filter-text) -----
    document.querySelectorAll("[data-filter-list]").forEach(function (input) {
        var list = document.querySelector(input.getAttribute("data-filter-list"));
        if (!list) return;
        input.addEventListener("input", function () {
            var q = input.value.trim().toLowerCase();
            list.querySelectorAll("[data-filter-text]").forEach(function (row) {
                row.hidden = q !== "" && row.getAttribute("data-filter-text").toLowerCase().indexOf(q) < 0;
            });
        });
    });

    // ----- Confirm before destructive actions -----
    document.addEventListener("submit", function (e) {
        var msg = e.target.getAttribute && e.target.getAttribute("data-confirm");
        if (msg && !window.confirm(msg)) e.preventDefault();
    });
})();
