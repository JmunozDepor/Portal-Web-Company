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
})();
