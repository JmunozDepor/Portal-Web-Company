using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Tests;

public class SapProductSyncServiceTests
{
    private static AuditoriaInventarioDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<AuditoriaInventarioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task SincronizarAsync_CreaProductosNuevosDesdeSap()
    {
        var db = CrearContexto();
        var companyId = Guid.NewGuid();
        var hana = new FakeHanaService(
            [
                new SapProductMasterRow { ItemCode = "SKU-1", ItemName = "Zapatilla Running", CodeBars = "111" },
                new SapProductMasterRow { ItemCode = "SKU-2", ItemName = "Polera", CodeBars = "222" },
            ]);
        var servicio = new SapProductSyncService(hana, db);

        var resultado = await servicio.SincronizarAsync(companyId);

        Assert.Equal(2, resultado.Creados);
        Assert.Equal(0, resultado.Actualizados);
        Assert.Equal(0, resultado.SinBarcode);
        Assert.Equal(2, resultado.TotalSap);
        var productos = await db.Products.Where(p => p.CompanyId == companyId).ToListAsync();
        Assert.Equal(2, productos.Count);
        Assert.Contains(productos, p => p.Barcode == "111" && p.ProductCode == "SKU-1" && p.Source == "INTEGRACION");
    }

    [Fact]
    public async Task SincronizarAsync_ActualizaUnProductoExistenteEnVezDeDuplicar()
    {
        var db = CrearContexto();
        var companyId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            CompanyId = companyId, Barcode = "111", ProductCode = "VIEJO", Description = "desc vieja", Source = "MANUAL",
        });
        await db.SaveChangesAsync();

        var hana = new FakeHanaService(
            [new SapProductMasterRow { ItemCode = "SKU-1", ItemName = "Nombre nuevo", CodeBars = "111" }]);
        var servicio = new SapProductSyncService(hana, db);

        var resultado = await servicio.SincronizarAsync(companyId);

        Assert.Equal(0, resultado.Creados);
        Assert.Equal(1, resultado.Actualizados);
        var producto = await db.Products.SingleAsync(p => p.CompanyId == companyId && p.Barcode == "111");
        Assert.Equal("SKU-1", producto.ProductCode);
        Assert.Equal("Nombre nuevo", producto.Description);
        Assert.Equal("INTEGRACION", producto.Source);
    }

    [Fact]
    public async Task SincronizarAsync_ArticulosSinCodeBarsSeCuentanPeroNoSeCargan()
    {
        var db = CrearContexto();
        var companyId = Guid.NewGuid();
        var hana = new FakeHanaService(
            [
                new SapProductMasterRow { ItemCode = "SKU-1", ItemName = "Con barra", CodeBars = "111" },
                new SapProductMasterRow { ItemCode = "SKU-2", ItemName = "Sin barra", CodeBars = null },
                new SapProductMasterRow { ItemCode = "SKU-3", ItemName = "Barra vacia", CodeBars = "" },
            ]);
        var servicio = new SapProductSyncService(hana, db);

        var resultado = await servicio.SincronizarAsync(companyId);

        Assert.Equal(1, resultado.Creados);
        Assert.Equal(2, resultado.SinBarcode);
        Assert.Equal(3, resultado.TotalSap);
    }

    [Fact]
    public async Task SincronizarAsync_DosArticulosSapConElMismoCodeBarsNoRompenElIndiceUnico()
    {
        var db = CrearContexto();
        var companyId = Guid.NewGuid();
        // Dato sucio de SAP: dos ItemCode distintos comparten CodeBars. Sin el dedupe
        // dentro del lote, el segundo Add() con el mismo (companyId, barcode) que el
        // primero (ambos nuevos, ninguno en `existentes` todavía) revienta el índice
        // único al hacer SaveChangesAsync.
        var hana = new FakeHanaService(
            [
                new SapProductMasterRow { ItemCode = "SKU-1", ItemName = "Primero", CodeBars = "999" },
                new SapProductMasterRow { ItemCode = "SKU-2", ItemName = "Segundo (gana)", CodeBars = "999" },
            ]);
        var servicio = new SapProductSyncService(hana, db);

        var resultado = await servicio.SincronizarAsync(companyId);

        Assert.Equal(1, resultado.Creados);
        var producto = await db.Products.SingleAsync(p => p.CompanyId == companyId && p.Barcode == "999");
        Assert.Equal("SKU-2", producto.ProductCode);
    }

    [Fact]
    public async Task SincronizarAsync_ItemNameNuloEnSapNoRevientaElLote()
    {
        // Dato real de SAP (2026-09-29): OITM.ItemName puede venir NULL para algunos
        // articulos -- el DTO lo declara no-nullable (caso comun) pero el mapeo por
        // reflection igual asigna null si la columna lo es.
        var db = CrearContexto();
        var companyId = Guid.NewGuid();
        var hana = new FakeHanaService(
            [new SapProductMasterRow { ItemCode = "SKU-1", ItemName = null!, CodeBars = "111" }]);
        var servicio = new SapProductSyncService(hana, db);

        var resultado = await servicio.SincronizarAsync(companyId);

        Assert.Equal(1, resultado.Creados);
        var producto = await db.Products.SingleAsync(p => p.CompanyId == companyId && p.Barcode == "111");
        Assert.Equal(string.Empty, producto.Description);
    }

    [Fact]
    public async Task SincronizarAsync_PaginaHastaQueSapDevuelveUnLoteVacio()
    {
        var db = CrearContexto();
        var companyId = Guid.NewGuid();
        var hana = new FakeHanaService(
            [new SapProductMasterRow { ItemCode = "SKU-1", ItemName = "Pagina 1", CodeBars = "111" }],
            [new SapProductMasterRow { ItemCode = "SKU-2", ItemName = "Pagina 2", CodeBars = "222" }],
            []);
        var servicio = new SapProductSyncService(hana, db);

        var resultado = await servicio.SincronizarAsync(companyId);

        Assert.Equal(2, resultado.Creados);
        Assert.Equal(2, resultado.TotalSap);
        // 2 paginas con datos + la pagina vacia que corta el loop.
        Assert.Equal(3, hana.Llamadas);
    }

    /// <summary>Doble de prueba manual (sin librería de mocking, mismo criterio que el resto del proyecto de tests).</summary>
    private sealed class FakeHanaService : IHanaService
    {
        private readonly Queue<IReadOnlyList<SapProductMasterRow>> _paginas;
        public int Llamadas { get; private set; }

        public FakeHanaService(params IReadOnlyList<SapProductMasterRow>[] paginas)
        {
            _paginas = new Queue<IReadOnlyList<SapProductMasterRow>>(paginas);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
        {
            Llamadas++;
            var pagina = _paginas.Count > 0 ? _paginas.Dequeue() : Array.Empty<SapProductMasterRow>();
            return Task.FromResult((IReadOnlyList<T>)(object)pagina);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(
            string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default) => Task.FromResult("hana");
    }
}
