using Flit.Admin.Application.OtClientProcedures.GetOtBandejaCounters;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RevocationRequests;
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

    [Fact] // HU #12598 AC6 — una tarjeta por estado real (ADR-0059).
    public async Task CuentaCadaClaseSobreTodoElUniverso()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            // Cola de placa: dos esperando placa, uno ya con ella (en manos del gestor).
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Preasignacion, "R-1");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Preasignacion, "R-2");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Asignado, "R-3");
            // Cola de decisión.
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-4");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-5");
            // Desenlaces.
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Aprobado, "R-6");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Rechazado, "R-7");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Revocado, "R-8");
            // Lo que el organismo no ve: ni cuenta.
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Borrador, "R-9");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Preparado, "R-10");
        }

        var counters = await Contar(db);

        counters.TransitOfficeResolved.Should().BeTrue();
        counters.Preasignacion.Should().Be(2);
        counters.Asignados.Should().Be(1);
        counters.PorDecidir.Should().Be(2);
        counters.Aprobados.Should().Be(1);
        counters.Rechazados.Should().Be(1);
        counters.Revocados.Should().Be(1);
    }

    // El conteo tiene que mirar el MISMO universo que la lista: si contara distinto, la tarjeta
    // prometería filas que al pulsarla no aparecerían. Desde HU #12350 AC7 ese universo incluye lo
    // ya recibido por el organismo aunque la empresa no tenga convenio vigente.
    [Fact]
    public async Task CuentaLosTramitesEntregadosAunqueLaEmpresaNoTengaConvenio()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-CON");

            // Empresa dirigida al mismo organismo pero SIN grant: su trámite ya entregado sigue
            // visible y contado (HU #12350 AC7) junto al de la empresa con convenio.
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

        counters.PorDecidir.Should().Be(2, "R-CON (con convenio) y R-SIN (entregado sin convenio) cuentan por igual");
    }

    // Pedido del usuario (2026-09-16) — la tarjeta "Solicitudes de revocatoria" cuenta SOLO las
    // ACTIVAS (`solicitada`/`en_revision`): una ya `rechazada` es una decisión que el organismo ya
    // tomó (el turno es del gestor), y una `aprobada` ya se ve como "Revocados" — sumarla aquí
    // también contaría el mismo desenlace dos veces.
    [Fact]
    public async Task CuentaSoloLasSolicitudesDeRevocatoriaActivas()
    {
        var db = NewDbName();
        var conSolicitud = Guid.NewGuid();
        var enRevision = Guid.NewGuid();
        var yaRechazada = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, conSolicitud, TramiteEstado.Aprobado, "R-SOL");
            SeedRevocationRequest(seed, conSolicitud, ProcedureRevocationRequestStatus.Solicitada);

            SeedProcedure(seed, enRevision, TramiteEstado.Aprobado, "R-REV");
            SeedRevocationRequest(seed, enRevision, ProcedureRevocationRequestStatus.EnRevision);

            SeedProcedure(seed, yaRechazada, TramiteEstado.Aprobado, "R-REC");
            SeedRevocationRequest(seed, yaRechazada, ProcedureRevocationRequestStatus.Rechazada);

            // Aprobado sin ninguna solicitud: no debe contar.
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Aprobado, "R-SINSOL");
            seed.SaveChanges();
        }

        var counters = await Contar(db);

        counters.SolicitudesRevocatoria.Should().Be(2);
        // No es lo mismo que "Aprobados": ese sigue contando los 4 (incluida la ya rechazada).
        counters.Aprobados.Should().Be(4);
    }

    [Fact]
    public async Task FiltraPorSolicitudDeRevocatoriaActiva()
    {
        var db = NewDbName();
        var conSolicitud = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, conSolicitud, TramiteEstado.Aprobado, "R-SOL");
            SeedRevocationRequest(seed, conSolicitud, ProcedureRevocationRequestStatus.Solicitada);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Aprobado, "R-SINSOL");
            seed.SaveChanges();
        }

        var bandeja = await Listar(db, status: null, hasActiveRevocationRequest: true);

        bandeja.TotalCount.Should().Be(1);
        bandeja.Data.Should().OnlyContain(p => p.ReferenceNumber == "R-SOL");
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
        counters.Preasignacion.Should().Be(0);
        counters.Asignados.Should().Be(0);
        counters.PorDecidir.Should().Be(0);
    }

    // ── Filtro de estado (una tarjeta = un estado; varios por coma para enlaces profundos) ──────

    [Fact]
    public async Task FiltraPorUnEstado()
    {
        var db = await SeedRutaDePlacaAsync();

        var bandeja = await Listar(db, status: TramiteEstado.Preasignacion);

        bandeja.TotalCount.Should().Be(1);
        bandeja.Data.Should().OnlyContain(p => p.ReferenceNumber == "R-PRE");
    }

    [Fact]
    public async Task FiltraPorVariosEstadosSeparadosPorComa()
    {
        var db = await SeedRutaDePlacaAsync();

        var bandeja = await Listar(db, status: "preasignacion, asignado");

        bandeja.TotalCount.Should().Be(2);
        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo("R-PRE", "R-ASI");
    }

    [Fact]
    public async Task SinFiltroDeEstado_DevuelveTodaLaBandeja()
    {
        var db = await SeedRutaDePlacaAsync();

        var bandeja = await Listar(db, status: null);

        bandeja.TotalCount.Should().Be(3);
    }

    /// <summary>Un trámite en cada estado de la cola del organismo, todos con convenio.</summary>
    private static async Task<string> SeedRutaDePlacaAsync()
    {
        var db = NewDbName();

        await using var seed = NewContext(db);
        SeedEscenarioBase(seed);
        SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Preasignacion, "R-PRE");
        SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Asignado, "R-ASI");
        SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "R-ENT");

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

    private static async Task<ListOtClientProceduresResult> Listar(
        string db, string? status, bool? hasActiveRevocationRequest = null)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        return await new ListOtClientProceduresHandler(repo).HandleAsync(
            new ListOtClientProceduresQuery
            {
                OtTenantId = OtTenant,
                Status = status,
                HasActiveRevocationRequest = hasActiveRevocationRequest,
            },
            TestContext.Current.CancellationToken);
    }

    private static void SeedRevocationRequest(FlitDbContext ctx, Guid instanceId, string status) =>
        ctx.ProcedureRevocationRequests.Add(new ProcedureRevocationRequest
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            AttemptNumber = 1,
            Status = status,
            RequestedBy = Guid.NewGuid(),
            RequestedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });

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
        string reference)
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureTypeA,
            ReferenceNumber = reference,
            Status = status,
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
