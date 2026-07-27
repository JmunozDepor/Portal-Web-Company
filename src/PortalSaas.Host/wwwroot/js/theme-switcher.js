// Selector de tema -- persistencia por navegador (localStorage), no por usuario --
// portado tal cual de referencia-original/PortalSAP_v2 (mismo patrón, mismos 4 temas:
// claro/oscuro/teal/violeta -- ver site.css [data-theme="..."]). Cualquier módulo
// nuevo hereda el tema activo sin hacer nada porque solo usa variables CSS, nunca
// colores literales.
(function () {
    const STORAGE_KEY = "ps-theme";
    const DEFAULT_THEME = "claro";

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        document.querySelectorAll("[data-theme-switch]").forEach(function (button) {
            button.setAttribute("data-active", button.getAttribute("data-theme-switch") === theme ? "true" : "false");
        });
    }

    function setTheme(theme) {
        localStorage.setItem(STORAGE_KEY, theme);
        applyTheme(theme);
    }

    document.addEventListener("DOMContentLoaded", function () {
        var savedTheme = localStorage.getItem(STORAGE_KEY) || DEFAULT_THEME;
        applyTheme(savedTheme);

        document.querySelectorAll("[data-theme-switch]").forEach(function (button) {
            button.addEventListener("click", function () {
                setTheme(button.getAttribute("data-theme-switch"));
            });
        });
    });
})();
