using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class PolicyProfileServiceTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_PersistsProfile()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_PersistsProfile));
        var sut = new PolicyProfileService(db);

        var id = await sut.CreateAsync(companyId, "Supervisor en terreno");

        var all = await sut.ListAllAsync(companyId);
        Assert.Single(all);
        Assert.Equal(id, all[0].Id);
        Assert.Equal("Supervisor en terreno", all[0].Name);
        Assert.True(all[0].IsActive);
    }

    [Fact]
    public async Task AssignAsync_SetsProfile_GetAssignedProfileIdAsync_ReturnsIt()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(AssignAsync_SetsProfile_GetAssignedProfileIdAsync_ReturnsIt));
        var sut = new PolicyProfileService(db);
        var profileId = await sut.CreateAsync(companyId, "Supervisor en terreno");

        await sut.AssignAsync(companyId, userId, profileId);

        var assigned = await sut.GetAssignedProfileIdAsync(companyId, userId);
        Assert.Equal(profileId, assigned);
    }

    [Fact]
    public async Task AssignAsync_NullProfileId_ClearsAssignment()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(AssignAsync_NullProfileId_ClearsAssignment));
        var sut = new PolicyProfileService(db);
        var profileId = await sut.CreateAsync(companyId, "Supervisor en terreno");
        await sut.AssignAsync(companyId, userId, profileId);

        await sut.AssignAsync(companyId, userId, null);

        var assigned = await sut.GetAssignedProfileIdAsync(companyId, userId);
        Assert.Null(assigned);
    }

    [Fact]
    public async Task AssignAsync_ReplacesExistingAssignment_OneActivePerUser()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(AssignAsync_ReplacesExistingAssignment_OneActivePerUser));
        var sut = new PolicyProfileService(db);
        var profile1 = await sut.CreateAsync(companyId, "Supervisor en terreno");
        var profile2 = await sut.CreateAsync(companyId, "Oficina");
        await sut.AssignAsync(companyId, userId, profile1);

        await sut.AssignAsync(companyId, userId, profile2);

        var assigned = await sut.GetAssignedProfileIdAsync(companyId, userId);
        Assert.Equal(profile2, assigned);
        Assert.Single(db.UserPolicyProfiles.Where(a => a.CompanyId == companyId && a.UserId == userId));
    }

    [Fact]
    public async Task ListAssignmentsAsync_ReturnsOnlyUsersWithAssignment()
    {
        var companyId = Guid.NewGuid();
        var userConAsignacion = Guid.NewGuid();
        var userSinAsignacion = Guid.NewGuid();
        await using var db = CreateDb(nameof(ListAssignmentsAsync_ReturnsOnlyUsersWithAssignment));
        var sut = new PolicyProfileService(db);
        var profileId = await sut.CreateAsync(companyId, "Supervisor en terreno");
        await sut.AssignAsync(companyId, userConAsignacion, profileId);

        var assignments = await sut.ListAssignmentsAsync(companyId);

        Assert.Single(assignments);
        Assert.True(assignments.ContainsKey(userConAsignacion));
        Assert.False(assignments.ContainsKey(userSinAsignacion));
        Assert.Equal("Supervisor en terreno", assignments[userConAsignacion].Name);
    }
}
