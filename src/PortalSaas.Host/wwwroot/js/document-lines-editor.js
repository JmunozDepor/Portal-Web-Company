// Editor de líneas de documento compartido por los 3 motores genéricos
// (Venta/Compra/Inventario) -- "+ Agregar línea"/"Quitar"/"Vaciar todas las líneas"
// + recálculo en vivo del Resumen total + plantilla/importación Excel, portado del
// comportamiento real de _TabContenido.cshtml (referencia-original/PortalSAP_v2).
//
// A diferencia del original, la importación corre 100% en el cliente (sin
// round-trip al servidor, vía SheetJS vendorizado -- ver wwwroot/lib/sheetjs, mismo
// criterio ya usado en Modulo.CierreMensual): no hay validación contra catálogos SAP
// (existencia de artículo, nombre resuelto) -- el usuario corrige a mano si un código
// no existe, recién al enviar el formulario (mismo error que ya devuelve el motor
// genérico al guardar). Esto es una simplificación deliberada frente al importador
// del original (que sí valida servidor-side fila por fila) para no tener que portar
// IImportadorLineasDocumentoService todavía -- ver CLAUDE.md, brecha pendiente.
//
// Era CSV hasta el 2026-08-02 (mismo criterio 100% cliente) -- reemplazado por Excel
// real a pedido del dueño del proyecto, para que el importador de líneas coincida con
// el formato de todos los demás importadores del portal (Modulo.ImportacionGenerica).
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

        // ---- Excel (SheetJS): plantilla + importación (cliente, sin validación de catálogo) ----
        if (!options.csvColumns) {
            return;
        }

        var botonDescargar = document.getElementById(options.downloadTemplateButtonId);
        if (botonDescargar) {
            botonDescargar.addEventListener('click', function () {
                var hoja = XLSX.utils.aoa_to_sheet([options.csvColumns]);
                var libro = XLSX.utils.book_new();
                XLSX.utils.book_append_sheet(libro, hoja, 'Líneas');
                XLSX.writeFile(libro, 'plantilla-lineas.xlsx');
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
                    var libro = XLSX.read(new Uint8Array(lector.result), { type: 'array' });
                    var hoja = libro.Sheets[libro.SheetNames[0]];
                    // defval: '' -- una celda vacía en medio de una fila no debe quedar
                    // "undefined" (sheet_to_json omite la propiedad si la celda está
                    // realmente vacía), sino string vacío, igual criterio que el parser
                    // CSV anterior (siempre asignaba algo, nunca undefined).
                    var filas = XLSX.utils.sheet_to_json(hoja, { defval: '' });

                    // Antes de volcar las líneas importadas, saca las filas vacías que
                    // ya estaban en la tabla (la fila en blanco con la que arranca todo
                    // documento nuevo) -- nunca toca una fila que ya tiene un artículo
                    // cargado.
                    if (filas.length > 0) {
                        Array.prototype.slice.call(cuerpo.querySelectorAll('tr')).forEach(function (fila) {
                            var campoArticulo = fila.querySelector(options.itemCodeSelector);
                            if (campoArticulo && !campoArticulo.value.trim()) {
                                fila.remove();
                            }
                        });
                    }

                    filas.forEach(function (filaExcel) {
                        var filaNueva = crearFilaDesdePlantilla();
                        cuerpo.appendChild(filaNueva);
                        options.csvColumns.forEach(function (columna) {
                            var input = filaNueva.querySelector('[name$=".' + columna + '"]');
                            if (input && filaExcel[columna] !== undefined) {
                                input.value = filaExcel[columna];
                            }
                        });
                    });

                    aplicarTipoLinea();
                    recalcularTotales();
                    inputArchivo.value = '';

                    if (divResultado) {
                        divResultado.innerHTML = '<p class="alert alert-success py-1 px-2">'
                            + filas.length + ' línea(s) importada(s) desde el Excel. Revisá los códigos de artículo/catálogo antes de guardar -- se validan recién al enviar el formulario.</p>';
                    }
                };
                lector.readAsArrayBuffer(archivo);
            });
        }
    };
})();
