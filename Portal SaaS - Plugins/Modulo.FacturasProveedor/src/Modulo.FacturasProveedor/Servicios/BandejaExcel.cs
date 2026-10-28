using ClosedXML.Excel;
using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Pages;

namespace Modulo.FacturasProveedor.Servicios;

/// <summary>Excel de la bandeja: las columnas de la pantalla, con todo lo filtrado (no solo la página visible).</summary>
public static class BandejaExcel
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly (string Title, double Width)[] Columns =
    {
        ("Fecha registro SII", 18), ("RUT", 13), ("Razón social", 40), ("Tipo", 6), ("Documento", 28), ("Folio", 12),
        ("Referencia", 24), ("Forma de pago", 14), ("Vencimiento", 12), ("Total", 14), ("OC", 12), ("Resultado de reglas", 50),
        ("Estado", 22), ("Vence plazo SII", 15),
    };

    public static byte[] Build(IReadOnlyList<ReviewRow> rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add("Facturas recibidas");
        for (var c = 0; c < Columns.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Columns[c].Title;
            sheet.Column(c + 1).Width = Columns[c].Width;
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        var line = 2;
        foreach (var r in rows)
        {
            if (r.SiiReceivedAt is { } at)
            {
                sheet.Cell(line, 1).Value = ChileClock.ToLocal(at.UtcDateTime);
                sheet.Cell(line, 1).Style.DateFormat.Format = "dd-mm-yyyy hh:mm";
            }
            else
            {
                sheet.Cell(line, 1).Value = r.SiiReceivedOn.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(line, 1).Style.DateFormat.Format = "dd-mm-yyyy";
            }
            sheet.Cell(line, 2).Value = r.IssuerTaxId;
            sheet.Cell(line, 3).Value = r.IssuerName;
            sheet.Cell(line, 4).Value = r.DteType;
            sheet.Cell(line, 5).Value = FacturasProveedorUi.DocumentTypeName(r.DteType);
            sheet.Cell(line, 6).Value = r.Folio;
            sheet.Cell(line, 7).Value = r.ReferenceLabel ?? "";
            sheet.Cell(line, 8).Value = r.PaymentForm is null ? "" : FacturasProveedorUi.PaymentFormLabel(r.PaymentForm);
            if (r.DueDate is { } due)
            {
                sheet.Cell(line, 9).Value = due.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(line, 9).Style.DateFormat.Format = "dd-mm-yyyy";
            }
            sheet.Cell(line, 10).Value = r.TotalAmount;
            sheet.Cell(line, 10).Style.NumberFormat.Format = "#,##0";
            sheet.Cell(line, 11).Value = string.Join(", ", r.PurchaseOrders);
            sheet.Cell(line, 12).Value = r.Summary ?? "";
            sheet.Cell(line, 13).Value = FacturasProveedorUi.StatusLabel(r.Status);
            if (r.Status != ReviewStatuses.NotRespondable)
            {
                sheet.Cell(line, 14).Value = r.LegalDeadlineOn.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(line, 14).Style.DateFormat.Format = "dd-mm-yyyy";
            }
            line++;
        }

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}
