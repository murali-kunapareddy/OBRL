// OBRL passkeys: create one on the account page, and sign in with one on the login page.
// The server sends WebAuthn options as JSON and expects the browser's answer as JSON.
(function () {
    "use strict";

    var supported = !!(window.PublicKeyCredential && navigator.credentials && navigator.credentials.create);

    // ----- base64url <-> ArrayBuffer, for browsers without the JSON helpers -----
    function toBuffer(value) {
        var s = value.replace(/-/g, "+").replace(/_/g, "/");
        while (s.length % 4) s += "=";
        var bin = atob(s), bytes = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return bytes.buffer;
    }
    function toBase64Url(buffer) {
        var bytes = new Uint8Array(buffer), bin = "";
        for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
        return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    }
    function withIds(list) {
        return (list || []).map(function (c) { return Object.assign({}, c, { id: toBuffer(c.id) }); });
    }
    function creationOptions(json) {
        if (PublicKeyCredential.parseCreationOptionsFromJSON) return PublicKeyCredential.parseCreationOptionsFromJSON(json);
        return Object.assign({}, json, {
            challenge: toBuffer(json.challenge),
            user: Object.assign({}, json.user, { id: toBuffer(json.user.id) }),
            excludeCredentials: withIds(json.excludeCredentials),
        });
    }
    function requestOptions(json) {
        if (PublicKeyCredential.parseRequestOptionsFromJSON) return PublicKeyCredential.parseRequestOptionsFromJSON(json);
        return Object.assign({}, json, { challenge: toBuffer(json.challenge), allowCredentials: withIds(json.allowCredentials) });
    }
    function credentialJson(cred) {
        if (typeof cred.toJSON === "function") return JSON.stringify(cred.toJSON());
        var r = cred.response, response = { clientDataJSON: toBase64Url(r.clientDataJSON) };
        if (r.attestationObject) {
            response.attestationObject = toBase64Url(r.attestationObject);
            response.transports = r.getTransports ? r.getTransports() : [];
            if (r.getAuthenticatorData) response.authenticatorData = toBase64Url(r.getAuthenticatorData());
            if (r.getPublicKey && r.getPublicKey()) response.publicKey = toBase64Url(r.getPublicKey());
            if (r.getPublicKeyAlgorithm) response.publicKeyAlgorithm = r.getPublicKeyAlgorithm();
        } else {
            response.authenticatorData = toBase64Url(r.authenticatorData);
            response.signature = toBase64Url(r.signature);
            if (r.userHandle) response.userHandle = toBase64Url(r.userHandle);
        }
        return JSON.stringify({
            id: cred.id,
            rawId: toBase64Url(cred.rawId),
            type: cred.type,
            authenticatorAttachment: cred.authenticatorAttachment || null,
            response: response,
            clientExtensionResults: cred.getClientExtensionResults ? cred.getClientExtensionResults() : {},
        });
    }

    function token(form) {
        var input = form.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : (window.obrl && window.obrl.csrf) || "";
    }
    function fetchOptions(url, form) {
        return fetch(url, { method: "POST", headers: { "X-CSRF-TOKEN": token(form) }, credentials: "same-origin" })
            .then(function (res) {
                if (!res.ok) throw new Error("The server did not answer (" + res.status + ").");
                return res.json();
            });
    }
    function showError(form, err) {
        var box = form.querySelector("[data-passkey-error]");
        if (!box) return;
        // Cancelling the browser prompt is not an error worth shouting about.
        box.textContent = err && err.name === "NotAllowedError"
            ? "The passkey prompt was cancelled or timed out."
            : err && err.name === "InvalidStateError"
                ? "This device already has a passkey for your account."
                : "Something went wrong: " + (err && err.message ? err.message : err);
        box.hidden = false;
    }

    // ----- Account page: add a passkey -----
    var addForm = document.getElementById("passkey-add");
    if (addForm) {
        if (!supported) {
            addForm.querySelector("button").disabled = true;
            var note = document.getElementById("passkey-unsupported");
            if (note) note.hidden = false;
        }
        addForm.addEventListener("submit", function (e) {
            if (addForm.credential.value) return; // second, real submit
            e.preventDefault();
            var button = addForm.querySelector("button");
            button.disabled = true;
            fetchOptions(addForm.getAttribute("data-options-url"), addForm)
                .then(function (json) { return navigator.credentials.create({ publicKey: creationOptions(json) }); })
                .then(function (cred) {
                    addForm.credential.value = credentialJson(cred);
                    addForm.submit();
                })
                .catch(function (err) {
                    button.disabled = false;
                    showError(addForm, err);
                });
        });
    }

    // ----- Login page: sign in with a passkey -----
    var signInForm = document.getElementById("passkey-signin");
    if (signInForm) {
        if (!supported) {
            signInForm.hidden = true;
            return;
        }
        signInForm.addEventListener("submit", function (e) {
            if (signInForm.credential.value) return;
            e.preventDefault();
            var button = signInForm.querySelector("button");
            button.disabled = true;
            fetchOptions(signInForm.getAttribute("data-options-url"), signInForm)
                .then(function (json) { return navigator.credentials.get({ publicKey: requestOptions(json) }); })
                .then(function (cred) {
                    signInForm.credential.value = credentialJson(cred);
                    signInForm.submit();
                })
                .catch(function (err) {
                    button.disabled = false;
                    showError(signInForm, err);
                });
        });
    }
})();
