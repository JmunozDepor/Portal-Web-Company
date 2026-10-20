// Herramientas genéricas para una tabla de solo lectura: ordenar por columna, exportar
// a Excel y copiar al portapapeles -- primer consumidor: grilla "Stock por almacén" del
// Maestro de Producto (Modulo.Inventario). Reutilizable por cualquier pantalla: solo se
// marca el markup, sin JS propio por tabla.
//
//   <th data-sort="text|number">   columna ordenable (click alterna asc/desc)
//   <td data-value="1234.5">       valor crudo (invariante) para ordenar/exportar; si no
//                                  viene se usa el texto visible de la celda
//   <tr data-table-empty>          fila de "sin datos": no se ordena ni se exporta
//
// Exportar usa SheetJS (~/lib/sheetjs/xlsx.full.min.js, ya vendorizado -- mismo que el
// importador de líneas); los números se exportan como número, no como texto con formato
// local. Copiar deja texto separado por tabulaciones: se pega directo en Excel.
(function () {
    window.wireTableTools = function (options) {
        var table = document.getElementById(options.tableId);
        if (!table) {
            return;
        }
        var tbody = table.tBodies[0];
        var headers = Array.prototype.slice.call(table.tHead.rows[0].cells);

        function dataRows() {
            return Array.prototype.slice.call(tbody.rows).filter(function (r) { return !r.hasAttribute('data-table-empty'); });
        }

        function cellValue(cell, type) {
            var raw = cell.hasAttribute('data-value') ? cell.getAttribute('data-value') : cell.textContent.trim();
            if (type === 'number') {
                var n = parseFloat(raw);
                return isNaN(n) ? 0 : n;
            }
            return raw;
        }

        // --- Ordenar -----------------------------------------------------------------
        headers.forEach(function (th, index) {
            var type = th.getAttribute('data-sort');
            if (!type) {
                return;
            }
            th.classList.add('table-sortable');
            th.setAttribute('tabindex', '0');
            th.setAttribute('aria-sort', 'none');

            function sort() {
                var ascending = th.getAttribute('aria-sort') !== 'ascending';
                headers.forEach(function (h) { if (h.hasAttribute('data-sort')) { h.setAttribute('aria-sort', 'none'); } });
                th.setAttribute('aria-sort', ascending ? 'ascending' : 'descending');

                var rows = dataRows();
                rows.sort(function (a, b) {
                    var va = cellValue(a.cells[index], type);
                    var vb = cellValue(b.cells[index], type);
                    var cmp = type === 'number' ? va - vb : String(va).localeCompare(String(vb), 'es', { numeric: true, sensitivity: 'base' });
                    return ascending ? cmp : -cmp;
                });
                rows.forEach(function (r) { tbody.appendChild(r); });
            }

            th.addEventListener('click', sort);
            th.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    sort();
                }
            });
        });

        // --- Matriz para exportar/copiar (respeta el orden actual) ---------------------
        function matrix() {
            var types = headers.map(function (h) { return h.getAttribute('data-sort') || 'text'; });
            var result = [headers.map(function (h) { return h.textContent.trim(); })];
            dataRows().forEach(function (r) {
                result.push(Array.prototype.map.call(r.cells, function (c, i) { return cellValue(c, types[i]); }));
            });
            return result;
        }

        function feedback(button, text) {
            if (!button) {
                return;
            }
            var original = button.getAttribute('data-original-html') || button.innerHTML;
            button.setAttribute('data-original-html', original);
            button.textContent = text;
            setTimeout(function () { button.innerHTML = original; }, 1500);
        }

        var exportButton = document.getElementById(options.exportButtonId);
        if (exportButton) {
            exportButton.addEventListener('click', function () {
                if (dataRows().length === 0) {
                    feedback(exportButton, 'Sin datos');
                    return;
                }
                if (typeof XLSX === 'undefined') {
                    console.error('table-tools: SheetJS (XLSX) no está cargado.');
                    return;
                }
                var sheet = XLSX.utils.aoa_to_sheet(matrix());
                var book = XLSX.utils.book_new();
                XLSX.utils.book_append_sheet(book, sheet, (options.sheetName || 'Datos').substring(0, 31));
                XLSX.writeFile(book, (options.fileName || 'exportacion') + '.xlsx');
            });
        }

        var copyButton = document.getElementById(options.copyButtonId);
        if (copyButton) {
            copyButton.addEventListener('click', function () {
                if (dataRows().length === 0) {
                    feedback(copyButton, 'Sin datos');
                    return;
                }
                // Números con coma decimal (es-CL) para que Excel en español los pegue como número.
                var tsv = matrix().map(function (row) {
                    return row.map(function (v) {
                        return typeof v === 'number' ? String(v).replace('.', ',') : String(v).replace(/[\t\r\n]+/g, ' ');
                    }).join('\t');
                }).join('\r\n');

                var done = function () { feedback(copyButton, 'Copiado'); };
                if (navigator.clipboard && window.isSecureContext) {
                    navigator.clipboard.writeText(tsv).then(done, function (e) { console.error('table-tools copiar: ', e); });
                } else {
                    var area = document.createElement('textarea');
                    area.value = tsv;
                    area.style.position = 'fixed';
                    area.style.opacity = '0';
                    document.body.appendChild(area);
                    area.select();
                    document.execCommand('copy');
                    area.remove();
                    done();
                }
            });
        }
    };
})();
