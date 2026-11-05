// Crop and adjust a book cover in the browser before it is uploaded (Cropper.js, MIT).
// Works for "My own cover image" (the chosen file) and "A page from the book" (a picture of the page from the server).
// The result is a 2:3 picture, 720 x 1080, that replaces the file sent with the form. The server still checks and
// resizes it, so a browser without this script simply sends the original file as before.
(function () {
    "use strict";
    var modalEl = document.getElementById("cover-crop-modal");
    if (!modalEl || typeof Cropper === "undefined" || typeof bootstrap === "undefined" || typeof DataTransfer === "undefined") return;

    var OUT_W = 720, OUT_H = 1080;
    var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    var image = modalEl.querySelector("[data-crop-image]");
    var sliders = {
        brightness: modalEl.querySelector("[data-adjust=brightness]"),
        contrast: modalEl.querySelector("[data-adjust=contrast]"),
        saturation: modalEl.querySelector("[data-adjust=saturation]"),
        straighten: modalEl.querySelector("[data-adjust=straighten]")
    };
    var cropper = null, quarterTurns = 0, flipped = false, onApply = null, lastObjectUrl = null;

    var coverImage = document.getElementById("CoverImage");
    var pageImage = document.getElementById("PageCoverImage");
    var coverPage = document.getElementById("CoverPage");
    var clearPageCrop = document.getElementById("ClearPageCrop");
    var originalFile = null; // the file as chosen, so it can be cropped again from scratch

    function filterCss() {
        return "brightness(" + sliders.brightness.value / 100 + ") contrast(" + sliders.contrast.value / 100 +
            ") saturate(" + sliders.saturation.value / 100 + ")";
    }

    function showFilter() {
        modalEl.querySelectorAll(".cropper-canvas img, .cropper-view-box img").forEach(function (img) { img.style.filter = filterCss(); });
        modalEl.querySelectorAll("[data-adjust-value]").forEach(function (out) {
            var input = sliders[out.getAttribute("data-adjust-value")];
            out.textContent = input.value + (input === sliders.straighten ? "°" : "%");
        });
    }

    function resetAdjustments() {
        sliders.brightness.value = 100;
        sliders.contrast.value = 100;
        sliders.saturation.value = 100;
        sliders.straighten.value = 0;
        quarterTurns = 0;
        flipped = false;
    }

    function rotation() { return quarterTurns * 90 + Number(sliders.straighten.value); }

    function open(url, apply) {
        onApply = apply;
        resetAdjustments();
        if (cropper) { cropper.destroy(); cropper = null; }
        image.src = url;
        modal.show();
    }

    modalEl.addEventListener("shown.bs.modal", function () {
        if (cropper) cropper.destroy();
        cropper = new Cropper(image, {
            aspectRatio: 2 / 3,
            viewMode: 1,
            dragMode: "move",
            autoCropArea: 1,
            background: false,
            checkOrientation: false,
            toggleDragModeOnDblclick: false,
            ready: showFilter
        });
    });
    modalEl.addEventListener("hidden.bs.modal", function () {
        if (cropper) { cropper.destroy(); cropper = null; }
        onApply = null;
    });

    modalEl.querySelectorAll("[data-crop-action]").forEach(function (button) {
        button.addEventListener("click", function () {
            if (!cropper) return;
            switch (button.getAttribute("data-crop-action")) {
                case "zoom-in": cropper.zoom(0.1); break;
                case "zoom-out": cropper.zoom(-0.1); break;
                case "rotate-left": quarterTurns--; cropper.rotateTo(rotation()); break;
                case "rotate-right": quarterTurns++; cropper.rotateTo(rotation()); break;
                case "flip": flipped = !flipped; cropper.scaleX(flipped ? -1 : 1); break;
                case "reset": resetAdjustments(); cropper.reset(); cropper.scaleX(1); showFilter(); break;
            }
        });
    });
    Object.keys(sliders).forEach(function (name) {
        sliders[name].addEventListener("input", function () {
            if (name === "straighten" && cropper) cropper.rotateTo(rotation());
            showFilter();
        });
    });

    // Brightness, contrast and saturation are applied pixel by pixel, the same way the CSS preview shows them,
    // so the result looks the same in every browser (not all of them support filters on a canvas).
    function applyAdjustments(canvas) {
        var b = sliders.brightness.value / 100, c = sliders.contrast.value / 100, s = sliders.saturation.value / 100;
        if (b === 1 && c === 1 && s === 1) return canvas;
        var ctx = canvas.getContext("2d");
        var data = ctx.getImageData(0, 0, canvas.width, canvas.height);
        var px = data.data;
        // Saturation matrix from the CSS Filter Effects specification.
        var m = [
            0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s,
            0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s,
            0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s
        ];
        for (var i = 0; i < px.length; i += 4) {
            var r = px[i] * b, g = px[i + 1] * b, bl = px[i + 2] * b;
            r = (r - 127.5) * c + 127.5; g = (g - 127.5) * c + 127.5; bl = (bl - 127.5) * c + 127.5;
            px[i] = m[0] * r + m[1] * g + m[2] * bl;
            px[i + 1] = m[3] * r + m[4] * g + m[5] * bl;
            px[i + 2] = m[6] * r + m[7] * g + m[8] * bl;
        }
        ctx.putImageData(data, 0, 0);
        return canvas;
    }

    modalEl.querySelector("[data-crop-apply]").addEventListener("click", function () {
        if (!cropper || !onApply) return;
        var canvas = cropper.getCroppedCanvas({ width: OUT_W, height: OUT_H, fillColor: "#fff", imageSmoothingQuality: "high" });
        if (!canvas) return;
        applyAdjustments(canvas).toBlob(function (blob) {
            if (!blob) return;
            var apply = onApply;
            modal.hide();
            apply(blob);
        }, "image/jpeg", 0.92);
    });

    function setFile(input, blob, name) {
        var dt = new DataTransfer();
        dt.items.add(new File([blob], name, { type: blob.type }));
        input.files = dt.files;
    }

    function showPreview(target, blob) {
        if (!target) return;
        if (lastObjectUrl) URL.revokeObjectURL(lastObjectUrl);
        lastObjectUrl = URL.createObjectURL(blob);
        target.innerHTML = "";
        var img = document.createElement("img");
        img.src = lastObjectUrl;
        img.alt = "";
        target.appendChild(img);
        target.hidden = false;
    }

    function pick(id) { var r = document.getElementById(id); if (r) r.checked = true; }

    // ----- My own cover image -----
    var customPreview = document.querySelector("[data-crop-preview=custom]");
    var customEdit = document.querySelector("[data-crop-edit=custom]");
    function cropCustom() {
        if (!originalFile) return;
        open(URL.createObjectURL(originalFile), function (blob) {
            setFile(coverImage, blob, "cover.jpg");
            showPreview(customPreview, blob);
            pick("cover-custom");
        });
    }
    if (coverImage) {
        coverImage.addEventListener("change", function () {
            var file = coverImage.files && coverImage.files[0];
            // Our own replacement fires no change event, so this is always a file the member picked.
            if (!file || !/^image\//.test(file.type)) return;
            originalFile = file;
            if (customEdit) customEdit.hidden = false;
            cropCustom();
        });
    }
    if (customEdit) customEdit.addEventListener("click", cropCustom);

    // ----- A page from the book -----
    var pageButton = document.querySelector("[data-crop-edit=page]");
    var pagePreview = document.querySelector("[data-crop-preview=page]");
    var pageUndo = document.querySelector("[data-crop-undo=page]");
    if (pageButton && coverPage && pageImage) {
        var base = pageButton.getAttribute("data-page-url");
        var max = Number(coverPage.getAttribute("max")) || Infinity;
        pageButton.addEventListener("click", function () {
            var page = parseInt(coverPage.value, 10);
            if (!(page >= 1 && page <= max)) { page = 1; coverPage.value = "1"; }
            open(base + "?page=" + page, function (blob) {
                setFile(pageImage, blob, "page-cover.jpg");
                if (clearPageCrop) clearPageCrop.value = "false";
                showPreview(pagePreview, blob);
                if (pageUndo) pageUndo.hidden = false;
                pick("cover-page");
            });
        });
        // Another page makes an earlier crop meaningless.
        function dropCrop() {
            pageImage.value = "";
            if (pagePreview) pagePreview.hidden = true;
        }
        coverPage.addEventListener("input", dropCrop);
        document.querySelectorAll("[data-cover-page]").forEach(function (tile) { tile.addEventListener("click", dropCrop); });
        if (pageUndo) {
            pageUndo.addEventListener("click", function () {
                dropCrop();
                if (clearPageCrop) clearPageCrop.value = "true";
                pageUndo.hidden = true;
            });
        }
    }
})();
