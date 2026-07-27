// Editor de líneas de documento compartido por los 3 motores genéricos
// (Venta/Compra/Inventario) -- "+ Agregar línea"/"Quitar"/"Vaciar todas las líneas"
// + recálculo en vivo del Resumen total + plantilla/importación CSV, portado del
// comportamiento real de _TabContenido.cshtml (referencia-original/PortalSAP_v2).
//
// A diferencia del original, la importación CSV corre 100% en el cliente (sin
// round-trip al servidor): no hay validación contra catálogos SAP (existencia de
// artículo, nombre resuelto) -- el usuario corrige a mano si un código no existe,
// recién al enviar el formulario (mismo error que ya devuelve el motor genérico al
// guardar). Esto es una simplificación deliberada frente al importador del original
// (que sí valida servidor-side fila por fila) para no tener que portar
// IImportadorLineasDocumentoService todavía -- ver CLAUDE.md, brecha pendiente.
(function () {
    window.wireDocumentLinesEditor = function (options) {
        var cuerpo = document.getElementById(options.tableBodyId);
        var plantilla = document.getElementById(options.templateId);
        var siguienteIndice = options.startIndex;

        function recalcularTotales() {
            if (!options.totals) {
                return;
            }
            var totalAntesDescuento = 0;
            var descuento = 0;
            cuerpo.querySelectorAll('tr').forEach(function (fila) {
                var inputCantidad = fila.querySelector(options.totals.qtySelector);
                var inputPrecio = fila.querySelector(options.totals.priceSelector);
                var inputDescuento = fila.querySelector(options.totals.discountSelector);
                var cantidad = inputCantidad ? parseFloat(inputCantidad.value) || 0 : 0;
                var precio = inputPrecio ? parseFloat(inputPrecio.value) || 0 : 0;
                var descuentoPct = inputDescuento ? parseFloat(inputDescuento.value) || 0 : 0;
                totalAntesDescuento += cantidad * precio;
                descuento += cantidad * precio * descuentoPct / 100;
            });

            var formato = { minimumFractionDigits: 2, maximumFractionDigits: 2 };
            var spanAntes = document.getElementById(options.totals.totalBeforeDiscountId);
            var spanDescuento = document.getElementById(options.totals.discountId);
            var spanTotal = document.getElementById(options.totals.documentTotalId);
            if (spanAntes) { spanAntes.textContent = totalAntesDescuento.toLocaleString('es-CL', formato); }
            if (spanDescuento) { spanDescuento.textContent = descuento.toLocaleString('es-CL', formato); }
            if (spanTotal) { spanTotal.textContent = (totalAntesDescuento - descuento).toLocaleString('es-CL', formato); }
        }

        if (options.totals) {
            cuerpo.addEventListener('input', function (evento) {
                if (evento.target.matches(options.totals.qtySelector + ', ' + options.totals.priceSelector + ', ' + options.totals.discountSelector)) {
                    recalcularTotales();
                }
            });
        }

        function crearFilaDesdePlantilla() {
            var indice = siguienteIndice;
            var html = plantilla.innerHTML.split('__INDEX__').join(indice);
            var contenedor = document.createElement('tbody');
            contenedor.innerHTML = html;
            siguienteIndice++;
            return contenedor.firstElementChild;
        }

        // El Tipo (Artículo/Servicio) es del documento entero, no por línea -- SAP no
        // permite mezclar artículos y servicios en un mismo documento. El selector vive
        // en la tab General (Input.LineType); acá solo se habilitan/deshabilitan las
        // columnas que correspondan a cada tipo -- mismo criterio que
        // aplicarTipoDocumento() del original (referencia-original/PortalSAP_v2,
        // _TabContenido.cshtml). Un input disabled no viaja en el POST, así que la
        // celda que no corresponde directamente no se postea.
        function aplicarTipoLinea() {
            if (!options.lineType) {
                return;
            }
            var select = document.getElementById(options.lineType.selectId);
            var esArticulo = !select || select.value === options.lineType.itemValue;

            cuerpo.querySelectorAll('tr').forEach(function (fila) {
                fila.querySelectorAll(options.lineType.itemOnlySelector + ' input, ' + options.lineType.itemOnlySelector + ' select').forEach(function (el) {
                    el.disabled = !esArticulo;
                });
                fila.querySelectorAll(options.lineType.serviceOnlySelector + ' input, ' + options.lineType.serviceOnlySelector + ' select').forEach(function (el) {
                    el.disabled = esArticulo;
                });

                var cantidadInput = fila.querySelector(options.totals ? options.totals.qtySelector : '.line-qty');
                if (cantidadInput) {
                    cantidadInput.readOnly = !esArticulo;
                    if (!esArticulo && (!cantidadInput.value || parseFloat(cantidadInput.value) <= 0)) {
                        cantidadInput.value = '1';
                    }
                }
            });
        }

        if (options.lineType) {
            var selectTipo = document.getElementById(options.lineType.selectId);
            if (selectTipo) {
                selectTipo.addEventListener('change', aplicarTipoLinea);
            }
            aplicarTipoLinea();
        }

        // Nombre resuelto del artículo, informativo -- se muestra debajo del buscador
        // apenas el texto tipeado coincide EXACTO con un código ya sugerido por el
        // datalist (no hace falta un evento propio del navegador para <input list>,
        // alcanza con mirar las <option> ya cargadas por wireCatalogSearch).
        if (options.itemCodeSelector && options.datalistId) {
            document.addEventListener('input', function (evento) {
                if (!evento.target.matches(options.itemCodeSelector)) {
                    return;
                }
                var input = evento.target;
                var span = input.parentElement.querySelector('.line-item-resolved-name');
                if (!span) {
                    span = document.createElement('small');
                    span.className = 'text-muted line-item-resolved-name d-block';
                    input.insertAdjacentElement('afterend', span);
                }

                var datalist = document.getElementById(options.datalistId);
                var opcion = datalist && Array.prototype.find.call(datalist.options, function (o) { return o.value === input.value; });
                span.textContent = opcion ? opcion.label.split(' — ')[1] || '' : '';
            });
        }

        var botonAgregar = document.getElementById(options.addButtonId);
        if (botonAgregar) {
            botonAgregar.addEventListener('click', function () {
                cuerpo.appendChild(crearFilaDesdePlantilla());
                aplicarTipoLinea();
            });
        }

        cuerpo.addEventListener('click', function (evento) {
            if (evento.target.matches(options.removeButtonSelector)) {
                evento.target.closest('tr').remove();
                recalcularTotales();
            }
        });

        var botonVaciar = document.getElementById(options.clearButtonId);
        if (botonVaciar) {
            botonVaciar.addEventListener('click', function () {
                cuerpo.innerHTML = '';
                recalcularTotales();
            });
        }

        // ---- CSV: plantilla + importación (cliente, sin validación de catálogo) ----
        if (!options.csvColumns) {
            return;
        }

        function parsearCsv(texto) {
            var lineas = texto.replace(/^﻿/, '').split(/\r\n|\n/).filter(function (l) { return l.trim().length > 0; });
            if (lineas.length === 0) {
                return { encabezados: [], filas: [] };
            }
            var separador = lineas[0].indexOf(';') > -1 && lineas[0].indexOf(',') === -1 ? ';' : ',';
            var encabezados = lineas[0].split(separador).map(function (h) { return h.trim(); });
            var filas = lineas.slice(1).map(function (linea) {
                var valores = linea.split(separador);
                var fila = {};
                encabezados.forEach(function (encabezado, i) { fila[encabezado] = (valores[i] || '').trim(); });
                return fila;
            });
            return { encabezados: encabezados, filas: filas };
        }

        var botonDescargar = document.getElementById(options.downloadTemplateButtonId);
        if (botonDescargar) {
            botonDescargar.addEventListener('click', function () {
                var contenido = options.csvColumns.join(',') + '\r\n';
                var blob = new Blob(['﻿' + contenido], { type: 'text/csv;charset=utf-8;' });
                var enlace = document.createElement('a');
                enlace.href = URL.createObjectURL(blob);
                enlace.download = 'plantilla-lineas.csv';
                enlace.click();
                URL.revokeObjectURL(enlace.href);
            });
        }

        var botonImportar = document.getElementById(options.importCsvButtonId);
        var inputArchivo = document.getElementById(options.csvFileInputId);
        var divResultado = document.getElementById(options.csvResultId);

        if (botonImportar && inputArchivo) {
            botonImportar.addEventListener('click', function () { inputArchivo.click(); });

            inputArchivo.addEventListener('change', function () {
                var archivo = inputArchivo.files[0];
                if (!archivo) {
                    return;
                }

                var lector = new FileReader();
                lector.onload = function () {
                    var resultado = parsearCsv(String(lector.result));
                    var filasValidas = 0;

                    // Antes de volcar las líneas importadas, saca las filas vacías que
                    // ya estaban en la tabla (la fila en blanco con la que arranca todo
                    // documento nuevo) -- nunca toca una fila que ya tiene un artículo
                    // cargado.
                    if (resultado.filas.length > 0) {
                        Array.prototype.slice.call(cuerpo.querySelectorAll('tr')).forEach(function (fila) {
                            var campoArticulo = fila.querySelector(options.itemCodeSelector);
                            if (campoArticulo && !campoArticulo.value.trim()) {
                                fila.remove();
                            }
                        });
                    }

                    resultado.filas.forEach(function (filaCsv) {
                        var filaNueva = crearFilaDesdePlantilla();
                        cuerpo.appendChild(filaNueva);
                        options.csvColumns.forEach(function (columna) {
                            var input = filaNueva.querySelector('[name$=".' + columna + '"]');
                            if (input && filaCsv[columna] !== undefined) {
                                input.value = filaCsv[columna];
                            }
                        });
                        if (filaCsv[options.csvColumns[0]] && filaCsv[options.csvColumns[0]].trim()) {
                            filasValidas++;
                        }
                    });

                    aplicarTipoLinea();
                    recalcularTotales();
                    inputArchivo.value = '';

                    if (divResultado) {
                        divResultado.innerHTML = '<p class="alert alert-success py-1 px-2">'
                            + resultado.filas.length + ' línea(s) importada(s) desde el CSV. Revisá los códigos de artículo/catálogo antes de guardar -- se validan recién al enviar el formulario.</p>';
                    }
                };
                lector.readAsText(archivo, 'utf-8');
            });
        }
    };
})();
