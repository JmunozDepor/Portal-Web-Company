// Editor de líneas de documento compartido por los 3 motores genéricos
// (Venta/Compra/Inventario) -- "+ Agregar línea"/"Quitar"/"Vaciar todas las líneas"
// + recálculo en vivo del Resumen total + plantilla/importación Excel, portado del
// comportamiento real de _TabContenido.cshtml (referencia-original/PortalSAP_v2).
//
// El parseo del Excel corre 100% en el cliente (sin round-trip al servidor para leer
// el archivo, vía SheetJS vendorizado -- ver wwwroot/lib/sheetjs, mismo criterio ya
// usado en Modulo.CierreMensual). Después de volcar las filas SÍ se valida (ver
// validarFilasImportadas, agregado 2026-08-11): catálogos chicos (Almacén/Cuenta
// Mayor/Centro de Costos) contra el <datalist> ya precargado, Artículo contra el
// catálogo real vía OnGetValidateItemCodesAsync (un solo round-trip con todos los
// códigos distintos, mismo mecanismo que Modulo.ImportacionGenerica), y Cantidad
// numérica > 0. Sigue sin portar IImportadorLineasDocumentoService completo (reglas
// de negocio más finas del original) -- ver CLAUDE.md, brecha pendiente.
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

        // Valor elegido en "Cambio masivo" por campo (selector -> código) -- las filas
        // nuevas (+ Agregar línea / Importar Excel sin esa columna) nacen con él.
        var valoresMasivos = {};

        function crearFilaDesdePlantilla() {
            var indice = siguienteIndice;
            var html = plantilla.innerHTML.split('__INDEX__').join(indice);
            var contenedor = document.createElement('tbody');
            contenedor.innerHTML = html;
            siguienteIndice++;
            var fila = contenedor.firstElementChild;
            Object.keys(valoresMasivos).forEach(function (selector) {
                var campo = fila.querySelector(selector);
                if (campo && !campo.value) {
                    campo.value = valoresMasivos[selector];
                }
            });
            return fila;
        }

        // ---- Cambio masivo de bodega (2026-10-20) -- una barra arriba de la grilla con
        // un buscador por campo de bodega (Venta/Compra: Almacén; Inventario: Almacén
        // origen + Almacén destino) y "Aplicar a todas": reemplaza ese campo en TODAS las
        // líneas y queda como valor por defecto de las líneas nuevas. Reusa el <datalist>
        // del catálogo ya precargado (catálogo chico, minChars: 0) para sugerir y validar
        // el código. Si el documento valida stock, se revalida enseguida (la bodega cambia
        // el disponible). La barra la arma este script: la vista solo pasa los campos.
        //   bulkWarehouses: [{ label, selector, datalistId, itemOnly }]
        //   itemOnly: true = se oculta en documentos de Servicio (no llevan almacén).
        var barraMasiva = null;
        if (options.bulkWarehouses && options.bulkWarehouses.length) {
            barraMasiva = document.createElement('div');
            barraMasiva.className = 'line-bulk-bar';
            var titulo = document.createElement('span');
            titulo.className = 'line-bulk-title';
            titulo.innerHTML = '<i class="bi bi-arrow-repeat"></i> Cambio masivo';
            barraMasiva.appendChild(titulo);

            var estadoMasivo = document.createElement('span');
            estadoMasivo.className = 'line-bulk-status';

            options.bulkWarehouses.forEach(function (campoMasivo) {
                var grupo = document.createElement('div');
                grupo.className = 'line-bulk-group';
                if (campoMasivo.itemOnly) {
                    grupo.setAttribute('data-item-only', '');
                }

                var etiqueta = document.createElement('label');
                etiqueta.textContent = campoMasivo.label;
                var entrada = document.createElement('input');
                entrada.className = 'form-control form-control-sm';
                entrada.setAttribute('list', campoMasivo.datalistId);
                entrada.setAttribute('placeholder', 'Buscar…');
                entrada.setAttribute('autocomplete', 'off');
                entrada.setAttribute('aria-label', campoMasivo.label + ' para todas las líneas');
                var boton = document.createElement('button');
                boton.type = 'button';
                boton.className = 'btn btn-sm btn-outline-secondary';
                boton.textContent = 'Aplicar a todas';

                function aplicar() {
                    var codigo = entrada.value.trim();
                    limpiarInvalido(entrada);
                    if (!codigo) {
                        marcarInvalido(entrada, 'Elegí una bodega.');
                        return;
                    }
                    var datalist = document.getElementById(campoMasivo.datalistId);
                    var opcion = datalist && Array.prototype.find.call(datalist.options, function (o) {
                        return o.value.trim().toUpperCase() === codigo.toUpperCase();
                    });
                    if (datalist && datalist.options.length > 0 && !opcion) {
                        marcarInvalido(entrada, 'Bodega no encontrada en el catálogo.');
                        estadoMasivo.textContent = 'La bodega "' + codigo + '" no existe.';
                        return;
                    }
                    if (opcion) {
                        codigo = opcion.value;
                        entrada.value = codigo;
                    }

                    var cambiadas = 0;
                    cuerpo.querySelectorAll(campoMasivo.selector).forEach(function (campo) {
                        if (campo.disabled) {
                            return;
                        }
                        campo.value = codigo;
                        limpiarInvalido(campo);
                        campo.dispatchEvent(new Event('input', { bubbles: true }));
                        campo.dispatchEvent(new Event('change', { bubbles: true }));
                        cambiadas++;
                    });
                    valoresMasivos[campoMasivo.selector] = codigo;
                    estadoMasivo.textContent = campoMasivo.label + ' "' + codigo + '" aplicado a ' + cambiadas + ' línea(s).';

                    if (options.lineRestrictions && cambiadas > 0) {
                        validarRestricciones([]).then(function (errores) {
                            var divResultadoMasivo = document.getElementById(options.csvResultId);
                            if (!divResultadoMasivo) {
                                return;
                            }
                            divResultadoMasivo.innerHTML = errores.length === 0
                                ? '<p class="alert alert-success py-1 px-2">Stock y precio validados con la nueva bodega -- sin problemas.</p>'
                                : '<p class="alert alert-warning py-1 px-2 mb-1">' + errores.length + ' línea(s) con problema(s) con la nueva bodega:</p>'
                                    + '<ul class="alert alert-warning py-1 px-2 mb-0">' + errores.map(function (e) { return '<li>' + e + '</li>'; }).join('') + '</ul>';
                        });
                    }
                }

                boton.addEventListener('click', aplicar);
                entrada.addEventListener('keydown', function (e) {
                    if (e.key === 'Enter') {
                        e.preventDefault(); // dentro del <form> de creación: no enviar el documento
                        aplicar();
                    }
                });

                grupo.appendChild(etiqueta);
                grupo.appendChild(entrada);
                grupo.appendChild(boton);
                barraMasiva.appendChild(grupo);
            });

            barraMasiva.appendChild(estadoMasivo);
            var anclaGrilla = cuerpo.closest('.line-items-wrapper') || cuerpo.closest('table');
            anclaGrilla.parentNode.insertBefore(barraMasiva, anclaGrilla);
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

            // Además de deshabilitar, las columnas del otro tipo se OCULTAN (encabezado y
            // celdas) vía la clase de la tabla -- antes quedaban visibles con "Buscar…"
            // deshabilitado, ensanchando la grilla sin aportar nada. La vista ya la
            // renderiza con la clase correcta (sin parpadeo, y sirve en solo-lectura).
            var tabla = cuerpo.closest('table');
            if (tabla) {
                tabla.classList.toggle('line-type-item', esArticulo);
                tabla.classList.toggle('line-type-service', !esArticulo);
            }
            if (barraMasiva) {
                barraMasiva.querySelectorAll('[data-item-only]').forEach(function (g) { g.hidden = !esArticulo; });
                barraMasiva.hidden = !Array.prototype.some.call(barraMasiva.querySelectorAll('.line-bulk-group'), function (g) { return !g.hidden; });
            }

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

        // Nombre resuelto del artículo, informativo -- apenas el texto tipeado coincide
        // EXACTO con un código ya sugerido por el datalist (alcanza con mirar las
        // <option> ya cargadas por wireCatalogSearch). Va a la columna Descripción de la
        // misma fila (.line-item-description: un <span> de solo lectura en Venta/Compra,
        // o el <input> de Descripción en Inventario, solo si estaba vacío o lo había
        // llenado esto mismo). Antes se insertaba un <small> DEBAJO del código, lo que
        // agrandaba la fila y desalineaba el resto de las columnas.
        if (options.itemCodeSelector && options.datalistId) {
            document.addEventListener('input', function (evento) {
                if (!evento.target.matches(options.itemCodeSelector)) {
                    return;
                }
                var input = evento.target;
                var datalist = document.getElementById(options.datalistId);
                var opcion = datalist && Array.prototype.find.call(datalist.options, function (o) { return o.value === input.value; });
                var nombre = opcion ? opcion.label.split(' — ')[1] || '' : '';

                var destino = input.closest('tr').querySelector('.line-item-description');
                if (!destino) {
                    return;
                }
                if (destino.tagName === 'INPUT') {
                    if (!destino.value || destino.dataset.autoName === '1') {
                        destino.value = nombre;
                        destino.dataset.autoName = '1';
                    }
                } else {
                    destino.textContent = nombre;
                    destino.title = nombre;
                }
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

                    var filasCreadas = [];
                    filas.forEach(function (filaExcel) {
                        var filaNueva = crearFilaDesdePlantilla();
                        cuerpo.appendChild(filaNueva);
                        filasCreadas.push(filaNueva);
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
                        divResultado.innerHTML = '<p class="alert alert-info py-1 px-2">Validando ' + filas.length + ' línea(s) importada(s)…</p>';
                    }
                    validarFilasImportadas(filasCreadas).then(validarRestricciones).then(function (errores) {
                        if (!divResultado) {
                            return;
                        }
                        if (errores.length === 0) {
                            divResultado.innerHTML = '<p class="alert alert-success py-1 px-2">'
                                + filas.length + ' línea(s) importada(s) desde el Excel -- todas válidas.</p>';
                            return;
                        }
                        var listaErrores = errores.map(function (e) { return '<li>' + e + '</li>'; }).join('');
                        divResultado.innerHTML = '<p class="alert alert-warning py-1 px-2 mb-1">'
                            + filas.length + ' línea(s) importada(s), ' + errores.length + ' con problema(s) -- revisá los campos marcados en rojo:</p>'
                            + '<ul class="alert alert-warning py-1 px-2 mb-0">' + listaErrores + '</ul>';
                    });
                };
                lector.readAsArrayBuffer(archivo);
            });
        }

        // ---- Validación post-importación: existencia de Artículo (server, catálogo
        // grande) + catálogos chicos ya precargados (Almacén/Cuenta Mayor/Centro de
        // Costos, cliente) + Cantidad numérica > 0 -- mismo criterio de "validar
        // correcto o con problema" que ya usa Modulo.ImportacionGenerica, para no
        // dejar que un código mal tipeado se descubra recién al enviar el documento
        // completo o, peor, en el rechazo de SAP Service Layer.
        // Los mensajes de error citan valores tal cual vinieron del Excel importado
        // (código de artículo, almacén, etc.) -- nunca confiar en ese contenido al
        // insertarlo vía innerHTML (ver divResultado más abajo), por eso se escapa acá
        // antes de concatenar.
        function escaparHtml(texto) {
            var div = document.createElement('div');
            div.textContent = texto;
            return div.innerHTML;
        }

        function marcarInvalido(input, mensaje) {
            input.classList.add('is-invalid');
            input.title = mensaje;
        }

        function limpiarInvalido(input) {
            input.classList.remove('is-invalid');
            input.removeAttribute('title');
        }

        function obtenerCodigosDatalist(datalistId) {
            var datalist = document.getElementById(datalistId);
            if (!datalist) {
                return null;
            }
            var set = {};
            Array.prototype.forEach.call(datalist.options, function (o) {
                set[o.value.trim().toUpperCase()] = true;
            });
            return set;
        }

        function validarFilasImportadas(filasCreadas) {
            var errores = [];

            filasCreadas.forEach(function (fila, indice) {
                var etiquetaFila = 'Línea ' + (indice + 1);
                var campoArticulo = options.itemCodeSelector ? fila.querySelector(options.itemCodeSelector) : null;
                if (campoArticulo) {
                    etiquetaFila += campoArticulo.value ? ' (Artículo ' + escaparHtml(campoArticulo.value) + ')' : '';
                }

                // Cantidad -- el nombre del campo siempre es "Quantity" en los 3 motores
                // (Venta/Compra/Inventario), aunque la clase CSS varíe.
                var inputCantidad = fila.querySelector('[name$=".Quantity"]');
                if (inputCantidad && !inputCantidad.disabled) {
                    limpiarInvalido(inputCantidad);
                    var cantidad = parseFloat(inputCantidad.value);
                    if (!inputCantidad.value || isNaN(cantidad) || cantidad <= 0) {
                        marcarInvalido(inputCantidad, 'La cantidad debe ser un número mayor a 0.');
                        errores.push(etiquetaFila + ': cantidad inválida ("' + escaparHtml(inputCantidad.value) + '").');
                    }
                }

                // Cualquier campo con list="..." salvo el de Artículo (catálogo grande,
                // se valida server-side aparte) es un catálogo chico que ya está
                // precargado completo en su <datalist> (minChars: 0, ver
                // catalog-search.js) -- alcanza con comparar contra las <option> ya
                // cargadas, sin ida y vuelta al servidor.
                fila.querySelectorAll('input[list]').forEach(function (input) {
                    if (input.disabled || (campoArticulo && input === campoArticulo)) {
                        return;
                    }
                    limpiarInvalido(input);
                    var valor = input.value.trim();
                    if (!valor) {
                        return;
                    }
                    var codigos = obtenerCodigosDatalist(input.getAttribute('list'));
                    if (codigos && !codigos[valor.toUpperCase()]) {
                        marcarInvalido(input, 'Código no encontrado en el catálogo.');
                        errores.push(etiquetaFila + ': "' + escaparHtml(valor) + '" no existe en el catálogo (' + escaparHtml(input.name) + ').');
                    }
                });
            });

            if (!options.itemCodeSelector) {
                return Promise.resolve(errores);
            }

            var camposArticulo = filasCreadas
                .map(function (fila) { return fila.querySelector(options.itemCodeSelector); })
                .filter(function (input) { return input && !input.disabled && input.value.trim(); });

            if (camposArticulo.length === 0) {
                return Promise.resolve(errores);
            }

            var codigosDistintos = [];
            camposArticulo.forEach(function (input) {
                var codigo = input.value.trim();
                if (codigosDistintos.indexOf(codigo) === -1) {
                    codigosDistintos.push(codigo);
                }
            });

            var query = '?handler=ValidateItemCodes&' + codigosDistintos.map(function (c) {
                return 'codes=' + encodeURIComponent(c);
            }).join('&');

            return fetch(query)
                .then(function (r) {
                    if (!r.ok) {
                        throw new Error('HTTP ' + r.status + ' validando códigos de artículo');
                    }
                    return r.json();
                })
                .then(function (encontrados) {
                    var nombresPorCodigo = {};
                    encontrados.forEach(function (item) {
                        nombresPorCodigo[item.itemCode.trim().toUpperCase()] = item.itemName;
                    });

                    camposArticulo.forEach(function (input) {
                        var codigo = input.value.trim();
                        var nombre = nombresPorCodigo[codigo.toUpperCase()];
                        if (nombre) {
                            // Nombre a la columna Descripción de la fila (nunca debajo del
                            // código: agrandaba la fila y desalineaba la grilla).
                            var destino = input.closest('tr').querySelector('.line-item-description');
                            if (destino && destino.tagName === 'INPUT') {
                                if (!destino.value || destino.dataset.autoName === '1') {
                                    destino.value = nombre;
                                    destino.dataset.autoName = '1';
                                }
                            } else if (destino) {
                                destino.textContent = nombre;
                                destino.title = nombre;
                            }
                        } else {
                            marcarInvalido(input, 'Artículo no encontrado en el catálogo.');
                            errores.push('Artículo "' + escaparHtml(codigo) + '": no existe en el catálogo.');
                        }
                    });

                    return errores;
                })
                .catch(function (error) {
                    console.error('document-lines-editor: validación de artículos falló', error);
                    errores.push('No se pudo validar la existencia de los artículos contra el catálogo (revisá la consola) -- se validarán igual al enviar el formulario.');
                    return errores;
                });
        }

        // ---- Restricciones de stock / precio (2026-10-20) -- mismo criterio que las
        // reglas de Importación Genérica, siempre bloqueantes (ver
        // IDocumentLineValidationService). Se valida la grilla COMPLETA, no solo lo
        // importado: el stock es por demanda agregada de todas las líneas del mismo
        // artículo y bodega. Qué se valida (stock/precio) lo decide el servidor según el
        // motor y el tipo de documento; al crear se vuelve a validar allá igual.
        function validarRestricciones(errores) {
            var cfg = options.lineRestrictions;
            if (!cfg) {
                return Promise.resolve(errores);
            }

            var filas = Array.prototype.slice.call(cuerpo.querySelectorAll('tr'));
            var lineas = [];
            filas.forEach(function (fila, indice) {
                var articulo = fila.querySelector(options.itemCodeSelector);
                if (!articulo || articulo.disabled || !articulo.value.trim()) {
                    return;
                }
                var cantidad = fila.querySelector('[name$=".Quantity"]');
                var precio = fila.querySelector('[name$=".UnitPrice"]');
                var bodega = cfg.warehouseSelector ? fila.querySelector(cfg.warehouseSelector) : null;
                var moneda = fila.querySelector('.line-currency');
                [cantidad, precio].forEach(function (campo) {
                    if (campo && campo.dataset.restriction === '1') {
                        limpiarInvalido(campo);
                        delete campo.dataset.restriction;
                    }
                });
                lineas.push({
                    key: indice,
                    itemCode: articulo.value.trim(),
                    warehouse: bodega ? bodega.value.trim() : null,
                    quantity: cantidad && cantidad.value ? parseFloat(cantidad.value) : null,
                    unitPrice: precio && precio.value ? parseFloat(precio.value) : null,
                    currency: moneda && moneda.value ? moneda.value : null,
                });
            });
            if (lineas.length === 0) {
                return Promise.resolve(errores);
            }

            var cabecera = cfg.getHeader ? cfg.getHeader() || {} : {};
            var formulario = cuerpo.closest('form');
            var campoToken = formulario ? formulario.querySelector('input[name="__RequestVerificationToken"]') : null;
            var panelLayout = document.querySelector('.doc-form-layout-panel');
            var token = campoToken ? campoToken.value : (panelLayout ? panelLayout.dataset.token : '');

            return fetch('?handler=' + (cfg.handlerName || 'ValidateLines'), {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({
                    cardCode: cabecera.cardCode || null,
                    priceList: cabecera.priceList ? parseInt(cabecera.priceList, 10) : null,
                    date: cabecera.date || null,
                    lines: lineas,
                }),
            })
                .then(function (r) {
                    if (!r.ok) {
                        throw new Error('HTTP ' + r.status + ' validando stock/precio');
                    }
                    return r.json();
                })
                .then(function (problemas) {
                    problemas.forEach(function (p) {
                        var fila = filas[p.key];
                        var campo = fila ? fila.querySelector(p.field === 'UnitPrice' ? '[name$=".UnitPrice"]' : '[name$=".Quantity"]') : null;
                        if (campo) {
                            marcarInvalido(campo, p.message);
                            campo.dataset.restriction = '1';
                        }
                        errores.push(escaparHtml(p.message));
                    });
                    return errores;
                })
                .catch(function (error) {
                    console.error('document-lines-editor: validación de stock/precio falló', error);
                    errores.push('No se pudo validar stock/precio (revisá la consola) -- se validará igual al crear el documento.');
                    return errores;
                });
        }
    };
})();
