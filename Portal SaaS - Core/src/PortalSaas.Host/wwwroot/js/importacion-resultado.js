// Reporte final y seguimiento de un trabajo de Importación Genérica (2026-10-15) --
// compartido por Importar (Pages/Importar/Index.cshtml) y el detalle de "Mis
// importaciones" (Pages/Trabajos/Detalle.cshtml): la creación en SAP corre en segundo
// plano (GenericImportJobWorker) y estas dos pantallas solo siguen su avance por polling.
(function () {
    function escaparHtml(texto) {
        var div = document.createElement('div');
        div.textContent = texto == null ? '' : String(texto);
        return div.innerHTML;
    }

    // Reporte final de la confirmación (2026-10-15): TODAS las OC del archivo en
    // pestañas -- creadas en SAP (con DocNum/DocEntry/total real), rechazadas por SAP
    // y no enviadas por errores de validación -- con las mismas grillas copiables que
    // la vista previa (Copiar / Descargar CSV, ver _ImportacionGenericaVistaPrevia).
    function renderizar(resultadoDiv, progreso) {
        var resultados = progreso.results || [];
        var formato = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 });
        var num = function (v) { return v == null ? '' : formato.format(v); };
        var hayVendedor = resultados.some(function (r) { return r.salesEmployee; });

        var grupos = [
            { id: 'creados', titulo: 'Pedidos creados', estado: 'Created', clase: '', insignia: 'bg-success' },
            { id: 'error-sap', titulo: 'Rechazados por SAP', estado: 'SapError', clase: 'table-danger', insignia: 'bg-danger' },
            { id: 'no-enviados', titulo: 'No enviados (errores de validación)', estado: 'Skipped', clase: 'table-warning', insignia: 'bg-warning text-dark' },
        ];

        var encabezado = '<tr><th>OC / grupo</th><th>N° referencia</th><th>Sucursal</th><th>Cliente</th>' +
            (hayVendedor ? '<th>Vendedor</th>' : '') +
            '<th class="text-end">Líneas</th><th class="text-end">Unidades</th><th class="text-end">Total OC</th>' +
            '<th class="text-end">Total SAP</th><th class="text-end">DocNum</th><th class="text-end">DocEntry</th><th>Mensaje</th></tr>';

        var pestanas = '';
        var paneles = '';
        grupos.forEach(function (grupo, indice) {
            var filas = resultados.filter(function (r) { return (r.status || (r.success ? 'Created' : 'SapError')) === grupo.estado; });
            var activo = indice === 0;
            pestanas += '<li class="nav-item" role="presentation"><button class="nav-link doc-tab-btn' + (activo ? ' active' : '') +
                '" data-bs-toggle="tab" data-bs-target="#import-resultado-' + grupo.id + '" type="button" role="tab">' +
                grupo.titulo + ' <span class="badge ' + (filas.length > 0 ? grupo.insignia : 'bg-secondary') + ' ms-1">' + filas.length + '</span></button></li>';

            var cuerpo = filas.map(function (r) {
                var cliente = (r.businessPartnerCardCode || '') + (r.businessPartnerName ? ' — ' + r.businessPartnerName : '');
                return '<tr class="' + grupo.clase + '">' +
                    '<td class="text-nowrap">' + escaparHtml(r.groupingKey) + '</td>' +
                    '<td class="text-nowrap">' + escaparHtml(r.customerReferenceNumber) + '</td>' +
                    '<td class="text-nowrap">' + escaparHtml(r.branches) + '</td>' +
                    '<td>' + escaparHtml(cliente) + '</td>' +
                    (hayVendedor ? '<td class="text-nowrap">' + escaparHtml(r.salesEmployee) + '</td>' : '') +
                    '<td class="text-end">' + num(r.lines) + '</td>' +
                    '<td class="text-end">' + num(r.units) + '</td>' +
                    '<td class="text-end">' + num(r.orderAmount) + '</td>' +
                    '<td class="text-end">' + num(r.sapDocTotal) + '</td>' +
                    '<td class="text-end fw-bold">' + (r.success ? escaparHtml(r.docNum) : '') + '</td>' +
                    '<td class="text-end">' + (r.success && r.docEntry ? escaparHtml(r.docEntry) : '') + '</td>' +
                    '<td>' + escaparHtml(r.message) + '</td>' +
                    '</tr>';
            }).join('');

            var suma = function (campo) { return filas.reduce(function (t, r) { return t + (r[campo] || 0); }, 0); };
            var pie = filas.length === 0 ? '' :
                '<tfoot><tr><td>TOTAL</td><td></td><td></td><td></td>' + (hayVendedor ? '<td></td>' : '') +
                '<td class="text-end">' + num(suma('lines')) + '</td><td class="text-end">' + num(suma('units')) + '</td>' +
                '<td class="text-end">' + num(suma('orderAmount')) + '</td><td class="text-end">' + num(suma('sapDocTotal')) + '</td>' +
                '<td></td><td></td><td></td></tr></tfoot>';
            var tablaId = 'import-grid-resultado-' + grupo.id;

            paneles += '<div class="tab-pane fade' + (activo ? ' show active' : '') + '" id="import-resultado-' + grupo.id + '" role="tabpanel">' +
                '<div class="import-grid-header"><strong>' + grupo.titulo + '</strong><div class="d-flex gap-2">' +
                '<button type="button" class="btn btn-sm btn-outline-secondary" data-copy-table="' + tablaId + '">Copiar</button>' +
                '<button type="button" class="btn btn-sm btn-outline-secondary" data-csv-table="' + tablaId + '" data-csv-name="pedidos-' + grupo.id + '">Descargar CSV</button>' +
                '</div></div>' +
                '<div class="document-list-table-wrapper import-grid-wrapper"><table class="table table-sm document-list-table import-grid" id="' + tablaId + '">' +
                '<thead>' + encabezado + '</thead><tbody>' +
                (cuerpo || '<tr><td colspan="' + (hayVendedor ? 12 : 11) + '" class="text-muted">Sin pedidos en este grupo.</td></tr>') +
                '</tbody>' + pie + '</table></div></div>';
        });

        resultadoDiv.innerHTML =
            '<div class="card-ps doc-tab-seccion mt-3">' +
            '<span class="doc-resumen-total-titulo">Resultado del envío a SAP</span>' +
            '<div class="alert ' + (progreso.success ? 'alert-success' : 'alert-warning') + '">' + escaparHtml(progreso.message) + '</div>' +
            (resultados.length > 0
                ? '<ul class="nav nav-tabs doc-tabs mb-2" role="tablist">' + pestanas + '</ul><div class="tab-content">' + paneles + '</div>'
                : '') +
            '</div>';
        if (resultadoDiv.scrollIntoView) {
            resultadoDiv.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    }


    // Polling del progreso hasta que el trabajo termina. onProgress se llama en cada
    // consulta; onFinished una sola vez con el progreso final (que trae el reporte).
    function seguir(progressUrl, onProgress, onFinished) {
        var detenido = false;
        function consultar() {
            if (detenido) {
                return;
            }
            fetch(progressUrl, { cache: 'no-store' })
                .then(function (respuesta) { return respuesta.ok ? respuesta.json() : null; })
                .then(function (progreso) {
                    if (!progreso) {
                        setTimeout(consultar, 3000);
                        return;
                    }
                    onProgress(progreso);
                    if (progreso.finished) {
                        detenido = true;
                        onFinished(progreso);
                        return;
                    }
                    setTimeout(consultar, 2000);
                })
                .catch(function () { setTimeout(consultar, 4000); });
        }
        consultar();
        return function () { detenido = true; };
    }

    function actualizarBarra(barra, mensaje, progreso) {
        var porcentaje = progreso.total > 0 ? Math.round((progreso.current / progreso.total) * 100) : 0;
        barra.style.width = porcentaje + '%';
        barra.textContent = porcentaje + '%';
        mensaje.textContent = progreso.message || '';
    }

    window.importacionResultado = {
        escaparHtml: escaparHtml,
        renderizar: renderizar,
        seguir: seguir,
        actualizarBarra: actualizarBarra,
    };
})();
