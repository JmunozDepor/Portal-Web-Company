using Modulo.AuditoriaInventario.Servicios;

namespace Modulo.AuditoriaInventario.Tests;

public class SapSyncStatusTrackerTests
{
    [Fact]
    public void Obtener_DevuelveNullSiNuncaSeInicioNada()
    {
        var tracker = new SapSyncStatusTracker();
        Assert.Null(tracker.Obtener(Guid.NewGuid()));
    }

    [Fact]
    public void IntentarIniciar_DevuelveTrueYQuedaEnCurso()
    {
        var tracker = new SapSyncStatusTracker();
        var companyId = Guid.NewGuid();

        var iniciado = tracker.IntentarIniciar(companyId);

        Assert.True(iniciado);
        var estado = tracker.Obtener(companyId);
        Assert.NotNull(estado);
        Assert.True(estado!.EnCurso);
        Assert.Equal(0, estado.Procesados);
    }

    [Fact]
    public void IntentarIniciar_DevuelveFalseSiYaHayUnaEnCursoParaLaMismaCompania()
    {
        var tracker = new SapSyncStatusTracker();
        var companyId = Guid.NewGuid();
        tracker.IntentarIniciar(companyId);

        var segundoIntento = tracker.IntentarIniciar(companyId);

        Assert.False(segundoIntento);
    }

    [Fact]
    public void IntentarIniciar_PermiteCompaniasDistintasEnParalelo()
    {
        var tracker = new SapSyncStatusTracker();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        tracker.IntentarIniciar(companyA);

        var iniciadoB = tracker.IntentarIniciar(companyB);

        Assert.True(iniciadoB);
    }

    [Fact]
    public void ActualizarProgreso_SoloActualizaSiLaCompaniaEstaEnCurso()
    {
        var tracker = new SapSyncStatusTracker();
        var companyId = Guid.NewGuid();
        tracker.IntentarIniciar(companyId);

        tracker.ActualizarProgreso(companyId, 500);

        Assert.Equal(500, tracker.Obtener(companyId)!.Procesados);
    }

    [Fact]
    public void Completar_DejaEnCursoEnFalseYGuardaElMensaje()
    {
        var tracker = new SapSyncStatusTracker();
        var companyId = Guid.NewGuid();
        tracker.IntentarIniciar(companyId);
        tracker.ActualizarProgreso(companyId, 1000);

        tracker.Completar(companyId, "listo");

        var estado = tracker.Obtener(companyId)!;
        Assert.False(estado.EnCurso);
        Assert.False(estado.Error);
        Assert.Equal("listo", estado.Mensaje);
        Assert.NotNull(estado.TerminadoEn);
    }

    [Fact]
    public void Fallar_DejaEnCursoEnFalseYMarcaError()
    {
        var tracker = new SapSyncStatusTracker();
        var companyId = Guid.NewGuid();
        tracker.IntentarIniciar(companyId);

        tracker.Fallar(companyId, "explotó");

        var estado = tracker.Obtener(companyId)!;
        Assert.False(estado.EnCurso);
        Assert.True(estado.Error);
        Assert.Equal("explotó", estado.Mensaje);
    }

    [Fact]
    public void DespuesDeCompletar_IntentarIniciarDeNuevoFunciona()
    {
        var tracker = new SapSyncStatusTracker();
        var companyId = Guid.NewGuid();
        tracker.IntentarIniciar(companyId);
        tracker.Completar(companyId, "listo");

        var reinicio = tracker.IntentarIniciar(companyId);

        Assert.True(reinicio);
        Assert.Equal(0, tracker.Obtener(companyId)!.Procesados);
    }
}
