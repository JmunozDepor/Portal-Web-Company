// Formulario personalizado de los documentos genéricos (Venta/Compra/Inventario) --
// panel "Personalizar" del componente DocumentForm. Cada casilla muestra/oculta los
// elementos con data-form-field="clave" reescribiendo el <style id="doc-form-layout-style">
// que el servidor ya renderizó, y guarda la lista en la cuenta del usuario (por
// compañía) vía el handler "FormLayout" del PageModel del documento. "Restablecer"
// borra la versión propia y vuelve a la base que definió el admin.
(function () {
    document.addEventListener('DOMContentLoaded', function () {
        var panel = document.querySelector('.doc-form-layout-panel');
        var style = document.getElementById('doc-form-layout-style');
        if (!panel || !style) {
            return;
        }

        var checkboxes = Array.prototype.slice.call(panel.querySelectorAll('input[type="checkbox"]'));
        var resetButton = panel.querySelector('.doc-form-layout-reset');
        var status = panel.querySelector('.doc-form-layout-status');
        var count = document.querySelector('.doc-form-layout-count');
        var timer = null;

        function hiddenKeys() {
            return checkboxes.filter(function (c) { return !c.checked; }).map(function (c) { return c.value; });
        }

        function apply(keys) {
            style.textContent = keys.map(function (k) {
                return '.doc-form [data-form-field="' + k + '"] { display: none !important; }';
            }).join('\n');
            checkboxes.forEach(function (c) { c.checked = keys.indexOf(c.value) < 0; });
            if (count) {
                count.textContent = keys.length;
                count.hidden = keys.length === 0;
            }
        }

        function send(body) {
            body.append('formKey', panel.dataset.formKey);
            if (status) {
                status.textContent = 'Guardando…';
            }
            return fetch('?handler=FormLayout', {
                method: 'POST',
                headers: { 'RequestVerificationToken': panel.dataset.token },
                body: body,
            }).then(function (r) {
                if (!r.ok) {
                    throw new Error('HTTP ' + r.status);
                }
                return r.json();
            }).then(function (result) {
                apply(result.hiddenFields || []);
                if (resetButton) {
                    resetButton.hidden = !result.hasUserOverride;
                }
                if (status) {
                    status.textContent = 'Guardado';
                }
            }).catch(function (error) {
                console.error('formulario personalizado: ', error);
                if (status) {
                    status.textContent = 'No se pudo guardar';
                }
            });
        }

        checkboxes.forEach(function (c) {
            c.addEventListener('change', function () {
                var keys = hiddenKeys();
                apply(keys);
                clearTimeout(timer);
                timer = setTimeout(function () {
                    var body = new FormData();
                    keys.forEach(function (k) { body.append('hidden', k); });
                    send(body);
                }, 400);
            });
        });

        if (resetButton) {
            resetButton.addEventListener('click', function () {
                clearTimeout(timer);
                var body = new FormData();
                body.append('reset', 'true');
                send(body);
            });
        }
    });
})();
