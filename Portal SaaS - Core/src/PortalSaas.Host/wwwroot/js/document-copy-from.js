// "Copiar de" (2026-10-29) -- panel lateral (drawer estándar) que lista los documentos
// base abiertos del socio (hoy: Pedidos de compra, en las facturas de Modulo.Compras) y
// trae sus líneas pendientes al editor de líneas, vinculadas a su documento base
// (BaseType/BaseEntry/BaseLine ocultos en cada fila). Genérico a propósito: Ventas/
// Inventario lo pueden cablear con sus propios handlers (regla de paridad entre motores).
// Qué se puede copiar lo decide SIEMPRE el servidor (misma moneda, mismo tipo de línea,
// mismo socio) y lo vuelve a validar al crear contra lo que sigue pendiente en SAP.
//
// Las líneas llegan con las claves de LineInput en camelCase (itemCode, quantity,
// baseEntry...) y se vuelcan en el campo cuyo name termina en ".ItemCode", ".Quantity"...
(function () {
    window.wireDocumentCopyFrom = function (options) {
        var boton = document.getElementById(options.buttonId);
        var drawer = document.getElementById(options.drawerId);
        var socio = document.querySelector(options.partnerInputSelector);
        var cuerpoLineas = document.getElementById(options.linesBodyId);
        var estado = document.getElementById(options.statusId);
        if (!boton || !drawer || !socio || !cuerpoLineas || !options.editor) {
            return;
        }

        var tablaPedidos = drawer.querySelector('[data-copy-orders]');
        var mensaje = drawer.querySelector('[data-copy-message]');
        var botonCopiar = drawer.querySelector('[data-copy-confirm]');

        function avisar(texto) {
            if (estado) {
                estado.textContent = texto || '';
            }
        }

        function avisarEnPanel(texto) {
            mensaje.textContent = texto || '';
            mensaje.hidden = !texto;
        }

        function filasCopiadas() {
            return Array.prototype.filter.call(cuerpoLineas.querySelectorAll('tr'), function (fila) {
                var base = fila.querySelector('[name$=".BaseEntry"]');
                return base && base.value;
            });
        }

        // Regla del dueño (2026-10-29): no se copia si hay líneas digitadas a mano (ver
        // hasManualLines en document-lines-editor.js).
        var avisoManuales = 'El documento tiene líneas digitadas: vacíalas antes de copiar.';

        // Socio de las líneas copiadas (también las que vienen de un borrador): si cambia,
        // el vínculo con el documento base deja de ser válido y esas líneas se quitan.
        var socioCopiado = filasCopiadas().length ? socio.value.trim() : null;

        function actualizarBoton() {
            boton.disabled = !socio.value.trim();
        }

        socio.addEventListener('input', actualizarBoton);
        socio.addEventListener('change', function () {
            actualizarBoton();
            if (socioCopiado && socio.value.trim() !== socioCopiado) {
                var copiadas = filasCopiadas();
                copiadas.forEach(function (fila) { fila.remove(); });
                options.editor.reindex();
                options.editor.recalculate();
                socioCopiado = null;
                if (copiadas.length) {
                    avisar('Se quitaron las líneas copiadas: eran de un documento de otro socio.');
                }
            }
        });
        actualizarBoton();

        function celda(fila, texto, clase) {
            var td = document.createElement('td');
            td.textContent = texto == null ? '' : texto;
            if (clase) {
                td.className = clase;
            }
            fila.appendChild(td);
        }

        function pintarPedidos(pedidos) {
            tablaPedidos.innerHTML = '';
            if (!pedidos.length) {
                avisarEnPanel('El socio no tiene documentos abiertos con líneas pendientes.');
                return;
            }
            var formato = { minimumFractionDigits: 2, maximumFractionDigits: 2 };
            pedidos.forEach(function (p) {
                var fila = document.createElement('tr');
                var tdCheck = document.createElement('td');
                var check = document.createElement('input');
                check.type = 'checkbox';
                check.className = 'form-check-input';
                check.value = p.docEntry;
                check.setAttribute('aria-label', 'Copiar ' + p.docNum);
                tdCheck.appendChild(check);
                fila.appendChild(tdCheck);
                celda(fila, p.docNum);
                celda(fila, p.docDate, 'text-nowrap');
                celda(fila, p.supplierReferenceNumber || '-');
                celda(fila, p.lineType);
                celda(fila, p.docCurrency);
                celda(fila, Number(p.docTotal).toLocaleString('es-CL', formato), 'text-end');
                celda(fila, p.openLineCount, 'text-end');
                tablaPedidos.appendChild(fila);
            });
        }

        boton.addEventListener('click', function () {
            avisarEnPanel('');
            if (options.editor.hasManualLines()) {
                tablaPedidos.innerHTML = '';
                avisarEnPanel(avisoManuales);
                return;
            }
            tablaPedidos.innerHTML = '<tr><td colspan="8" class="text-muted">Cargando…</td></tr>';
            fetch('?handler=' + options.ordersHandler + '&cardCode=' + encodeURIComponent(socio.value.trim()))
                .then(function (r) {
                    if (!r.ok) {
                        throw new Error('HTTP ' + r.status);
                    }
                    return r.json();
                })
                .then(pintarPedidos)
                .catch(function (error) {
                    console.error('copiar de: listado', error);
                    tablaPedidos.innerHTML = '';
                    avisarEnPanel('No se pudieron leer los documentos abiertos.');
                });
        });

        function seleccionar(id, valor) {
            var select = document.getElementById(id);
            if (!select || valor == null || valor === '') {
                return;
            }
            var existe = Array.prototype.some.call(select.options, function (o) { return o.value === String(valor); });
            if (existe && select.value !== String(valor)) {
                select.value = String(valor);
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }

        function aPascal(clave) {
            return clave.charAt(0).toUpperCase() + clave.slice(1);
        }

        function llenarFila(fila, linea) {
            Object.keys(linea).forEach(function (clave) {
                var campo = fila.querySelector('[name$=".' + aPascal(clave) + '"]');
                if (campo) {
                    campo.value = linea[clave] == null ? '' : linea[clave];
                }
            });
            var etiquetaMoneda = fila.querySelector('.line-currency-label');
            if (etiquetaMoneda) {
                etiquetaMoneda.textContent = linea.currency || '';
            }
            var descripcion = fila.querySelector('.line-item-description');
            if (descripcion) {
                descripcion.textContent = linea.description || '';
                descripcion.title = linea.description || '';
            }
            // El artículo queda fijo: cambiarlo rompería el vínculo con la línea base.
            var articulo = fila.querySelector('[name$=".ItemCode"]');
            if (articulo) {
                articulo.readOnly = true;
            }
            var marca = fila.querySelector('.line-base-badge');
            if (marca) {
                marca.textContent = options.baseLabel + ' ' + linea.baseDocNum;
                marca.hidden = false;
            }
        }

        botonCopiar.addEventListener('click', function () {
            var elegidos = Array.prototype.map.call(tablaPedidos.querySelectorAll('input[type=checkbox]:checked'), function (c) { return c.value; });
            if (!elegidos.length) {
                avisarEnPanel('Elige al menos un documento.');
                return;
            }
            var consulta = '?handler=' + options.linesHandler + '&cardCode=' + encodeURIComponent(socio.value.trim())
                + elegidos.map(function (d) { return '&docEntries=' + encodeURIComponent(d); }).join('');
            fetch(consulta)
                .then(function (r) {
                    if (!r.ok) {
                        throw new Error('HTTP ' + r.status);
                    }
                    return r.json();
                })
                .then(function (respuesta) {
                    if (respuesta.error) {
                        avisarEnPanel(respuesta.error);
                        return;
                    }

                    if (options.editor.hasManualLines()) {
                        avisarEnPanel(avisoManuales);
                        return;
                    }

                    var tipo = document.getElementById(options.lineTypeSelectId);
                    var tipoNuevo = respuesta.lineType === 'Service' ? options.serviceLineTypeValue : options.itemLineTypeValue;
                    if (tipo && tipo.value !== tipoNuevo) {
                        if (filasCopiadas().length) {
                            avisarEnPanel('El documento ya tiene líneas de otro tipo: vacíalas antes de copiar este documento.');
                            return;
                        }
                        tipo.value = tipoNuevo;
                        tipo.dispatchEvent(new Event('change', { bubbles: true }));
                    }

                    seleccionar(options.currencySelectId, respuesta.docCurrency);
                    var comprador = document.getElementById(options.buyerSelectId);
                    if (comprador && !comprador.value) {
                        seleccionar(options.buyerSelectId, respuesta.buyerCode);
                    }
                    var comentarios = document.getElementById(options.commentsId);
                    if (comentarios && respuesta.comment && comentarios.value.indexOf(respuesta.comment) < 0) {
                        comentarios.value = comentarios.value.trim() ? comentarios.value.trim() + '\n' + respuesta.comment : respuesta.comment;
                    }

                    // Una línea base ya copiada no se vuelve a traer.
                    var presentes = {};
                    filasCopiadas().forEach(function (fila) {
                        presentes[fila.querySelector('[name$=".BaseEntry"]').value + ':' + fila.querySelector('[name$=".BaseLine"]').value] = true;
                    });
                    var copiadas = 0;
                    respuesta.lines.forEach(function (linea) {
                        if (presentes[linea.baseEntry + ':' + linea.baseLine]) {
                            return;
                        }
                        llenarFila(options.editor.appendRow(), linea);
                        copiadas++;
                    });
                    options.editor.reindex();
                    options.editor.recalculate();
                    socioCopiado = socio.value.trim();
                    drawer.querySelector('[data-drawer-close]').click();
                    avisar(copiadas
                        ? 'Se copiaron ' + copiadas + ' línea(s). Puedes bajar la cantidad para facturar una parte.'
                        : 'Esas líneas ya estaban copiadas.');
                })
                .catch(function (error) {
                    console.error('copiar de: líneas', error);
                    avisarEnPanel('No se pudieron leer las líneas pendientes.');
                });
        });
    };
})();
