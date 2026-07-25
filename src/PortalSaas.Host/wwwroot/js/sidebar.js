// Sidebar: persistencia de qué grupos quedaron expandidos (localStorage), sobre el
// componente `collapse` nativo de Bootstrap 5 (bootstrap.bundle.min.js) -- este script
// nunca oculta/muestra nada a mano, solo llama a la API de Collapse y escucha sus
// eventos para guardar el estado. Aparte, modo "solo iconos" del sidebar completo
// (no es un collapse de Bootstrap, es un modo de layout distinto).
(function () {
    var SIDEBAR_STORAGE_KEY = 'portalsaas-sidebar-collapsed';
    var GROUPS_STORAGE_KEY = 'portalsaas-sidebar-groups-expanded';

    function getExpandedGroups() {
        try {
            return new Set(JSON.parse(localStorage.getItem(GROUPS_STORAGE_KEY) || '[]'));
        } catch (e) {
            return new Set();
        }
    }

    function saveExpandedGroups(groups) {
        localStorage.setItem(GROUPS_STORAGE_KEY, JSON.stringify(Array.from(groups)));
    }

    document.addEventListener('DOMContentLoaded', function () {
        var expandedGroups = getExpandedGroups();

        document.querySelectorAll('.sidebar-nav [data-bs-toggle="collapse"]').forEach(function (toggle) {
            var targetSelector = toggle.getAttribute('href') || toggle.getAttribute('data-bs-target');
            var target = targetSelector ? document.querySelector(targetSelector) : null;
            if (!target) {
                return;
            }

            var groupId = target.id;
            // toggle:false -- no animar/expandir nada todavía, solo crear la instancia.
            var collapseInstance = bootstrap.Collapse.getOrCreateInstance(target, { toggle: false });

            if (expandedGroups.has(groupId)) {
                collapseInstance.show();
            }

            target.addEventListener('shown.bs.collapse', function () {
                expandedGroups.add(groupId);
                saveExpandedGroups(expandedGroups);
            });

            target.addEventListener('hidden.bs.collapse', function () {
                expandedGroups.delete(groupId);
                saveExpandedGroups(expandedGroups);
            });
        });

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
