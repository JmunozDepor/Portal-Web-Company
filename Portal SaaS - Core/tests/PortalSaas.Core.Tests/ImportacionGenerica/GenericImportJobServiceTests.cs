using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Catalogos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

/// <summary>Trabajos de importación en segundo plano (2026-10-15): borrador, cola, doble envío y progreso.</summary>
public sealed class GenericImportJobServiceTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly GenericImportParametersDto Parameters =
        new(GenericImportModule.Sales, "SalesOrder", GenericImportLineType.Item, "C1", 7, 12);

    private sealed class FakeQueue : IGenericImportJobQueue
    {
        public List<Guid> Enqueued { get; } = [];
        public void Enqueue(Guid jobId) => Enqueued.Add(jobId);
        public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeCompany(Guid companyId) : ICurrentCompanyAccessor
    {
        public bool HasCompany => true;
        public Guid CompanyId => companyId;
        public string Code => "TEST";
        public string Database => throw new NotSupportedException();
        public string ServiceLayerUrl => throw new NotSupportedException();
        public string Country => throw new NotSupportedException();
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Username => "jmunoz";
        public bool IsAdmin => true;
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static (GenericImportJobService Service, FakeQueue Queue, PortalSaasDbContext Db, GenericImportProgressStore Progress) Create(string name, Guid? companyId = null)
    {
        var db = new PortalSaasDbContext(new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(name).Options);
        var queue = new FakeQueue();
        var progress = new GenericImportProgressStore();
        return (new GenericImportJobService(db, new FakeCompany(companyId ?? CompanyId), new FakeUser(), queue, progress), queue, db, progress);
    }

    private static readonly byte[] File1 = [1, 2, 3, 4];

    [Fact]
    public async Task Borrador_guarda_archivo_y_parametros_y_no_aparece_en_el_listado()
    {
        var (service, _, db, _) = Create(nameof(Borrador_guarda_archivo_y_parametros_y_no_aparece_en_el_listado));
        await using var _db = db;

        var id = await service.CreateDraftAsync(Parameters, @"C:\fakepath\orden.xlsx", File1);

        Assert.Equal(File1, await service.GetFileAsync(id));
        var job = await service.GetAsync(id);
        Assert.Equal(GenericImportJobStatus.Draft, job!.Status);
        Assert.Equal("orden.xlsx", job.FileName);
        Assert.Equal(Parameters, job.Parameters);
        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task Encolar_pasa_a_Queued_y_lo_entrega_al_worker_una_sola_vez()
    {
        var (service, queue, db, _) = Create(nameof(Encolar_pasa_a_Queued_y_lo_entrega_al_worker_una_sola_vez));
        await using var _db = db;
        var id = await service.CreateDraftAsync(Parameters, "orden.xlsx", File1);

        await service.EnqueueAsync(id, Parameters with { SalesPersonCode = 99 });

        var job = await service.GetAsync(id);
        Assert.Equal(GenericImportJobStatus.Queued, job!.Status);
        Assert.Equal(99, job.Parameters.SalesPersonCode);
        Assert.Equal([id], queue.Enqueued);

        // Segundo "Confirmar" del mismo trabajo: rechazado.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync(id, Parameters));
        Assert.Single(queue.Enqueued);
    }

    [Fact]
    public async Task Mismo_archivo_con_otro_trabajo_en_curso_se_rechaza()
    {
        var (service, _, db, _) = Create(nameof(Mismo_archivo_con_otro_trabajo_en_curso_se_rechaza));
        await using var _db = db;
        var first = await service.CreateDraftAsync(Parameters, "orden.xlsx", File1);
        await service.EnqueueAsync(first, Parameters);

        var second = await service.CreateDraftAsync(Parameters, "orden-copia.xlsx", File1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync(second, Parameters));
        Assert.Contains("en curso", ex.Message);
    }

    [Fact]
    public async Task Trabajo_de_otra_compania_no_se_ve()
    {
        var name = nameof(Trabajo_de_otra_compania_no_se_ve);
        var (service, _, db, _) = Create(name);
        await using var _db = db;
        var id = await service.CreateDraftAsync(Parameters, "orden.xlsx", File1);

        var (otherCompany, _, otherDb, _) = Create(name, Guid.NewGuid());
        await using var _otherDb = otherDb;

        Assert.Null(await otherCompany.GetAsync(id));
        Assert.Null(await otherCompany.GetFileAsync(id));
    }

    [Fact]
    public async Task Progreso_terminado_sale_de_la_base_con_el_reporte()
    {
        var (service, _, db, _) = Create(nameof(Progreso_terminado_sale_de_la_base_con_el_reporte));
        await using var _db = db;
        var id = await service.CreateDraftAsync(Parameters, "orden.xlsx", File1);
        var job = await db.GenericImportJobs.SingleAsync(j => j.Id == id);
        job.Status = GenericImportJobStatus.Completed;
        job.ResultMessage = "Proceso completado: 1 pedido(s) creado(s).";
        job.ResultJson = JsonSerializer.Serialize(new List<GenericImportDocumentResultDto>
        {
            new("OC-1", true, 5512, "Documento creado correctamente.") { DocEntry = 901, Units = 10 },
        }, GenericImportJobService.ResultJsonOptions);
        job.DocumentsCreated = 1;
        await db.SaveChangesAsync();

        var progress = await service.GetProgressAsync(id);

        Assert.True(progress!.Finished);
        Assert.True(progress.Success);
        var result = Assert.Single(progress.Results!);
        Assert.Equal(5512, result.DocNum);
        Assert.Equal(901, result.DocEntry);
        Assert.Equal("Created", result.Status);
    }

    [Fact]
    public async Task Progreso_en_curso_sale_de_la_memoria()
    {
        var (service, _, db, progressStore) = Create(nameof(Progreso_en_curso_sale_de_la_memoria));
        await using var _db = db;
        var id = await service.CreateDraftAsync(Parameters, "orden.xlsx", File1);
        var job = await db.GenericImportJobs.SingleAsync(j => j.Id == id);
        job.Status = GenericImportJobStatus.Running;
        await db.SaveChangesAsync();
        progressStore.Update(id.ToString(), new GenericImportProgressDto("Creando documento 3 de 10...", 3, 10, true, null, false));

        var progress = await service.GetProgressAsync(id);

        Assert.Equal(3, progress!.Current);
        Assert.False(progress.Finished);
    }

    // ------------------------------------------------ consultas por tramos

    [Fact]
    public async Task Listas_grandes_se_consultan_en_tramos_de_500()
    {
        var keys = Enumerable.Range(1, 1234).Select(i => $"A{i}").ToList();
        var calls = new List<int>();

        var result = await CatalogSqlHelper.InChunksAsync(keys, chunk =>
        {
            calls.Add(chunk.Count);
            return Task.FromResult<IReadOnlyDictionary<string, int>>(chunk.ToDictionary(k => k, k => 1));
        });

        Assert.Equal([500, 500, 234], calls);
        Assert.Equal(1234, result.Count);
    }
}
