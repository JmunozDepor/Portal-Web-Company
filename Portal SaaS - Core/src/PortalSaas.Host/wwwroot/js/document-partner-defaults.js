// Valores por defecto del socio de negocio + precio de línea, compartido por los
// formularios genéricos de Venta y Compra (mismo criterio que catalog-search.js: un
// solo script, cada motor solo pasa sus ids).
//
// Al elegir el socio llama al handler "PartnerDefaults" del PageModel y aplica (quedan
// modificables): Vendedor/Comprador, Lista de precio, Moneda y, si el motor la tiene,
// Sucursal de destino. El precio de cada línea de Artículo sale del handler
// "LinePrices": precio especial del socio o, si no tiene, el de la lista elegida, con
// la moneda en que está expresado (se postea como moneda de la línea; SAP convierte a
// la moneda del documento). Un precio autocompletado se marca con data-auto-price y se
// recalcula si cambia socio/lista/fecha; uno digitado a mano no se vuelve a pisar y
// queda en la moneda del documento.
(function () {
    window.wirePartnerDefaults = function (options) {
        document.addEventListener('DOMContentLoaded', function () {
            var partnerInput = document.querySelector(options.partnerInputSelector);
            if (!partnerInput) {
                return;
            }
            var employee = document.getElementById(options.employeeSelectId);
            var priceList = document.getElementById(options.priceListSelectId);
            var currency = document.getElementById(options.currencySelectId);
            var currencyLabel = document.getElementById(options.documentCurrencyLabelId);
            var docDate = document.getElementById(options.docDateId);
            var lineType = document.getElementById(options.lineTypeSelectId);
            var linesBody = document.getElementById(options.linesBodyId);
            var shipTo = options.shipTo ? document.getElementById(options.shipTo.selectId) : null;
            var shipToAddress = options.shipTo ? document.getElementById(options.shipTo.addressInputId) : null;

            var lastCardCode = partnerInput.value.trim();
            var settingPrice = false;

            function selectValue(select, value) {
                if (!select) {
                    return;
                }
                var text = value === null || value === undefined ? '' : String(value);
                var exists = Array.prototype.some.call(select.options, function (o) { return o.value === text; });
                select.value = exists ? text : '';
            }

            function updateCurrencyLabel() {
                if (currency && currencyLabel) {
                    currencyLabel.textContent = currency.value;
                }
            }

            function updateShipToAddress() {
                if (!shipTo || !shipToAddress) {
                    return;
                }
                var option = shipTo.options[shipTo.selectedIndex];
                shipToAddress.value = option && option.dataset.address ? option.dataset.address : '';
            }

            function renderShipTo(addresses, selected) {
                if (!shipTo) {
                    return;
                }
                shipTo.innerHTML = '';
                var empty = document.createElement('option');
                empty.value = '';
                empty.textContent = addresses.length ? 'Por defecto del cliente' : 'El cliente no tiene sucursales de despacho';
                shipTo.appendChild(empty);
                addresses.forEach(function (a) {
                    var option = document.createElement('option');
                    option.value = a.code;
                    option.textContent = a.address ? a.code + ' — ' + a.address : a.code;
                    option.dataset.address = a.address || '';
                    shipTo.appendChild(option);
                });
                selectValue(shipTo, selected);
                updateShipToAddress();
            }

            function isServiceDocument() {
                return lineType && lineType.value !== options.itemLineTypeValue;
            }

            function setLineCurrency(row, code) {
                var hidden = row.querySelector('.line-currency');
                var label = row.querySelector('.line-currency-label');
                if (hidden) {
                    hidden.value = code || '';
                }
                if (label) {
                    label.textContent = code || '';
                }
            }

            function setPrice(input, quote) {
                var row = input.closest('tr');
                settingPrice = true;
                input.value = quote ? String(quote.price) : '';
                if (quote) {
                    input.dataset.autoPrice = '1';
                    input.title = quote.source === 'special' ? 'Precio especial del socio' : 'Precio de la lista seleccionada';
                } else {
                    delete input.dataset.autoPrice;
                    input.removeAttribute('title');
                }
                setLineCurrency(row, quote ? quote.currency : null);
                input.dispatchEvent(new Event('input', { bubbles: true }));
                settingPrice = false;
            }

            // Solo se tocan filas con precio vacío o autocompletado.
            function repriceRows(rows) {
                if (!linesBody || isServiceDocument()) {
                    return;
                }
                var targets = rows.map(function (row) {
                    return { code: row.querySelector(options.itemCodeSelector), price: row.querySelector('.line-price') };
                }).filter(function (t) {
                    return t.code && t.price && t.code.value.trim() && (!t.price.value || t.price.dataset.autoPrice === '1');
                });
                if (targets.length === 0) {
                    return;
                }

                var query = '?handler=LinePrices'
                    + '&cardCode=' + encodeURIComponent(partnerInput.value.trim())
                    + '&priceList=' + encodeURIComponent(priceList ? priceList.value : '')
                    + '&date=' + encodeURIComponent(docDate ? docDate.value : '');
                targets.forEach(function (t) { query += '&codes=' + encodeURIComponent(t.code.value.trim()); });

                fetch(query)
                    .then(function (r) {
                        if (!r.ok) {
                            throw new Error('HTTP ' + r.status + ' resolviendo precios');
                        }
                        return r.json();
                    })
                    .then(function (prices) {
                        var byCode = {};
                        prices.forEach(function (p) { byCode[p.itemCode.toUpperCase()] = p; });
                        targets.forEach(function (t) {
                            var found = byCode[t.code.value.trim().toUpperCase()];
                            if (found) {
                                setPrice(t.price, found);
                            } else if (t.price.dataset.autoPrice === '1') {
                                setPrice(t.price, null);
                            }
                        });
                    })
                    .catch(function (error) { console.error('precios de línea: ', error); });
            }

            function repriceAll() {
                if (linesBody) {
                    repriceRows(Array.prototype.slice.call(linesBody.querySelectorAll('tr')));
                }
            }

            function loadPartnerDefaults() {
                var cardCode = partnerInput.value.trim();
                if (!cardCode || cardCode === lastCardCode) {
                    return;
                }
                lastCardCode = cardCode;
                fetch('?handler=PartnerDefaults&cardCode=' + encodeURIComponent(cardCode))
                    .then(function (r) { return r.ok ? r.json() : null; })
                    .then(function (defaults) {
                        if (!defaults) {
                            renderShipTo([], null);
                            return;
                        }
                        selectValue(employee, defaults.salesEmployeeCode);
                        selectValue(priceList, defaults.priceListCode);
                        if (defaults.currency) {
                            selectValue(currency, defaults.currency);
                            updateCurrencyLabel();
                        }
                        renderShipTo(defaults.addresses || [], defaults.shipToDefault);
                        repriceAll();
                    })
                    .catch(function (error) { console.error('valores por defecto del socio: ', error); });
            }

            // Elegir una sugerencia del datalist dispara "input" (no siempre "change" hasta
            // perder el foco) -- se carga apenas el texto coincide exacto con una opción.
            partnerInput.addEventListener('input', function () {
                var datalist = document.getElementById(options.partnerDatalistId);
                var value = partnerInput.value;
                if (datalist && Array.prototype.some.call(datalist.options, function (o) { return o.value === value; })) {
                    loadPartnerDefaults();
                }
            });
            partnerInput.addEventListener('change', loadPartnerDefaults);

            if (priceList) {
                priceList.addEventListener('change', repriceAll);
            }
            if (docDate) {
                docDate.addEventListener('change', repriceAll);
            }
            if (currency) {
                currency.addEventListener('change', updateCurrencyLabel);
            }
            if (shipTo) {
                shipTo.addEventListener('change', updateShipToAddress);
            }

            if (linesBody) {
                // Cambio de artículo en una fila -> precio de esa fila.
                linesBody.addEventListener('change', function (evento) {
                    if (evento.target.matches(options.itemCodeSelector)) {
                        var row = evento.target.closest('tr');
                        var price = row.querySelector('.line-price');
                        if (price && price.dataset.autoPrice === '1') {
                            price.value = '';
                        }
                        repriceRows([row]);
                    }
                });

                // Precio digitado a mano: deja de ser automático y queda en la moneda del documento.
                linesBody.addEventListener('input', function (evento) {
                    if (!settingPrice && evento.target.matches('.line-price')) {
                        delete evento.target.dataset.autoPrice;
                        evento.target.removeAttribute('title');
                        setLineCurrency(evento.target.closest('tr'), null);
                    }
                });

                // Filas agregadas por "Importar Excel" sin precio -> se completan igual.
                var pending = null;
                new MutationObserver(function () {
                    clearTimeout(pending);
                    pending = setTimeout(repriceAll, 300);
                }).observe(linesBody, { childList: true });
            }
        });
    };
})();
