// Runs before the page paints, so the saved theme shows without a flash.
(function () {
    var match = document.cookie.match(/(?:^|;\s*)obrl_theme=([a-z]+)/);
    var choice = match ? match[1] : "auto";
    var theme = choice === "auto"
        ? (window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")
        : choice;
    var root = document.documentElement;
    root.setAttribute("data-obrl-theme", theme);
    root.setAttribute("data-obrl-theme-choice", choice);
    root.setAttribute("data-bs-theme", theme === "dark" ? "dark" : "light");
})();
