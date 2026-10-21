// Comportamiento común de las pantallas de mantenedores de Despachos (sin JS por página):
//   <input data-maestro-search="tabla-id">      filtra las filas <tr data-buscable="texto">
//   <table data-maestro-table id="tabla-id">    activa el orden por columna (table-tools.js del Host)
//   <button data-maestro-new="form-id">         deja el formulario del drawer vacío (alta)
//   <button data-maestro-edit data-row='{...}'> rellena el formulario con la fila (claves = <prefijo>.<Clave>; prefijo = data-input-prefix del form, por defecto Input)
//   <form data-confirm="texto">                 pide confirmación antes de enviar (eliminar)
//   <input data-option-filter="select-id">      filtra las opciones de un select múltiple
// El drawer se abre con data-drawer-open (drawer.js global del Host).
(function () {
    function norm(s) {
        return (s || '').toString().toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
    }

    function applySearch(input) {
        var table = document.getElementById(input.getAttribute('data-maestro-search'));
        if (!table) { return; }
        var q = norm(input.value.trim());
        Array.prototype.forEach.call(table.tBodies[0].rows, function (row) {
            if (row.hasAttribute('data-table-empty')) { return; }
            row.hidden = q !== '' && norm(row.getAttribute('data-buscable')).indexOf(q) === -1;
        });
    }

    function drawerOf(button) {
        return document.getElementById(button.getAttribute('data-drawer-open'));
    }

    function setTitle(drawer, edit) {
        if (!drawer) { return; }
        var title = drawer.querySelector('[data-drawer-title]');
        if (title) { title.textContent = title.getAttribute(edit ? 'data-title-edit' : 'data-title-new'); }
        var submit = drawer.querySelector('[data-submit-label]');
        if (submit) { submit.textContent = submit.getAttribute(edit ? 'data-label-edit' : 'data-label-new'); }
    }

    // Prefijo de los campos del formulario: "Input" por defecto; data-input-prefix="Card" en pantallas con varios formularios.
    function prefixOf(form) {
        return form.getAttribute('data-input-prefix') || 'Input';
    }

    function resetForm(form) {
        form.reset();
        var id = form.elements[prefixOf(form) + '.Id'];
        if (id) { id.value = '0'; }
        Array.prototype.forEach.call(form.querySelectorAll('[data-option-filter]'), function (f) {
            f.value = '';
            f.dispatchEvent(new Event('input', { bubbles: true }));
        });
    }

    function fillForm(form, row) {
        resetForm(form);
        Object.keys(row).forEach(function (key) {
            var el = form.elements[prefixOf(form) + '.' + key];
            if (!el) { return; }
            var value = row[key];
            if (el.type === 'checkbox') {
                el.checked = value === true;
            } else if (el.tagName === 'SELECT' && el.multiple) {
                var wanted = (value || []).map(String);
                Array.prototype.forEach.call(el.options, function (o) { o.selected = wanted.indexOf(o.value) !== -1; });
            } else {
                el.value = value === null || value === undefined ? '' : value;
            }
        });
    }

    document.addEventListener('click', function (e) {
        var edit = e.target.closest('[data-maestro-edit]');
        if (edit) {
            var drawer = drawerOf(edit);
            var form = drawer && drawer.querySelector('form');
            if (form) {
                fillForm(form, JSON.parse(edit.getAttribute('data-row') || '{}'));
                setTitle(drawer, true);
            }
            return;
        }
        var nuevo = e.target.closest('[data-maestro-new]');
        if (nuevo) {
            var form2 = document.getElementById(nuevo.getAttribute('data-maestro-new'));
            if (form2) {
                resetForm(form2);
                setTitle(drawerOf(nuevo), false);
            }
        }
    });

    document.addEventListener('input', function (e) {
        var search = e.target.closest('[data-maestro-search]');
        if (search) { applySearch(search); return; }
        var filter = e.target.closest('[data-option-filter]');
        if (filter) {
            var select = document.getElementById(filter.getAttribute('data-option-filter'));
            var q = norm(filter.value.trim());
            Array.prototype.forEach.call(select.options, function (o) {
                // Las opciones seleccionadas nunca se ocultan: así no se pierde lo ya elegido.
                o.hidden = q !== '' && !o.selected && norm(o.textContent).indexOf(q) === -1;
            });
        }
    });

    document.addEventListener('submit', function (e) {
        var text = e.target.getAttribute && e.target.getAttribute('data-confirm');
        if (text && !window.confirm(text)) { e.preventDefault(); }
    });

    document.addEventListener('DOMContentLoaded', function () {
        if (typeof window.wireTableTools !== 'function') { return; }
        Array.prototype.forEach.call(document.querySelectorAll('table[data-maestro-table]'), function (t) {
            window.wireTableTools({ tableId: t.id });
        });
    });
})();
