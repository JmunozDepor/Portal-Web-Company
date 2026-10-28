// Facturas de Proveedor: pestañas del detalle (data-tab-btn / data-tab-panel dentro de la misma tarjeta, mismo
// comportamiento que el detalle de manifiesto de Despachos) y confirmación de formularios (data-confirm="texto").
(function () {
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-tab-btn]');
        if (!btn) return;
        var scope = btn.closest('.card-surface') || document;
        var name = btn.getAttribute('data-tab-btn');
        scope.querySelectorAll('[data-tab-btn]').forEach(function (b) { b.classList.toggle('active', b === btn); });
        scope.querySelectorAll('[data-tab-panel]').forEach(function (p) {
            p.style.display = p.getAttribute('data-tab-panel') === name ? '' : 'none';
        });
    });

    document.addEventListener('submit', function (e) {
        var text = e.target.getAttribute && e.target.getAttribute('data-confirm');
        if (text && !window.confirm(text)) e.preventDefault();
    });

    // Vista previa de la bandeja: el botón abre el drawer (drawer.js del host, data-drawer-open) y acá se trae su
    // contenido desde el handler de la página (data-preview-url).
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-preview-url]');
        if (!btn) return;
        var body = document.querySelector('[data-preview-body]');
        if (!body) return;
        body.innerHTML = '<p class="text-muted">Cargando…</p>';
        fetch(btn.getAttribute('data-preview-url'), { credentials: 'same-origin' })
            .then(function (r) {
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.text();
            })
            .then(function (html) { body.innerHTML = html; })
            .catch(function () {
                body.innerHTML = '<div class="alert alert-danger">No se pudo cargar la vista previa. Recargue la página y vuelva a intentar.</div>';
            });
    });
})();
