namespace Modulo.Rendiciones.Models;

/// <summary>
/// Dedupe del recordatorio diario -- una fila por día en que ya se mandó, evita
/// reenviar el mismo día si RendicionesReminderBackgroundService reinicia.
/// </summary>
public class ReminderLog
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required DateOnly SentDate { get; set; }
}
