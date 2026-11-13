(function () {
    "use strict";

    var storageKey = "ratcache-theme";
    var root = document.documentElement;
    var darkHighlightTheme = document.getElementById("highlight-dark-theme");
    var theme = "light";

    try {
        theme = localStorage.getItem(storageKey) === "dark" ? "dark" : "light";
    } catch (error) {
        // The toggle still works for this page when browser storage is unavailable.
    }

    function applyTheme() {
        root.dataset.theme = theme;
        if (darkHighlightTheme) darkHighlightTheme.disabled = theme !== "dark";

        var button = document.querySelector("[data-theme-toggle]");
        if (button) {
            var dark = theme === "dark";
            button.textContent = dark ? "Light mode" : "Dark mode";
            button.setAttribute("aria-label", dark ? "Switch to light theme" : "Switch to dark theme");
            button.setAttribute("aria-pressed", dark ? "true" : "false");
        }
    }

    document.addEventListener("click", function (event) {
        var button = event.target.closest && event.target.closest("[data-theme-toggle]");
        if (!button) return;

        theme = theme === "dark" ? "light" : "dark";
        applyTheme();
        try {
            localStorage.setItem(storageKey, theme);
        } catch (error) {
            // Theme changes remain active until this page is closed.
        }
    });

    applyTheme();
})();
