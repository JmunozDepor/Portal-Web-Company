using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ReceiptSuggestionServiceTests
{
    private static readonly Guid Company = Guid.NewGuid();

    [Fact]
    public async Task Derives_net_and_iva_for_an_afecta_boleta_without_breakdown()
    {
        var ocr = Ocr(amount: 11900m, siiCode: ChileanDocumentKind.BoletaAfecta);
        var sut = Build();

        var p = await sut.BuildAsync(Company, ocr);

        Assert.Equal(10000m, p.NetAmount);
        Assert.Equal(1900m, p.TaxAmount);
    }

    [Fact]
    public async Task Does_not_touch_breakdown_when_ocr_already_has_tax()
    {
        var ocr = Ocr(amount: 11900m, tax: 1900m, net: 10000m, siiCode: ChileanDocumentKind.BoletaAfecta);
        var sut = Build();

        var p = await sut.BuildAsync(Company, ocr);

        Assert.Equal(10000m, p.NetAmount);
        Assert.Equal(1900m, p.TaxAmount);
    }

    [Fact]
    public async Task Preselects_category_when_supplier_memory_is_confident()
    {
        var tipos = new[] { Type(5, "Almuerzo"), Type(6, "Peaje") };
        var hint = new SupplierHint { CompanyId = Company, SupplierTaxId = "76192083-9", DefaultExpenseTypeId = 5, TimesCategoryConfirmed = 3 };
        var sut = Build(tipos: tipos, hint: hint);

        var p = await sut.BuildAsync(Company, Ocr(rut: "76.192.083-9"));

        Assert.Equal(5, p.ExpenseTypeId);
        Assert.Null(p.SuggestedExpenseTypeId);
        Assert.Equal("mixed", p.CaptureSource);
    }

    [Fact]
    public async Task Only_suggests_category_when_memory_is_weak()
    {
        var tipos = new[] { Type(5, "Almuerzo") };
        var hint = new SupplierHint { CompanyId = Company, SupplierTaxId = "76192083-9", DefaultExpenseTypeId = 5, TimesCategoryConfirmed = 1 };
        var sut = Build(tipos: tipos, hint: hint);

        var p = await sut.BuildAsync(Company, Ocr(rut: "76.192.083-9"));

        Assert.Null(p.ExpenseTypeId);
        Assert.Equal(5, p.SuggestedExpenseTypeId);
    }

    [Fact]
    public async Task Maps_model_category_text_to_an_expense_type_when_no_memory()
    {
        var tipos = new[] { Type(5, "Almuerzo"), Type(7, "Combustible") };
        var sut = Build(tipos: tipos);

        var p = await sut.BuildAsync(Company, Ocr() with { SuggestedCategoryText = "combustible" });

        Assert.Null(p.ExpenseTypeId);
        Assert.Equal(7, p.SuggestedExpenseTypeId);
        Assert.Equal("document", p.CaptureSource);
    }

    [Fact]
    public async Task Fills_supplier_name_from_memory_when_ocr_missing_it()
    {
        var hint = new SupplierHint { CompanyId = Company, SupplierTaxId = "76192083-9", SupplierName = "Comercial Ejemplo SpA" };
        var sut = Build(hint: hint);

        var p = await sut.BuildAsync(Company, Ocr(rut: "76.192.083-9", supplierName: null));

        Assert.Equal("Comercial Ejemplo SpA", p.SupplierName);
        Assert.Equal("mixed", p.CaptureSource);
    }

    [Fact]
    public async Task Normalizes_rut_and_survives_an_invalid_one()
    {
        var sut = Build();

        var ok = await sut.BuildAsync(Company, Ocr(rut: "76.192.083-9"));
        Assert.Equal("76192083-9", ok.SupplierTaxId);

        var bad = await sut.BuildAsync(Company, Ocr(rut: "76.192.083-1")); // DV incorrecto
        Assert.Equal("76.192.083-1", bad.SupplierTaxId); // se deja tal cual, sin reventar
    }

    [Fact]
    public async Task Passes_through_ocr_error()
    {
        var sut = Build();
        var p = await sut.BuildAsync(Company, new ExtractedReceiptDto(null, null, null, null, null, null, null, Error: "boom"));
        Assert.Equal("boom", p.Error);
    }

    private static ExtractedReceiptDto Ocr(decimal? amount = null, decimal? tax = null, decimal? net = null,
        int? siiCode = null, string? rut = "76.192.083-9", string? supplierName = "Proveedor OCR") =>
        new(amount, tax, new DateTime(2026, 2, 14), "000123", rut, supplierName, null,
            Error: null, TransactionTime: new TimeOnly(13, 5), NetAmount: net, ExemptAmount: null,
            SiiDocumentCode: siiCode, Items: null, SuggestedCategoryText: null, Source: ReceiptSource.GeminiOcr);

    private static ExpenseType Type(long id, string name) => new()
    {
        Id = id, CompanyId = Company, Name = name,
    };

    private static ReceiptSuggestionService Build(ExpenseType[]? tipos = null, SupplierHint? hint = null,
        DocumentType? docBySii = null) =>
        new(new StubHints(hint), new StubExpenseTypes(tipos ?? Array.Empty<ExpenseType>()),
            new StubDocumentTypes(docBySii), NullLogger<ReceiptSuggestionService>.Instance);

    private sealed class StubHints : ISupplierHintService
    {
        private readonly SupplierHint? _hint;
        public StubHints(SupplierHint? hint) => _hint = hint;

        public Task<SupplierHint?> GetAsync(Guid companyId, string normalizedRut, CancellationToken ct = default) =>
            Task.FromResult(_hint is not null && _hint.SupplierTaxId == normalizedRut ? _hint : null);

        public Task RegisterAsync(Guid companyId, string? rawRut, string? supplierName, long? expenseTypeId, long? documentTypeId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubExpenseTypes : IExpenseTypeService
    {
        private readonly IReadOnlyList<ExpenseType> _tipos;
        public StubExpenseTypes(IReadOnlyList<ExpenseType> tipos) => _tipos = tipos;

        public Task<IReadOnlyList<ExpenseType>> ListActiveAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult(_tipos);
        public Task<IReadOnlyList<ExpenseType>> ListAllAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult(_tipos);
        public Task<long> CreateAsync(Guid companyId, string name, string? sapGlAccount, bool isMileage, decimal? ratePerKm, CancellationToken ct = default) => Task.FromResult(1L);
        public Task UpdateAsync(long id, Guid companyId, string name, string? sapGlAccount, bool isActive, bool isMileage, decimal? ratePerKm, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubDocumentTypes : IDocumentTypeService
    {
        private readonly DocumentType? _bySii;
        public StubDocumentTypes(DocumentType? bySii) => _bySii = bySii;

        public Task<IReadOnlyList<DocumentType>> ListActiveAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DocumentType>>(Array.Empty<DocumentType>());
        public Task<IReadOnlyList<DocumentType>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DocumentType>>(Array.Empty<DocumentType>());
        public Task<DocumentType?> FindBySiiCodeAsync(Guid companyId, int siiCode, CancellationToken ct = default) =>
            Task.FromResult(_bySii);
        public Task<long> CreateAsync(Guid companyId, string name, bool appliesTax, decimal taxPercentage, CancellationToken ct = default) => Task.FromResult(1L);
        public Task UpdateAsync(long id, Guid companyId, string name, bool appliesTax, decimal taxPercentage, bool isActive, CancellationToken ct = default) => Task.CompletedTask;
    }
}
