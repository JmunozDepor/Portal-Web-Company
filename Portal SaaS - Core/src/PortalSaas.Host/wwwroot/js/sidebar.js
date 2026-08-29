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

/* ---------------------------------------------------------------------------
   Drawer off-canvas del shell en <=900px (ver sidebar.css @media). En
   escritorio el CSS neutraliza el efecto -- este código solo togglea un
   atributo inerte ahí. El drawer SIEMPRE arranca cerrado (no se persiste).
   --------------------------------------------------------------------------- */
(function () {
  var root = document.documentElement;
  var toggle = document.getElementById('shell-nav-toggle');
  var scrim = document.getElementById('shell-scrim');
  var sidebar = document.querySelector('.app-shell .sidebar');
  if (!toggle || !scrim || !sidebar) return;

  function open() {
    root.setAttribute('data-shell-nav', 'open');
    scrim.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
  }

  function close() {
    root.removeAttribute('data-shell-nav');
    scrim.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
  }

  toggle.addEventListener('click', function () {
    if (root.getAttribute('data-shell-nav') === 'open') { close(); } else { open(); }
  });

  scrim.addEventListener('click', close);

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && root.getAttribute('data-shell-nav') === 'open') { close(); }
  });

  // Navegar dentro del drawer lo cierra (el destino carga en la misma pestaña).
  sidebar.addEventListener('click', function (e) {
    if (e.target.closest('a[href]')) { close(); }
  });

  // Rotar/agrandar a escritorio limpia el estado (evita un drawer "abierto"
  // invisible que igual bloquea clicks vía el scrim).
  window.addEventListener('resize', function () {
    if (window.innerWidth > 900) { close(); }
  });
})();
