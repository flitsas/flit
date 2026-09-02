using Flit.Admin.Application.OtClientProcedures.GetOtBandejaHealth;
using Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;
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
/// HU #11945 — el organismo de tránsito solo ve los trámites que ya le fueron entregados.
///
/// <para>Antes, la consulta base de la bandeja (<c>BuildAccessibleQuery</c>) solo filtraba por
/// organismo destino y convenio vigente, sin mirar el estado. Bastaba con listar sin filtro —o pedir
/// el detalle por id— para leer trámites que la empresa cliente todavía estaba redactando. Estas
/// pruebas fijan el universo visible: <c>entregado</c>, <c>aprobado</c> y <c>rechazado</c>.</para>
///
/// <para>Todos los escenarios siembran el trámite CON convenio vigente a propósito: así, si algo
/// falla, no puede ser el grant. Lo único que se está midiendo es el estado.</para>
/// </summary>
public sealed class OtBandejaEstadosRecibidosTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureTypeA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // AC1 — un borrador dirigido al organismo no existe para él: ni en la lista ni en el total.
    [Fact]
    public async Task AC1_Borrador_NoApareceEnLaBandeja()
    {
        var db = NewDbName();
        var borrador = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, borrador, TramiteEstado.Borrador, "REF-BORRADOR");
        }

        var bandeja = await ListarSinFiltro(db);

        bandeja.Data.Should().NotContain(p => p.Id == borrador);
        bandeja.TotalCount.Should().Be(0);
    }

    // AC2 — 'preparado' es el trámite listo que el cliente todavía no ha enviado: tampoco llegó.
    [Fact]
    public async Task AC2_Preparado_NoApareceEnLaBandeja()
    {
        var db = NewDbName();
        var preparado = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, preparado, TramiteEstado.Preparado, "REF-PREPARADO");
        }

        var bandeja = await ListarSinFiltro(db);

        bandeja.Data.Should().NotContain(p => p.Id == preparado);
        bandeja.TotalCount.Should().Be(0);
    }

    // AC3 — la contraparte de AC1/AC2: lo que sí llegó se sigue viendo. Sin esta prueba, un filtro
    // demasiado agresivo (vaciar la bandeja) pasaría AC1 y AC2 sin problema.
    [Fact]
    public async Task AC3_EntregadoAprobadoYRechazado_SeSiguenViendo()
    {
        var db = NewDbName();
        var entregado = Guid.NewGuid();
        var aprobado = Guid.NewGuid();
        var rechazado = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, entregado, TramiteEstado.Entregado, "REF-ENTREGADO");
            SeedProcedure(seed, aprobado, TramiteEstado.Aprobado, "REF-APROBADO");
            SeedProcedure(seed, rechazado, TramiteEstado.Rechazado, "REF-RECHAZADO");
        }

        var bandeja = await ListarSinFiltro(db);

        bandeja.TotalCount.Should().Be(3);
        bandeja.Data.Select(p => p.Id)
            .Should().BeEquivalentTo([entregado, aprobado, rechazado]);
    }

    // AC3 (contrato) — con estados mezclados, la bandeja deja pasar exactamente los tres recibidos.
    // Es el escenario realista: el organismo y la empresa comparten la misma tabla.
    [Fact]
    public async Task AC3_ConEstadosMezclados_SoloPasanLosRecibidos()
    {
        var db = NewDbName();
        var entregado = Guid.NewGuid();
        var aprobado = Guid.NewGuid();
        var rechazado = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Borrador, "REF-B");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Preparado, "REF-P");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Anulado, "REF-A");
            SeedProcedure(seed, entregado, TramiteEstado.Entregado, "REF-E");
            SeedProcedure(seed, aprobado, TramiteEstado.Aprobado, "REF-AP");
            SeedProcedure(seed, rechazado, TramiteEstado.Rechazado, "REF-R");
        }

        var bandeja = await ListarSinFiltro(db);

        bandeja.TotalCount.Should().Be(3);
        bandeja.Data.Select(p => p.Id)
            .Should().BeEquivalentTo([entregado, aprobado, rechazado]);
    }

    // AC4 — el agujero que el desplegable no tapaba: con el id en la mano, el detalle lo servía igual.
    [Fact]
    public async Task AC4_DetallePorIdDeUnBorrador_DevuelveNotFound()
    {
        var db = NewDbName();
        var borrador = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, borrador, TramiteEstado.Borrador, "REF-BORRADOR");
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var detalle = await new GetOtClientProcedureHandler(repo).HandleAsync(
            new GetOtClientProcedureQuery { OtTenantId = OtTenant, ProcedureInstanceId = borrador },
            TestContext.Current.CancellationToken);

        detalle.Status.Should().Be(GetOtClientProcedureStatus.NotFound);
        detalle.Procedure.Should().BeNull();
    }

    // AC4 (contraparte) — el detalle de un entregado sigue resolviendo. Un 404 universal también
    // pasaría la prueba anterior.
    [Fact]
    public async Task AC4_DetallePorIdDeUnEntregado_SigueResolviendo()
    {
        var db = NewDbName();
        var entregado = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, entregado, TramiteEstado.Entregado, "REF-ENTREGADO");
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var detalle = await new GetOtClientProcedureHandler(repo).HandleAsync(
            new GetOtClientProcedureQuery { OtTenantId = OtTenant, ProcedureInstanceId = entregado },
            TestContext.Current.CancellationToken);

        detalle.Status.Should().Be(GetOtClientProcedureStatus.Found);
        detalle.Procedure!.Id.Should().Be(entregado);
    }

    // AC5 — el deep-link '?status=borrador' no es un error de cliente: es una petición legítima cuya
    // respuesta correcta es "no hay nada". Devolver 400 obligaría al frontend a distinguir casos.
    [Fact]
    public async Task AC5_FiltrarPorEstadoNoPermitido_DevuelveListaVacia()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Borrador, "REF-BORRADOR");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-ENTREGADO");
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var bandeja = await new ListOtClientProceduresHandler(repo).HandleAsync(
            new ListOtClientProceduresQuery { OtTenantId = OtTenant, Status = TramiteEstado.Borrador },
            TestContext.Current.CancellationToken);

        bandeja.Data.Should().BeEmpty();
        bandeja.TotalCount.Should().Be(0);
    }

    // AC6 — el diagnóstico de bandeja mide otra cosa (entregados con y sin convenio) y NO puede
    // moverse con este cambio: es la herramienta con la que se depura un convenio mal configurado.
    [Fact]
    public async Task AC6_ElDiagnosticoDeBandeja_NoSeVeAfectado()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedEscenarioBase(seed);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Borrador, "REF-BORRADOR");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Preparado, "REF-PREPARADO");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-E1");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-E2");
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var health = await new GetOtBandejaHealthHandler(repo).HandleAsync(
            new GetOtBandejaHealthQuery { OtTenantId = OtTenant },
            TestContext.Current.CancellationToken);

        // Cuenta los dos entregados y ninguno de los no entregados: el diagnóstico ya filtraba por
        // 'entregado' por su cuenta, así que el borrador y el preparado nunca contaron.
        health.TransitOfficeResolved.Should().BeTrue();
        health.DeliveredTotal.Should().Be(2);
        health.DeliveredWithGrant.Should().Be(2);
        health.DeliveredWithoutGrant.Should().Be(0);
    }

    private static async Task<ListOtClientProceduresResult> ListarSinFiltro(string db)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        return await new ListOtClientProceduresHandler(repo).HandleAsync(
            new ListOtClientProceduresQuery { OtTenantId = OtTenant },
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

    private static void SeedProcedure(FlitDbContext ctx, Guid id, string status, string reference)
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
