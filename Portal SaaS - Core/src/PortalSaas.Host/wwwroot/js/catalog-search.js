// Búsqueda en vivo genérica contra un handler AJAX de Razor Pages, poblando un
// <datalist> nativo -- generaliza el bloque que antes vivía inline en
// Modulo.Ventas/Pages/Shared/_TabContentVentas.cshtml, para no reescribirlo por cada
// catálogo nuevo (Cliente, Artículo, Cuenta Mayor, Proveedor...) en los motores de
// Venta/Compra/Inventario. Sin librería externa, un <datalist> nativo alcanza.
(function () {
    window.wireCatalogSearch = function (options) {
        var datalist = document.getElementById(options.datalistId);
        var timer = null;

        // "options.minChars || 3" trataría minChars:0 como "no vino", cayendo siempre
        // a 3 -- bug real que impedía el caso de catálogos chicos (Almacén/Cuenta
        // Mayor/Centro de Costos) que piden minChars:0. Comparación explícita contra
        // undefined en vez de "||".
        var minChars = options.minChars === undefined ? 3 : options.minChars;

        // extraParams: función opcional que devuelve un objeto {clave: valor} con
        // parámetros adicionales para el handler AJAX -- ej. Importación Genérica
        // necesita saber el Módulo elegido (Venta/Compra) para decidir si busca en
        // Cliente o Proveedor, ver Pages/Importar/Index.cshtml. Se lee en cada
        // búsqueda (no una sola vez al cablear), así refleja el valor actual del
        // campo del que depende.
        function construirQueryString(text) {
            var query = '?handler=' + options.handlerName + '&text=' + encodeURIComponent(text);
            if (options.extraParams) {
                var extra = options.extraParams() || {};
                Object.keys(extra).forEach(function (key) {
                    query += '&' + encodeURIComponent(key) + '=' + encodeURIComponent(extra[key]);
                });
            }
            return query;
        }

        function ejecutarBusqueda(text) {
            fetch(construirQueryString(text))
                .then(function (r) {
                    if (!r.ok) {
                        throw new Error('HTTP ' + r.status + ' buscando "' + options.handlerName + '"');
                    }
                    return r.json();
                })
                .then(function (items) {
                    datalist.innerHTML = '';
                    items.forEach(function (item) {
                        var option = document.createElement('option');
                        option.value = options.getValue(item);
                        option.label = options.getLabel(item);
                        datalist.appendChild(option);
                    });
                })
                // Sin esto, un error de red o una excepción del lado servidor (ej.
                // 500 en el handler AJAX) fallaba en silencio -- el usuario tipeaba
                // y nunca veía sugerencias, sin ningún indicio de qué pasó. Con
                // catch, al menos queda en la consola del navegador para poder
                // diagnosticarlo (F12), en vez de parecer "la búsqueda no anda".
                .catch(function (error) {
                    console.error('catalog-search: ', error);
                });
        }

        // Delegado en document (no querySelectorAll fijo al momento de llamar) --
        // así una fila agregada después con "+ Agregar línea" (ver
        // document-lines-editor.js) también dispara la búsqueda, sin tener que
        // volver a llamar wireCatalogSearch por cada fila nueva.
        document.addEventListener('input', function (evento) {
            var input = evento.target;
            if (!input.matches(options.inputSelector)) {
                return;
            }

            var text = input.value.trim();
            if (text.length < minChars) {
                datalist.innerHTML = '';
                return;
            }
            clearTimeout(timer);
            timer = setTimeout(function () { ejecutarBusqueda(text); }, 300);
        });

        // Catálogos chicos (minChars: 0, ej. Almacén/Cuenta Mayor/Centro de Costos --
        // tamaño acotado, ver CLAUDE.md) precargan el <datalist> UNA vez al cablear el
        // campo, con texto vacío -- el servidor sirve el listado COMPLETO cuando
        // searchText viene vacío (ver CatalogSqlHelper), así el navegador ya puede
        // mostrar el desplegable nativo apenas el usuario hace click/foco, sin tener
        // que escribir nada primero. Catálogos grandes (Artículo/Cliente/Proveedor,
        // minChars >= 2) NUNCA hacen esto -- servirían el catálogo completo sin que el
        // usuario haya pedido nada, exactamente lo que la regla dura de CLAUDE.md
        // prohíbe para esos.
        if (minChars === 0) {
            ejecutarBusqueda('');
        }
    };
})();
