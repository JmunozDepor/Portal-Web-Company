using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Fakes;

public sealed class FakeUserContactLookupService : IUserContactLookupService
{
    private readonly Dictionary<Guid, UserContactDto> _contacts = new();

    public FakeUserContactLookupService With(Guid userId, UserContactDto contact)
    {
        _contacts[userId] = contact;
        return this;
    }

    public Task<UserContactDto?> GetContactAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_contacts.TryGetValue(userId, out var contact) ? contact : null);
}
