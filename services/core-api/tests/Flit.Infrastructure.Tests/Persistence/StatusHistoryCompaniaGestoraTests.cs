using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12184 — la compañía de quien ejecutó cada movimiento del historial.
///
/// <para>El caso que gobierna el diseño es el <b>usuario sin <c>home_tenant_id</c></b>: es NULLABLE
/// y hay usuarios legítimos que no lo tienen. Resolver la compañía mirando solo esa columna los
/// dejaría a todos sin ella, y el historial se vería «bien» —solo que sin el dato justo para los
/// usuarios de siempre—. El criterio correcto es el mismo que usa el emisor del JWT: home y, si
/// falta, la asignación de rol activa más antigua.</para>
/// </summary>
public sealed class StatusHistoryCompaniaGestoraTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static void SeedTramite(FlitDbContext db) =>
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "18",
            CreatedAt = Base,
        });

    private static void SeedMovimiento(FlitDbContext db, Guid? changedBy, string toStatus, int minuto) =>
        db.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            ToStatus = toStatus,
            ChangedAt = Base.AddMinutes(minuto),
            ChangedBy = changedBy,
        });

    private static Tenant Compania(Guid id, string razon) =>
        new() { Id = id, Code = $"C{id:N}"[..8], LegalName = razon, TaxId = "900123456", TenantType = "COMPANY" };

    [Fact]
    public async Task UsuarioConHomeTenant_ResuelveSuRazonSocial()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await using var db = NewContext(nameof(UsuarioConHomeTenant_ResuelveSuRazonSocial));

        db.Tenants.Add(Compania(TenantId, "Renting Colombia S.A.S"));
        db.Users.Add(new User
        {
            Id = userId,
            Email = "laura@flit.io",
            DisplayName = "Laura Restrepo",
            HomeTenantId = TenantId,
        });
        SeedTramite(db);
        SeedMovimiento(db, userId, "preparado", 1);
        await db.SaveChangesAsync(ct);

        var page = await new ProcedureInstanceRepository(db)
            .GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct);

        page!.Value.Items.Single().ChangedByCompania.Should().Be("Renting Colombia S.A.S");
    }

    [Fact]
    public async Task UsuarioSinHomeTenant_CaeALaAsignacionDeRolMasAntigua()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var primera = Guid.NewGuid();
        var posterior = Guid.NewGuid();
        await using var db = NewContext(nameof(UsuarioSinHomeTenant_CaeALaAsignacionDeRolMasAntigua));

        db.Tenants.Add(Compania(primera, "Comercializadora del Norte S.A.S"));
        db.Tenants.Add(Compania(posterior, "Tránsito de Funza"));
        db.Users.Add(new User
        {
            Id = userId,
            Email = "demo@flit.local",
            DisplayName = "Usuario Demo",
            HomeTenantId = null,
        });
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = posterior,
            RoleId = Guid.NewGuid(),
            AssignedAt = Base.AddDays(-1),
        });
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = primera,
            RoleId = Guid.NewGuid(),
            AssignedAt = Base.AddDays(-30),
        });
        SeedTramite(db);
        SeedMovimiento(db, userId, "preparado", 1);
        await db.SaveChangesAsync(ct);

        var page = await new ProcedureInstanceRepository(db)
            .GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct);

        page!.Value.Items.Single().ChangedByCompania.Should().Be("Comercializadora del Norte S.A.S");
    }

    [Fact]
    public async Task AsignacionBorrada_NoResuelveCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var ajena = Guid.NewGuid();
        await using var db = NewContext(nameof(AsignacionBorrada_NoResuelveCompania));

        db.Tenants.Add(Compania(ajena, "Compañía que ya no lo emplea"));
        db.Users.Add(new User { Id = userId, Email = "ex@flit.io", DisplayName = "Ex Gestor" });
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = ajena,
            RoleId = Guid.NewGuid(),
            AssignedAt = Base.AddDays(-30),
            DeletedAt = Base.AddDays(-1),
        });
        SeedTramite(db);
        SeedMovimiento(db, userId, "preparado", 1);
        await db.SaveChangesAsync(ct);

        var page = await new ProcedureInstanceRepository(db)
            .GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct);

        page!.Value.Items.Single().ChangedByCompania.Should().BeNull();
    }

    [Fact]
    public async Task MovimientoAutomaticoSinUsuario_NoLanzaYQuedaSinCompania()
    {
        // Un movimiento sin `changed_by` (proceso automático) es normal, no un error: el historial
        // tiene que devolverlo igual, sin nombre y sin compañía.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(MovimientoAutomaticoSinUsuario_NoLanzaYQuedaSinCompania));

        SeedTramite(db);
        SeedMovimiento(db, changedBy: null, "entregado", 2);
        await db.SaveChangesAsync(ct);

        var page = await new ProcedureInstanceRepository(db)
            .GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct);

        var item = page!.Value.Items.Single();
        item.ChangedByUserId.Should().BeNull();
        item.ChangedByCompania.Should().BeNull();
    }

    [Fact]
    public async Task DosCompaniasDistintasEnElMismoTramite_CadaMovimientoLlevaLaSuya()
    {
        // Es el caso que justifica la HU: el trámite lo abre una compañía y lo mueve, después, quien
        // lo revisa. El tenant del trámite es el mismo en las dos filas y no distingue nada.
        var ct = TestContext.Current.CancellationToken;
        var gestora = Guid.NewGuid();
        var organismo = Guid.NewGuid();
        var userGestora = Guid.NewGuid();
        var userOrganismo = Guid.NewGuid();
        await using var db = NewContext(nameof(DosCompaniasDistintasEnElMismoTramite_CadaMovimientoLlevaLaSuya));

        db.Tenants.Add(Compania(gestora, "Renting Colombia S.A.S"));
        db.Tenants.Add(Compania(organismo, "Tránsito de Funza"));
        db.Users.Add(new User { Id = userGestora, Email = "g@flit.io", DisplayName = "Laura", HomeTenantId = gestora });
        db.Users.Add(new User { Id = userOrganismo, Email = "o@flit.io", DisplayName = "Carlos", HomeTenantId = organismo });
        SeedTramite(db);
        SeedMovimiento(db, userGestora, "preparado", 1);
        SeedMovimiento(db, userOrganismo, "aprobado", 2);
        await db.SaveChangesAsync(ct);

        var page = await new ProcedureInstanceRepository(db)
            .GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct);

        var items = page!.Value.Items;
        items.Single(i => i.ToStatus == "preparado").ChangedByCompania.Should().Be("Renting Colombia S.A.S");
        items.Single(i => i.ToStatus == "aprobado").ChangedByCompania.Should().Be("Tránsito de Funza");
    }
}
