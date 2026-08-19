namespace Servicios.TransferenciaAutomatica_v2.Domain;

/// <summary>
/// El algoritmo de cascada que antes vivía en SP_DEP_ORDER_ABS (SQL dinámico, un mundo
/// distinto por motor), acá como función pura en C# -- mismo criterio de negocio (tomar de
/// la bodega de mayor prioridad primero, cortar apenas se cubre la necesidad), pero
/// testeable sin HANA ni SQL Server (ver AllocationEngineTests) y compartido entre motores.
///
/// El llamador (Worker.cs) es responsable de pasar "candidatosEnOrden" ya neto de lo
/// comprometido en el ciclo actual (ver el "ledger" por ciclo en Worker.cs) -- este método
/// no sabe nada de concurrencia entre documentos, solo resuelve UNA línea.
/// </summary>
public static class AllocationEngine
{
    public static IReadOnlyList<(string WhsCodeOrigen, decimal Cantidad)> Asignar(
        decimal necesidad, IReadOnlyList<(string WhsCode, decimal Disponible)> candidatosEnOrden)
    {
        if (necesidad <= 0)
        {
            return [];
        }

        var resultado = new List<(string WhsCodeOrigen, decimal Cantidad)>();
        var necesidadRestante = necesidad;

        foreach (var (whsCode, disponible) in candidatosEnOrden)
        {
            if (necesidadRestante <= 0)
            {
                break;
            }

            if (disponible <= 0)
            {
                continue;
            }

            var aTomar = Math.Min(necesidadRestante, disponible);
            resultado.Add((whsCode, aTomar));
            necesidadRestante -= aTomar;
        }

        return resultado;
    }
}
