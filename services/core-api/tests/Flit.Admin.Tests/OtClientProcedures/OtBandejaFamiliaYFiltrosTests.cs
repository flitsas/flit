using Flit.Admin.Application.OtClientProcedures.GetOtBandejaCounters;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Api.Endpoints;
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
/// Epic #12686 (HU #12803) — pestañas de familia en la bandeja del OT y contadores que cuentan bajo
/// los mismos filtros que la tabla.
///
/// <para>Lo que fijan: la tarjeta seleccionada tiene que decir cuántas filas trae la tabla. Si los
/// contadores miraran todo el organismo mientras la tabla mira una familia o una búsqueda, la
/// tarjeta prometería filas que no aparecen.</para>
/// </summary>
public sealed class OtBandejaFamiliaYFiltrosTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid OtroOrganismo = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid TipoMatricula = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TipoTraspaso = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact] // AC1
    public async Task ListadoPorFamilia_DevuelveSoloEsaFamilia()
    {
        var db = await SembrarAsync();

        var bandeja = await Listar(db, new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            Status = TramiteEstado.Entregado,
            Familia = ProcedureFamilyCodes.Matriculas,
        });

        bandeja.TotalCount.Should().Be(1);
        bandeja.Data.Should().OnlyContain(p => p.ReferenceNumber == "M-ENT");
    }

    [Fact] // AC2
    public async Task ContadoresPorFamilia_CuentanSoloEsaFamilia()
    {
        var db = await SembrarAsync();

        var traspaso = await Contar(db, new OtClientProcedureFilter { Familia = "traspaso" });

        traspaso.PorDecidir.Should().Be(2);
        traspaso.Aprobados.Should().Be(1);
        traspaso.Preasignacion.Should().Be(0, "la familia Traspaso no usa la ruta de placa");
        traspaso.Asignados.Should().Be(0);

        var matriculas = await Contar(db, new OtClientProcedureFilter { Familia = ProcedureFamilyCodes.Matriculas });
        matriculas.Preasignacion.Should().Be(1);
        matriculas.Asignados.Should().Be(1);
        matriculas.PorDecidir.Should().Be(1);
    }

    [Fact] // AC3
    public async Task ContadoresConFiltros_CoincidenConElTotalDeLaTabla()
    {
        var db = await SembrarAsync();
        var filtro = new OtClientProcedureFilter { Familia = ProcedureFamilyCodes.Traspaso, Vin = "vin-t1" };

        var counters = await Contar(db, filtro);
        var tabla = await Listar(db, new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            Status = TramiteEstado.Entregado,
            Familia = ProcedureFamilyCodes.Traspaso,
            Vin = "vin-t1",
        });

        counters.PorDecidir.Should().Be(1);
        tabla.TotalCount.Should().Be(counters.PorDecidir);
        counters.Aprobados.Should().Be(0);
    }

    [Fact] // AC3 — el estado y la marca de revocatoria del filtro no acotan los contadores.
    public async Task ContadoresIgnoranElEstadoYLaRevocatoriaDelFiltro()
    {
        var db = await SembrarAsync();

        var counters = await Contar(db, new OtClientProcedureFilter
        {
            Familia = ProcedureFamilyCodes.Traspaso,
            Status = TramiteEstado.Aprobado,
            HasActiveRevocationRequest = true,
        });

        counters.PorDecidir.Should().Be(2, "cada tarjeta cuenta su propia clase, no la elegida");
        counters.Aprobados.Should().Be(1);
    }

    [Fact] // AC4
    public async Task SinFiltro_CuentaTodoComoHoy()
    {
        var db = await SembrarAsync();

        var sinFiltro = await Contar(db, null);
        var filtroVacio = await Contar(db, new OtClientProcedureFilter());

        sinFiltro.PorDecidir.Should().Be(3);
        sinFiltro.Preasignacion.Should().Be(1);
        sinFiltro.Aprobados.Should().Be(1);
        filtroVacio.Should().BeEquivalentTo(sinFiltro);
    }

    [Fact] // AC5
    public async Task AprobadoConRevocatoriaActiva_SumaEnAprobadosYEnSolicitudes()
    {
        var db = await SembrarAsync();

        var counters = await Contar(db, new OtClientProcedureFilter { Familia = ProcedureFamilyCodes.Traspaso });

        counters.Aprobados.Should().Be(1);
        counters.SolicitudesRevocatoria.Should().Be(1);
    }

    [Fact] // AC7
    public async Task ContadoresNuncaCuentanOtroOrganismo()
    {
        var db = await SembrarAsync();

        var counters = await Contar(db, new OtClientProcedureFilter { Familia = ProcedureFamilyCodes.Matriculas });

        // M-OTRO (entregado, otro organismo) no entra aunque cumpla la familia.
        counters.PorDecidir.Should().Be(1);
    }

    [Theory] // AC6
    [InlineData("XYZ")]
    [InlineData("matricula_inicial")]
    public void FamiliaInvalida_DevuelveMensajeConLaFamilia(string familia)
    {
        var error = new OtBandejaSearchRequest { Familia = familia }.ValidarFamilia();

        error.Should().NotBeNull();
        error.Should().Contain(familia).And.Contain("MATRICULAS");
    }

    [Theory] // AC6 — lo válido y lo ausente no son error.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MATRICULAS")]
    [InlineData(" otros ")]
    public void FamiliaValidaOAusente_NoEsError(string? familia) =>
        new OtBandejaSearchRequest { Familia = familia }.ValidarFamilia().Should().BeNull();

    [Fact]
    public void FiltroDeConteo_DescartaEstadoRevocatoriaOrdenYPagina()
    {
        var filtro = new OtBandejaSearchRequest
        {
            Familia = "TRASPASO",
            Busqueda = "ABC123",
            Status = TramiteEstado.Entregado,
            HasActiveRevocationRequest = true,
            SortBy = "vin",
            Page = 3,
        }.ToFiltroDeConteo();

        filtro.Familia.Should().Be("TRASPASO");
        filtro.Busqueda.Should().Be("ABC123");
        filtro.Status.Should().BeNull();
        filtro.HasActiveRevocationRequest.Should().BeNull();
        filtro.SortBy.Should().BeNull();
        filtro.Page.Should().Be(1);
    }

    // ── Escenario ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Matrículas: preasignación, asignado y entregado. Traspaso: dos entregados y un aprobado con
    /// solicitud de revocatoria activa. Más un entregado de matrícula dirigido a OTRO organismo.
    /// </summary>
    private static async Task<string> SembrarAsync()
    {
        var db = Guid.NewGuid().ToString();
        await using var ctx = NewContext(db);

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
        ctx.ProcedureTypes.Add(Tipo(TipoMatricula, "matricula_inicial", "MATRICULAS"));
        ctx.ProcedureTypes.Add(Tipo(TipoTraspaso, "traspaso", "TRASPASO"));

        Tramite(ctx, TipoMatricula, TramiteEstado.Preasignacion, "M-PRE", "VIN-M1");
        Tramite(ctx, TipoMatricula, TramiteEstado.Asignado, "M-ASI", "VIN-M2");
        Tramite(ctx, TipoMatricula, TramiteEstado.Entregado, "M-ENT", "VIN-M3");
        Tramite(ctx, TipoMatricula, TramiteEstado.Entregado, "M-OTRO", "VIN-M4", OtroOrganismo);
        Tramite(ctx, TipoTraspaso, TramiteEstado.Entregado, "T-ENT1", "VIN-T1");
        Tramite(ctx, TipoTraspaso, TramiteEstado.Entregado, "T-ENT2", "VIN-T2");
        var aprobado = Tramite(ctx, TipoTraspaso, TramiteEstado.Aprobado, "T-APR", "VIN-T3");
        ctx.ProcedureRevocationRequests.Add(new ProcedureRevocationRequest
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = aprobado,
            AttemptNumber = 1,
            Status = ProcedureRevocationRequestStatus.Solicitada,
            RequestedBy = Guid.NewGuid(),
            RequestedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static ProcedureType Tipo(Guid id, string code, string family) => new()
    {
        Id = id,
        Code = code,
        Name = code,
        Family = family,
        IsActive = true,
        PublicationStatus = PublicationStatus.Published,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Guid Tramite(
        FlitDbContext ctx, Guid tipo, string status, string reference, string vin, Guid? organismo = null)
    {
        var id = Guid.NewGuid();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = tipo,
            ReferenceNumber = reference,
            Status = status,
            Vin = vin,
            TransitOfficeId = organismo ?? TransitOffice,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return id;
    }

    private static async Task<GetOtBandejaCountersResult> Contar(string db, OtClientProcedureFilter? filtro)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        return await new GetOtBandejaCountersHandler(repo).HandleAsync(
            new GetOtBandejaCountersQuery { OtTenantId = OtTenant, Filtro = filtro },
            TestContext.Current.CancellationToken);
    }

    private static async Task<ListOtClientProceduresResult> Listar(string db, ListOtClientProceduresQuery query)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        return await new ListOtClientProceduresHandler(repo).HandleAsync(query, TestContext.Current.CancellationToken);
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
