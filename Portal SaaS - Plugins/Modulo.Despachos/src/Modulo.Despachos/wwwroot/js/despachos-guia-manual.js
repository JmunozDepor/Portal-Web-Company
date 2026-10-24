// Formulario de la guía manual (GuiasManuales/Editar):
//   - filas de detalle dinámicas (Form.Lines[i].*) con monto y totales en vivo (IVA 19% si el motivo es afecto);
//   - búsqueda del receptor en SAP por RUT (direcciones de despacho a elegir) y atajo "a sucursal propia";
//   - importación del detalle (texto pegado o .xlsx) con vista previa de errores, reemplazar o agregar;
//   - modo de transporte: maestros o particular.
(function () {
    var form = document.getElementById('guia-form');
    if (!form) { return; }
    var nf = new Intl.NumberFormat('es-CL', { maximumFractionDigits: 0 });
    var tbody = document.querySelector('#g-lines tbody');
    var template = document.getElementById('g-line-template');
    var vatCodes = (form.getAttribute('data-vat') || '').split(',').filter(Boolean);
    var token = (form.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';
    function $(id) { return document.getElementById(id); }
    function num(v) { var n = parseFloat(String(v || '').replace(',', '.')); return isNaN(n) ? 0 : n; }

    // ------------------------------------------------------------ líneas
    function renumber() {
        tbody.querySelectorAll('tr[data-line]').forEach(function (tr, i) {
            tr.querySelectorAll('input').forEach(function (input) {
                var field = input.getAttribute('data-name') || (input.name.match(/\]\.(\w+)$/) || [])[1];
                if (field) { input.setAttribute('data-name', field); input.name = 'Form.Lines[' + i + '].' + field; }
            });
        });
    }

    function addLine(values) {
        var tr = template.content.firstElementChild.cloneNode(true);
        if (values) {
            ['ItemCode', 'Description', 'Quantity', 'Unit', 'UnitPrice'].forEach(function (f) {
                var key = f.charAt(0).toLowerCase() + f.slice(1);
                tr.querySelector('[data-name="' + f + '"]').value = values[key] == null ? '' : values[key];
            });
        }
        tbody.appendChild(tr);
        renumber();
        recalc();
        return tr;
    }

    function recalc() {
        var net = 0;
        tbody.querySelectorAll('tr[data-line]').forEach(function (tr) {
            var amount = Math.round(num(tr.querySelector('[data-qty]').value) * num(tr.querySelector('[data-price]').value));
            tr.querySelector('[data-amount]').textContent = '$' + nf.format(amount);
            net += amount;
        });
        var affected = vatCodes.indexOf(($('g-motive') || {}).value) >= 0;
        var vat = affected ? Math.round(net * 0.19) : 0;
        $('g-net').textContent = '$' + nf.format(net);
        $('g-vat').textContent = '$' + nf.format(vat);
        $('g-total').textContent = '$' + nf.format(net + vat);
    }

    $('g-add-line').addEventListener('click', function () { addLine(null).querySelector('input').focus(); });
    tbody.addEventListener('input', recalc);
    tbody.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-remove-line]');
        if (!btn) { return; }
        btn.closest('tr').remove();
        if (!tbody.querySelector('tr[data-line]')) { addLine(null); }
        renumber();
        recalc();
    });
    if ($('g-motive')) { $('g-motive').addEventListener('change', recalc); }
    renumber();
    recalc();

    // ------------------------------------------------------------ receptor
    var addressList = $('g-address-list');
    function setMsg(text, kind) {
        var msg = $('g-rut-msg');
        msg.textContent = text;
        msg.className = 'form-text' + (kind ? ' text-' + kind : '');
    }

    function fillAddress(a) {
        if (!a) { return; }
        $('g-address').value = a.address || '';
        $('g-commune').value = a.commune || '';
        $('g-city').value = a.city || '';
    }

    function lookup() {
        var rut = $('g-rut').value.trim();
        if (!rut) { return; }
        setMsg('Buscando en SAP…');
        fetch(form.getAttribute('data-receptor-url') + '&taxId=' + encodeURIComponent(rut), { credentials: 'same-origin' })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                addressList.hidden = true;
                addressList.innerHTML = '';
                if (data.error) { setMsg(data.error, 'danger'); return; }
                if (!data.found) {
                    $('g-cardcode').value = '';
                    setMsg('El RUT no está en SAP: digite los datos del receptor (solo para esta guía).', 'warning');
                    return;
                }
                var p = data.partner;
                $('g-cardcode').value = p.cardCode;
                $('g-name').value = p.name || '';
                if (p.activity) { $('g-giro').value = p.activity.substring(0, 40); }
                setMsg('Socio SAP ' + p.cardCode + (p.active ? '' : ' (inactivo)') + '.', p.active ? 'success' : 'warning');
                if (p.addresses.length > 0) {
                    p.addresses.forEach(function (a, i) {
                        var o = document.createElement('option');
                        o.value = String(i);
                        o.textContent = (a.name ? a.name + ': ' : '') + (a.address || '') + ', ' + (a.commune || '');
                        addressList.appendChild(o);
                    });
                    addressList.hidden = p.addresses.length < 2;
                    addressList.onchange = function () { fillAddress(p.addresses[+addressList.value]); };
                    fillAddress(p.addresses[0]);
                }
            })
            .catch(function () { setMsg('No se pudo consultar SAP; digite los datos del receptor.', 'danger'); });
    }

    $('g-buscar').addEventListener('click', lookup);
    $('g-rut').addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); lookup(); } });

    var branch = $('g-branch');
    if (branch) {
        branch.addEventListener('change', function () {
            var o = branch.selectedOptions[0];
            if (!o || !o.value) { return; }
            $('g-rut').value = form.getAttribute('data-issuer') || '';
            $('g-dest-address').value = o.getAttribute('data-address') || '';
            $('g-dest-commune').value = o.getAttribute('data-commune') || '';
            lookup();
        });
    }

    $('g-copy-dest').addEventListener('click', function () {
        $('g-dest-address').value = $('g-address').value;
        $('g-dest-commune').value = $('g-commune').value;
        $('g-dest-city').value = $('g-city').value;
    });

    // ------------------------------------------------------------ transporte
    function syncTransport() {
        var checked = form.querySelector('[data-transport-mode]:checked');
        form.querySelectorAll('[data-transport]').forEach(function (box) {
            box.hidden = !checked || box.getAttribute('data-transport') !== checked.value;
        });
    }
    form.querySelectorAll('[data-transport-mode]').forEach(function (r) { r.addEventListener('change', syncTransport); });
    syncTransport();

    // ------------------------------------------------------------ importación
    var imported = null;
    var applyBtn = $('imp-apply');
    $('imp-preview').addEventListener('click', function () {
        var body = new FormData();
        var file = $('imp-file').files[0];
        if (file) { body.append('file', file); } else { body.append('text', $('imp-text').value); }
        body.append('__RequestVerificationToken', token);
        var result = $('imp-result');
        result.textContent = 'Leyendo…';
        fetch(form.getAttribute('data-importar-url'), {
            method: 'POST', body: body, credentials: 'same-origin', headers: { 'RequestVerificationToken': token },
        })
            .then(function (r) {
                if (!r.ok || (r.headers.get('Content-Type') || '').indexOf('json') < 0) { throw new Error('Sesión expirada o sin permiso. Recargue la página.'); }
                return r.json();
            })
            .then(function (data) {
                result.innerHTML = '';
                imported = null;
                var errors = data.errors || [];
                var p = document.createElement('div');
                if (errors.length > 0) {
                    p.className = 'alert alert-danger';
                    p.textContent = errors.length + ' fila(s) con error; corrija y vuelva a leer: ' + errors.join(' ');
                } else if ((data.lines || []).length === 0) {
                    p.className = 'alert alert-warning';
                    p.textContent = 'No se encontraron líneas.';
                } else {
                    p.className = 'alert alert-success';
                    p.textContent = data.lines.length + ' línea(s) listas para aplicar.';
                    imported = data.lines;
                }
                result.appendChild(p);
                applyBtn.disabled = !imported;
            })
            .catch(function (err) { result.textContent = err.message || 'No se pudo leer el detalle.'; applyBtn.disabled = true; });
    });

    applyBtn.addEventListener('click', function () {
        if (!imported) { return; }
        var mode = (document.querySelector('input[name="imp-mode"]:checked') || {}).value;
        if (mode === 'replace') { tbody.innerHTML = ''; }
        else {
            // Una fila vacía al final no cuenta.
            tbody.querySelectorAll('tr[data-line]').forEach(function (tr) {
                var empty = Array.prototype.every.call(tr.querySelectorAll('input'), function (i) { return !i.value; });
                if (empty) { tr.remove(); }
            });
        }
        imported.forEach(function (l) { addLine(l); });
        var close = document.querySelector('#drawer-importar [data-drawer-close]');
        if (close) { close.click(); }
        imported = null;
        applyBtn.disabled = true;
    });
})();
