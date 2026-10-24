// Agrupar traslados: "Seleccionar todos" general y por sucursal, y contador del botón.
// Las transferencias parten desmarcadas; el servidor exige al menos una.
(function () {
    var all = document.querySelector('[data-select-all]');
    var items = function (scope) { return Array.prototype.slice.call((scope || document).querySelectorAll('input[name="docEntries"]')); };
    var count = document.querySelector('[data-selected-count]');

    function refresh() {
        var boxes = items();
        var checked = boxes.filter(function (b) { return b.checked; }).length;
        if (count) { count.textContent = checked; }
        if (all) {
            all.checked = boxes.length > 0 && checked === boxes.length;
            all.indeterminate = checked > 0 && checked < boxes.length;
        }
        document.querySelectorAll('[data-select-group]').forEach(function (group) {
            var head = group.querySelector('[data-select-branch]');
            if (!head) { return; }
            var own = items(group);
            var n = own.filter(function (b) { return b.checked; }).length;
            head.checked = own.length > 0 && n === own.length;
            head.indeterminate = n > 0 && n < own.length;
        });
    }

    if (all) {
        all.addEventListener('change', function () {
            items().forEach(function (b) { b.checked = all.checked; });
            refresh();
        });
    }
    document.querySelectorAll('[data-select-group]').forEach(function (group) {
        var head = group.querySelector('[data-select-branch]');
        if (!head) { return; }
        head.addEventListener('change', function () {
            items(group).forEach(function (b) { b.checked = head.checked; });
            refresh();
        });
    });
    items().forEach(function (b) { b.addEventListener('change', refresh); });
    refresh();
})();
