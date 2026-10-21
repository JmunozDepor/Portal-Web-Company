// Pantallas de manifiesto de Despachos: pestañas y filtro de vehículo/chofer por transportista.
//   [data-tab-btn="x"] / [data-tab-panel="x"]   pestañas (la inicial la fija el servidor)
//   [data-carrier-select]                        select de transportista de la cabecera
//   [data-by-carrier]                            selects cuyas <option data-carrier="id"> se filtran por el transportista
(function () {
    function showTab(name) {
        document.querySelectorAll('[data-tab-btn]').forEach(function (b) {
            b.classList.toggle('active', b.getAttribute('data-tab-btn') === name);
        });
        document.querySelectorAll('[data-tab-panel]').forEach(function (p) {
            p.style.display = p.getAttribute('data-tab-panel') === name ? '' : 'none';
        });
    }

    // Muestra solo las opciones del transportista elegido (o sin transportista). Si la opción
    // seleccionada queda oculta se limpia la selección.
    function filterByCarrier() {
        var carrier = document.querySelector('[data-carrier-select]');
        if (!carrier) { return; }
        var id = carrier.value;
        document.querySelectorAll('select[data-by-carrier]').forEach(function (select) {
            Array.prototype.forEach.call(select.options, function (opt) {
                var c = opt.getAttribute('data-carrier');
                if (c === null) { return; } // opción vacía
                var show = !id || !c || c === id;
                opt.hidden = !show;
                opt.disabled = !show;
            });
            var sel = select.options[select.selectedIndex];
            if (sel && sel.disabled) { select.value = ''; }
        });
    }

    document.addEventListener('click', function (e) {
        var tab = e.target.closest('[data-tab-btn]');
        if (tab) { showTab(tab.getAttribute('data-tab-btn')); }
    });

    document.addEventListener('change', function (e) {
        if (e.target.closest('[data-carrier-select]')) { filterByCarrier(); }
    });

    document.addEventListener('DOMContentLoaded', filterByCarrier);
})();
