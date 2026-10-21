using Microsoft.AspNetCore.Mvc;

namespace Modulo.Despachos.Pages.Despachos.Manifiestos;

/// <summary>
/// PDF servido inline (se abre en el visor del navegador para imprimir): FileContentResult con
/// FileDownloadName fuerza "attachment", por eso se escribe la cabecera Content-Disposition a mano.
/// </summary>
public sealed class InlinePdfResult : FileContentResult
{
    public InlinePdfResult(byte[] bytes, string fileName) : base(bytes, "application/pdf")
    {
        FileDownloadName = fileName;
    }

    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.ContentType = "application/pdf";
        response.Headers.ContentDisposition = $"inline; filename=\"{FileDownloadName}\"";
        response.ContentLength = FileContents.Length;
        await response.Body.WriteAsync(FileContents, context.HttpContext.RequestAborted);
    }
}
