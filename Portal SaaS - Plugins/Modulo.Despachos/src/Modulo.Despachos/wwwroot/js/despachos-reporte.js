// Reporte mensual: expande/colapsa el detalle (turno x transportista) de cada día.
//   [data-day-toggle="yyyyMMdd"]  botón de la fila del día
//   [data-day-detail="yyyyMMdd"]  filas de detalle de ese día
(function () {
    document.querySelectorAll('[data-day-toggle]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var key = btn.getAttribute('data-day-toggle');
            var open = btn.getAttribute('aria-expanded') !== 'true';
            btn.setAttribute('aria-expanded', open ? 'true' : 'false');
            var icon = btn.querySelector('i');
            if (icon) { icon.className = open ? 'bi bi-chevron-down' : 'bi bi-chevron-right'; }
            document.querySelectorAll('[data-day-detail="' + key + '"]').forEach(function (row) {
                row.hidden = !open;
            });
        });
    });
})();
