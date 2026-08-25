// Selector de tema -- portado tal cual de referencia-original/PortalSAP_v2 (mismos 4
// temas: claro/oscuro/teal/violeta -- ver site.css [data-theme="..."]). Cualquier
// módulo nuevo hereda el tema activo sin hacer nada porque solo usa variables CSS,
// nunca colores literales.
//
// BUG REAL CORREGIDO (2026-08-19, "no funciona el tema al cambiarlo en
// preferencias"): este archivo asumía que localStorage era SIEMPRE la fuente de
// verdad -- en cada DOMContentLoaded pisaba `data-theme` con
// `localStorage.getItem(STORAGE_KEY) || DEFAULT_THEME`, sin importar qué hubiera
// puesto ahí el script inline de <head> (_LayoutMaestro.cshtml). Desde la
// consolidación de preferencias, ese script YA fija el tema correcto ANTES de que
// este archivo corra -- para una sesión de tenant, desde IUserPreferenceService
// (la cuenta), nunca desde localStorage. Este archivo terminaba revirtiendo esa
// elección al valor local del navegador (o "claro" si nunca se había tocado el
// selector viejo) apenas terminaba de cargar el DOM -- el cambio en Preferences se
// guardaba en la base, pero la página seguía mostrando el tema anterior SIEMPRE.
// Fix: solo aplicar el fallback de localStorage si `data-theme` todavía no tiene
// ningún valor (anónimo/PlatformAdmin, ver el script inline) -- si ya viene fijado
// (sesión de tenant), se respeta tal cual.
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
        if (!document.documentElement.getAttribute("data-theme")) {
            applyTheme(localStorage.getItem(STORAGE_KEY) || DEFAULT_THEME);
        } else {
            // Ya viene fijado por el script inline -- solo sincroniza qué swatch
            // se marca "activo" (Login/SelectCompany siguen usando esos botones).
            applyTheme(document.documentElement.getAttribute("data-theme"));
        }

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
