// Retorno de manifiesto: el motivo es obligatorio al cambiar un documento a un estado distinto de "Entregado".
//   [data-ret-row]      fila de documento
//   [data-ret-status]   select de resultado (delivered / partially_delivered / rejected / not_delivered)
//   [data-ret-reason]   select de motivo; sus <option data-applies="rejection|not_delivered"> se filtran según el resultado
(function () {
    var APPLIES = { partially_delivered: 'rejection', rejected: 'rejection', not_delivered: 'not_delivered' };

    function refresh(row) {
        var status = row.querySelector('[data-ret-status]');
        var reason = row.querySelector('[data-ret-reason]');
        if (!status || !reason) { return; }
        var applies = APPLIES[status.value] || null;
        Array.prototype.forEach.call(reason.options, function (opt) {
            var a = opt.getAttribute('data-applies');
            if (a === null) { return; } // opción vacía
            var ok = applies !== null && a === applies;
            opt.hidden = !ok;
            opt.disabled = !ok;
        });
        var current = reason.options[reason.selectedIndex];
        if (applies === null || (current && current.disabled)) { reason.value = ''; }
        reason.required = applies !== null;
        reason.style.visibility = applies === null ? 'hidden' : '';
        row.classList.toggle('ret-row--changed', applies !== null);
    }

    function refreshAll() {
        document.querySelectorAll('[data-ret-row]').forEach(refresh);
    }

    document.addEventListener('change', function (e) {
        var status = e.target.closest('[data-ret-status]');
        if (status) { refresh(status.closest('[data-ret-row]')); }
    });

    document.addEventListener('DOMContentLoaded', refreshAll);
})();
