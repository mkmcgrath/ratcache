// Paste page behavior: syntax highlighting (FR-4.2) and the copy/share buttons (FR-2.9).
// The page is static SSR, so this runs on first load and again after each Blazor
// enhanced navigation ("enhancedload"), which swaps the page without rerunning scripts.
(function () {
    "use strict";

    // Highlighting runs on the main thread; past this size it would freeze the tab.
    var MAX_HIGHLIGHT_CHARS = 200000;

    function highlight() {
        if (!window.hljs) return;
        document.querySelectorAll("code[data-lang]:not([data-highlighted])").forEach(function (el) {
            var lang = el.getAttribute("data-lang");
            // "text" and anything unknown stay plain (highlight.js would treat "text" as an alias).
            if (lang === "text" || !hljs.getLanguage(lang)) return;
            if (el.textContent.length > MAX_HIGHLIGHT_CHARS) {
                var note = document.querySelector("[data-highlight-note]");
                if (note) note.hidden = false;
                return;
            }
            // highlight.js reads textContent and writes escaped HTML, so paste content is
            // never treated as markup (NFR-3.3).
            el.classList.add("language-" + lang);
            hljs.highlightElement(el);
        });
    }

    function showShareButtons() {
        document.querySelectorAll("[data-share-bar]").forEach(function (bar) {
            bar.hidden = false;
            var share = bar.querySelector("[data-share]");
            if (share) share.hidden = typeof navigator.share !== "function";
        });
    }

    function pasteUrl(bar, kind) {
        var id = bar.getAttribute("data-id");
        return new URL((kind === "raw" ? "raw/" : "p/") + encodeURIComponent(id), document.baseURI).href;
    }

    // navigator.clipboard needs a secure context (https or localhost). Elsewhere, fall back
    // to the old selection + execCommand way.
    function copyText(text) {
        if (navigator.clipboard && window.isSecureContext) {
            return navigator.clipboard.writeText(text);
        }
        return new Promise(function (resolve, reject) {
            var area = document.createElement("textarea");
            area.value = text;
            area.setAttribute("readonly", "");
            area.style.position = "fixed";
            area.style.opacity = "0";
            document.body.appendChild(area);
            area.select();
            var ok = false;
            try { ok = document.execCommand("copy"); } catch (e) { ok = false; }
            document.body.removeChild(area);
            ok ? resolve() : reject(new Error("copy failed"));
        });
    }

    var statusTimer;
    function setStatus(bar, text) {
        var status = bar.querySelector("[data-copy-status]");
        if (!status) return;
        status.textContent = text;
        clearTimeout(statusTimer);
        statusTimer = setTimeout(function () { status.textContent = ""; }, 2500);
    }

    // One listener for the whole document, so it keeps working across enhanced navigations.
    document.addEventListener("click", function (e) {
        var button = e.target.closest && e.target.closest("[data-copy], [data-share]");
        if (!button) return;
        var bar = button.closest("[data-share-bar]");
        if (!bar) return;

        if (button.hasAttribute("data-share")) {
            navigator.share({ title: document.title, url: pasteUrl(bar, "page") })
                .catch(function (err) {
                    if (err && err.name !== "AbortError") setStatus(bar, "couldn't share");
                });
            return;
        }

        var kind = button.getAttribute("data-copy");
        var text, done;
        if (kind === "content") {
            var code = document.querySelector("code[data-lang]");
            text = code ? code.textContent : "";
            done = "copied text";
        } else if (kind === "raw") {
            text = pasteUrl(bar, "raw");
            done = "copied raw link";
        } else {
            text = pasteUrl(bar, "page");
            done = "copied link";
        }
        copyText(text).then(
            function () { setStatus(bar, done); },
            function () { setStatus(bar, "couldn't copy; select it and copy by hand"); });
    });

    function init() {
        highlight();
        showShareButtons();
    }

    init();
    if (window.Blazor && typeof Blazor.addEventListener === "function") {
        Blazor.addEventListener("enhancedload", init);
    }
})();
