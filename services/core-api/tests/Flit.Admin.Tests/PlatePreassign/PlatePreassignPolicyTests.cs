using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.PlatePreassign;

/// <summary>
/// Decisión de ruta de preasignación al radicar (HU #10608, Feature #10587): matrícula inicial con
/// preasignación activa → asignado (con placa) o preasignado (sin placa); en otro caso, estándar.
/// </summary>
public sealed class PlatePreassignPolicyTests
{
    [Fact]
    public async Task Decide_FlujoA_ConPlacaDisponible_Asignado()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        var instance = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            await SeedRouteAsync(seed, company, office);
            await new PlateRangeRepository(seed).CreateRangeAsync(company, office, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
            SeedInstance(seed, instance, company, "matricula_inicial", office, plate: "ABC100");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var policy = new PlatePreassignPolicy(ctx, new PlateRangeRepository(ctx));
        var decision = await policy.DecideAsync(company, instance, TestContext.Current.CancellationToken);

        decision.Decision.Should().Be(PlateRouteDecision.Asignado);
        var detail = await ctx.PlateRangeDetails.FirstAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken);
        detail.ProcedureInstanceId.Should().Be(instance);
    }

    [Fact]
    public async Task Decide_FlujoB_SinPlaca_Preasignado()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        var instance = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            await SeedRouteAsync(seed, company, office);
            SeedInstance(seed, instance, company, "matricula_inicial", office, plate: null);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var policy = new PlatePreassignPolicy(ctx, new PlateRangeRepository(ctx));
        (await policy.DecideAsync(company, instance, TestContext.Current.CancellationToken))
            .Decision.Should().Be(PlateRouteDecision.Preasignado);
    }

    [Fact] // HU #10806 — el dígito de preferencia es informativo: sin placa (con o sin dígito) ⇒ Flujo B.
    public async Task Decide_FlujoB_SinPlacaConDigitoPreferencia_Preasignado()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        var instance = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            await SeedRouteAsync(seed, company, office);
            SeedInstance(seed, instance, company, "matricula_inicial", office, plate: null);
            // El radicador expresó un dígito de preferencia pero NO eligió placa: no debe alterar la ruta.
            seed.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(), ProcedureInstanceId = instance, TenantId = company,
                FieldKey = "plate_preferred_last_digit", ValueText = "7",
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var policy = new PlatePreassignPolicy(ctx, new PlateRangeRepository(ctx));
        var result = await policy.DecideAsync(company, instance, TestContext.Current.CancellationToken);

        result.Decision.Should().Be(PlateRouteDecision.Preasignado);
        result.Reason.Should().Be(PlateRouteReason.NoPlate);
    }

    [Fact]
    public async Task Decide_NoMatriculaInicial_Standard()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        var instance = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            await SeedRouteAsync(seed, company, office);
            SeedInstance(seed, instance, company, "traspaso", office, plate: null);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var policy = new PlatePreassignPolicy(ctx, new PlateRangeRepository(ctx));
        (await policy.DecideAsync(company, instance, TestContext.Current.CancellationToken))
            .Decision.Should().Be(PlateRouteDecision.Standard);
    }

    [Fact]
    public async Task Decide_RutaNoActiva_Standard()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        var instance = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            // Sin flag/grant/allow: la ruta no está activa.
            SeedInstance(seed, instance, company, "matricula_inicial", office, plate: "ABC100");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var policy = new PlatePreassignPolicy(ctx, new PlateRangeRepository(ctx));
        var result = await policy.DecideAsync(company, instance, TestContext.Current.CancellationToken);
        result.Decision.Should().Be(PlateRouteDecision.Standard);
        // La compañía no tiene el flag → estándar sin fricción (no bloqueo).
        result.Reason.Should().Be(PlateRouteReason.PreassignNotEnabled);
    }

    [Fact] // HU #10806 — la compañía SÍ tiene preasignación activa pero el OT está mal configurado
           // (sin grant/allow): se bloquea la radicación en vez de degradar a estándar en silencio.
    public async Task Decide_CompaniaActivaPeroOtMalConfigurado_Blocked()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();
        var instance = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            // Solo el flag de la compañía; falta el grant y el allow_plate_preassign del OT.
            seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(), TenantId = company, PlatePreassignEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
            });
            SeedInstance(seed, instance, company, "matricula_inicial", office, plate: null);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var policy = new PlatePreassignPolicy(ctx, new PlateRangeRepository(ctx));
        var result = await policy.DecideAsync(company, instance, TestContext.Current.CancellationToken);
        result.Decision.Should().Be(PlateRouteDecision.Blocked);
        result.Reason.Should().Be(PlateRouteReason.PreassignMisconfigured);
    }

    private static async Task SeedRouteAsync(FlitDbContext ctx, Guid company, Guid office)
    {
        ctx.TenantOperationalPolicies.Add(new TenantOperationalPolicy
        {
            Id = Guid.NewGuid(), TenantId = company, PlatePreassignEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(), TenantId = company, TransitOfficeId = office, IsEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.OtRequirements.Add(new OtRequirementsEntity
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), TransitOfficeId = office, AllowPlatePreassign = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static void SeedInstance(
        FlitDbContext ctx, Guid instanceId, Guid company, string modalidad, Guid office, string? plate)
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = instanceId,
            TenantId = company,
            ProcedureTypeId = Guid.NewGuid(),
            ModalidadEntrada = modalidad,
            Status = "preparado",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(), ProcedureInstanceId = instanceId, TenantId = company,
            FieldKey = "transit_office_id", ValueText = office.ToString(),
        });
        if (plate is not null)
        {
            ctx.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(), ProcedureInstanceId = instanceId, TenantId = company,
                FieldKey = "plate", ValueText = plate,
            });
        }
    }

    private static string NewDbName() => $"flit-platepol-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
