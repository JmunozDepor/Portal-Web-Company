using Servicios.TransferenciaAutomatica_v2.Estado;
using Xunit;

namespace Servicios.TransferenciaAutomatica_v2.Tests;

public class IntentosAsignacionStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "intentos-asignacion-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void RegistrarIntentoParcial_acumula_por_documento()
    {
        var store = new IntentosAsignacionStore(_folder);

        Assert.Equal(1, store.RegistrarIntentoParcial("DEP01", "17", 100));
        Assert.Equal(2, store.RegistrarIntentoParcial("DEP01", "17", 100));
        Assert.Equal(3, store.RegistrarIntentoParcial("DEP01", "17", 100));
    }

    [Fact]
    public void RegistrarIntentoParcial_sobrevive_a_una_nueva_instancia_del_store()
    {
        // Simula un reinicio del Windows Service: el contador debe persistir en disco.
        var store1 = new IntentosAsignacionStore(_folder);
        store1.RegistrarIntentoParcial("DEP01", "17", 100);
        store1.RegistrarIntentoParcial("DEP01", "17", 100);

        var store2 = new IntentosAsignacionStore(_folder);
        Assert.Equal(3, store2.RegistrarIntentoParcial("DEP01", "17", 100));
    }

    [Fact]
    public void RegistrarIntentoParcial_no_mezcla_documentos_distintos()
    {
        var store = new IntentosAsignacionStore(_folder);

        store.RegistrarIntentoParcial("DEP01", "17", 100);
        store.RegistrarIntentoParcial("DEP01", "17", 100);
        Assert.Equal(1, store.RegistrarIntentoParcial("DEP01", "13", 100));
        Assert.Equal(1, store.RegistrarIntentoParcial("DEP01", "17", 200));
    }

    [Fact]
    public void RegistrarIntentoParcial_no_mezcla_compañías_distintas()
    {
        var store = new IntentosAsignacionStore(_folder);

        store.RegistrarIntentoParcial("DEP01", "17", 100);
        store.RegistrarIntentoParcial("DEP01", "17", 100);
        Assert.Equal(1, store.RegistrarIntentoParcial("DEP02", "17", 100));
    }

    [Fact]
    public void Limpiar_reinicia_el_contador_del_documento()
    {
        var store = new IntentosAsignacionStore(_folder);

        store.RegistrarIntentoParcial("DEP01", "17", 100);
        store.RegistrarIntentoParcial("DEP01", "17", 100);
        store.Limpiar("DEP01", "17", 100);

        Assert.Equal(1, store.RegistrarIntentoParcial("DEP01", "17", 100));
    }
}
