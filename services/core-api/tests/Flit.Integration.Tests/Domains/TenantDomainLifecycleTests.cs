using System.Text.Json;
using Flit.Admin.Application.Auditing;
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

    // ── Bug #12766 · el alta y el retiro POR EL REPOSITORIO contra Postgres real ─────────────
    // Hasta aquí ninguna prueba llamaba a RegisterOrReplaceAsync/RetireAsync contra el motor: las de
    // arriba siembran la fila con ctx.TenantDomains.Add(...) y se saltan el repositorio, y las
    // unitarias de RegisterDomainHandlerTests lo mockean. Por eso AddAudit pudo escribir el host como
    // texto plano en columnas jsonb (22P02, que no cae en ningún catch: DbUpdateException → HTTP 500).

    /// <summary>Host vigente de la red, leído del propio contexto (Map no expone DeletedAt).</summary>
    private async Task<(int Total, int Vigentes)> CountDomainsAsync()
    {
        await using var ctx = NewContext();
        var total = await ctx.TenantDomains.AsNoTracking().CountAsync(d => d.TenantId == MarcaBlancaHeadId, TestContext.Current.CancellationToken);
        var vigentes = await ctx.TenantDomains.AsNoTracking().CountAsync(d => d.TenantId == MarcaBlancaHeadId && d.DeletedAt == null, TestContext.Current.CancellationToken);
        return (total, vigentes);
    }

    private async Task<List<TenantConfigAuditLog>> HostAuditsAsync()
    {
        await using var ctx = NewContext();
        return await ctx.TenantConfigAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == MarcaBlancaHeadId && a.EntityName == "TenantDomain" && a.FieldName == "host")
            .OrderBy(a => a.ChangedAt)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary><c>jsonb_typeof</c> de la columna tal y como quedó en el motor (<c>null</c> si la celda es NULL SQL).</summary>
    private async Task<string?> JsonbTypeOfAsync(string column)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT jsonb_typeof({column}) FROM admin.tenant_config_audit_logs WHERE field_name = 'host' ORDER BY changed_at DESC LIMIT 1",
            connection);
        var value = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is null or DBNull ? null : (string)value;
    }

    /// <summary>
    /// Bug #12766 — el alta por el repositorio persiste la auditoría del host en columnas <c>jsonb</c>.
    /// Esta prueba sola atrapa el defecto: con el host como texto plano, <c>SaveChangesAsync</c> revienta
    /// con 22P02 (<c>invalid_text_representation</c>) y ni siquiera llega a las aserciones.
    /// </summary>
    [PostgresFact]
    public async Task Bug12766_AltaPorElRepositorio_EscribeLaAuditoriaDelHostComoJsonValido()
    {
        await SeedClientAsync();

        await using var ctx = NewContext();
        var repo = new TenantDomainRepository(ctx);

        var registered = await repo.RegisterOrReplaceAsync(
            MarcaBlancaHeadId, "app.example.com", "tok-0000000000000011", changedBy: null, TestContext.Current.CancellationToken);

        registered.Host.Should().Be("app.example.com");
        registered.Status.Should().Be(TenantDomainStatus.Pending);

        var audits = await HostAuditsAsync();
        audits.Should().ContainSingle("el alta deja exactamente una fila de auditoría del campo host");

        var audit = audits[0];
        audit.Operation.Should().Be(AuditVocabulary.Operations.Create);
        audit.OldValue.Should().BeNull("no había host anterior");
        audit.NewValue.Should().NotBeNull();

        // JSON válido y, al deserializarlo, el host: cadena JSON desnuda, no objeto (FieldName ya nombra el campo).
        JsonSerializer.Deserialize<string>(audit.NewValue!).Should().Be("app.example.com");
        (await JsonbTypeOfAsync("new_value")).Should().Be("string", "la columna es jsonb y el valor es una cadena JSON");
    }

    /// <summary>
    /// Bug #12766 — cambiar de host retira el anterior y da de alta el nuevo en el MISMO
    /// <c>SaveChangesAsync</c>. Fija el supuesto frágil documentado en
    /// <c>TenantDomainRepository</c>: que el UPDATE (deleted_at del vigente) se emita antes que el
    /// INSERT del nuevo, para que el índice único parcial <c>uq_tenant_domains_tenant_id</c> no
    /// colisione. Si EF cambiara ese orden, esta prueba falla con 23505.
    /// </summary>
    [PostgresFact]
    public async Task Bug12766_CambioDeHostPorElRepositorio_RetiraElAnteriorYAuditaAmbosValores()
    {
        await SeedClientAsync();

        await using (var ctx = NewContext())
        {
            var repo = new TenantDomainRepository(ctx);
            await repo.RegisterOrReplaceAsync(MarcaBlancaHeadId, "app.example.com", "tok-0000000000000012", null, TestContext.Current.CancellationToken);
        }

        await using (var ctx = NewContext())
        {
            var repo = new TenantDomainRepository(ctx);
            var replaced = await repo.RegisterOrReplaceAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000013", null, TestContext.Current.CancellationToken);
            replaced.Host.Should().Be("red.example.com");
            replaced.Status.Should().Be(TenantDomainStatus.Pending);
        }

        var (total, vigentes) = await CountDomainsAsync();
        total.Should().Be(2, "el anterior queda retirado (deleted_at), no se borra");
        vigentes.Should().Be(1, "solo el nuevo host está vigente");

        await using (var ctx = NewContext())
        {
            var retirado = await ctx.TenantDomains.AsNoTracking()
                .SingleAsync(d => d.TenantId == MarcaBlancaHeadId && d.Host == "app.example.com", TestContext.Current.CancellationToken);
            retirado.DeletedAt.Should().NotBeNull();
        }

        var audits = await HostAuditsAsync();
        audits.Should().HaveCount(2);

        audits[0].Operation.Should().Be(AuditVocabulary.Operations.Create);
        audits[0].OldValue.Should().BeNull();
        JsonSerializer.Deserialize<string>(audits[0].NewValue!).Should().Be("app.example.com");

        audits[1].Operation.Should().Be(AuditVocabulary.Operations.Update);
        JsonSerializer.Deserialize<string>(audits[1].OldValue!).Should().Be("app.example.com");
        JsonSerializer.Deserialize<string>(audits[1].NewValue!).Should().Be("red.example.com");
    }

    /// <summary>
    /// Bug #12766 — el retiro (segunda ruta de <c>AddAudit</c>) deja el host anterior como JSON y el
    /// nuevo valor en NULL SQL. La guarda de null importa: <c>JsonSerializer.Serialize(null)</c>
    /// habría escrito el literal JSON <c>null</c>, indistinguible de "no hay valor" al leerlo.
    /// </summary>
    [PostgresFact]
    public async Task Bug12766_RetiroPorElRepositorio_AuditaElHostAnteriorYDejaElNuevoEnNullSql()
    {
        await SeedClientAsync();

        await using (var ctx = NewContext())
        {
            var repo = new TenantDomainRepository(ctx);
            await repo.RegisterOrReplaceAsync(MarcaBlancaHeadId, "app.example.com", "tok-0000000000000014", null, TestContext.Current.CancellationToken);
        }

        await using (var ctx = NewContext())
        {
            var repo = new TenantDomainRepository(ctx);
            var retired = await repo.RetireAsync(MarcaBlancaHeadId, changedBy: null, TestContext.Current.CancellationToken);
            retired.Should().NotBeNull();
        }

        var (total, vigentes) = await CountDomainsAsync();
        total.Should().Be(1);
        vigentes.Should().Be(0, "tras el retiro la red no tiene dominio vigente");

        var audits = await HostAuditsAsync();
        audits.Should().HaveCount(2);

        var retiro = audits[1];
        retiro.Operation.Should().Be(AuditVocabulary.Operations.Update);
        JsonSerializer.Deserialize<string>(retiro.OldValue!).Should().Be("app.example.com");
        retiro.NewValue.Should().BeNull("NULL SQL, no el literal JSON \"null\"");
        (await JsonbTypeOfAsync("new_value")).Should().BeNull("jsonb_typeof(NULL) es NULL; sería 'null' si se hubiera serializado el literal");
    }
}
