using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica.Reglas;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica.Reglas;

public sealed class DuplicateOrderInSapRuleTests
{
    /// <summary>Devuelve filas fijas a QueryDynamicAsync y guarda el SQL recibido.</summary>
    private sealed class FakeHana(params (string DocNum, string CardCode, string Reference)[] existing) : IHanaService
    {
        public string? LastSql { get; private set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
        {
            LastSql = sqlParametrizado;
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = existing
                .Select(e => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["DocNum"] = e.DocNum, ["CardCode"] = e.CardCode, ["Reference"] = e.Reference })
                .ToList();
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static GenericImportRowDto Row(int rowNumber, string oc, string cardCode) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = oc,
        CustomerReferenceNumber = oc,
        BusinessPartnerCardCode = cardCode,
        IsValid = true,
    };

    private static readonly Dictionary<string, object?> SalesOrder = new() { [DuplicateOrderInSapRule.DocumentTypeParameter] = "SalesOrder" };

    [Fact]
    public async Task OC_ya_cargada_para_el_mismo_socio_marca_sus_lineas()
    {
        var hana = new FakeHana(("5512", "C77398220-1", "4000158406"));
        var rows = new[] { Row(1, "4000158406", "C77398220-1"), Row(2, "4000158406", "C77398220-1"), Row(3, "4000158999", "C77398220-1") };

        var result = await new DuplicateOrderInSapRule(hana).ValidateAsync(rows, GenericImportModule.Sales, SalesOrder, CancellationToken.None);

        Assert.Equal([1, 2], result.Keys.Order());
        Assert.Contains("documento N° 5512", result[1][0]);
        Assert.Contains("\"ORDR\"", hana.LastSql);
        Assert.Contains("\"NumAtCard\"", hana.LastSql);
        Assert.Contains("\"CANCELED\" = 'N'", hana.LastSql);
    }

    [Fact]
    public async Task Misma_OC_de_otro_socio_no_es_duplicado()
    {
        var hana = new FakeHana(("5512", "C-OTRO", "4000158406"));

        var result = await new DuplicateOrderInSapRule(hana).ValidateAsync([Row(1, "4000158406", "C77398220-1")],
            GenericImportModule.Sales, SalesOrder, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public void Inventario_busca_en_U_NumAtCard_de_su_tabla()
    {
        Assert.Equal(("OWTQ", "U_NumAtCard"), DuplicateOrderInSapRule.SourceOf(GenericImportModule.Inventory, "InventoryTransferRequest"));
        Assert.Equal(("OPOR", "NumAtCard"), DuplicateOrderInSapRule.SourceOf(GenericImportModule.Purchase, "PurchaseOrder"));
    }
}
