// Book form: when a publisher is picked from the list, fill its names in the other language tabs.
// A typed name that matches no publisher leaves the boxes as they are, so a new publisher can be named in every language.
(function () {
    "use strict";
    var input = document.querySelector("[data-publisher-input]");
    var data = document.getElementById("publisher-names");
    if (!input || !data) return;
    var known = JSON.parse(data.textContent || "{}");
    var boxes = document.querySelectorAll("[data-publisher-name]");

    function normalize(s) { return (s || "").trim().split(/\s+/).join(" ").toUpperCase(); }

    var last = normalize(input.value);
    function fill() {
        var key = normalize(input.value);
        if (key === last) return;
        var previous = known[last];
        last = key;
        var names = known[key];
        boxes.forEach(function (box) {
            var code = box.getAttribute("data-publisher-name");
            if (names) box.value = names[code] || "";
            // Moving from a known publisher to a new one: don't carry the old publisher's names over.
            else if (previous && box.value === (previous[code] || "")) box.value = "";
        });
    }
    input.addEventListener("change", fill);
    input.addEventListener("input", fill);
})();
