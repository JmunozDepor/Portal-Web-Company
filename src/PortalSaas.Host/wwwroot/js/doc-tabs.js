// Tabs de un documento (General/Contenido/Logística/Finanzas): son anclas de scroll,
// no toggles -- todo el documento se ve de una sola vez hacia abajo, portado tal cual
// de referencia-original/PortalSAP_v2 (doc-tabs.js) donde el dueño del proyecto pidió
// explícitamente que las secciones nunca se oculten. Este script hace scroll suave al
// click y resalta la pestaña activa según qué sección está visible (scrollspy, vía
// IntersectionObserver).
(function () {
    document.addEventListener('click', function (evento) {
        var boton = evento.target.closest('.doc-tab-btn');
        if (!boton) {
            return;
        }
        var id = boton.getAttribute('data-tab-anchor');
        var seccion = id && document.getElementById(id);
        if (!seccion) {
            return;
        }
        evento.preventDefault();
        seccion.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    if (!('IntersectionObserver' in window)) {
        return;
    }

    document.querySelectorAll('.doc-form').forEach(function (form) {
        var botones = form.querySelectorAll('.doc-tab-btn');
        if (botones.length === 0) {
            return;
        }

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entrada) {
                if (!entrada.isIntersecting) {
                    return;
                }
                botones.forEach(function (b) {
                    b.classList.toggle('active', b.getAttribute('data-tab-anchor') === entrada.target.id);
                });
            });
        }, { root: null, rootMargin: '-20% 0px -70% 0px', threshold: 0 });

        form.querySelectorAll(':scope > .doc-tab-seccion').forEach(function (seccion) {
            observer.observe(seccion);
        });
    });
})();
