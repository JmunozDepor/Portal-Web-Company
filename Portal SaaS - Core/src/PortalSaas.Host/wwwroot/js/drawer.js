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
        if (panel) {
            prepararPanel(panel); // por si el drawer llegó después de la carga (fetch parcial)
            panel.classList.add('is-open');
        }
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

    // Ancho ajustable (2026-09-29): todo drawer se puede ampliar hacia la izquierda,
    // arrastrando su borde izquierdo o con el botón expandir del header. Doble clic
    // en el borde vuelve al ancho original. El ancho elegido se recuerda por drawer
    // (id del overlay) en localStorage: es una preferencia de este navegador, no del
    // usuario. Ninguna vista tiene que agregar marcado, se inyecta acá.
    var ANCHO_EXPANDIDO = 0.85;   // fracción del viewport al usar el botón expandir
    var MARGEN_IZQUIERDO = 64;    // nunca tapar el rail del sidebar

    function claveAncho(panel) {
        var overlay = panel.closest('[data-drawer]');
        return overlay && overlay.id ? 'drawer-ancho:' + overlay.id : null;
    }

    function leerAncho(panel) {
        var clave = claveAncho(panel);
        if (!clave) return null;
        try { return parseInt(localStorage.getItem(clave), 10) || null; } catch (e) { return null; }
    }

    function guardarAncho(panel, ancho) {
        var clave = claveAncho(panel);
        if (!clave) return;
        try {
            if (ancho) localStorage.setItem(clave, String(ancho));
            else localStorage.removeItem(clave);
        } catch (e) { /* storage bloqueado: el ajuste dura solo esta página */ }
    }

    function anchoMaximo() {
        return window.innerWidth - MARGEN_IZQUIERDO;
    }

    function anchoBase(panel) {
        if (!panel.dataset.anchoBase) {
            var previo = panel.style.maxWidth;
            panel.style.maxWidth = '';
            panel.dataset.anchoBase = String(parseInt(getComputedStyle(panel).maxWidth, 10) || 420);
            panel.style.maxWidth = previo;
        }
        return parseInt(panel.dataset.anchoBase, 10);
    }

    function aplicarAncho(panel, ancho) {
        var base = anchoBase(panel);
        var ampliado = ancho && ancho > base;
        if (ampliado) {
            ancho = Math.min(ancho, anchoMaximo());
            panel.style.maxWidth = ancho + 'px';
        } else {
            panel.style.maxWidth = '';
        }
        panel.classList.toggle('is-ampliado', !!ampliado);
        var boton = panel.querySelector('.drawer-expand-toggle');
        if (boton) {
            boton.innerHTML = ampliado ? '<i class="bi bi-arrows-angle-contract"></i>' : '<i class="bi bi-arrows-angle-expand"></i>';
            boton.title = ampliado ? 'Volver al ancho original' : 'Ampliar panel';
            boton.setAttribute('aria-label', boton.title);
        }
    }

    function prepararPanel(panel) {
        // Opt-out explícito: <aside class="drawer-panel" data-drawer-fijo> conserva su ancho.
        if (panel.dataset.ajustable || panel.hasAttribute('data-drawer-fijo')) return;
        panel.dataset.ajustable = '1';

        var borde = document.createElement('div');
        borde.className = 'drawer-resize-handle';
        borde.title = 'Arrastrar para cambiar el ancho (doble clic: ancho original)';
        panel.appendChild(borde);

        var header = panel.querySelector('.drawer-header');
        if (header) {
            var boton = document.createElement('button');
            boton.type = 'button';
            boton.className = 'btn-ghost drawer-expand-toggle';
            var cerrar = header.querySelector('[data-drawer-close]');
            if (cerrar) {
                // Agrupa expandir + cerrar a la derecha sin romper el space-between del header.
                var acciones = document.createElement('div');
                acciones.className = 'drawer-header-actions';
                cerrar.parentNode.insertBefore(acciones, cerrar);
                acciones.appendChild(boton);
                acciones.appendChild(cerrar);
            } else {
                header.appendChild(boton);
            }
            boton.addEventListener('click', function () {
                var ampliado = panel.classList.contains('is-ampliado');
                var nuevo = ampliado ? null : Math.round(window.innerWidth * ANCHO_EXPANDIDO);
                aplicarAncho(panel, nuevo);
                guardarAncho(panel, panel.classList.contains('is-ampliado') ? parseInt(panel.style.maxWidth, 10) : null);
            });
        }

        borde.addEventListener('pointerdown', function (e) {
            e.preventDefault();
            borde.setPointerCapture(e.pointerId);
            panel.classList.add('is-resizing');
            function mover(ev) {
                aplicarAncho(panel, Math.max(anchoBase(panel), window.innerWidth - ev.clientX));
            }
            function soltar() {
                borde.removeEventListener('pointermove', mover);
                borde.removeEventListener('pointerup', soltar);
                borde.removeEventListener('pointercancel', soltar);
                panel.classList.remove('is-resizing');
                guardarAncho(panel, panel.classList.contains('is-ampliado') ? parseInt(panel.style.maxWidth, 10) : null);
            }
            borde.addEventListener('pointermove', mover);
            borde.addEventListener('pointerup', soltar);
            borde.addEventListener('pointercancel', soltar);
        });

        borde.addEventListener('dblclick', function () {
            aplicarAncho(panel, null);
            guardarAncho(panel, null);
        });

        aplicarAncho(panel, leerAncho(panel));
    }

    function prepararTodos() {
        document.querySelectorAll('[data-drawer] .drawer-panel').forEach(prepararPanel);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', prepararTodos);
    else prepararTodos();

    // Si la ventana se achica, ningún drawer ampliado queda más ancho que la pantalla.
    window.addEventListener('resize', function () {
        document.querySelectorAll('.drawer-panel.is-ampliado').forEach(function (panel) {
            aplicarAncho(panel, parseInt(panel.style.maxWidth, 10));
        });
    });
})();
