// Búsqueda en vivo genérica contra un handler AJAX de Razor Pages, poblando un
// <datalist> nativo -- generaliza el bloque que antes vivía inline en
// Modulo.Ventas/Pages/Shared/_TabContent.cshtml, para no reescribirlo por cada
// catálogo nuevo (Cliente, Artículo, Cuenta Mayor, Proveedor...) en los motores de
// Venta/Compra/Inventario. Sin librería externa, un <datalist> nativo alcanza.
(function () {
    window.wireCatalogSearch = function (options) {
        var datalist = document.getElementById(options.datalistId);
        var timer = null;

        document.querySelectorAll(options.inputSelector).forEach(function (input) {
            input.addEventListener('input', function () {
                var text = input.value.trim();
                if (text.length < 2) {
                    return;
                }
                clearTimeout(timer);
                timer = setTimeout(function () {
                    fetch('?handler=' + options.handlerName + '&text=' + encodeURIComponent(text))
                        .then(function (r) { return r.json(); })
                        .then(function (items) {
                            datalist.innerHTML = '';
                            items.forEach(function (item) {
                                var option = document.createElement('option');
                                option.value = options.getValue(item);
                                option.label = options.getLabel(item);
                                datalist.appendChild(option);
                            });
                        });
                }, 300);
            });
        });
    };
})();
