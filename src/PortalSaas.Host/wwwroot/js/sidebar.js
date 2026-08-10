// Sidebar: qué grupos quedan expandidos lo decide el SERVIDOR en cada request
// (_MenuNode.cshtml, class="collapse show" si el grupo contiene la página activa) --
// este script ya no persiste nada de eso en localStorage. Bootstrap's data-api propio
// (bootstrap.bundle.min.js) ya wirea el click de [data-bs-toggle="collapse"] solo, sin
// JS de acá -- lo único que queda es el modo "solo iconos" del sidebar completo (no es
// un collapse de Bootstrap, es un modo de layout distinto).
//
// Antes esto recordaba en localStorage CUALQUIER grupo que el usuario hubiera abierto
// a mano alguna vez, y lo reabría en cada navegación sin importar la página actual --
// bug real reportado: click en un submenú (ej. "Ofertas de Compra") reabría un grupo
// completamente distinto (ej. "Rendiciones" y todo su árbol) porque había quedado
// "recordado" de una exploración anterior en la misma sesión del navegador. Confirmado
// con curl que el servidor SOLO marcaba el grupo correcto como abierto -- el bug era
// 100% esta persistencia del lado del cliente. Con el servidor como única fuente de
// verdad, un grupo abierto a mano se cierra solo en la próxima navegación si no
// contiene la página activa -- comportamiento simple y predecible.
(function () {
    var SIDEBAR_STORAGE_KEY = 'portalsaas-sidebar-collapsed';

    document.addEventListener('DOMContentLoaded', function () {
        var sidebarToggleButton = document.getElementById('btn-collapse-sidebar');
        if (sidebarToggleButton) {
            sidebarToggleButton.addEventListener('click', function () {
                var isCollapsed = document.documentElement.getAttribute('data-sidebar') === 'collapsed';
                if (isCollapsed) {
                    document.documentElement.removeAttribute('data-sidebar');
                    localStorage.setItem(SIDEBAR_STORAGE_KEY, 'expanded');
                } else {
                    document.documentElement.setAttribute('data-sidebar', 'collapsed');
                    localStorage.setItem(SIDEBAR_STORAGE_KEY, 'collapsed');
                }
            });
        }
    });
})();
