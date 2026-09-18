using Flit.Admin.Domain.Companies.Domains;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Domains;

/// <summary>
/// HU #12425 AC2, AC3, AC4, AC5, AC7 — el ciclo de estados (<see cref="TenantDomainRepository.ApplyCheckOutcomeAsync"/>,
/// <see cref="TenantDomainRepository.ApplyCertificateAsync"/>) contra PostgreSQL real: pending → verified
/// → active, failed con motivo, el CHECK <c>ck_tenant_domains_active_requires_verified</c> sigue
/// rechazando <c>active</c> sin certificado (AC3, "no relajar el CHECK"), y la auditoría de
/// <c>admin.tenant_config_audit_logs</c> queda con <c>changedBy=job:dns-verification</c> dentro del
/// JSON (decisión documentada: <c>TenantConfigAuditLog.ChangedBy</c> es <c>Guid?</c>).
/// </summary>
public sealed class TenantDomainLifecycleTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid MarcaBlancaHeadId = TenantSeed.ParentId;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task SeedClientAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(MarcaBlancaHeadId, "IT-MB-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca));
        await ctx.SaveChangesAsync();
    }

    private async Task AddDomainAsync(string host, string token)
    {
        await using var ctx = NewContext();
        ctx.TenantDomains.Add(new TenantDomainEntity { TenantId = MarcaBlancaHeadId, Host = host, VerificationToken = token });
        await ctx.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task AC2_PendingAVerified_EscribeVerifiedAtYAuditaLaTransicion()
    {
        await SeedClientAsync();
        await AddDomainAsync("app.example.com", "tok-0000000000000001");

        await using var ctx = NewContext();
        var repo = new TenantDomainRepository(ctx);

        var updated = await repo.ApplyCheckOutcomeAsync(
            MarcaBlancaHeadId,
            TenantDomainStatus.Verified,
            statusReason: null,
            verifiedAt: Now,
            graceUntil: null,
            checkAttempts: 0,
            nextCheckAt: null,
            now: Now,
            changedByUserId: null,
            changedByJob: "job:dns-verification",
            TestContext.Current.CancellationToken);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be(TenantDomainStatus.Verified);
        updated.VerifiedAt.Should().NotBeNull();

        await using var check = NewContext();
        var audit = await check.TenantConfigAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == MarcaBlancaHeadId && a.EntityName == "TenantDomain" && a.FieldName == "status")
            .SingleAsync(TestContext.Current.CancellationToken);
        audit.NewValue.Should().Contain("verified").And.Contain("job:dns-verification");
    }

    [PostgresFact]
    public async Task AC2_Failed_EscribeMotivoYPermiteReintentoConNextCheckAt()
    {
        await SeedClientAsync();
        await AddDomainAsync("app.example.com", "tok-0000000000000002");

        await using var ctx = NewContext();
        var repo = new TenantDomainRepository(ctx);

        var nextCheck = Now.AddMinutes(5);
        var updated = await repo.ApplyCheckOutcomeAsync(
            MarcaBlancaHeadId,
            TenantDomainStatus.Failed,
            DomainStatusReasons.TxtNotFound,
            verifiedAt: null,
            graceUntil: null,
            checkAttempts: 1,
            nextCheckAt: nextCheck,
            now: Now,
            changedByUserId: null,
            changedByJob: "job:dns-verification",
            TestContext.Current.CancellationToken);

        updated!.Status.Should().Be(TenantDomainStatus.Failed);
        updated.StatusReason.Should().Be(DomainStatusReasons.TxtNotFound);
        updated.NextCheckAt.Should().BeCloseTo(nextCheck, TimeSpan.FromSeconds(1));
    }

    [PostgresFact]
    public async Task AC3_VerifiedAActive_ExigeCertificado_ElCheckDeLaBdSigueVigente()
    {
        await SeedClientAsync();
        await AddDomainAsync("app.example.com", "tok-0000000000000003");

        await using (var ctx = NewContext())
        {
            var repo = new TenantDomainRepository(ctx);
            await repo.ApplyCheckOutcomeAsync(
                MarcaBlancaHeadId, TenantDomainStatus.Verified, null, Now, null, 0, null, Now, null, "job:dns-verification",
                TestContext.Current.CancellationToken);
        }

        // Intentar activar SIN pasar por ApplyCertificateAsync (sin certificate_issued_at) debe seguir
        // rechazado por el motor (ck_tenant_domains_active_requires_verified) — AC3 "no relajar el CHECK".
        await using (var ctx = NewContext())
        {
            var domain = await ctx.TenantDomains.SingleAsync(d => d.TenantId == MarcaBlancaHeadId);
            domain.Status = TenantDomainStatuses.Active;
            domain.ActivatedAt = Now;
            var act = () => ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var ex = await act.Should().ThrowAsync<DbUpdateException>();
            var pg = ex.Which.InnerException as PostgresException;
            pg.Should().NotBeNull();
            pg!.ConstraintName.Should().Be("ck_tenant_domains_active_requires_verified");
        }

        // El camino correcto (#12426 → ApplyCertificateAsync) SÍ activa.
        await using (var ctx = NewContext())
        {
            var repo = new TenantDomainRepository(ctx);
            var activated = await repo.ApplyCertificateAsync(
                "app.example.com", TenantDomainStatus.Active, Now, Now, Now.AddDays(90), "job:certificate-issuer",
                TestContext.Current.CancellationToken);

            activated!.Status.Should().Be(TenantDomainStatus.Active);
            activated.ActivatedAt.Should().NotBeNull();
        }

        await using (var ctx = NewContext())
        {
            (await ctx.ActiveNetworkDomains.CountAsync()).Should().Be(1, "active con certificado sí resuelve red (AC3)");
        }
    }

    [PostgresFact]
    public async Task AC4_ActivoConGracia_SigueApareciendoEnLaVistaHastaQueVenceLaGracia()
    {
        await SeedClientAsync();
        await AddDomainAsync("app.example.com", "tok-0000000000000004");

        await using (var ctx = NewContext())
        {
            var repo = new TenantDomainRepository(ctx);
            await repo.ApplyCheckOutcomeAsync(MarcaBlancaHeadId, TenantDomainStatus.Verified, null, Now, null, 0, null, Now, null, "job:dns-verification", TestContext.Current.CancellationToken);
            await repo.ApplyCertificateAsync("app.example.com", TenantDomainStatus.Active, Now, Now, Now.AddDays(90), "job:certificate-issuer", TestContext.Current.CancellationToken);
        }

        await using (var ctx = NewContext())
        {
            var repo = new TenantDomainRepository(ctx);
            // TXT desaparece: entra en gracia, sigue active (AC4 "durante la gracia sigue operando").
            var graced = await repo.ApplyCheckOutcomeAsync(
                MarcaBlancaHeadId, TenantDomainStatus.Active, null, null, Now.AddDays(3), 0, Now.AddMinutes(5), Now, null, "job:dns-verification",
                TestContext.Current.CancellationToken);
            graced!.Status.Should().Be(TenantDomainStatus.Active);
            graced.GraceUntil.Should().NotBeNull();
        }

        await using (var ctx = NewContext())
        {
            (await ctx.ActiveNetworkDomains.CountAsync()).Should().Be(1, "en gracia sigue resolviendo");
        }
    }

    [PostgresFact]
    public async Task AC5_LaAuditoriaLlevaEstadoAnteriorNuevoMotivoYAutor()
    {
        await SeedClientAsync();
        await AddDomainAsync("app.example.com", "tok-0000000000000005");

        await using var ctx = NewContext();
        var repo = new TenantDomainRepository(ctx);
        await repo.ApplyCheckOutcomeAsync(
            MarcaBlancaHeadId, TenantDomainStatus.Failed, DomainStatusReasons.DnsError, null, null, 1, Now.AddMinutes(5), Now, null, "job:dns-verification",
            TestContext.Current.CancellationToken);

        await using var check = NewContext();
        var audit = await check.TenantConfigAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == MarcaBlancaHeadId && a.EntityName == "TenantDomain" && a.FieldName == "status")
            .SingleAsync(TestContext.Current.CancellationToken);

        audit.OldValue.Should().Contain("pending");
        audit.NewValue.Should().Contain("failed").And.Contain("DNS_ERROR").And.Contain("job:dns-verification");
        audit.ChangedAt.Should().BeCloseTo(Now, TimeSpan.FromSeconds(5));
    }
}
