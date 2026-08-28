// Slide-over drawer -- patrón único de creación/edición sin navegar a otra
// página (regla dura de gobernanza UI, ver components.css .drawer-overlay/
// .drawer-panel). Un solo script global, consumido por cualquier vista que
// tenga un <div id="drawer-overlay" class="drawer-overlay"> +
// <aside id="drawer-panel" class="drawer-panel"> en su markup -- nunca un
// drawer.js propio por módulo.
//
// Uso: <button data-drawer-open="drawer-nuevo-x">Nuevo</button>
//      <div id="drawer-nuevo-x" class="drawer-overlay" data-drawer>
//        <aside class="drawer-panel"> ... <button data-drawer-close>Cancelar</button> </aside>
//      </div>
(function () {
    function openDrawer(id) {
        var overlay = document.getElementById(id);
        if (!overlay) return;
        overlay.classList.add('is-open');
        var panel = overlay.querySelector('.drawer-panel');
        if (panel) panel.classList.add('is-open');
        document.body.classList.add('drawer-locked');
    }

    function closeDrawer(overlay) {
        overlay.classList.remove('is-open');
        var panel = overlay.querySelector('.drawer-panel');
        if (panel) panel.classList.remove('is-open');
        document.body.classList.remove('drawer-locked');
    }

    document.addEventListener('click', function (e) {
        var openTrigger = e.target.closest('[data-drawer-open]');
        if (openTrigger) {
            openDrawer(openTrigger.getAttribute('data-drawer-open'));
            return;
        }
        var closeTrigger = e.target.closest('[data-drawer-close]');
        if (closeTrigger) {
            var overlay = closeTrigger.closest('[data-drawer]');
            if (overlay) closeDrawer(overlay);
            return;
        }
        // Click en el overlay (fuera del panel) también cierra.
        if (e.target.matches('[data-drawer].is-open')) {
            closeDrawer(e.target);
        }
    });

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;
        document.querySelectorAll('[data-drawer].is-open').forEach(closeDrawer);
    });
})();
