using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtRequirements;

/// <summary>
/// Tests de la política de exigibilidad de identidad por OT (HU #10548): resuelve el OT destino y
/// lee <c>admin.ot_requirements.identity_validation_enabled</c> con default seguro (se exige).
/// </summary>
public sealed class IdentityValidationPolicyTests
{
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Office = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact] // AC1 — OT con identidad deshabilitada → no se exige.
    public async Task IdentityDisabledForOffice_NotRequired()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            SeedRequirements(seed, OtTenant, Office, identityEnabled: false);
        }

        await using var ctx = NewContext(db);
        var policy = new IdentityValidationPolicy(ctx, new StubGrants());

        var required = await policy.IsIdentityValidationRequiredAsync(
            ClientTenant, Office, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
    }

    [Fact] // AC2 — sin fila de requisitos → default seguro: se exige.
    public async Task NoRequirementsRow_Required()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var policy = new IdentityValidationPolicy(ctx, new StubGrants());

        var required = await policy.IsIdentityValidationRequiredAsync(
            ClientTenant, Office, TestContext.Current.CancellationToken);

        required.Should().BeTrue();
    }

    [Fact] // Sin OT explícito, resuelve el único grant vigente de la empresa.
    public async Task ResolvesOfficeViaSingleGrant()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            SeedRequirements(seed, OtTenant, Office, identityEnabled: false);
        }

        await using var ctx = NewContext(db);
        var policy = new IdentityValidationPolicy(ctx, new StubGrants(Office));

        var required = await policy.IsIdentityValidationRequiredAsync(
            ClientTenant, transitOfficeId: null, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
    }

    [Fact] // Sin OT resoluble (0 o >1 grants) → se exige (seguro).
    public async Task NoResolvableOffice_Required()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var policy = new IdentityValidationPolicy(ctx, new StubGrants(/* ninguno */));

        var required = await policy.IsIdentityValidationRequiredAsync(
            ClientTenant, transitOfficeId: null, TestContext.Current.CancellationToken);

        required.Should().BeTrue();
    }

    private static void SeedRequirements(FlitDbContext ctx, Guid otTenantId, Guid officeId, bool identityEnabled)
    {
        ctx.OtRequirements.Add(new OtRequirementsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = otTenantId,
            TransitOfficeId = officeId,
            RequiresRnmc = false,
            AllowPlatePreassign = false,
            IdentityValidationEnabled = identityEnabled,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    /// <summary>Stub de grants: devuelve los ids configurados como habilitados.</summary>
    private sealed class StubGrants : ITransitGrantRepository
    {
        private readonly IReadOnlyList<Guid> _offices;

        public StubGrants(params Guid[] offices) => _offices = offices;

        public Task<IReadOnlyList<Guid>> ListEnabledOfficeIdsAsync(
            Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_offices);

        public Task<bool> AddGrantAsync(
            Guid tenantId, Guid transitOfficeId, Guid? createdBy, Guid? correlationId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> RemoveGrantAsync(
            Guid tenantId, Guid transitOfficeId, Guid? changedBy, Guid? correlationId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
