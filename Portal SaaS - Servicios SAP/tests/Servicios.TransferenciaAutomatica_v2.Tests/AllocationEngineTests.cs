using Servicios.TransferenciaAutomatica_v2.Domain;
using Xunit;

namespace Servicios.TransferenciaAutomatica_v2.Tests;

public class AllocationEngineTests
{
    [Fact]
    public void Necesidad_cubierta_por_primera_bodega_no_toca_las_siguientes()
    {
        var candidatos = new List<(string WhsCode, decimal Disponible)>
        {
            ("BO01", 20m),
            ("BO02", 50m)
        };

        var resultado = AllocationEngine.Asignar(10m, candidatos);

        var asignacion = Assert.Single(resultado);
        Assert.Equal("BO01", asignacion.WhsCodeOrigen);
        Assert.Equal(10m, asignacion.Cantidad);
    }

    [Fact]
    public void Necesidad_se_reparte_entre_varias_bodegas_en_orden_de_prioridad()
    {
        var candidatos = new List<(string WhsCode, decimal Disponible)>
        {
            ("BO01", 4m),
            ("BO02", 3m),
            ("BO03", 100m)
        };

        var resultado = AllocationEngine.Asignar(10m, candidatos);

        Assert.Equal(3, resultado.Count);
        Assert.Equal(("BO01", 4m), resultado[0]);
        Assert.Equal(("BO02", 3m), resultado[1]);
        Assert.Equal(("BO03", 3m), resultado[2]);
    }

    [Fact]
    public void Necesidad_no_cubierta_del_todo_devuelve_lo_maximo_disponible()
    {
        var candidatos = new List<(string WhsCode, decimal Disponible)>
        {
            ("BO01", 2m),
            ("BO02", 1m)
        };

        var resultado = AllocationEngine.Asignar(10m, candidatos);

        Assert.Equal(2, resultado.Count);
        Assert.Equal(3m, resultado.Sum(a => a.Cantidad));
    }

    [Fact]
    public void Sin_bodegas_candidatas_no_devuelve_asignaciones()
    {
        var resultado = AllocationEngine.Asignar(10m, []);

        Assert.Empty(resultado);
    }

    [Fact]
    public void Necesidad_cero_o_negativa_no_devuelve_asignaciones()
    {
        var candidatos = new List<(string WhsCode, decimal Disponible)> { ("BO01", 100m) };

        Assert.Empty(AllocationEngine.Asignar(0m, candidatos));
        Assert.Empty(AllocationEngine.Asignar(-5m, candidatos));
    }

    [Fact]
    public void Bodegas_con_disponible_cero_o_negativo_se_saltean()
    {
        var candidatos = new List<(string WhsCode, decimal Disponible)>
        {
            ("BO01", 0m),
            ("BO02", -3m),
            ("BO03", 5m)
        };

        var resultado = AllocationEngine.Asignar(5m, candidatos);

        var asignacion = Assert.Single(resultado);
        Assert.Equal("BO03", asignacion.WhsCodeOrigen);
        Assert.Equal(5m, asignacion.Cantidad);
    }
}
