using System.Linq;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Integraciones;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class SapDocumentConnectorTests
{
    private sealed class SalesDocumentServiceFalso : ISalesDocumentService
    {
        public Task<bool> CanCreateAsync(SalesDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(SalesDocumentType type, string portalUsername, SalesDocumentDto document, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task AddLinesAsync(SalesDocumentType type, int docEntry, IReadOnlyList<SalesDocumentLineDto> newLines, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<SalesDocumentDto?> GetAsync(SalesDocumentType type, int docEntry, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<SalesDocumentListResult> ListAsync(SalesDocumentType type, SalesDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public int GetSapObjectCode(SalesDocumentType type)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private sealed class PurchaseDocumentServiceFalso : IPurchaseDocumentService
    {
        public Task<bool> CanCreateAsync(PurchaseDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(PurchaseDocumentType type, string portalUsername, PurchaseDocumentDto document, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task AddLinesAsync(PurchaseDocumentType type, int docEntry, IReadOnlyList<PurchaseDocumentLineDto> newLines, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<PurchaseDocumentDto?> GetAsync(PurchaseDocumentType type, int docEntry, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<PurchaseDocumentListResult> ListAsync(PurchaseDocumentType type, PurchaseDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public int GetSapObjectCode(PurchaseDocumentType type)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private sealed class InventoryDocumentServiceFalso : IInventoryDocumentService
    {
        public Task<bool> CanCreateAsync(InventoryDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(InventoryDocumentType type, string portalUsername, InventoryDocumentDto document, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task AddLinesAsync(InventoryDocumentType type, int docEntry, IReadOnlyList<InventoryDocumentLineDto> newLines, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<InventoryDocumentDto?> GetAsync(InventoryDocumentType type, int docEntry, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<InventoryDocumentListResult> ListAsync(InventoryDocumentType type, InventoryDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public int GetSapObjectCode(InventoryDocumentType type)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private sealed class SapConnectionProviderFalso : ISapConnectionProvider
    {
        private readonly ISapSession _sesion;

        public SapConnectionProviderFalso(ISapSession sesion)
        {
            _sesion = sesion;
        }

        public Task<ISapSession> GetConnectionAsync(CancellationToken ct = default) => Task.FromResult(_sesion);
    }

    private sealed class SapSessionFalsa : ISapSession
    {
        private readonly object? _resultado;

        public SapSessionFalsa(object? resultado)
        {
            _resultado = resultado;
        }

        public Task<T?> GetAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, CancellationToken ct = default)
            => Task.FromResult((T?)_resultado);

        public Task<IReadOnlyList<T>> GetAllAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, CancellationToken ct = default)
        {
            // Simula lo que B1SLayer.SLRequest.GetAllAsync<T>() ya hace internamente:
            // agota la paginación de Service Layer (odata.nextLink) y devuelve TODAS
            // las filas de todas las páginas en una sola lista -- acá el fake ya recibe
            // el resultado "acumulado" (ej. varias páginas concatenadas por el test).
            var lista = (IReadOnlyList<T>?)_resultado ?? Array.Empty<T>();
            return Task.FromResult(lista);
        }

        public Task<T?> PostAsync<T>(string recurso, object cuerpo, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");

        public Task PatchAsync(string recurso, object clave, object cuerpo, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");

        public Task DeleteAsync(string recurso, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private sealed class SapSessionFalsaParaPicking : ISapSession
    {
        private readonly IReadOnlyList<SapWmsPickListRow> _pickLists;
        private readonly Dictionary<int, SapWmsOrderBaseRow> _ordenesPorDocEntry;

        public Dictionary<string, int> LlamadasPorRecurso { get; } = new();

        public SapSessionFalsaParaPicking(List<SapWmsPickListRow> pickLists, List<SapWmsOrderBaseRow> ordenes)
        {
            _pickLists = pickLists;
            _ordenesPorDocEntry = ordenes.ToDictionary(o => o.DocEntry);
        }

        public Task<T?> GetAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, CancellationToken ct = default)
        {
            // recurso llega como "Orders(500)" -- se extrae el nombre base para contar
            // llamadas por recurso y el DocEntry para ubicar la orden falsa.
            var nombreRecurso = recurso.Split('(')[0];
            LlamadasPorRecurso[nombreRecurso] = LlamadasPorRecurso.GetValueOrDefault(nombreRecurso) + 1;

            var docEntryTexto = recurso.Contains('(')
                ? recurso.Substring(recurso.IndexOf('(') + 1).TrimEnd(')')
                : null;

            if (docEntryTexto is not null && int.TryParse(docEntryTexto, out var docEntry) &&
                _ordenesPorDocEntry.TryGetValue(docEntry, out var orden))
            {
                return Task.FromResult((T?)(object?)orden);
            }

            return Task.FromResult(default(T));
        }

        public Task<IReadOnlyList<T>> GetAllAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, CancellationToken ct = default)
        {
            LlamadasPorRecurso[recurso] = LlamadasPorRecurso.GetValueOrDefault(recurso) + 1;

            if (recurso == "PickLists")
            {
                return Task.FromResult((IReadOnlyList<T>)(object)_pickLists);
            }

            return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
        }

        public Task<T?> PostAsync<T>(string recurso, object cuerpo, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");

        public Task PatchAsync(string recurso, object clave, object cuerpo, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");

        public Task DeleteAsync(string recurso, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private static SapDocumentConnector CrearConector(ISapConnectionProvider? proveedorSap = null)
        => new(
            new SalesDocumentServiceFalso(),
            new PurchaseDocumentServiceFalso(),
            new InventoryDocumentServiceFalso(),
            proveedorSap ?? new SapConnectionProviderFalso(new SapSessionFalsa(null)));

    [Fact]
    public void Tipo_EsSap()
    {
        var conector = CrearConector();

        Assert.Equal("Sap", conector.Tipo);
    }

    [Fact]
    public async Task PullAsync_TipoEntidadItem_ConsultaServiceLayerYMapeaCampos()
    {
        var filas = new List<SapWmsItemRow>
        {
            new() { ItemCode = "ITM001", ItemName = "Artículo de prueba", CodeBars = "7801234567890", UpdateDate = new DateTime(2026, 8, 15) },
        };
        var sesionFalsa = new SapSessionFalsa(filas);
        var proveedorFalso = new SapConnectionProviderFalso(sesionFalsa);
        var conector = CrearConector(proveedorFalso);

        var config = """{"TipoEntidad":"Item"}""";
        var resultado = await conector.PullAsync(config, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("ITM001", registro["ItemCode"]);
        Assert.Equal("Artículo de prueba", registro["ItemName"]);
        Assert.Equal("7801234567890", registro["BarCode"]);
    }

    [Fact]
    public async Task PullAsync_TipoEntidadItem_ConMasFilasQueUnaSolaPaginaDeServiceLayer_TraeTodasLasFilas()
    {
        // Simula 2 "páginas" de Service Layer (por default ~20 filas cada una) ya
        // combinadas por GetAllAsync -- si PullAsync siguiera usando GetAsync<List<T>>
        // (una sola página), este total (25) nunca se vería reflejado completo.
        var primeraPagina = Enumerable.Range(1, 20)
            .Select(i => new SapWmsItemRow { ItemCode = $"ITM{i:000}", ItemName = $"Artículo {i}", CodeBars = $"780000000{i:0000}", UpdateDate = DateTime.Today });
        var segundaPagina = Enumerable.Range(21, 5)
            .Select(i => new SapWmsItemRow { ItemCode = $"ITM{i:000}", ItemName = $"Artículo {i}", CodeBars = $"780000000{i:0000}", UpdateDate = DateTime.Today });
        var filas = primeraPagina.Concat(segundaPagina).ToList();

        var sesionFalsa = new SapSessionFalsa(filas);
        var conector = CrearConector(new SapConnectionProviderFalso(sesionFalsa));

        var resultado = await conector.PullAsync("""{"TipoEntidad":"Item"}""", CancellationToken.None);

        Assert.Equal(25, resultado.Count);
    }

    [Fact]
    public async Task PullAsync_TipoEntidadItem_ExcluyeArticulosSinCodigoDeBarra()
    {
        var filas = new List<SapWmsItemRow>
        {
            new() { ItemCode = "ITM001", ItemName = "Con barra", CodeBars = "7801234567890", UpdateDate = DateTime.Today },
            new() { ItemCode = "ITM002", ItemName = "Sin barra", CodeBars = null, UpdateDate = DateTime.Today },
            new() { ItemCode = "ITM003", ItemName = "Barra cero", CodeBars = "0", UpdateDate = DateTime.Today },
        };
        var sesionFalsa = new SapSessionFalsa(filas);
        var conector = CrearConector(new SapConnectionProviderFalso(sesionFalsa));

        var resultado = await conector.PullAsync("""{"TipoEntidad":"Item"}""", CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("ITM001", registro["ItemCode"]);
    }

    [Fact]
    public async Task PullAsync_TipoEntidadStore_MapeaDireccionShipToYExcluyeSinShipTo()
    {
        var filas = new List<SapWmsStoreRow>
        {
            new()
            {
                CardCode = "C001",
                CardName = "Tienda Uno",
                UpdateDate = new DateTime(2026, 8, 1),
                BPAddresses = new List<SapWmsBpAddressRow>
                {
                    new() { AddressType = "bo_BillTo", Street = "Calle Facturación" },
                    new() { AddressType = "bo_ShipTo", Street = "Av. Siempreviva 742", City = "Springfield", ZipCode = "1234" },
                },
            },
            new()
            {
                CardCode = "C002",
                CardName = "Tienda Sin ShipTo",
                UpdateDate = DateTime.Today,
                BPAddresses = new List<SapWmsBpAddressRow>
                {
                    new() { AddressType = "bo_BillTo", Street = "Otra calle" },
                },
            },
        };
        var sesionFalsa = new SapSessionFalsa(filas);
        var conector = CrearConector(new SapConnectionProviderFalso(sesionFalsa));

        var resultado = await conector.PullAsync("""{"TipoEntidad":"Store"}""", CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("C001", registro["CardCode"]);
        Assert.Equal("Tienda Uno", registro["CardName"]);
        Assert.Equal("Av. Siempreviva 742", registro["Street"]);
        Assert.Equal("Springfield", registro["City"]);
        Assert.Equal("1234", registro["ZipCode"]);
    }

    [Fact]
    public async Task PullAsync_TipoEntidadInboundTraslado_MapeaCabeceraYLineasExcluyendoCantidadCero()
    {
        var filas = new List<SapWmsTrasladoRow>
        {
            new()
            {
                DocEntry = 1001,
                U_NX_shipment_type = "NORMAL",
                UpdateDate = new DateTime(2026, 8, 10),
                StockTransferLines = new List<SapWmsTrasladoLineaRow>
                {
                    new() { ItemCode = "ITEM-A", Quantity = 10, WarehouseCode = "01" },
                    new() { ItemCode = "ITEM-B", Quantity = 0, WarehouseCode = "02" },
                },
            },
        };
        var sesionFalsa = new SapSessionFalsa(filas);
        var conector = CrearConector(new SapConnectionProviderFalso(sesionFalsa));

        var resultado = await conector.PullAsync("""{"TipoEntidad":"InboundTraslado"}""", CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal(1001, registro["SapDocEntry"]);
        Assert.Equal("NORMAL", registro["ShipmentType"]);
        var lineas = Assert.IsType<List<IntegrationRecord>>(registro["Lineas"]);
        var linea = Assert.Single(lineas);
        Assert.Equal("ITEM-A", linea["ItemCode"]);
        Assert.Equal(10m, linea["Quantity"]);
        Assert.Equal("01", linea["WhsCode"]);
        Assert.Equal(0, linea["LineNum"]);
    }

    [Fact]
    public async Task PullAsync_TipoEntidadPicking_AgrupaPorDocumentoBaseYArmaRegistro()
    {
        // Nota: el test ilustrativo del brief traía un campo "Owner" en SapWmsPickListRow
        // para CardCode/CardName -- se descartó porque el diseño real (LeerPickingAsync)
        // saca esos campos del documento base (SapWmsOrderBaseRow), no de PickLists, igual
        // que el SP legado (join con OCRD/T4 vía el documento base, no la Lista de Picking).
        var pickList = new SapWmsPickListRow
        {
            AbsEntry = 100,
            PickDate = new DateTime(2026, 8, 16),
            UpdateDate = new DateTime(2026, 8, 16),
            U_NX_order_type = "VTA",
            PickListsLines = new List<SapWmsPickListLineRow>
            {
                new() { BaseObjectType = 17, OrderEntry = 500, OrderLine = 0, ReleasedQuantity = 5m },
            },
        };
        var ordenBase = new SapWmsOrderBaseRow
        {
            DocEntry = 500,
            CardCode = "C001",
            CardName = "Cliente de prueba",
            NumAtCard = "PO-1",
            CancelDate = null,
            DocDueDate = null,
            ShipToCode = "SHIP1",
            DocumentLines = new List<SapWmsOrderBaseLineRow> { new() { LineNum = 0, ItemCode = "ITM001", WarehouseCode = "01" } },
        };

        var sesionFalsa = new SapSessionFalsaParaPicking(
            pickLists: new List<SapWmsPickListRow> { pickList },
            ordenes: new List<SapWmsOrderBaseRow> { ordenBase });
        var proveedorFalso = new SapConnectionProviderFalso(sesionFalsa);
        var conector = CrearConector(proveedorFalso);

        var config = """{"TipoEntidad":"Picking"}""";
        var resultado = await conector.PullAsync(config, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("C001", registro["CardCode"]);
        Assert.Equal(500, registro["BaseEntry"]);
        var lineas = Assert.IsType<List<IntegrationRecord>>(registro["Lineas"]);
        var linea = Assert.Single(lineas);
        Assert.Equal("ITM001", linea["ItemCode"]);
        Assert.Equal(5m, linea["Quantity"]);
    }

    [Fact]
    public async Task PullAsync_TipoEntidadPicking_DosLineasDelMismoDocumentoBase_ConsultaUnaSolaVez()
    {
        var pickList = new SapWmsPickListRow
        {
            AbsEntry = 200,
            PickDate = new DateTime(2026, 8, 16),
            UpdateDate = new DateTime(2026, 8, 16),
            U_NX_order_type = "VTA",
            PickListsLines = new List<SapWmsPickListLineRow>
            {
                new() { BaseObjectType = 17, OrderEntry = 500, OrderLine = 0, ReleasedQuantity = 5m },
                new() { BaseObjectType = 17, OrderEntry = 500, OrderLine = 1, ReleasedQuantity = 3m },
            },
        };
        var ordenBase = new SapWmsOrderBaseRow
        {
            DocEntry = 500,
            CardCode = "C001",
            CardName = "Cliente de prueba",
            DocumentLines = new List<SapWmsOrderBaseLineRow>
            {
                new() { LineNum = 0, ItemCode = "ITM001", WarehouseCode = "01" },
                new() { LineNum = 1, ItemCode = "ITM002", WarehouseCode = "01" },
            },
        };

        var sesionFalsa = new SapSessionFalsaParaPicking(
            pickLists: new List<SapWmsPickListRow> { pickList },
            ordenes: new List<SapWmsOrderBaseRow> { ordenBase });
        var conector = CrearConector(new SapConnectionProviderFalso(sesionFalsa));

        var resultado = await conector.PullAsync("""{"TipoEntidad":"Picking"}""", CancellationToken.None);

        var registro = Assert.Single(resultado);
        var lineas = Assert.IsType<List<IntegrationRecord>>(registro["Lineas"]);
        Assert.Equal(2, lineas.Count);
        Assert.Equal(1, sesionFalsa.LlamadasPorRecurso.GetValueOrDefault("Orders"));
    }

    [Fact]
    public async Task PullAsync_TipoEntidadDesconocido_LanzaExcepcionClara()
    {
        var conector = CrearConector();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => conector.PullAsync("""{"TipoEntidad":"Desconocido"}""", CancellationToken.None));
        Assert.Contains("Desconocido", ex.Message);
    }

    [Fact]
    public async Task PushAsync_SinTipoDocumento_LanzaAggregateExceptionConNotSupportedExceptionAdentro()
    {
        var conector = CrearConector();
        var registros = new List<IntegrationRecord> { new(new Dictionary<string, object?>()) };

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
        var inner = Assert.Single(ex.InnerExceptions);
        Assert.IsType<NotSupportedException>(inner);
        Assert.Contains("TipoDocumento", inner.Message);
    }

    [Theory]
    [InlineData("Sales")]
    [InlineData("Purchase")]
    public async Task PushAsync_ConTipoDocumentoConocido_LanzaAggregateExceptionDeMapeoPendiente(string tipoDocumento)
    {
        var conector = CrearConector();
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = tipoDocumento }),
        };

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
        var inner = Assert.Single(ex.InnerExceptions);
        Assert.Contains("mapeo DTO pendiente", inner.Message);
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoDesconocido_LanzaAggregateExceptionExplicito()
    {
        var conector = CrearConector();
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = "Nomina" }),
        };

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
        var inner = Assert.Single(ex.InnerExceptions);
        Assert.Contains("Nomina", inner.Message);
    }

    [Fact]
    public async Task PushAsync_ConTipoInventoryYLineas_LlamaCreateAsyncConStockTransfer()
    {
        InventoryDocumentType? tipoUsado = null;
        InventoryDocumentDto? dtoUsado = null;
        var inventoryServiceFalso = new InventoryDocumentServiceCapturador((tipo, usuario, dto, ct) =>
        {
            tipoUsado = tipo;
            dtoUsado = dto;
            return Task.FromResult(999);
        });
        var conector = new SapDocumentConnector(new SalesDocumentServiceFalso(), new PurchaseDocumentServiceFalso(), inventoryServiceFalso, new SapConnectionProviderFalso(new SapSessionFalsa(null)));

        var lineas = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["ItemCode"] = "ITEM-A", ["Quantity"] = "10", ["BaseType"] = 1250000001, ["BaseEntry"] = 42, ["BaseLine"] = 0 }),
        };
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory", ["Lineas"] = lineas }),
        };

        var resultados = await conector.PushAsync("{}", registros, CancellationToken.None);

        Assert.Equal(InventoryDocumentType.StockTransfer, tipoUsado);
        Assert.NotNull(dtoUsado);
        Assert.Single(dtoUsado!.Lines);
        Assert.Equal("ITEM-A", dtoUsado.Lines[0].ItemCode);
        Assert.Equal(42, dtoUsado.Lines[0].BaseEntry);
        var resultado = Assert.Single(resultados);
        Assert.True(resultado.Exito);
    }

    [Fact]
    public async Task PushAsync_ConDocDateEnElRegistro_UsaEseValorEnVezDelFallback()
    {
        DateOnly? docDateUsado = null;
        var inventoryServiceFalso = new InventoryDocumentServiceCapturador((tipo, usuario, dto, ct) =>
        {
            docDateUsado = dto.DocDate;
            return Task.FromResult(999);
        });
        var conector = new SapDocumentConnector(new SalesDocumentServiceFalso(), new PurchaseDocumentServiceFalso(), inventoryServiceFalso, new SapConnectionProviderFalso(new SapSessionFalsa(null)));

        var docDateEsperado = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var lineas = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["ItemCode"] = "ITEM-A", ["Quantity"] = "10" }),
        };
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory", ["Lineas"] = lineas, ["DocDate"] = docDateEsperado }),
        };

        await conector.PushAsync("{}", registros, CancellationToken.None);

        Assert.Equal(DateOnly.FromDateTime(docDateEsperado), docDateUsado);
    }

    [Fact]
    public async Task PushAsync_QuantityComoStringInvarianteConDecimales_SeParseaCorrectamente()
    {
        decimal? cantidadUsada = null;
        var inventoryServiceFalso = new InventoryDocumentServiceCapturador((tipo, usuario, dto, ct) =>
        {
            cantidadUsada = dto.Lines[0].Quantity;
            return Task.FromResult(999);
        });
        var conector = new SapDocumentConnector(new SalesDocumentServiceFalso(), new PurchaseDocumentServiceFalso(), inventoryServiceFalso, new SapConnectionProviderFalso(new SapSessionFalsa(null)));

        var lineas = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["ItemCode"] = "ITEM-A", ["Quantity"] = "10.5" }),
        };
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory", ["Lineas"] = lineas }),
        };

        var resultados = await conector.PushAsync("{}", registros, CancellationToken.None);

        Assert.Equal(10.5m, cantidadUsada);
        Assert.True(Assert.Single(resultados).Exito);
    }

    [Fact]
    public async Task PushAsync_QuantityNoParseable_ResultadoFallidoParaEseRegistroSinTumbarElLote()
    {
        var inventoryServiceFalso = new InventoryDocumentServiceCapturador((tipo, usuario, dto, ct) => Task.FromResult(999));
        var conector = new SapDocumentConnector(new SalesDocumentServiceFalso(), new PurchaseDocumentServiceFalso(), inventoryServiceFalso, new SapConnectionProviderFalso(new SapSessionFalsa(null)));

        var lineasRegistroMalo = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["ItemCode"] = "ITEM-A", ["Quantity"] = "no-es-un-numero" }),
        };
        var lineasRegistroBueno = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["ItemCode"] = "ITEM-B", ["Quantity"] = "5" }),
        };
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory", ["Lineas"] = lineasRegistroMalo }),
            new(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory", ["Lineas"] = lineasRegistroBueno }),
        };

        var resultados = await conector.PushAsync("{}", registros, CancellationToken.None);

        Assert.Equal(2, resultados.Count);
        Assert.False(resultados[0].Exito);
        Assert.Contains("Quantity", resultados[0].MensajeError);
        Assert.True(resultados[1].Exito);
    }

    [Fact]
    public async Task PushAsync_ListaVacia_RetornaListaVaciaSinLanzar()
    {
        var conector = CrearConector();

        var resultados = await conector.PushAsync("{}", Array.Empty<IntegrationRecord>(), CancellationToken.None);

        Assert.Empty(resultados);
    }

    private sealed class InventoryDocumentServiceCapturador : IInventoryDocumentService
    {
        private readonly Func<InventoryDocumentType, string, InventoryDocumentDto, CancellationToken, Task<int>> _onCreate;

        public InventoryDocumentServiceCapturador(Func<InventoryDocumentType, string, InventoryDocumentDto, CancellationToken, Task<int>> onCreate)
        {
            _onCreate = onCreate;
        }

        public Task<bool> CanCreateAsync(InventoryDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(InventoryDocumentType type, string portalUsername, InventoryDocumentDto document, CancellationToken ct = default)
            => _onCreate(type, portalUsername, document, ct);
        public Task AddLinesAsync(InventoryDocumentType type, int docEntry, IReadOnlyList<InventoryDocumentLineDto> newLines, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<InventoryDocumentDto?> GetAsync(InventoryDocumentType type, int docEntry, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<InventoryDocumentListResult> ListAsync(InventoryDocumentType type, InventoryDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public int GetSapObjectCode(InventoryDocumentType type)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }
}
