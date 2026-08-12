using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Fakes;

public sealed class FakeEmailSenderService : IEmailSenderService
{
    public List<(Guid OrganizationId, EmailMessage Message)> Sent { get; } = new();

    public Task SendAsync(Guid organizationId, EmailMessage message, CancellationToken ct = default)
    {
        Sent.Add((organizationId, message));
        return Task.CompletedTask;
    }
}
