using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtMetrics;

/// <summary>
/// Foto instantánea para alertas por umbral del organismo (Reportes 2.0, HU-D, tercera ola):
/// <see cref="OtMetricsReadRepository.GetAlertSnapshotAsync"/>. Mismas definiciones que sus
/// contrapartes de empresa (stuck_count/rejection_rate_pct), calculadas cruzando el grant.
/// </summary>
public sealed class OtAlertSnapshotTests
{
    private static readonly Guid OtTenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ClientTenant = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid TransitOffice = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid ProcedureType = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact] // Mismo umbral que stuck_count de empresa: sin cambios hace más de 7 días.
    public async Task Cuenta_los_pendientes_con_mas_de_7_dias_de_espera()
    {
        var db = NewDbName();
        var viejo = Guid.NewGuid();
        var reciente = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, viejo, TramiteEstado.Entregado, "REF-1");
            SeedDelivery(seed, viejo, DateTimeOffset.UtcNow.AddDays(-9));
            SeedProcedure(seed, reciente, TramiteEstado.Entregado, "REF-2");
            SeedDelivery(seed, reciente, DateTimeOffset.UtcNow.AddDays(-1));
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var snapshot = await new OtMetricsReadRepository(ctx).GetAlertSnapshotAsync(
            OtTenant, windowMinutes: 1440, cancellationToken: TestContext.Current.CancellationToken);

        snapshot.Should().NotBeNull();
        snapshot!.StuckCount.Should().Be(1);
    }

    [Fact] // Mismo default que rejection_rate_pct de empresa: 0 sin decididos, no un vacío ambiguo.
    public async Task Sin_decisiones_en_la_ventana_la_tasa_de_rechazo_es_cero()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var snapshot = await new OtMetricsReadRepository(ctx).GetAlertSnapshotAsync(
            OtTenant, windowMinutes: 60, cancellationToken: TestContext.Current.CancellationToken);

        snapshot.Should().NotBeNull();
        snapshot!.RejectionRatePct.Should().Be(0m);
    }

    [Fact]
    public async Task Calcula_la_tasa_de_rechazo_sobre_las_decisiones_dentro_de_la_ventana()
    {
        var db = NewDbName();
        var aprobado = Guid.NewGuid();
        var rechazado1 = Guid.NewGuid();
        var rechazado2 = Guid.NewGuid();
        var fueraDeVentana = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, aprobado, TramiteEstado.Aprobado, "REF-1");
            SeedProcedure(seed, rechazado1, TramiteEstado.Rechazado, "REF-2");
            SeedProcedure(seed, rechazado2, TramiteEstado.Rechazado, "REF-3");
            SeedProcedure(seed, fueraDeVentana, TramiteEstado.Rechazado, "REF-4");
            SeedDecision(seed, aprobado, TramiteEstado.Aprobado, DateTimeOffset.UtcNow.AddMinutes(-10));
            SeedDecision(seed, rechazado1, TramiteEstado.Rechazado, DateTimeOffset.UtcNow.AddMinutes(-20));
            SeedDecision(seed, rechazado2, TramiteEstado.Rechazado, DateTimeOffset.UtcNow.AddMinutes(-30));
            // Fuera de la ventana de 60 minutos: no debe contar.
            SeedDecision(seed, fueraDeVentana, TramiteEstado.Rechazado, DateTimeOffset.UtcNow.AddMinutes(-120));
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var snapshot = await new OtMetricsReadRepository(ctx).GetAlertSnapshotAsync(
            OtTenant, windowMinutes: 60, cancellationToken: TestContext.Current.CancellationToken);

        snapshot.Should().NotBeNull();
        // 2 rechazados de 3 decididos dentro de la ventana = 66.67 %.
        snapshot!.RejectionRatePct.Should().Be(66.67m);
    }

    [Fact] // Tenant sin perfil OT: nada que medir, no un error de evaluación.
    public async Task Tenant_sin_organismo_asociado_devuelve_null()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);

        var snapshot = await new OtMetricsReadRepository(ctx).GetAlertSnapshotAsync(
            Guid.NewGuid(), windowMinutes: 60, cancellationToken: TestContext.Current.CancellationToken);

        snapshot.Should().BeNull();
    }

    [Fact] // Resolución inversa: id de organismo -> tenant dueño, la usa SuperAdmin al programar.
    public async Task Resuelve_el_tenant_dueno_de_un_organismo_por_su_id()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var tenant = await new OtMetricsReadRepository(ctx).ResolveTenantIdForTransitOfficeAsync(
            TransitOffice, TestContext.Current.CancellationToken);

        tenant.Should().Be(OtTenant);
    }

    [Fact]
    public async Task Resolver_un_id_de_organismo_desconocido_devuelve_null()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);

        var tenant = await new OtMetricsReadRepository(ctx).ResolveTenantIdForTransitOfficeAsync(
            Guid.NewGuid(), TestContext.Current.CancellationToken);

        tenant.Should().BeNull();
    }

    private static void SeedScope(FlitDbContext ctx)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            TransitOfficeId = TransitOffice,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.Tenants.Add(new Tenant
        {
            Id = ClientTenant,
            Code = "client-alert-snapshot",
            LegalName = "Flota Andina S.A.S.",
            TaxId = "900000001",
            TenantType = "client",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = ProcedureType,
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedProcedure(
        FlitDbContext ctx, Guid id, string status, string reference)
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureType,
            ReferenceNumber = reference,
            Status = status,
            TransitOfficeId = TransitOffice,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        ctx.SaveChanges();
    }

    private static void SeedDelivery(FlitDbContext ctx, Guid instanceId, DateTimeOffset at) =>
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            FromStatus = TramiteEstado.Preparado,
            ToStatus = TramiteEstado.Entregado,
            ChangedAt = at,
        });

    private static void SeedDecision(FlitDbContext ctx, Guid instanceId, string toStatus, DateTimeOffset at) =>
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            FromStatus = TramiteEstado.Entregado,
            ToStatus = toStatus,
            ChangedAt = at,
        });

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
