// Visor de comprobante "a demanda": el <iframe> no carga hasta que el usuario aprieta
// "Ver documento" -- evita bajar varios documentos pesados de una sola vez (ej. un
// informe con varios gastos, cada uno con su visor) y deja al usuario decidir cuándo
// quiere ver la imagen/PDF en vez de que aparezca sola. El mismo botón alterna a
// "Cerrar documento" -- vive en el header del visor, SIEMPRE visible (pedido
// explícito), no depende de que se abra o cierre ninguna otra cosa primero. El ancho
// del visor se ajusta arrastrando la esquina inferior derecha (resize: horizontal
// nativo del navegador, ver rendiciones.css .rnd-gasto-visor) -- sin JS para eso.
(function () {
    function wire(visor) {
        var boton = visor.querySelector(".rnd-btn-ver-documento");
        var iframe = visor.querySelector("iframe");
        if (!boton || !iframe) return;

        var etiqueta = boton.querySelector(".rnd-btn-ver-documento-label");

        function set(abierto) {
            if (abierto && !iframe.getAttribute("src")) {
                iframe.setAttribute("src", visor.dataset.src);
            }
            iframe.hidden = !abierto;
            var texto = abierto ? "Cerrar documento" : "Ver documento";
            if (etiqueta) etiqueta.textContent = texto;
            boton.title = texto;
        }

        set(false);
        boton.addEventListener("click", function () {
            set(iframe.hidden);
        });
    }

    document.querySelectorAll(".rnd-gasto-visor[data-src]").forEach(wire);
})();
