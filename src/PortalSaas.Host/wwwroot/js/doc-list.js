// Click en cualquier parte de la fila (menos un link propio) navega al detalle --
// portado tal cual de referencia-original/PortalSAP_v2 (mismo comportamiento del list
// view de SAP B1 Web Client). Un solo listener a nivel de documento -- funciona para
// cualquier tabla .doc-list-fila que se agregue después (ej. tras un filtro/paginación),
// sin tener que volver a cablear nada.
document.addEventListener('click', function (evento) {
    var fila = evento.target.closest('.doc-list-fila');
    if (!fila || evento.target.closest('a')) {
        return;
    }
    var url = fila.getAttribute('data-url-detalle');
    if (url) {
        window.location.href = url;
    }
});

// Bug reportado: filtrar/paginar un listado (form GET normal) navega la página
// COMPLETA -- el sidebar se recolapsa y se vuelve a expandir desde localStorage
// (ver sidebar.js), lo que se ve como "el menú se refresca" en cada búsqueda. Acá se
// intercepta el submit del filtro y los links de paginación/tamaño de página, se pide
// SOLO el fragmento del listado (header X-Requested-With, ver _ViewStart.cshtml de
// cada plugin -- responde sin Layout, sin sidebar) y se reemplaza únicamente el
// <div class="doc-list">, dejando el sidebar intacto de punta a punta. Fallback a
// navegación normal si fetch/pushState no están disponibles, o si algo sale mal.
(function () {
    if (typeof fetch !== 'function' || typeof window.history.pushState !== 'function') {
        return;
    }

    async function reemplazarListado(url) {
        var actual = document.querySelector('.doc-list');
        if (!actual) {
            window.location.href = url;
            return;
        }

        var respuesta;
        try {
            respuesta = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        } catch (error) {
            window.location.href = url;
            return;
        }

        if (!respuesta.ok) {
            window.location.href = url;
            return;
        }

        var html = await respuesta.text();
        var envoltorio = document.createElement('div');
        envoltorio.innerHTML = html;
        var nuevo = envoltorio.querySelector('.doc-list');
        if (!nuevo) {
            window.location.href = url;
            return;
        }

        actual.replaceWith(nuevo);
        window.history.pushState({ docListAjax: true }, '', url);
    }

    document.addEventListener('submit', function (evento) {
        var form = evento.target;
        if (!form.matches('.doc-list .filter-card')) {
            return;
        }
        evento.preventDefault();
        var query = new URLSearchParams(new FormData(form)).toString();
        reemplazarListado(window.location.pathname + (query ? '?' + query : ''));
    });

    document.addEventListener('click', function (evento) {
        var link = evento.target.closest('.doc-list-paginacion a');
        if (!link || link.classList.contains('disabled')) {
            return;
        }
        evento.preventDefault();
        reemplazarListado(link.getAttribute('href'));
    });

    // Atrás/Adelante del navegador sobre un estado cargado por AJAX -- se recarga
    // completo en vez de intentar reconstruir el fragmento, es la opción simple y
    // confiable (no es el caso frecuente: filtrar y paginar sí lo son).
    window.addEventListener('popstate', function (evento) {
        if (evento.state && evento.state.docListAjax) {
            window.location.reload();
        }
    });
})();
