// OBRL reader. Page mode draws watermarked page images on a canvas; text mode shows cleaned HTML sections.
// Neither mode ever receives the original book file.
(function () {
    "use strict";

    var root = document.getElementById("reader");
    var bookId = root.dataset.book;
    var mode = root.dataset.mode;
    var count = parseInt(root.dataset.count, 10);
    var position = parseInt(root.dataset.position, 10) || 0;
    var startOffset = parseFloat(root.dataset.offset) || 0;
    var stage = document.getElementById("stage");
    var posInput = document.getElementById("pos");
    var toastEl = document.getElementById("toast");
    var csrf = (document.querySelector('meta[name="csrf-token"]') || {}).content || "";

    var t = (window.obrl && window.obrl.t ? window.obrl.t : function (s) { return s; });
    function $(id) { return document.getElementById(id); }
    function on(id, evt, fn) { var el = $(id); if (el) el.addEventListener(evt, fn); }

    function toast(text) {
        toastEl.textContent = text;
        toastEl.classList.add("show");
        clearTimeout(toast.t);
        toast.t = setTimeout(function () { toastEl.classList.remove("show"); }, 1800);
    }

    function api(method, url, body) {
        return fetch(url, {
            method: method,
            credentials: "same-origin",
            keepalive: method !== "GET",
            headers: body ? { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf } : { "X-CSRF-TOKEN": csrf },
            body: body ? JSON.stringify(body) : undefined,
        });
    }

    function stageOffset() {
        var max = stage.scrollHeight - stage.clientHeight;
        return max > 0 ? Math.min(1, Math.max(0, stage.scrollTop / max)) : 0;
    }

    function percent() {
        if (count <= 0) return 0;
        return mode === "page"
            ? Math.min(100, ((position + 1) / count) * 100)
            : Math.min(100, ((position + stageOffset()) / count) * 100);
    }

    // ---------- Progress: saved a moment after the reader stops moving ----------
    var saveTimer = null;
    function saveSoon() {
        clearTimeout(saveTimer);
        saveTimer = setTimeout(saveNow, 1500);
    }
    function saveNow() {
        clearTimeout(saveTimer);
        api("POST", "/read/" + bookId + "/progress", { position: position, offset: stageOffset(), percent: percent() });
    }
    document.addEventListener("visibilitychange", function () { if (document.hidden) saveNow(); });
    window.addEventListener("pagehide", saveNow);
    stage.addEventListener("scroll", function () { saveSoon(); if (mode === "text") showProgress(); }, { passive: true });

    function setPositionLabel() {
        posInput.value = position + 1;
        $("prev").disabled = position <= 0;
        $("next").disabled = position >= count - 1;
        showProgress();
    }

    function showProgress() {
        var p = Math.round(percent());
        $("pct").textContent = p + "%";
        $("progressLine").style.width = p + "%";
    }

    // ---------- Copy protection (deterrence; screenshots cannot be blocked) ----------
    ["copy", "cut", "contextmenu", "dragstart", "selectstart"].forEach(function (evt) {
        document.addEventListener(evt, function (e) {
            if (e.target.closest && e.target.closest("input, textarea")) return;
            e.preventDefault();
        });
    });
    document.addEventListener("keydown", function (e) {
        var k = e.key.toLowerCase();
        if ((e.ctrlKey || e.metaKey) && ["c", "x", "a", "s", "p", "u"].indexOf(k) >= 0) {
            e.preventDefault();
            if (k === "p") toast(t("Printing is not available"));
        }
    });

    // ---------- Full screen and the auto-hiding toolbar ----------
    var hideTimer = null;
    function nudgeBar() {
        root.classList.add("show-bar");
        clearTimeout(hideTimer);
        hideTimer = setTimeout(function () { root.classList.remove("show-bar"); }, 2500);
    }
    function toggleFullscreen() {
        if (document.fullscreenElement) document.exitFullscreen();
        else if (document.documentElement.requestFullscreen) document.documentElement.requestFullscreen();
    }
    on("fullscreen", "click", toggleFullscreen);
    document.addEventListener("fullscreenchange", function () {
        var full = !!document.fullscreenElement;
        root.classList.toggle("immersive", full);
        $("fullscreen").innerHTML = full ? '<i class="bi bi-fullscreen-exit"></i>' : '<i class="bi bi-fullscreen"></i>';
        if (full) nudgeBar();
        if (mode === "page") window.dispatchEvent(new Event("resize"));
    });
    document.addEventListener("mousemove", function (e) { if (root.classList.contains("immersive") && e.clientY < 80) nudgeBar(); });

    // ---------- Bookmarks ----------
    var marks = JSON.parse($("marksData").textContent || "[]");
    function markLabel(m) {
        return t(mode === "page" ? "Page {0}" : "Section {0}", m.position + 1) + (m.note ? " · " + m.note : "");
    }
    function renderMarks() {
        var box = $("marks");
        box.innerHTML = "";
        if (marks.length === 0) {
            var empty = document.createElement("div");
            empty.className = "px-3 py-1 small text-muted";
            empty.textContent = t("No bookmarks yet.");
            box.appendChild(empty);
            return;
        }
        marks.sort(function (a, b) { return a.position - b.position || a.offset - b.offset; });
        marks.forEach(function (m) {
            var row = document.createElement("div");
            row.className = "mark-row";
            var go = document.createElement("button");
            go.className = "dropdown-item";
            go.textContent = markLabel(m);
            go.addEventListener("click", function () { goTo(m.position, m.offset); });
            var del = document.createElement("button");
            del.className = "btn btn-sm btn-link text-muted";
            del.title = t("Remove bookmark");
            del.innerHTML = '<i class="bi bi-x-lg"></i>';
            del.addEventListener("click", function (e) {
                e.stopPropagation();
                api("DELETE", "/read/" + bookId + "/bookmarks/" + m.id).then(function () {
                    marks = marks.filter(function (x) { return x.id !== m.id; });
                    renderMarks();
                });
            });
            row.appendChild(go);
            row.appendChild(del);
            box.appendChild(row);
        });
    }
    on("addMark", "click", function () {
        var note = window.prompt(t("Optional note for this bookmark:"), "");
        if (note === null) return;
        api("POST", "/read/" + bookId + "/bookmarks", { position: position, offset: stageOffset(), note: note })
            .then(function (r) { return r.json(); })
            .then(function (m) {
                marks.push({ id: m.id, position: m.position, offset: m.offset, note: m.note });
                renderMarks();
                toast(t("Bookmark added"));
            });
    });
    renderMarks();

    // ---------- Shared navigation ----------
    var goTo;
    on("prev", "click", function () { goTo(position - 1, 0); });
    on("next", "click", function () { goTo(position + 1, 0); });
    posInput.addEventListener("change", function () {
        var n = parseInt(posInput.value, 10);
        if (!isNaN(n)) goTo(Math.min(count, Math.max(1, n)) - 1, 0);
    });

    // Swipe left/right on touch screens.
    var touchX = null, touchY = null;
    stage.addEventListener("touchstart", function (e) {
        if (e.touches.length === 1) { touchX = e.touches[0].clientX; touchY = e.touches[0].clientY; }
        else touchX = null; // two fingers: a pinch, not a swipe
    }, { passive: true });
    stage.addEventListener("touchend", function (e) {
        if (touchX === null) return;
        var dx = e.changedTouches[0].clientX - touchX, dy = e.changedTouches[0].clientY - touchY;
        touchX = null;
        if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5 && stage.scrollWidth <= stage.clientWidth + 2) {
            goTo(position + (dx < 0 ? 1 : -1), 0);
        }
    }, { passive: true });

    // ---------- Touch screens: tap the edges to turn the page, tap the middle for a full-page view ----------
    var coarse = window.matchMedia("(pointer: coarse)");
    var small = window.matchMedia("(max-width: 900px)");

    // Forward: scroll down one screen; at the bottom, the next page. Back is the same upwards.
    function pageForward() {
        if (stage.scrollTop + stage.clientHeight < stage.scrollHeight - 4)
            stage.scrollBy({ top: stage.clientHeight * 0.9, behavior: "smooth" });
        else goTo(position + 1, 0);
    }
    function pageBack() {
        if (stage.scrollTop > 4) stage.scrollBy({ top: -stage.clientHeight * 0.9, behavior: "smooth" });
        else goTo(position - 1, 1);
    }
    function toggleChrome(force) {
        var hide = typeof force === "boolean" ? force : !root.classList.contains("chrome-hidden");
        root.classList.toggle("chrome-hidden", hide);
    }
    stage.addEventListener("click", function (e) {
        if (!coarse.matches) return;
        if (e.target.closest && e.target.closest("a, button, input, select, textarea, .text-nav")) return;
        var rect = stage.getBoundingClientRect();
        var x = (e.clientX - rect.left) / rect.width;
        if (x < 0.28) pageBack();
        else if (x > 0.72) pageForward();
        else if (small.matches) toggleChrome();
    });
    // Leaving the small layout (turning a tablet, say) brings the bars back.
    small.addEventListener("change", function () { if (!small.matches) toggleChrome(false); });
    if (coarse.matches && small.matches) {
        var hinted = false;
        try { hinted = localStorage.getItem("obrl.tapHint") === "1"; localStorage.setItem("obrl.tapHint", "1"); } catch (e) { hinted = true; }
        if (!hinted) setTimeout(function () {
            toast(t("Tap the middle to show or hide the bars. Tap the left or right edge to turn the page."));
            clearTimeout(toast.t);
            toast.t = setTimeout(function () { toastEl.classList.remove("show"); }, 5000);
        }, 900);
    }

    // Redraw when the reading area changes size: window resize, phone turned, bars hidden or shown.
    function onStageResize(fn) {
        var timer = null, lastW = stage.clientWidth, lastH = stage.clientHeight;
        function check() {
            if (stage.clientWidth === lastW && stage.clientHeight === lastH) return;
            lastW = stage.clientWidth; lastH = stage.clientHeight;
            clearTimeout(timer);
            timer = setTimeout(fn, 150);
        }
        if (window.ResizeObserver) new ResizeObserver(check).observe(stage);
        else window.addEventListener("resize", check);
    }

    if (mode === "page") initPageMode(); else initTextMode();

    // =====================================================================
    // Page mode
    // =====================================================================
    function initPageMode() {
        var canvas = $("canvas");
        var ctx = canvas.getContext("2d");
        var wrap = $("pageWrap");
        var fit = localStorage.getItem("obrl.fit") || "width"; // width | page | custom
        var zoom = 1;                                          // used when fit = custom
        var aspect = 1.414;                                    // height / width, learned from the first page
        var urls = {};                                         // n -> { url, width, expires }
        var bitmaps = {};                                      // "n@width" -> ImageBitmap
        var order = [];                                        // for evicting old bitmaps
        var renderToken = 0;

        function gutter() { return stage.clientWidth < 600 ? 8 : 32; }
        function availableWidth() { return Math.max(160, stage.clientWidth - gutter()); }
        function availableHeight() { return Math.max(160, stage.clientHeight - (stage.clientWidth < 600 ? 24 : 32)); }

        function displayWidth() {
            if (fit === "width") return availableWidth();
            if (fit === "page") return Math.min(availableWidth(), availableHeight() / aspect);
            return Math.round(availableWidth() * zoom);
        }

        function wantedPixels() {
            return Math.min(1600, Math.ceil(displayWidth() * (window.devicePixelRatio || 1)));
        }

        function updateZoomUi() {
            var pct = Math.round((displayWidth() / availableWidth()) * 100);
            if ($("zoomLabel")) $("zoomLabel").textContent = pct + "%";
            $("fitWidth").classList.toggle("active", fit === "width");
            $("fitPage").classList.toggle("active", fit === "page");
        }

        function fetchUrls(start, w) {
            return fetch("/read/" + bookId + "/pages?start=" + start + "&count=4&w=" + w, { credentials: "same-origin" })
                .then(function (r) {
                    if (!r.ok) throw new Error(r.status);
                    return r.json();
                })
                .then(function (data) {
                    var expires = Date.now() + 45 * 1000;
                    data.items.forEach(function (it) { urls[it.n] = { url: it.url, width: data.width, expires: expires }; });
                    return data.width;
                });
        }

        function getBitmap(n, w) {
            var cached = urls[n];
            var ready = cached && cached.width >= Math.min(w, 1600) && cached.expires > Date.now()
                ? Promise.resolve(cached.width)
                : fetchUrls(n, w);
            return ready.then(function () {
                var entry = urls[n];
                var key = n + "@" + entry.width;
                if (bitmaps[key]) return bitmaps[key];
                return fetch(entry.url, { credentials: "same-origin" })
                    .then(function (r) {
                        if (r.status === 429) throw new Error("slow");
                        if (!r.ok) throw new Error(r.status);
                        return r.blob();
                    })
                    .then(function (blob) { return createImageBitmap(blob); })
                    .then(function (bmp) {
                        bitmaps[key] = bmp;
                        order.push(key);
                        while (order.length > 6) {
                            var old = order.shift();
                            if (bitmaps[old]) { bitmaps[old].close && bitmaps[old].close(); delete bitmaps[old]; }
                        }
                        return bmp;
                    });
            });
        }

        function draw(bmp) {
            aspect = bmp.height / bmp.width;
            canvas.width = bmp.width;
            canvas.height = bmp.height;
            ctx.drawImage(bmp, 0, 0);
            var w = displayWidth();
            canvas.style.width = w + "px";
            canvas.style.height = Math.round(w * aspect) + "px";
            updateZoomUi();
        }

        // quiet: keep the current picture fully visible while a sharper one loads (after a pinch).
        function render(offset, quiet) {
            var token = ++renderToken;
            if (!quiet) wrap.classList.add("loading");
            setPositionLabel();
            return getBitmap(position, wantedPixels())
                .then(function (bmp) {
                    if (token !== renderToken) return;
                    draw(bmp);
                    wrap.classList.remove("loading");
                    var max = stage.scrollHeight - stage.clientHeight;
                    if (!quiet) stage.scrollTop = offset > 0 && max > 0 ? offset * max : 0;
                    // Get the next page ready in the background.
                    if (position + 1 < count) getBitmap(position + 1, wantedPixels()).catch(function () {});
                })
                .catch(function (err) {
                    if (token !== renderToken) return;
                    wrap.classList.remove("loading");
                    toast(t(err.message === "slow" ? "You are turning pages very fast. Please wait a moment." : "This page could not be loaded. Try again."));
                });
        }

        goTo = function (n, offset) {
            if (n < 0 || n >= count) return;
            position = n;
            render(offset || 0);
            saveSoon();
            stage.focus({ preventScroll: true });
        };

        function setFit(f) {
            fit = f;
            if (f !== "custom") localStorage.setItem("obrl.fit", f);
            render(stageOffset());
        }

        function zoomBy(factor) {
            var current = displayWidth() / availableWidth();
            zoom = Math.min(4, Math.max(0.3, current * factor));
            fit = "custom";
            render(stageOffset());
        }

        on("zoomIn", "click", function () { zoomBy(1.2); });
        on("zoomOut", "click", function () { zoomBy(1 / 1.2); });
        on("fitWidth", "click", function () { setFit("width"); });
        on("fitPage", "click", function () { setFit("page"); });

        onStageResize(function () { if (!pinch) render(stageOffset(), true); });

        // ---------- Pinch to zoom (touch screens) ----------
        var pinch = null;
        function touchDistance(e) {
            var a = e.touches[0], b = e.touches[1];
            return Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY) || 1;
        }
        stage.addEventListener("touchstart", function (e) {
            if (e.touches.length !== 2) return;
            var rect = stage.getBoundingClientRect();
            var midX = (e.touches[0].clientX + e.touches[1].clientX) / 2 - rect.left;
            var midY = (e.touches[0].clientY + e.touches[1].clientY) / 2 - rect.top;
            pinch = {
                distance: touchDistance(e),
                width: canvas.getBoundingClientRect().width,
                // The spot under the fingers, in page content coordinates, stays under the fingers.
                x: stage.scrollLeft + midX, y: stage.scrollTop + midY, midX: midX, midY: midY,
                newWidth: null,
            };
        }, { passive: true });
        stage.addEventListener("touchmove", function (e) {
            if (!pinch || e.touches.length !== 2) return;
            e.preventDefault();
            var w = Math.min(availableWidth() * 4, Math.max(availableWidth() * 0.5, pinch.width * touchDistance(e) / pinch.distance));
            var ratio = w / pinch.width;
            pinch.newWidth = w;
            canvas.style.width = Math.round(w) + "px";
            canvas.style.height = Math.round(w * aspect) + "px";
            stage.scrollLeft = pinch.x * ratio - pinch.midX;
            stage.scrollTop = pinch.y * ratio - pinch.midY;
            $("zoomLabel") && ($("zoomLabel").textContent = Math.round(w / availableWidth() * 100) + "%");
        }, { passive: false });
        stage.addEventListener("touchend", function (e) {
            if (!pinch || e.touches.length >= 2) return;
            var w = pinch.newWidth;
            pinch = null;
            if (!w) return;
            // Close to the screen width: snap to "fit width" so the page fits again.
            if (Math.abs(w / availableWidth() - 1) < 0.08) { fit = "width"; localStorage.setItem("obrl.fit", "width"); }
            else { zoom = w / availableWidth(); fit = "custom"; }
            render(stageOffset(), true);
        });
        // Older iPhones: stop Safari zooming the whole screen.
        document.addEventListener("gesturestart", function (e) { e.preventDefault(); });

        document.addEventListener("keydown", function (e) {
            if (e.target === posInput) return;
            switch (e.key) {
                case "ArrowRight": case "PageDown": if (stage.scrollTop + stage.clientHeight >= stage.scrollHeight - 2 || e.key === "ArrowRight") { e.preventDefault(); goTo(position + 1, 0); } break;
                case "ArrowLeft": case "PageUp": if (stage.scrollTop <= 0 || e.key === "ArrowLeft") { e.preventDefault(); goTo(position - 1, 1); } break;
                case "Home": e.preventDefault(); goTo(0, 0); break;
                case "End": e.preventDefault(); goTo(count - 1, 0); break;
                case "+": case "=": zoomBy(1.2); break;
                case "-": zoomBy(1 / 1.2); break;
                case "w": case "W": setFit("width"); break;
                case "p": case "P": if (!e.ctrlKey && !e.metaKey) setFit("page"); break;
                case "f": case "F": toggleFullscreen(); break;
            }
        });

        render(startOffset);
    }

    // =====================================================================
    // Text mode
    // =====================================================================
    function initTextMode() {
        var article = $("text");
        var toc = JSON.parse($("tocData").textContent || "[]");
        var prefs = {
            size: parseInt(localStorage.getItem("obrl.size") || "19", 10),
            leading: localStorage.getItem("obrl.leading") || "1.7",
            measure: localStorage.getItem("obrl.measure") || "42em",
            font: localStorage.getItem("obrl.font") || "serif",
        };

        function applyPrefs() {
            article.style.setProperty("--reader-size", prefs.size + "px");
            article.style.setProperty("--reader-leading", prefs.leading);
            article.style.setProperty("--reader-measure", prefs.measure);
            article.classList.toggle("sans", prefs.font === "sans");
            document.querySelectorAll("[data-font]").forEach(function (b) { b.classList.toggle("active", b.dataset.font === prefs.font); });
            document.querySelectorAll("[data-leading]").forEach(function (b) { b.classList.toggle("active", b.dataset.leading === prefs.leading); });
            document.querySelectorAll("[data-measure]").forEach(function (b) { b.classList.toggle("active", b.dataset.measure === prefs.measure); });
            Object.keys(prefs).forEach(function (k) { localStorage.setItem("obrl." + k, prefs[k]); });
            if ($("fontSize")) $("fontSize").textContent = prefs.size;
        }

        function keepPlace(fn) {
            var off = stageOffset();
            fn();
            applyPrefs();
            var max = stage.scrollHeight - stage.clientHeight;
            stage.scrollTop = off * max;
        }

        on("fontUp", "click", function () { keepPlace(function () { prefs.size = Math.min(32, prefs.size + 1); }); });
        on("fontDown", "click", function () { keepPlace(function () { prefs.size = Math.max(13, prefs.size - 1); }); });
        document.querySelectorAll("[data-font-step]").forEach(function (b) {
            b.addEventListener("click", function (e) {
                e.stopPropagation(); // keep the settings menu open while trying sizes
                var step = parseInt(b.dataset.fontStep, 10);
                keepPlace(function () { prefs.size = Math.min(32, Math.max(13, prefs.size + step)); });
            });
        });
        onStageResize(function () { $("watermark").style.height = stage.scrollHeight + "px"; showProgress(); });
        document.querySelectorAll("[data-font]").forEach(function (b) { b.addEventListener("click", function () { keepPlace(function () { prefs.font = b.dataset.font; }); }); });
        document.querySelectorAll("[data-leading]").forEach(function (b) { b.addEventListener("click", function () { keepPlace(function () { prefs.leading = b.dataset.leading; }); }); });
        document.querySelectorAll("[data-measure]").forEach(function (b) { b.addEventListener("click", function () { keepPlace(function () { prefs.measure = b.dataset.measure; }); }); });
        applyPrefs();

        // Watermark: the reader's name tiled faintly over the text.
        var label = (root.dataset.user || "") + " · " + root.dataset.userShort;
        var svg = '<svg xmlns="http://www.w3.org/2000/svg" width="420" height="220">' +
            '<text x="10" y="120" transform="rotate(-25 210 110)" font-family="sans-serif" font-size="15" fill="#000">' +
            label.replace(/[<>&"]/g, "") + "</text></svg>";
        $("watermark").style.backgroundImage = 'url("data:image/svg+xml;utf8,' + encodeURIComponent(svg) + '")';

        // Table of contents.
        var tocBox = $("toc");
        if (toc.length === 0) {
            var none = document.createElement("div");
            none.className = "px-3 py-1 small text-muted";
            none.textContent = t("No contents list.");
            tocBox.appendChild(none);
        }
        toc.forEach(function (t) {
            var b = document.createElement("button");
            b.className = "dropdown-item";
            b.textContent = t.Title;
            b.addEventListener("click", function () { goTo(t.Section, 0); });
            tocBox.appendChild(b);
        });

        var loadToken = 0;
        function load(n, offset) {
            var token = ++loadToken;
            setPositionLabel();
            $("prevSection").disabled = n <= 0;
            $("nextSection").disabled = n >= count - 1;
            return fetch("/read/" + bookId + "/section/" + n, { credentials: "same-origin" })
                .then(function (r) {
                    if (r.status === 429) throw new Error("slow");
                    if (!r.ok) throw new Error(r.status);
                    return r.json();
                })
                .then(function (data) {
                    if (token !== loadToken) return;
                    article.innerHTML = data.html;
                    article.querySelectorAll("img[data-obrl-res]").forEach(function (img) {
                        var url = data.resources[img.getAttribute("data-obrl-res")];
                        if (url) img.src = url; else img.remove();
                    });
                    $("watermark").style.height = stage.scrollHeight + "px";
                    requestAnimationFrame(function () {
                        var max = stage.scrollHeight - stage.clientHeight;
                        stage.scrollTop = offset > 0 && max > 0 ? offset * max : 0;
                        $("watermark").style.height = stage.scrollHeight + "px";
                    });
                })
                .catch(function (err) {
                    toast(t(err.message === "slow" ? "You are moving very fast. Please wait a moment." : "This part could not be loaded. Try again."));
                });
        }

        goTo = function (n, offset) {
            if (n < 0 || n >= count) return;
            position = n;
            load(n, offset || 0);
            saveSoon();
            stage.focus({ preventScroll: true });
        };

        on("prevSection", "click", function () { goTo(position - 1, 1); });
        on("nextSection", "click", function () { goTo(position + 1, 0); });

        document.addEventListener("keydown", function (e) {
            if (e.target === posInput) return;
            if (e.key === "ArrowRight") { e.preventDefault(); goTo(position + 1, 0); }
            else if (e.key === "ArrowLeft") { e.preventDefault(); goTo(position - 1, 1); }
            else if (e.key === "f" || e.key === "F") toggleFullscreen();
        });

        load(position, startOffset);
    }
})();
