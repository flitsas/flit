using Flit.Admin.Domain.PlatePreassign;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.PlatePreassign;

/// <summary>
/// Inventario de preasignación de placa (HU #10650, Feature #10587): ciclo de vida de la placa
/// (máquina de estados), reglas del rango y repositorio (creación con explosión + listados).
/// </summary>
public sealed class PlatePreassignTests
{
    // ---------- Ciclo de vida de la placa ----------

    [Theory]
    [InlineData("disponible", "preasignada")]
    [InlineData("disponible", "bloqueada")]
    [InlineData("preasignada", "utilizada")]
    [InlineData("preasignada", "revocada")]
    [InlineData("bloqueada", "disponible")]
    [InlineData("revocada", "disponible")]
    public void PlateStateMachine_TransicionesValidas(string from, string to)
    {
        PlateStateMachine.IsValidTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData("disponible", "utilizada")]
    [InlineData("utilizada", "disponible")]
    [InlineData("preasignada", "bloqueada")]
    [InlineData("bloqueada", "preasignada")]
    public void PlateStateMachine_TransicionesInvalidas(string from, string to)
    {
        PlateStateMachine.IsValidTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void PlateStateMachine_UtilizadaEsTerminal()
    {
        PlateStateMachine.TransitionsFrom(PlateState.Utilizada).Should().BeEmpty();
        PlateState.Todos.Should().HaveCount(5);
        PlateState.EsValido("disponible").Should().BeTrue();
        PlateState.EsValido("desconocido").Should().BeFalse();
    }

    // ---------- Reglas del rango ----------

    [Fact]
    public void PlateRangeRules_ValidaPrefijoYRango()
    {
        PlateRangeRules.Validate("ABC", 100, 200).Should().BeNull();
        PlateRangeRules.Validate("AB", 100, 200).Should().NotBeNull();       // prefijo corto
        PlateRangeRules.Validate("ABC", 200, 100).Should().NotBeNull();      // from > to
        PlateRangeRules.Validate("ABC", -1, 200).Should().NotBeNull();       // fuera de rango
        PlateRangeRules.Validate("ABC", 0, 999).Should().BeNull();           // límite exacto (1000)
        PlateRangeRules.Validate("abc", 100, 200).Should().NotBeNull();      // minúsculas
    }

    [Fact]
    public void PlateRangeRules_FormateaYExplota()
    {
        PlateRangeRules.Format("ABC", 7).Should().Be("ABC007");
        var plates = PlateRangeRules.Enumerate("ABC", 100, 102).ToList();
        plates.Should().Equal("ABC100", "ABC101", "ABC102");
    }

    // ---------- Repositorio ----------

    [Fact]
    public async Task CreateRange_ExplotaEnPlacasDisponibles()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            var repo = new PlateRangeRepository(act);
            var result = await repo.CreateRangeAsync(company, office, "ABC", 100, 109, null, TestContext.Current.CancellationToken);
            result.Success.Should().BeTrue();
            result.PlatesCreated.Should().Be(10);
        }

        await using var verify = NewContext(db);
        var repo2 = new PlateRangeRepository(verify);
        var details = await repo2.ListDetailsAsync(company, office, null, TestContext.Current.CancellationToken);
        details.Should().HaveCount(10);
        details.Should().OnlyContain(d => d.State == PlateState.Disponible);

        var ranges = await repo2.ListRangesAsync(company, null, TestContext.Current.CancellationToken);
        ranges.Should().ContainSingle();
        ranges[0].TotalPlates.Should().Be(10);
        ranges[0].AvailablePlates.Should().Be(10);
    }

    [Fact]
    public async Task CreateRange_RechazaRangoInvalido()
    {
        await using var ctx = NewContext(NewDbName());
        var repo = new PlateRangeRepository(ctx);
        var result = await repo.CreateRangeAsync(Guid.NewGuid(), Guid.NewGuid(), "AB", 100, 200, null, TestContext.Current.CancellationToken);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.PlatesCreated.Should().Be(0);
    }

    [Fact]
    public async Task CreateRange_RechazaSolapamientoEnElMismoOT()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();

        await using (var a = NewContext(db))
        {
            var repo = new PlateRangeRepository(a);
            (await repo.CreateRangeAsync(company, office, "ABC", 100, 110, null, TestContext.Current.CancellationToken)).Success.Should().BeTrue();
        }

        await using var b = NewContext(db);
        var repo2 = new PlateRangeRepository(b);
        // Se solapa en ABC105–ABC110 para el mismo OT.
        var overlap = await repo2.CreateRangeAsync(company, office, "ABC", 105, 115, null, TestContext.Current.CancellationToken);
        overlap.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ListDetails_FiltraPorEstado()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();

        await using (var a = NewContext(db))
        {
            var repo = new PlateRangeRepository(a);
            await repo.CreateRangeAsync(company, office, "XYZ", 500, 502, null, TestContext.Current.CancellationToken);
        }

        await using var b = NewContext(db);
        var repo2 = new PlateRangeRepository(b);
        var disponibles = await repo2.ListDetailsAsync(company, office, PlateState.Disponible, TestContext.Current.CancellationToken);
        disponibles.Should().HaveCount(3);
        var utilizadas = await repo2.ListDetailsAsync(company, office, PlateState.Utilizada, TestContext.Current.CancellationToken);
        utilizadas.Should().BeEmpty();
    }

    // ---------- Consola OT (HU #10651): edición 60 min, estado, autorización ----------

    [Fact]
    public async Task EditRange_ReExplotaDentroDeLaVentana()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        Guid rangeId;

        await using (var a = NewContext(db))
        {
            var repo = new PlateRangeRepository(a);
            var r = await repo.CreateRangeAsync(company, office, "ABC", 100, 102, null, TestContext.Current.CancellationToken);
            rangeId = r.RangeId!.Value;
        }

        await using (var b = NewContext(db))
        {
            var repo = new PlateRangeRepository(b);
            var edit = await repo.EditRangeAsync(rangeId, "ABC", 200, 204, null, TestContext.Current.CancellationToken);
            edit.Success.Should().BeTrue();
            edit.PlatesCreated.Should().Be(5);
        }

        await using var verify = NewContext(db);
        var repo2 = new PlateRangeRepository(verify);
        var details = await repo2.ListDetailsAsync(company, office, null, TestContext.Current.CancellationToken);
        details.Should().HaveCount(5);
        details.Select(d => d.Plate).Should().Contain("ABC200").And.NotContain("ABC100");
    }

    [Fact]
    public async Task EditRange_FallaFueraDeLaVentana()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        Guid rangeId;

        await using (var a = NewContext(db))
        {
            var repo = new PlateRangeRepository(a);
            rangeId = (await repo.CreateRangeAsync(company, office, "ABC", 100, 102, null, TestContext.Current.CancellationToken)).RangeId!.Value;
        }

        await using (var expire = NewContext(db))
        {
            var range = await expire.PlateRanges.SingleAsync(r => r.Id == rangeId, TestContext.Current.CancellationToken);
            range.EditableUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            await expire.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var b = NewContext(db);
        var repo2 = new PlateRangeRepository(b);
        var edit = await repo2.EditRangeAsync(rangeId, "ABC", 200, 204, null, TestContext.Current.CancellationToken);
        edit.Success.Should().BeFalse();
        edit.Error.Should().Contain("60 min");
    }

    [Fact]
    public async Task EditRange_FallaSiHayPlacaEnUso()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        Guid rangeId;

        await using (var a = NewContext(db))
        {
            var repo = new PlateRangeRepository(a);
            rangeId = (await repo.CreateRangeAsync(company, office, "ABC", 100, 102, null, TestContext.Current.CancellationToken)).RangeId!.Value;
        }

        await using (var use = NewContext(db))
        {
            var plate = await use.PlateRangeDetails.FirstAsync(d => d.PlateRangeId == rangeId, TestContext.Current.CancellationToken);
            plate.State = PlateState.Preasignada;
            await use.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var b = NewContext(db);
        var repo2 = new PlateRangeRepository(b);
        var edit = await repo2.EditRangeAsync(rangeId, "ABC", 200, 204, null, TestContext.Current.CancellationToken);
        edit.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SetPlateState_BloquearDesbloquearYRechazarInvalida()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        Guid plateId;

        await using (var a = NewContext(db))
        {
            var repo = new PlateRangeRepository(a);
            await repo.CreateRangeAsync(company, office, "ABC", 100, 100, null, TestContext.Current.CancellationToken);
        }

        await using (var pick = NewContext(db))
        {
            plateId = (await pick.PlateRangeDetails.FirstAsync(TestContext.Current.CancellationToken)).Id;
        }

        await using var b = NewContext(db);
        var repo2 = new PlateRangeRepository(b);
        (await repo2.SetPlateStateAsync(plateId, PlateState.Bloqueada, TestContext.Current.CancellationToken)).Success.Should().BeTrue();
        (await repo2.SetPlateStateAsync(plateId, PlateState.Disponible, TestContext.Current.CancellationToken)).Success.Should().BeTrue();
        // disponible → utilizada no es válida.
        (await repo2.SetPlateStateAsync(plateId, PlateState.Utilizada, TestContext.Current.CancellationToken)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task IsAssignmentAllowed_ExigeFlagGrantYAllow()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        var otTenant = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(), TenantId = company, PlatePreassignEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(), TenantId = company, TransitOfficeId = office, IsEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.OtRequirements.Add(new OtRequirementsEntity
            {
                Id = Guid.NewGuid(), TenantId = otTenant, TransitOfficeId = office, AllowPlatePreassign = true, CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new PlateRangeRepository(ctx);
        (await repo.IsAssignmentAllowedAsync(company, office, TestContext.Current.CancellationToken)).Should().BeTrue();
        // Sin flag de otra compañía → false.
        (await repo.IsAssignmentAllowedAsync(Guid.NewGuid(), office, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    // ---------- HU #10797: selector de compañías elegibles ----------

    [Fact] // Solo compañías con preasignación activa + grant vigente con el OT; devuelve el nombre.
    public async Task ListEligibleCompanies_FiltraPorPreasignacionYGrant()
    {
        var db = NewDbName();
        var office = Guid.NewGuid();
        var otTenant = Guid.NewGuid();
        var elegible = Guid.NewGuid();
        var sinFlag = Guid.NewGuid();
        var sinGrant = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seed = NewContext(db))
        {
            seed.OtRequirements.Add(new OtRequirementsEntity
            {
                Id = Guid.NewGuid(), TenantId = otTenant, TransitOfficeId = office, AllowPlatePreassign = true, CreatedAt = now,
            });
            // Elegible: flag activo + grant vigente.
            seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy { Id = Guid.NewGuid(), TenantId = elegible, PlatePreassignEnabled = true, CreatedAt = now });
            seed.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant { Id = Guid.NewGuid(), TenantId = elegible, TransitOfficeId = office, IsEnabled = true, CreatedAt = now });
            seed.Tenants.Add(new Tenant { Id = elegible, LegalName = "Flota Andina S.A.S.", Code = "FA", TaxId = "900000001-1", TenantType = "company", CreatedAt = now });
            // Sin flag: grant pero preasignación desactivada → excluida.
            seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy { Id = Guid.NewGuid(), TenantId = sinFlag, PlatePreassignEnabled = false, CreatedAt = now });
            seed.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant { Id = Guid.NewGuid(), TenantId = sinFlag, TransitOfficeId = office, IsEnabled = true, CreatedAt = now });
            seed.Tenants.Add(new Tenant { Id = sinFlag, LegalName = "Sin Flag S.A.", Code = "SF", TaxId = "2", TenantType = "company", CreatedAt = now });
            // Sin grant: flag activo pero sin grant con este OT → excluida.
            seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy { Id = Guid.NewGuid(), TenantId = sinGrant, PlatePreassignEnabled = true, CreatedAt = now });
            seed.Tenants.Add(new Tenant { Id = sinGrant, LegalName = "Sin Grant S.A.", Code = "SG", TaxId = "3", TenantType = "company", CreatedAt = now });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new PlateRangeRepository(ctx);
        var companies = await repo.ListEligibleCompaniesAsync(office, TestContext.Current.CancellationToken);

        companies.Should().ContainSingle();
        companies[0].TenantId.Should().Be(elegible);
        companies[0].Name.Should().Be("Flota Andina S.A.S.");
    }

    [Fact] // OT sin allow_plate_preassign → ninguna compañía elegible.
    public async Task ListEligibleCompanies_OtSinAllow_Vacio()
    {
        var db = NewDbName();
        var office = Guid.NewGuid();
        var company = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seed = NewContext(db))
        {
            seed.OtRequirements.Add(new OtRequirementsEntity { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), TransitOfficeId = office, AllowPlatePreassign = false, CreatedAt = now });
            seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy { Id = Guid.NewGuid(), TenantId = company, PlatePreassignEnabled = true, CreatedAt = now });
            seed.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant { Id = Guid.NewGuid(), TenantId = company, TransitOfficeId = office, IsEnabled = true, CreatedAt = now });
            seed.Tenants.Add(new Tenant { Id = company, LegalName = "X S.A.", Code = "X", TaxId = "1", TenantType = "company", CreatedAt = now });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new PlateRangeRepository(ctx);
        (await repo.ListEligibleCompaniesAsync(office, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    // ---------- Reserva de placa (Flujo A) ----------

    [Fact]
    public async Task TryReservePlate_ReservaDisponibleYEsIdempotente()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        var instance = Guid.NewGuid();

        await using (var a = NewContext(db))
        {
            await new PlateRangeRepository(a).CreateRangeAsync(company, office, "ABC", 100, 102, null, TestContext.Current.CancellationToken);
        }

        await using var b = NewContext(db);
        var repo = new PlateRangeRepository(b);
        (await repo.TryReservePlateAsync(company, office, "ABC100", instance, TestContext.Current.CancellationToken)).Should().BeTrue();
        // Idempotente: misma placa, mismo trámite.
        (await repo.TryReservePlateAsync(company, office, "ABC100", instance, TestContext.Current.CancellationToken)).Should().BeTrue();
        // Tomada por otro trámite → false.
        (await repo.TryReservePlateAsync(company, office, "ABC100", Guid.NewGuid(), TestContext.Current.CancellationToken)).Should().BeFalse();
        // Placa inexistente → false.
        (await repo.TryReservePlateAsync(company, office, "ZZZ999", instance, TestContext.Current.CancellationToken)).Should().BeFalse();

        var detail = await b.PlateRangeDetails.FirstAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken);
        detail.State.Should().Be(PlateState.Preasignada);
        detail.ProcedureInstanceId.Should().Be(instance);
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-plate-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
