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

        // Bug real: <details class="user-menu"> (topbar, _Layout.cshtml) es el panel
        // de tema/preferencias/cerrar sesión -- <details> nativo solo se cierra al
        // volver a tocar su <summary> o al navegar (recarga completa de página), NUNCA
        // al hacer click afuera. Expandir/colapsar un grupo del sidebar usa el
        // collapse de Bootstrap (data-bs-toggle="collapse", ver sidebar.js) SIN
        // navegar -- con el panel abierto, tocar un grupo del sidebar lo dejaba
        // flotando encima del resto de la página indefinidamente. Cierre estándar:
        // click afuera del <details> (o Escape) lo cierra, sin tocar el toggle nativo
        // de <summary> (que sigue abriendo/cerrando igual que siempre).
        var userMenu = document.querySelector("details.user-menu");
        if (userMenu) {
            document.addEventListener("click", function (event) {
                if (userMenu.open && !userMenu.contains(event.target)) {
                    userMenu.open = false;
                }
            });
            document.addEventListener("keydown", function (event) {
                if (event.key === "Escape" && userMenu.open) {
                    userMenu.open = false;
                }
            });
        }
    });
})();
