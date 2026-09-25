using Flit.DrFlit.Application.Abstractions;
using Flit.Infrastructure.DrFlit;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.DrFlit;

/// <summary>
/// HU #12931 — evidencia del consentimiento en <c>dr_flit.consent_acceptances</c> contra PostgreSQL real.
/// Uso de ejemplo:
/// <code>
/// await store.RecordAsync(new DrFlitConsentAcceptance(tenant, user, "2026-09-25", ip, ua), ct);
/// await store.HasAcceptedAsync(user, "2026-09-25", ct); // true
/// </code>
/// </summary>
public sealed class DrFlitConsentStoreTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = TenantSeed.LoneId;
    private static readonly Guid TenantB = TenantSeed.ParentId;
    private static readonly Guid User = Guid.Parse("0199a000-0000-7000-8000-00000000d301");
    private const string Version = "2026-09-25";

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
            return;

        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Tenants.Add(TenantSeed.GroupParent());
        ctx.Users.Add(new User
        {
            Id = User, Email = "consent@example.test", DisplayName = "Consent", Status = "active",
            HomeTenantId = TenantA, CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private DrFlitConsentStore Store() => new(NewContext());

    private async Task<long> CountAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM dr_flit.consent_acceptances", cn);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [PostgresFact]
    public async Task AC1_RegistraLaEvidenciaYQuedaAceptada()
    {
        var ct = TestContext.Current.CancellationToken;
        (await Store().HasAcceptedAsync(User, Version, ct)).Should().BeFalse();

        await Store().RecordAsync(new DrFlitConsentAcceptance(TenantA, User, Version, "190.0.0.1", "Mozilla/5.0"), ct);

        (await Store().HasAcceptedAsync(User, Version, ct)).Should().BeTrue();
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT tenant_id, client_ip, user_agent, accepted_at IS NOT NULL FROM dr_flit.consent_acceptances", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        (await r.ReadAsync(ct)).Should().BeTrue();
        r.GetGuid(0).Should().Be(TenantA);
        r.GetString(1).Should().Be("190.0.0.1");
        r.GetString(2).Should().Be("Mozilla/5.0");
        r.GetBoolean(3).Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC3_UnaVezPorUsuarioYVersion_AceptarDeNuevoNoDuplica()
    {
        var ct = TestContext.Current.CancellationToken;
        await Store().RecordAsync(new DrFlitConsentAcceptance(TenantA, User, Version, "190.0.0.1", null), ct);

        await Store().RecordAsync(new DrFlitConsentAcceptance(TenantB, User, Version, "190.0.0.2", null), ct);

        (await CountAsync()).Should().Be(1, "se conserva la primera evidencia");
    }

    [PostgresFact]
    public async Task AC3_ValeParaLaPersonaEnCualquierTenant_PeroNoParaOtraVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        await Store().RecordAsync(new DrFlitConsentAcceptance(TenantA, User, Version, null, null), ct);

        (await Store().HasAcceptedAsync(User, Version, ct)).Should().BeTrue();
        (await Store().HasAcceptedAsync(User, "2027-01-01", ct)).Should().BeFalse("una versión nueva del texto se vuelve a pedir");
    }
}
