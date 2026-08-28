// Toggle expandir/colapsar del sidebar (restaurado 2026-08-19 a pedido del
// dueño del proyecto). Solo cambia el atributo data-sidebar del <html> y
// persiste en localStorage -- SidebarMenu/Default.cshtml no recursa a hijos
// en ningún estado, así que expandir nunca revela un árbol de sub-menús
// (regla dura vigente, ver _LayoutMaestro.cshtml).
(function () {
    var toggle = document.getElementById('sidebar-toggle');
    if (!toggle) {
        return;
    }
    toggle.addEventListener('click', function () {
        var isExpanded = document.documentElement.getAttribute('data-sidebar') === 'expanded';
        var next = isExpanded ? 'collapsed' : 'expanded';
        document.documentElement.setAttribute('data-sidebar', next);
        localStorage.setItem('portalsaas-sidebar-collapsed', next);
    });
})();
