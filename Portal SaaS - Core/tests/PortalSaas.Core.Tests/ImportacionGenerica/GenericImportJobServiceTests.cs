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

    // ------------------------------------------------ guardar sin enviar (borradores etapa 2)

    [Fact]
    public async Task Guardar_pasa_a_Saved_con_motivo_y_parametros_y_aparece_en_el_listado()
    {
        var (service, queue, db, _) = Create(nameof(Guardar_pasa_a_Saved_con_motivo_y_parametros_y_aparece_en_el_listado));
        await using var _db = db;
        var id = await service.CreateDraftAsync(Parameters, "orden.xlsx", File1);

        await service.SaveAsync(id, Parameters with { SalesPersonCode = 33 }, "  Corregir precios en SAP  ");

        var job = Assert.Single(await service.ListAsync());
        Assert.Equal(GenericImportJobStatus.Saved, job.Status);
        Assert.True(job.IsSaved);
        Assert.Equal("Corregir precios en SAP", job.SavedReason);
        Assert.NotNull(job.SavedAt);
        Assert.Equal(33, job.Parameters.SalesPersonCode);
        Assert.Empty(queue.Enqueued);

        // Guardar dos veces: rechazado.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(id, Parameters, "otra vez"));
    }

    [Fact]
    public async Task Guardar_sin_motivo_se_rechaza()
    {
        var (service, _, db, _) = Create(nameof(Guardar_sin_motivo_se_rechaza));
        await using var _db = db;
        var id = await service.CreateDraftAsync(Parameters, "orden.xlsx", File1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(id, Parameters, "   "));
        Assert.Equal(GenericImportJobStatus.Draft, (await service.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Guardada_la_envia_otro_usuario_con_los_parametros_guardados()
    {
        var name = nameof(Guardada_la_envia_otro_usuario_con_los_parametros_guardados);
        var (creator, _, db, _) = Create(name);
        await using var _db = db;
        var id = await creator.CreateDraftAsync(Parameters, "orden.xlsx", File1);
        await creator.SaveAsync(id, Parameters, "Esperando confirmación");

        // Otro usuario (otro FakeUser) la envía; los parámetros que mande se ignoran (sin edición).
        var otherDb = new PortalSaasDbContext(new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(name).Options);
        await using var _otherDb = otherDb;
        var otherQueue = new FakeQueue();
        var sender = new GenericImportJobService(otherDb, new FakeCompany(CompanyId), new FakeUserNamed("pedro"), otherQueue, new GenericImportProgressStore());

        await sender.EnqueueAsync(id, Parameters with { SalesPersonCode = 999 });

        var stored = await otherDb.GenericImportJobs.AsNoTracking().SingleAsync(j => j.Id == id);
        Assert.Equal(GenericImportJobStatus.Queued, stored.Status);
        Assert.Equal(12, stored.SalesPersonCode);
        Assert.Equal("pedro", stored.SubmittedByUsername);
        Assert.Equal([id], otherQueue.Enqueued);
        Assert.Equal("pedro", (await sender.GetAsync(id))!.SubmittedByUsername);
    }

    [Fact]
    public async Task Mismo_archivo_guardado_bloquea_enviar_o_guardar_otro_trabajo()
    {
        var (service, _, db, _) = Create(nameof(Mismo_archivo_guardado_bloquea_enviar_o_guardar_otro_trabajo));
        await using var _db = db;
        var saved = await service.CreateDraftAsync(Parameters, "orden.xlsx", File1);
        await service.SaveAsync(saved, Parameters, "Corregir stock");

        var again = await service.CreateDraftAsync(Parameters, "orden-de-nuevo.xlsx", File1);

        var send = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync(again, Parameters));
        Assert.Contains("guardado", send.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(again, Parameters, "duplicado"));
    }

    [Fact]
    public async Task Descartar_elimina_solo_cargas_guardadas()
    {
        var (service, _, db, _) = Create(nameof(Descartar_elimina_solo_cargas_guardadas));
        await using var _db = db;
        var draft = await service.CreateDraftAsync(Parameters, "a.xlsx", File1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DiscardAsync(draft));

        await service.SaveAsync(draft, Parameters, "No va todavía");
        await service.DiscardAsync(draft);

        Assert.Null(await service.GetAsync(draft));
    }

    [Fact]
    public async Task Listado_trae_las_guardadas_aunque_queden_fuera_del_tope()
    {
        var (service, _, db, _) = Create(nameof(Listado_trae_las_guardadas_aunque_queden_fuera_del_tope));
        await using var _db = db;
        var old = await service.CreateDraftAsync(Parameters, "vieja.xlsx", [9, 9]);
        await service.SaveAsync(old, Parameters, "Esperando cliente");
        (await db.GenericImportJobs.SingleAsync(j => j.Id == old)).CreatedAt = DateTimeOffset.UtcNow.AddDays(-10);
        for (var i = 0; i < 3; i++)
        {
            var sent = await service.CreateDraftAsync(Parameters, $"enviada{i}.xlsx", [(byte)i]);
            await service.EnqueueAsync(sent, Parameters);
        }
        await db.SaveChangesAsync();

        var list = await service.ListAsync(take: 2);

        Assert.Equal(3, list.Count);
        Assert.Contains(list, j => j.Id == old && j.IsSaved);
    }

    [Fact]
    public void Clave_de_marca_es_deterministica_corta_y_distinta_por_documento()
    {
        var job = Guid.NewGuid();

        var a = GenericImportService.MarkerKey(job, "OC-1");

        Assert.Equal(a, GenericImportService.MarkerKey(job, "OC-1"));
        Assert.NotEqual(a, GenericImportService.MarkerKey(job, "OC-2"));
        Assert.NotEqual(a, GenericImportService.MarkerKey(Guid.NewGuid(), "OC-1"));
        Assert.True(a.Length <= 50);
    }

    private sealed class FakeUserNamed(string username) : ICurrentUserContext
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Username => username;
        public bool IsAdmin => true;
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
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
