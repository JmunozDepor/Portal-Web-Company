// Resize manual del sidebar por drag (2026-08-19, pedido explícito -- "que se
// pueda mover el Menú izquierdo para ampliar o achicar manualmente"). Solo
// activo en modo expandido (ver sidebar-resize-handle, oculto en modo rail).
// Persiste el ancho elegido en localStorage, sobrescribiendo --sidebar-width
// (el valor fijo de 250px sigue siendo el default para quien nunca arrastró).
(function () {
    var MIN_WIDTH = 200;
    var MAX_WIDTH = 420;
    var STORAGE_KEY = 'portalsaas-sidebar-width';

    var handle = document.getElementById('sidebar-resize-handle');
    if (!handle) {
        return;
    }

    var savedWidth = localStorage.getItem(STORAGE_KEY);
    if (savedWidth) {
        document.documentElement.style.setProperty('--sidebar-width', savedWidth + 'px');
    }

    var dragging = false;

    handle.addEventListener('mousedown', function (event) {
        dragging = true;
        document.body.classList.add('sidebar-resizing');
        event.preventDefault();
    });

    document.addEventListener('mousemove', function (event) {
        if (!dragging) {
            return;
        }
        var width = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, event.clientX));
        document.documentElement.style.setProperty('--sidebar-width', width + 'px');
    });

    document.addEventListener('mouseup', function () {
        if (!dragging) {
            return;
        }
        dragging = false;
        document.body.classList.remove('sidebar-resizing');
        var currentWidth = getComputedStyle(document.documentElement).getPropertyValue('--sidebar-width').trim();
        if (currentWidth) {
            localStorage.setItem(STORAGE_KEY, parseInt(currentWidth, 10));
        }
    });
})();
