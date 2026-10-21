// Tarifario de Despachos: pestañas y campos de condición según el tipo de recargo.
//   [data-tab-btn="x"] / [data-tab-panel="x"]   pestañas (la inicial la fija el servidor)
//   [data-cond-select]                          select del tipo de condición
//   [data-cond-fields="tipo1 tipo2"]            campos que solo se muestran para esos tipos
(function () {
    function showTab(name) {
        document.querySelectorAll('[data-tab-btn]').forEach(function (b) {
            b.classList.toggle('active', b.getAttribute('data-tab-btn') === name);
        });
        document.querySelectorAll('[data-tab-panel]').forEach(function (p) {
            p.style.display = p.getAttribute('data-tab-panel') === name ? '' : 'none';
        });
    }

    function refreshConditionFields() {
        var select = document.querySelector('[data-cond-select]');
        if (!select) { return; }
        document.querySelectorAll('[data-cond-fields]').forEach(function (box) {
            var types = box.getAttribute('data-cond-fields').split(/\s+/);
            box.style.display = types.indexOf(select.value) !== -1 ? '' : 'none';
        });
    }

    document.addEventListener('click', function (e) {
        var tab = e.target.closest('[data-tab-btn]');
        if (tab) { showTab(tab.getAttribute('data-tab-btn')); return; }
        // Alta o edición: despachos-maestros.js rellena el formulario en su propio listener
        // (cargado antes); se refrescan los campos cuando ya terminó.
        if (e.target.closest('[data-maestro-edit], [data-maestro-new]')) { setTimeout(refreshConditionFields, 0); }
    });

    document.addEventListener('change', function (e) {
        if (e.target.closest('[data-cond-select]')) { refreshConditionFields(); }
    });

    document.addEventListener('DOMContentLoaded', refreshConditionFields);
})();
