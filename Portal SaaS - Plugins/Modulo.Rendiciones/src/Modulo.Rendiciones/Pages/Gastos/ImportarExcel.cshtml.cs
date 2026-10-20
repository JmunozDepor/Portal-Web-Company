using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Pages.Gastos;

/// <summary>
/// Import masivo de gastos digitados desde un Excel (sin OCR, sin comprobante): cada
/// fila crea un gasto Loose con ExpenseReceiptId null -- a diferencia de
/// Pages/Gastos/Importar (OCR, 1 archivo = 1 gasto con su comprobante ya adjunto), acá
/// el usuario tipea los datos en la planilla y después adjunta la foto/PDF de cada
/// boleta a mano, uno por uno, en Pages/Gastos/Detalle (mismo flujo que "Nuevo gasto"
/// manual). ExpenseReportService.AttachExpensesAsync exige que todo gasto tenga
/// comprobante antes de poder agregarlo a un informe, así que esta importación nunca
/// deja boletas "fantasma" sin respaldo.
/// </summary>
public sealed class ImportarExcelModel : RendicionesRendidorPageModelBase
{
    private const int MaxFilas = 200;

    private readonly IExpenseTypeService _expenseTypes;
    private readonly IDocumentTypeService _documentTypes;
    private readonly IExpenseService _expenses;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly INotificationService _notifications;
    private readonly ILogger<ImportarExcelModel> _logger;

    public ImportarExcelModel(IExpenseTypeService expenseTypes, IDocumentTypeService documentTypes, IExpenseService expenses,
        IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany,
        INotificationService notifications, ILogger<ImportarExcelModel> logger)
        : base(roles, currentUser, currentCompany)
    {
        _expenseTypes = expenseTypes;
        _documentTypes = documentTypes;
        _expenses = expenses;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
        _notifications = notifications;
        _logger = logger;
    }

    [BindProperty]
    public IFormFile? Archivo { get; set; }

    public List<ImportResult> Results { get; private set; } = new();

    public void OnGet()
    {
    }

    /// <summary>Plantilla descargable con los tipos de gasto/documento ACTIVOS de la compañía como listas
    /// desplegables -- evita el error más común (nombre de categoría mal escrito).</summary>
    public async Task<IActionResult> OnGetPlantillaAsync(CancellationToken ct)
    {
        var tiposGastoActivos = (await _expenseTypes.ListActiveAsync(_currentCompany.CompanyId, ct))
            .Where(t => !t.IsMileage) // kilometraje se calcula con ruta, no se importa por planilla
            .OrderBy(t => t.Name).ToList();
        var tiposDocActivos = (await _documentTypes.ListActiveAsync(_currentCompany.CompanyId, ct))
            .OrderBy(t => t.Name).ToList();
        var tiposGasto = tiposGastoActivos.Select(t => t.Name).ToList();
        var tiposDoc = tiposDocActivos.Select(t => t.Name).ToList();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Gastos");
        string[] encabezados =
        {
            "Fecha (dd-mm-aaaa)", "Tipo de gasto", "Tipo de documento", "Monto total",
            "IVA (opcional)", "Neto (opcional)", "Exento (opcional)", "N° Documento",
            "RUT Proveedor", "Proveedor", "Notas",
        };
        for (var i = 0; i < encabezados.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = encabezados[i];
            cell.Style.Font.Bold = true;
        }

        // Fila de ejemplo CON el formato real (todas las columnas llenas, con una
        // categoría/documento que sí existen) para que el usuario vea cómo llenarla --
        // resaltada y marcada en Notas para que no se le pase borrarla antes de importar.
        // Si la compañía todavía no tiene ningún tipo de gasto activo, no hay con qué
        // armar un ejemplo válido: se deja un texto que explica eso en vez de inventar uno.
        var filaEjemplo = ws.Range(2, 1, 2, encabezados.Length);
        filaEjemplo.Style.Fill.BackgroundColor = XLColor.LightYellow;

        if (tiposGastoActivos.Count > 0)
        {
            var tipoEjemplo = tiposGastoActivos[0];
            var docEjemplo = tiposDocActivos.FirstOrDefault();
            ws.Cell(2, 1).Value = DateTime.Today.ToString("dd-MM-yyyy");
            ws.Cell(2, 2).Value = tipoEjemplo.Name;
            ws.Cell(2, 3).Value = docEjemplo?.Name ?? "";
            ws.Cell(2, 4).Value = 15000;
            ws.Cell(2, 5).Value = 2395;
            ws.Cell(2, 6).Value = 12605;
            ws.Cell(2, 8).Value = "12345";
            ws.Cell(2, 9).Value = "76.123.456-7";
            ws.Cell(2, 10).Value = "Proveedor Ejemplo";
            ws.Cell(2, 11).Value = "EJEMPLO -- borrá esta fila antes de importar";
        }
        else
        {
            ws.Cell(2, 2).Value = "Primero creá un tipo de gasto activo en Configuración";
            ws.Cell(2, 11).Value = "EJEMPLO -- borrá esta fila antes de importar";
        }

        var listas = workbook.Worksheets.Add("Listas (no editar)");
        listas.Cell(1, 1).Value = "Tipos de gasto activos";
        listas.Cell(1, 2).Value = "Tipos de documento activos";
        listas.Cell(1, 1).Style.Font.Bold = true;
        listas.Cell(1, 2).Style.Font.Bold = true;
        for (var i = 0; i < tiposGasto.Count; i++)
            listas.Cell(i + 2, 1).Value = tiposGasto[i];
        for (var i = 0; i < tiposDoc.Count; i++)
            listas.Cell(i + 2, 2).Value = tiposDoc[i];

        if (tiposGasto.Count > 0)
        {
            var rango = ws.Range(2, 2, MaxFilas + 1, 2);
            rango.CreateDataValidation().List(listas.Range(2, 1, tiposGasto.Count + 1, 1));
        }
        if (tiposDoc.Count > 0)
        {
            var rango = ws.Range(2, 3, MaxFilas + 1, 3);
            rango.CreateDataValidation().List(listas.Range(2, 2, tiposDoc.Count + 1, 2));
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla-importar-gastos.xlsx");
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Archivo is null || Archivo.Length == 0)
        {
            ErrorMessage = "Elegí el archivo Excel (.xlsx) a importar.";
            return Page();
        }

        var tiposGasto = await _expenseTypes.ListActiveAsync(_currentCompany.CompanyId, ct);
        var tiposDoc = await _documentTypes.ListActiveAsync(_currentCompany.CompanyId, ct);

        using var stream = new MemoryStream();
        await Archivo.CopyToAsync(stream, ct);
        stream.Position = 0;

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo abrir el Excel de importación de gastos para la compañía {CompanyId}.", _currentCompany.CompanyId);
            ErrorMessage = "El archivo no es un Excel válido (.xlsx).";
            return Page();
        }

        using (workbook)
        {
            var ws = workbook.Worksheet(1);
            var ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 1;
            var filasATratar = Math.Min(ultimaFila, MaxFilas + 1);

            if (filasATratar > MaxFilas)
            {
                ErrorMessage = $"Máximo {MaxFilas} filas por archivo -- subí el resto en una segunda tanda.";
                return Page();
            }

            for (var fila = 2; fila <= filasATratar; fila++)
            {
                var row = ws.Row(fila);
                if (row.IsEmpty())
                    continue;

                var nombreTipoGasto = row.Cell(2).GetString().Trim();
                if (nombreTipoGasto.Length == 0 && row.Cell(1).IsEmpty())
                    continue; // fila totalmente vacía entre datos, no cuenta como error

                // La fila de ejemplo de la plantilla queda marcada con "EJEMPLO" al
                // principio de Notas -- si el usuario se olvida de borrarla, se ignora
                // sola en vez de crear un gasto trucho con datos de mentira.
                if (row.Cell(11).GetString().TrimStart().StartsWith("EJEMPLO", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var linea = LeerFila(row, tiposGasto, tiposDoc);
                    var guardado = await _expenses.CreateAsync(linea, ct);
                    Results.Add(new ImportResult(fila, linea.SupplierName ?? linea.ExpenseType?.Name, guardado.Id, null, guardado.Warnings));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fila {Fila} del Excel de gastos no se pudo importar (compañía {CompanyId}).", fila, _currentCompany.CompanyId);
                    Results.Add(new ImportResult(fila, nombreTipoGasto, 0, DescribeError(ex), Array.Empty<string>()));
                }
            }
        }

        var ok = Results.Count(r => r.ExpenseId > 0);
        var fallidos = Results.Count - ok;

        if (ok > 0)
        {
            SuccessMessage = ok == 1
                ? "Se importó 1 gasto -- ahora andá a cada uno y adjuntale la foto o PDF de su boleta (es obligatorio antes de poder agregarlo a un informe)."
                : $"Se importaron {ok} gastos -- ahora andá a cada uno y adjuntale la foto o PDF de su boleta (es obligatorio antes de poder agregarlos a un informe).";
        }
        if (fallidos > 0)
        {
            ErrorMessage = fallidos == 1
                ? "1 fila no se pudo importar -- mirá el detalle en la lista de abajo."
                : $"{fallidos} filas no se pudieron importar -- mirá el detalle en la lista de abajo.";
        }
        if (Results.Count == 0)
        {
            ErrorMessage = "El archivo no tiene filas con datos a partir de la fila 2.";
        }

        if (Results.Count > 0)
        {
            await _notifications.TryEnqueueAsync(_currentUser.UserId, "Importación de gastos (Excel)",
                $"{ok} gasto(s) importado(s), {fallidos} fila(s) con error.",
                fallidos == 0 ? NotificationSeverity.Success : ok == 0 ? NotificationSeverity.Danger : NotificationSeverity.Warning,
                "/rendiciones/gastos", ct);
        }

        return Page();
    }

    private ExpenseReportLine LeerFila(IXLRow row, IReadOnlyList<ExpenseType> tiposGasto, IReadOnlyList<DocumentType> tiposDoc)
    {
        if (!TryGetDate(row.Cell(1), out var fecha))
            throw new InvalidOperationException("La columna Fecha está vacía o no tiene un formato de fecha válido.");

        var nombreTipoGasto = row.Cell(2).GetString().Trim();
        var tipoGasto = tiposGasto.FirstOrDefault(t => string.Equals(t.Name, nombreTipoGasto, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Tipo de gasto '{nombreTipoGasto}' no existe (o está inactivo) en esta compañía.");

        DocumentType? tipoDoc = null;
        var nombreTipoDoc = row.Cell(3).GetString().Trim();
        if (nombreTipoDoc.Length > 0)
        {
            tipoDoc = tiposDoc.FirstOrDefault(t => string.Equals(t.Name, nombreTipoDoc, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Tipo de documento '{nombreTipoDoc}' no existe (o está inactivo) en esta compañía.");
        }

        if (!TryGetDecimal(row.Cell(4), out var monto) || monto <= 0)
            throw new InvalidOperationException("La columna Monto total está vacía o no es un número mayor a cero.");

        TryGetDecimal(row.Cell(5), out var iva);
        TryGetDecimal(row.Cell(6), out var neto);
        TryGetDecimal(row.Cell(7), out var exento);

        return new ExpenseReportLine
        {
            CompanyId = _currentCompany.CompanyId,
            UserId = _currentUser.UserId,
            ExpenseTypeId = tipoGasto.Id,
            ExpenseType = tipoGasto,
            DocumentTypeId = tipoDoc?.Id,
            Date = fecha,
            Amount = monto,
            TaxAmount = iva > 0 ? iva : null,
            NetAmount = neto > 0 ? neto : null,
            ExemptAmount = exento > 0 ? exento : null,
            Currency = "CLP",
            DocumentNumber = Clamp(row.Cell(8).GetString(), 50),
            SupplierTaxId = Clamp(row.Cell(9).GetString(), 20),
            SupplierName = Clamp(row.Cell(10).GetString(), 200),
            Notes = Clamp(row.Cell(11).GetString(), 2000),
            CaptureSource = "manual",
            ExpenseReceiptId = null,
        };
    }

    private static bool TryGetDate(IXLCell cell, out DateTimeOffset fecha)
    {
        fecha = default;
        if (cell.IsEmpty())
            return false;
        if (cell.DataType == XLDataType.DateTime)
        {
            fecha = new DateTimeOffset(cell.GetDateTime(), TimeSpan.Zero);
            return true;
        }
        if (DateTime.TryParse(cell.GetString(), out var parsed))
        {
            fecha = new DateTimeOffset(parsed.Date, TimeSpan.Zero);
            return true;
        }
        return false;
    }

    private static bool TryGetDecimal(IXLCell cell, out decimal valor)
    {
        valor = 0m;
        if (cell.IsEmpty())
            return false;
        if (cell.DataType == XLDataType.Number)
        {
            valor = (decimal)cell.GetDouble();
            return true;
        }
        return decimal.TryParse(cell.GetString(), out valor);
    }

    /// <summary>Recorta a <paramref name="max"/> caracteres (null/blank -&gt; null).</summary>
    private static string? Clamp(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    /// <summary>Aplana la cadena de InnerException -- ver mismo helper en Pages/Gastos/Importar.cshtml.cs.</summary>
    private static string DescribeError(Exception ex)
    {
        var mensajes = new List<string>();
        for (var actual = ex; actual is not null; actual = actual.InnerException)
        {
            var m = actual.Message.Trim();
            if (m.Length > 0 && (mensajes.Count == 0 || mensajes[^1] != m))
                mensajes.Add(m);
        }
        return string.Join(" → ", mensajes);
    }

    public sealed record ImportResult(int Fila, string? Descripcion, long ExpenseId, string? Error, IReadOnlyList<string> Warnings);
}
