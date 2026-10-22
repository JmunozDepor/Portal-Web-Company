// Escaneo continuo de documentos del manifiesto (UNA sola implementación para todas las pantallas).
//   - Lector de mano en modo teclado: el input mantiene el foco; Enter envía la lectura.
//   - Cámara: ZXing.BrowserPDF417Reader (@zxing/library vendorizado en /lib/zxing); el botón solo aparece si window.ZXing existe.
//   - Ingreso manual tipo + folio; si el folio es ambiguo (33/34) se pide el tipo y se reenvía.
// El servidor responde JSON { outcome, message, warnings, otherManifestNumber, document, totalDocuments, totalUnits, totalPackages }.
(function () {
    var box = document.querySelector('[data-scan]');
    if (!box) { return; }

    var url = box.getAttribute('data-url');
    var tokenInput = box.querySelector('input[name="__RequestVerificationToken"]');
    var input = document.getElementById('scan-input');
    var feedback = document.getElementById('scan-feedback');
    var ambiguous = document.getElementById('scan-ambiguous');
    var cameraBtn = document.getElementById('scan-camera');
    var cameraPanel = document.getElementById('scan-camera-panel');
    var video = document.getElementById('scan-video');
    var tbody = document.getElementById('docs-body');
    var template = document.getElementById('doc-row-template');
    var nf = new Intl.NumberFormat('es-CL');

    var queue = Promise.resolve();
    var pendingAmbiguous = null; // última captura manual ambigua, a la espera del tipo SII
    var tedTimer = null;
    var cameraReader = null;
    var lastCamera = { text: '', at: 0 };

    // ------------------------------------------------------------ sonido
    var audio = null;
    function beep(freq, ms) {
        try {
            audio = audio || new (window.AudioContext || window.webkitAudioContext)();
            var osc = audio.createOscillator();
            var gain = audio.createGain();
            osc.frequency.value = freq;
            gain.gain.value = 0.08;
            osc.connect(gain);
            gain.connect(audio.destination);
            osc.start();
            osc.stop(audio.currentTime + ms / 1000);
        } catch (e) { /* sin audio: solo color */ }
    }

    // ------------------------------------------------------------ feedback
    function show(kind, text) {
        feedback.className = 'scan-feedback scan-feedback--' + kind;
        feedback.textContent = text;
        if (kind === 'ok') { beep(880, 90); }
        else if (kind === 'warn') { beep(520, 160); }
        else { beep(220, 300); }
    }

    function addRow(doc) {
        if (!doc || !template || !tbody) { return; }
        var empty = tbody.querySelector('[data-table-empty]');
        if (empty) { empty.remove(); }
        var row = template.content.firstElementChild.cloneNode(true);
        var fields = { kind: doc.kind, folio: doc.folio, customer: doc.customer, commune: doc.commune || '—', units: nf.format(doc.units), packages: doc.packages, amount: '$' + nf.format(doc.amount || 0) };
        Object.keys(fields).forEach(function (k) { row.querySelector('[data-f="' + k + '"]').textContent = fields[k]; });
        var form = row.querySelector('form');
        form.setAttribute('action', form.getAttribute('action').replace('00000000-0000-0000-0000-000000000000', doc.id));
        // El token de la plantilla ya viene dentro del formulario clonado.
        tbody.appendChild(row);
    }

    function setCounters(data) {
        document.querySelectorAll('[data-doc-count]').forEach(function (e) { e.textContent = data.totalDocuments; });
        document.querySelectorAll('[data-doc-units]').forEach(function (e) { e.textContent = nf.format(data.totalUnits); });
        document.querySelectorAll('[data-doc-packages]').forEach(function (e) { e.textContent = data.totalPackages; });
        document.querySelectorAll('[data-doc-amount]').forEach(function (e) { e.textContent = '$' + nf.format(data.totalAmount || 0); });
    }

    // ------------------------------------------------------------ envío
    function submit(fields) {
        // Una captura a la vez: un lector rápido puede disparar varias lecturas seguidas.
        queue = queue.then(function () { return send(fields); });
        return queue;
    }

    function send(fields) {
        var body = new FormData();
        Object.keys(fields).forEach(function (k) {
            if (fields[k] !== null && fields[k] !== undefined && fields[k] !== '') { body.append(k, fields[k]); }
        });
        var token = tokenInput ? tokenInput.value : '';
        body.append('__RequestVerificationToken', token);
        return fetch(url, { method: 'POST', body: body, headers: { 'RequestVerificationToken': token }, credentials: 'same-origin' })
            .then(function (r) {
                // Sesión vencida: el servidor redirige al login (HTML) o responde 401/403.
                var type = (r.headers.get('Content-Type') || '').toLowerCase();
                if (r.redirected || r.status === 401 || r.status === 403 || (r.ok && type.indexOf('json') < 0)) {
                    throw new Error('Sesión expirada o sin permiso. Recargue la página.');
                }
                if (!r.ok) { throw new Error('El servidor respondió ' + r.status + '.'); }
                return r.json();
            })
            .then(function (data) { handle(data, fields); })
            .catch(function (err) { show('error', err.message || 'No se pudo enviar la lectura.'); })
            .then(function () { input.focus(); });
    }

    function handle(data, fields) {
        ambiguous.hidden = true;
        pendingAmbiguous = null;
        var warnings = (data.warnings && data.warnings.length) ? ' — ' + data.warnings.join(' ') : '';
        if (data.outcome === 'added') {
            addRow(data.document);
            setCounters(data);
            show(warnings ? 'warn' : 'ok', data.message + warnings);
        } else if (data.outcome === 'duplicate_in_this') {
            show('warn', data.message);
        } else if (data.outcome === 'ambiguous') {
            pendingAmbiguous = { DocumentKind: fields.DocumentKind, Folio: fields.Folio };
            ambiguous.hidden = false;
            show('warn', data.message);
        } else {
            show('error', data.message);
        }
    }

    // ------------------------------------------------------------ lector de mano (modo teclado)
    function submitScan() {
        var text = input.value;
        if (tedTimer) { clearTimeout(tedTimer); tedTimer = null; }
        if (!text.trim()) { return; }
        input.value = '';
        submit({ RawScan: text });
    }

    input.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter') { return; }
        e.preventDefault();
        var v = input.value;
        // Timbre a medio llegar (Enter dentro del XML): esperar el cierre antes de enviar.
        if (/<TED/i.test(v) && !/<\/TED>/i.test(v)) {
            if (tedTimer) { clearTimeout(tedTimer); }
            tedTimer = setTimeout(submitScan, 1500);
            return;
        }
        submitScan();
    });

    input.addEventListener('input', function () {
        // Pegado o lector sin Enter final: al llegar el cierre del timbre se envía solo.
        if (/<\/TED>/i.test(input.value)) {
            if (tedTimer) { clearTimeout(tedTimer); }
            tedTimer = setTimeout(submitScan, 150);
        }
    });

    // El foco vuelve al input cuando se pierde hacia la nada (clic en el fondo), pero no si el usuario está en otro campo.
    document.addEventListener('focusout', function () {
        setTimeout(function () {
            var a = document.activeElement;
            if (!a || a === document.body) { input.focus(); }
        }, 50);
    });
    input.focus();

    // ------------------------------------------------------------ manual
    var manualFolio = document.getElementById('manual-folio');
    document.getElementById('manual-add').addEventListener('click', function () {
        var kind = document.getElementById('manual-kind').value;
        var f = manualFolio.value.trim();
        if (!/^\d+$/.test(f)) { show('error', 'Ingrese un folio numérico.'); manualFolio.focus(); return; }
        manualFolio.value = '';
        submit({ DocumentKind: kind, Folio: f });
    });
    manualFolio.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); document.getElementById('manual-add').click(); }
    });

    ambiguous.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-sii]');
        if (!btn || !pendingAmbiguous) { return; }
        var p = pendingAmbiguous;
        submit({ DocumentKind: p.DocumentKind, Folio: p.Folio, SiiDocumentType: btn.getAttribute('data-sii') });
    });

    // ------------------------------------------------------------ cámara (PDF417)
    function stopCamera() {
        if (cameraReader) { try { cameraReader.reset(); } catch (e) { /* ya cerrada */ } cameraReader = null; }
        cameraPanel.hidden = true;
        input.focus();
    }

    if (window.ZXing && window.ZXing.BrowserPDF417Reader && navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {
        cameraBtn.hidden = false;
        cameraBtn.addEventListener('click', function () {
            if (cameraReader) { return; }
            cameraPanel.hidden = false;
            cameraReader = new window.ZXing.BrowserPDF417Reader();
            cameraReader.decodeFromVideoDevice(null, video, function (result) {
                if (!result) { return; } // sin código en el cuadro: seguir leyendo
                var text = result.getText();
                var now = Date.now();
                // El mismo código sigue frente a la cámara: no reenviar durante 3 s.
                if (text === lastCamera.text && now - lastCamera.at < 3000) { return; }
                lastCamera = { text: text, at: now };
                submit({ RawScan: text });
            }).catch(function () {
                show('error', 'No se pudo abrir la cámara (permiso denegado o sin cámara).');
                stopCamera();
            });
        });
        document.getElementById('scan-camera-close').addEventListener('click', stopCamera);
    }
})();
