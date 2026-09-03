using Flit.Admin.Application.OtClientProcedures.GetOtBandejaCounters;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// Contadores de la cabecera de la bandeja OT y el filtro de sub-estado de placa que los hace
/// pulsables.
///
/// <para>Lo que fijan estas pruebas es la relación entre CONTAR y FILTRAR: cada tarjeta promete una
/// cifra y, al pulsarla, tiene que llevar exactamente a esas filas. Si el conteo y el filtro
/// miraran universos distintos, la tarjeta diría "9" y la lista mostraría otra cosa — que es peor
/// que no tener tarjeta.</para>
///
/// <para>Todos los escenarios siembran CON convenio vigente salvo el que mide justo eso: así, si
/// algo falla, no puede ser el grant.</para>
/// </summary>
public sealed class OtBandejaContadoresTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtroTenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureTypeA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ── Contadores ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CuentaCadaClaseSobreTodoElUniverso()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            // Ruta de placa: dos esperando placa, uno ya con ella y otro terminado.
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-1", PlateFlowStatus.Preasignado);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-2", PlateFlowStatus.Preasignado);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-3", PlateFlowStatus.Asignado);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-4", PlateFlowStatus.Terminado);
            // Fuera de la ruta de placa: nadie los ha tocado.
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-5", plateFlowStatus: null);
            // Desenlaces.
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Aprobado, "R-6", plateFlowStatus: null);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Rechazado, "R-7", plateFlowStatus: null);
        }

        var counters = await Contar(db);

        counters.TransitOfficeResolved.Should().BeTrue();
        counters.SinAsignarPlaca.Should().Be(2);
        counters.ConPlacaAsignada.Should().Be(2);
        counters.SinGestion.Should().Be(1);
        counters.Aprobados.Should().Be(1);
        counters.Rechazados.Should().Be(1);
    }

    // "Sin gestión" es lo que NADIE ha empezado. Un trámite ya preasignado sí se está gestionando
    // —el organismo tiene que ponerle placa—, así que contarlo ahí inflaría la tarjeta que
    // precisamente dice "esto está parado".
    [Fact]
    public async Task SinGestion_NoIncluyeLosQueYaEstanEnRutaDePlaca()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-1", PlateFlowStatus.Preasignado);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-2", plateFlowStatus: null);
        }

        var counters = await Contar(db);

        counters.SinGestion.Should().Be(1);
        counters.SinAsignarPlaca.Should().Be(1);
    }

    // El conteo tiene que mirar el MISMO universo que la lista: si contara más ancho, la tarjeta
    // prometería filas que al pulsarla no aparecerían.
    [Fact]
    public async Task NoCuentaLosTramitesDeUnaEmpresaSinConvenio()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-CON", plateFlowStatus: null);

            // Empresa dirigida al mismo organismo pero SIN grant: invisible para la bandeja.
            seed.Tenants.Add(new Tenant
            {
                Id = OtroTenant,
                Code = "sin-grant",
                LegalName = "Sin Convenio S.A.S.",
                TaxId = "900999999",
                TenantType = "client",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = OtroTenant,
                ProcedureTypeId = ProcedureTypeA,
                ReferenceNumber = "R-SIN",
                Status = TramiteEstado.Entregado,
                TransitOfficeId = TransitOffice,
                CreatedByUserId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.SaveChanges();
        }

        var counters = await Contar(db);

        counters.SinGestion.Should().Be(1);
    }

    [Fact]
    public async Task SinTramites_DevuelveCerosYNoNulos()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
        }

        var counters = await Contar(db);

        // Resuelto y en cero: es distinto de "el tenant no tiene organismo", que la tira pinta
        // con guiones en vez de con ceros.
        counters.TransitOfficeResolved.Should().BeTrue();
        counters.SinAsignarPlaca.Should().Be(0);
        counters.ConPlacaAsignada.Should().Be(0);
        counters.SinGestion.Should().Be(0);
    }

    // ── Filtro de sub-estado de placa ─────────────────────────────────────────────────

    [Fact]
    public async Task FiltraPorUnSubEstadoDePlaca()
    {
        var db = await SeedRutaDePlacaAsync();

        var bandeja = await Listar(db, plateFlowStatus: PlateFlowStatus.Preasignado);

        bandeja.TotalCount.Should().Be(1);
        bandeja.Data.Should().OnlyContain(p => p.ReferenceNumber == "R-PRE");
    }

    // La tarjeta "Con placa asignada" cuenta dos sub-estados a la vez: el filtro tiene que aceptar
    // los dos en una sola petición o la lista no coincidiría con la cifra.
    [Fact]
    public async Task FiltraPorVariosSubEstadosSeparadosPorComa()
    {
        var db = await SeedRutaDePlacaAsync();

        var bandeja = await Listar(db, plateFlowStatus: "asignado,terminado");

        bandeja.TotalCount.Should().Be(2);
        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo("R-ASI", "R-TER");
    }

    // `sin_ruta` no es un valor de la columna sino su AUSENCIA: es lo que hace pulsable la tarjeta
    // "Sin gestión", que cuenta justo los que no entraron en la ruta de placa.
    [Fact]
    public async Task FiltraLosQueNoEstanEnRutaDePlaca()
    {
        var db = await SeedRutaDePlacaAsync();

        var bandeja = await Listar(db, plateFlowStatus: "sin_ruta");

        bandeja.TotalCount.Should().Be(1);
        bandeja.Data.Should().OnlyContain(p => p.ReferenceNumber == "R-NULA");
    }

    [Fact]
    public async Task SinFiltroDePlaca_DevuelveTodaLaBandeja()
    {
        var db = await SeedRutaDePlacaAsync();

        var bandeja = await Listar(db, plateFlowStatus: null);

        bandeja.TotalCount.Should().Be(4);
    }

    /// <summary>Los cuatro sub-estados posibles, uno de cada, todos entregados y con convenio.</summary>
    private static async Task<string> SeedRutaDePlacaAsync()
    {
        var db = NewDbName();

        await using var seed = NewContext(db);
        SeedEscenarioBase(seed);
        SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-PRE", PlateFlowStatus.Preasignado);
        SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-ASI", PlateFlowStatus.Asignado);
        SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-TER", PlateFlowStatus.Terminado);
        SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-NULA", plateFlowStatus: null);

        await Task.CompletedTask;
        return db;
    }

    private static async Task<GetOtBandejaCountersResult> Contar(string db)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        return await new GetOtBandejaCountersHandler(repo).HandleAsync(
            new GetOtBandejaCountersQuery { OtTenantId = OtTenant },
            TestContext.Current.CancellationToken);
    }

    private static async Task<ListOtClientProceduresResult> Listar(string db, string? plateFlowStatus)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        return await new ListOtClientProceduresHandler(repo).HandleAsync(
            new ListOtClientProceduresQuery
            {
                OtTenantId = OtTenant,
                PlateFlowStatus = plateFlowStatus,
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Organismo, convenio VIGENTE y catálogo: todo lo que no se está midiendo aquí.</summary>
    private static void SeedEscenarioBase(FlitDbContext ctx)
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
            Code = "client",
            LegalName = "Flota Andina S.A.S.",
            TaxId = "900000000",
            TenantType = "client",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = ProcedureTypeA,
            Code = "matricula_inicial",
            Name = "Matrícula inicial",
            Family = "MATRICULAS",
            IsActive = true,
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        ctx.SaveChanges();
    }

    private static void SeedProcedure(
        FlitDbContext ctx,
        Guid id,
        string status,
        string reference,
        string? plateFlowStatus)
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureTypeA,
            ReferenceNumber = reference,
            Status = status,
            PlateFlowStatus = plateFlowStatus,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
