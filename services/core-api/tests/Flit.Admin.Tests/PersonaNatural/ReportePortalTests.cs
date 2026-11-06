using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Api.Endpoints;
using Flit.Api.PersonaNatural;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.PersonaNatural;
using Flit.Platform.Sdk;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14397 y #14398 (Feature #14396, Épica #14297) — reporte de los trámites del portal por alcance y su Excel. Sobre EF en
/// memoria: el alcance, los canales, los filtros y la paginación son lógica de consulta, no de la base.
/// </summary>
public sealed class ReportePortalTests
{
    private static readonly Guid OtA = Guid.NewGuid();
    private static readonly Guid OtB = Guid.NewGuid();
    private static readonly Guid OficinaA = Guid.NewGuid();
    private static readonly Guid OficinaB = Guid.NewGuid();
    private static readonly Guid TipoUno = Guid.NewGuid();
    private static readonly Guid TipoDos = Guid.NewGuid();
    private static readonly Guid Gestora = Guid.NewGuid();
    private static readonly Guid Gestor = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Portal = PlatformTenants.PersonaNatural;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Identificadores de las filas sembradas, por lo que prueban.
    private sealed record Sembrado(
        Guid WebA, Guid PuntoAConOtMovido, Guid WebB, Guid PuntoB, Guid DeGestor, Guid Historico, Guid Ict,
        Guid Aplicacion, Guid RechazadoAlCrear, Guid AnuladoPorUsuario);

    private static (string Db, Sembrado Ids) Sembrar(bool permisoDelPortalParaA = true)
    {
        var db = Guid.NewGuid().ToString();
        using var ctx = Contexto(db);
        var now = DateTimeOffset.UtcNow;

        ctx.Users.Add(new User { Id = Usuario, Email = "u@x.local", DisplayName = "U" });
        foreach (var (id, nombre) in new[] { (OficinaA, "Organismo A"), (OficinaB, "Organismo B") })
            ctx.TransitOffices.Add(new TransitOffice { Id = id, Code = nombre[^1].ToString(), Name = nombre, DepartmentCode = "11", CityCode = "11001" });
        foreach (var (tenant, oficina) in new[] { (OtA, OficinaA), (OtB, OficinaB) })
            ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
            { Id = Guid.NewGuid(), TenantId = tenant, TransitOfficeId = oficina, OperationMode = "dashboard", CreatedAt = now });
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant { Id = Guid.NewGuid(), TenantId = Portal, TransitOfficeId = OficinaB, IsEnabled = true, CreatedAt = now });
        if (permisoDelPortalParaA)
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant { Id = Guid.NewGuid(), TenantId = Portal, TransitOfficeId = OficinaA, IsEnabled = true, CreatedAt = now });
        ctx.ProcedureTypes.Add(new ProcedureType { Id = TipoUno, Code = "T1", Name = "Traspaso", Family = "TRASPASO" });
        ctx.ProcedureTypes.Add(new ProcedureType { Id = TipoDos, Code = "T2", Name = "Cambio de color", Family = "OTROS" });

        var ids = new Sembrado(
            WebA: Tramite(ctx, "R-1", CanalesReportePortal.Web, Portal, OficinaA, null, TipoUno, now.AddDays(-1)),
            // El punto de A cuyo trámite ya se movió a B al enviarlo: el organismo es el del punto.
            PuntoAConOtMovido: Tramite(ctx, "R-2", CanalesReportePortal.Punto, Portal, OficinaB, OficinaA, TipoDos, now.AddDays(-2)),
            WebB: Tramite(ctx, "R-3", CanalesReportePortal.Web, Portal, OficinaB, null, TipoUno, now.AddDays(-3)),
            PuntoB: Tramite(ctx, "R-4", CanalesReportePortal.Punto, Portal, OficinaB, OficinaB, TipoUno, now.AddDays(-4)),
            DeGestor: Tramite(ctx, "R-5", TramiteCanal.Tramites, Gestora, OficinaA, null, TipoUno, now.AddDays(-5)),
            Historico: Tramite(ctx, "R-6", null, Portal, OficinaA, null, TipoUno, now.AddDays(-6)),
            Ict: Tramite(ctx, "R-7", TramiteCanal.Integracion, Portal, OficinaA, null, TipoUno, now.AddDays(-7)),
            Aplicacion: Tramite(ctx, "R-8", CanalesReportePortal.Aplicacion, Gestora, OficinaA, null, TipoUno, now.AddDays(-8)),
            RechazadoAlCrear: Tramite(ctx, "R-9", CanalesReportePortal.Web, Portal, OficinaA, null, TipoUno, now.AddDays(-9), TramiteEstado.Anulado),
            AnuladoPorUsuario: Tramite(ctx, "R-10", CanalesReportePortal.Web, Portal, OficinaA, null, TipoUno, now.AddDays(-10), TramiteEstado.Anulado));

        Anulacion(ctx, ids.RechazadoAlCrear, $"{MotivosAnulacionPortal.RechazoEnCreacion} [documentos_incompletos]");
        Anulacion(ctx, ids.AnuladoPorUsuario, "La persona desistió");
        ctx.SaveChanges();
        return (db, ids);
    }

    private static Guid Tramite(
        FlitDbContext ctx, string radicado, string? canal, Guid tenant, Guid oficina, Guid? oficinaDelCanal, Guid tipo, DateTimeOffset creado,
        string estado = TramiteEstado.Borrador)
    {
        var id = Guid.NewGuid();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id, TenantId = tenant, ProcedureTypeId = tipo, ReferenceNumber = radicado, Status = estado, TransitOfficeId = oficina,
            Channel = canal, ChannelTransitOfficeId = oficinaDelCanal, CreatedByUserId = Usuario, CreatedAt = creado,
        });
        return id;
    }

    private static void Anulacion(FlitDbContext ctx, Guid id, string motivo) =>
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(), TenantId = Portal, ProcedureInstanceId = id, FromStatus = TramiteEstado.Borrador,
            ToStatus = TramiteEstado.Anulado, ChangedAt = DateTimeOffset.UtcNow, Reason = motivo,
        });

    private static FlitDbContext Contexto(string db) => new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(db).Options);

    private static readonly AlcanceReportePortal Plataforma = new(true, null, []);
    private static AlcanceReportePortal Organismo(Guid ot, params Guid[] pedidos) => new(false, ot, pedidos);

    private static async Task<(PaginaReportePortal? Pagina, string? Error)> Listar(
        string db, AlcanceReportePortal alcance, SolicitudReportePortal? solicitud = null, int? page = null, int? pageSize = null)
    {
        await using var ctx = Contexto(db);
        var handler = new ListarReportePortalHandler(new ReportePortalReadRepository(ctx));
        var r = await handler.HandleAsync(alcance, solicitud ?? Todo, page, pageSize, Ct);
        return (r.Pagina, r.Error);
    }

    private static readonly SolicitudReportePortal Todo = new([], [], null, null);

    private static HashSet<string?> Radicados(PaginaReportePortal p) => [.. p.Items.Select(i => i.Radicado)];

    // ── E1 · AC1 SuperAdmin ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_VeWebPuntoYAplicacionDeTodosLosOrganismos_YNadaDelGestorNiDeIct()
    {
        var (db, _) = Sembrar();
        var (pagina, error) = await Listar(db, Plataforma);

        error.Should().BeNull();
        Radicados(pagina!).Should().BeEquivalentTo(["R-1", "R-2", "R-3", "R-4", "R-8", "R-10"]);
        pagina!.TotalCount.Should().Be(6);
    }

    // ── E1 · AC2 OT ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Organismo_VeSoloWebYPuntoDeSuOrganismo()
    {
        var (db, _) = Sembrar();
        var (pagina, error) = await Listar(db, Organismo(OtA));

        error.Should().BeNull();
        // R-2: punto de A aunque su organismo del trámite ya sea B. R-3 y R-4 son de B. R-8 (aplicación) no es del organismo.
        Radicados(pagina!).Should().BeEquivalentTo(["R-1", "R-2", "R-10"]);
        pagina!.Items.Single(i => i.Radicado == "R-2").Organismo.Should().Be("Organismo A");
    }

    [Fact]
    public async Task Organismo_UnTramiteWebSigueElOrganismoDelTramite_NoElDelCanal()
    {
        var (db, _) = Sembrar();
        var (pagina, _) = await Listar(db, Organismo(OtB));

        // B ve su web (R-3) y su punto (R-4); no ve el punto de A, aunque ese trámite hoy tenga transit_office_id = B.
        Radicados(pagina!).Should().BeEquivalentTo(["R-3", "R-4"]);
    }

    // ── E1 · AC3 otro organismo ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Organismo_PedirOtroOrganismo_Da403SinDatos()
    {
        var (db, _) = Sembrar();
        var (pagina, error) = await Listar(db, Organismo(OtA, OficinaB));

        error.Should().Be(ReportePortalErrores.OtroOrganismo);
        pagina.Should().BeNull();
        ((IStatusCodeHttpResult)ReportePortalRespuestas.Falla(error!)).StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Organismo_UnIdAjenoDentroDeLaLista_RechazaTodo_AunqueVengaElPropio()
    {
        var (db, _) = Sembrar();
        var (pagina, error) = await Listar(db, Organismo(OtA, OficinaA, OficinaB));

        error.Should().Be(ReportePortalErrores.OtroOrganismo);
        pagina.Should().BeNull();
    }

    [Fact]
    public async Task Organismo_PedirElPropio_EsValido()
    {
        var (db, _) = Sembrar();
        var (pagina, error) = await Listar(db, Organismo(OtA, OficinaA));

        error.Should().BeNull();
        pagina!.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Organismo_SinPerfilDeOrganismo_Da403()
    {
        var (db, _) = Sembrar();
        var (_, error) = await Listar(db, Organismo(Guid.NewGuid()));

        error.Should().Be(ReportePortalErrores.SinOrganismo);
        ((IStatusCodeHttpResult)ReportePortalRespuestas.Falla(error!)).StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Organismo_SiLaEmpresaDelPortalNoEstaEnSuAlcance_NoVeNada()
    {
        var (db, _) = Sembrar(permisoDelPortalParaA: false);
        var (pagina, error) = await Listar(db, Organismo(OtA));

        error.Should().BeNull();
        pagina!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SuperAdminMirandoUnOrganismo_ExigeUnoSolo_YVeLaMismaVistaQueElOrganismo()
    {
        var (db, _) = Sembrar();
        var sinOrganismo = new AlcanceReportePortal(false, null, []);
        (await Listar(db, sinOrganismo)).Error.Should().Be(ReportePortalErrores.OrganismoRequerido);
        (await Listar(db, sinOrganismo with { Organismos = [OficinaA, OficinaB] })).Error.Should().Be(ReportePortalErrores.OrganismoRequerido);

        var (pagina, error) = await Listar(db, sinOrganismo with { Organismos = [OficinaA] });
        error.Should().BeNull();
        Radicados(pagina!).Should().BeEquivalentTo(["R-1", "R-2", "R-10"]);
    }

    // ── E1 · AC4 gestor y lo que no cayó ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NuncaAparece_ElGestor_ElHistoricoSinCanal_Ict_NiElRechazadoAlCrear()
    {
        var (db, _) = Sembrar();
        foreach (var alcance in new[] { Plataforma, Organismo(OtA), Organismo(OtB) })
        {
            var (pagina, _) = await Listar(db, alcance);
            Radicados(pagina!).Should().NotContain(["R-5", "R-6", "R-7", "R-9"]);
        }
    }

    [Fact]
    public async Task UnTramiteConCanalDelPortalPeroDeOtraEmpresa_NoAparece()
    {
        var (db, _) = Sembrar();
        await using (var ctx = Contexto(db))
        {
            Tramite(ctx, "R-X", CanalesReportePortal.Web, Gestora, OficinaA, null, TipoUno, DateTimeOffset.UtcNow);
            await ctx.SaveChangesAsync(Ct);
        }

        Radicados((await Listar(db, Plataforma)).Pagina!).Should().NotContain("R-X");
        Radicados((await Listar(db, Organismo(OtA))).Pagina!).Should().NotContain("R-X");
    }

    // ── E1 · AC5 filtros y paginación ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Filtros_CanalTipoYFechas_YListasSeparadasPorComa()
    {
        var (db, _) = Sembrar();

        var web = await Listar(db, Plataforma, new SolicitudReportePortal(["pn_web"], [], null, null));
        Radicados(web.Pagina!).Should().BeEquivalentTo(["R-1", "R-3", "R-10"]);

        var dos = await Listar(db, Plataforma, new SolicitudReportePortal(["pn_web", "pn_punto"], [], null, null));
        Radicados(dos.Pagina!).Should().BeEquivalentTo(["R-1", "R-2", "R-3", "R-4", "R-10"]);

        var tipo = await Listar(db, Plataforma, new SolicitudReportePortal([], [TipoDos], null, null));
        Radicados(tipo.Pagina!).Should().BeEquivalentTo(["R-2"]);

        var tipos = await Listar(db, Plataforma, new SolicitudReportePortal([], [TipoDos, TipoUno], null, null));
        tipos.Pagina!.TotalCount.Should().Be(6);

        var hoy = OtTenantScope.TodayInBogota();
        var fechas = await Listar(db, Plataforma, new SolicitudReportePortal([], [], hoy.AddDays(-4), hoy));
        Radicados(fechas.Pagina!).Should().BeEquivalentTo(["R-1", "R-2", "R-3", "R-4"]);
    }

    [Fact]
    public async Task Filtros_PlataformaPuedeFiltrarPorVariosOrganismos()
    {
        var (db, _) = Sembrar();
        var (pagina, _) = await Listar(db, Plataforma with { Organismos = [OficinaB] });

        Radicados(pagina!).Should().BeEquivalentTo(["R-3", "R-4"]);
        (await Listar(db, Plataforma with { Organismos = [OficinaA, OficinaB] })).Pagina!.TotalCount.Should().Be(6);
    }

    [Fact]
    public async Task Canal_AplicacionNoTrae_AlOrganismo_Ni_UnCanalAjenoAlPortalEsValido()
    {
        var (db, _) = Sembrar();
        var (pagina, error) = await Listar(db, Organismo(OtA), new SolicitudReportePortal(["aplicacion"], [], null, null));
        error.Should().BeNull();
        pagina!.Items.Should().BeEmpty();

        (await Listar(db, Plataforma, new SolicitudReportePortal(["tramites"], [], null, null))).Error.Should().Be(ReportePortalErrores.CanalInvalido);
        (await Listar(db, Plataforma, new SolicitudReportePortal(["pn_web", "integracion"], [], null, null))).Error.Should().Be(ReportePortalErrores.CanalInvalido);
    }

    [Fact]
    public async Task Rango_InvertidoYPaginaInvalida_Dan400()
    {
        var (db, _) = Sembrar();
        var hoy = OtTenantScope.TodayInBogota();
        (await Listar(db, Plataforma, new SolicitudReportePortal([], [], hoy, hoy.AddDays(-1)))).Error.Should().Be(ReportePortalErrores.RangoInvalido);
        (await Listar(db, Plataforma, Todo, page: 0)).Error.Should().Be(ReportePortalErrores.PaginaInvalida);
        (await Listar(db, Plataforma, Todo, pageSize: 101)).Error.Should().Be(ReportePortalErrores.PaginaInvalida);
    }

    [Fact]
    public async Task Paginacion_RespetaPaginaTamanoYTotal_DeLaMasRecienteALaMasAntigua()
    {
        var (db, _) = Sembrar();
        var p1 = (await Listar(db, Plataforma, Todo, 1, 4)).Pagina!;
        var p2 = (await Listar(db, Plataforma, Todo, 2, 4)).Pagina!;

        p1.TotalCount.Should().Be(6);
        p1.Items.Select(i => i.Radicado).Should().Equal("R-1", "R-2", "R-3", "R-4");
        p2.Items.Select(i => i.Radicado).Should().Equal("R-8", "R-10");
        (p2.Page, p2.PageSize).Should().Be((2, 4));
    }

    [Fact]
    public async Task Fila_ModalidadYValoresVanNulos_SinInventarDatos()
    {
        var (db, _) = Sembrar();
        var fila = (await Listar(db, Organismo(OtA))).Pagina!.Items.Single(i => i.Radicado == "R-1");

        (fila.Modalidad, fila.ValorTramite, fila.ValorGestion).Should().Be((null, null, null));
        (fila.Canal, fila.Organismo, fila.Tipo, fila.Estado).Should().Be(("pn_web", "Organismo A", "Traspaso", TramiteEstado.Borrador));
    }

    // ── Lista separada por comas y contrato ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Leer_AceptaListasSeparadasPorComa_YRechazaUnIdQueNoEsGuid()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var ok = ReportePortalRespuestas.Leer("pn_web, pn_punto", $"{a},{b}", $"{a}", null, null);

        ok.Falla.Should().BeNull();
        ok.Solicitud!.Canales.Should().Equal("pn_web", "pn_punto");
        ok.Solicitud.Tipos.Should().Equal(a, b);
        ok.Organismos.Should().Equal(a);

        var mala = ReportePortalRespuestas.Leer(null, "no-es-guid", null, null, null);
        ((IStatusCodeHttpResult)mala.Falla!).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ReportePortalRespuestas.Leer(null, null, $"{a},xx", null, null).Falla.Should().NotBeNull();
        ReportePortalRespuestas.Leer(null, null, null, null, null).Solicitud!.Canales.Should().BeEmpty();
    }

    [Fact]
    public void Contrato_LaPaginaSeSerializaConItemsTotalCountPageYPageSize_YLaFilaEnCamelCase()
    {
        var pagina = new PaginaReportePortal(
            [new FilaReportePortal("pn_web", "O", "T", "R", "borrador", DateTimeOffset.UtcNow, null, null, null)], 1, 1, 20);
        var json = System.Text.Json.JsonSerializer.Serialize(pagina, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        json.Should().Contain("\"items\"").And.Contain("\"totalCount\"").And.Contain("\"page\"").And.Contain("\"pageSize\"");
        foreach (var campo in new[] { "canal", "organismo", "tipo", "radicado", "estado", "fechaCaida", "modalidad", "valorTramite", "valorGestion" })
            json.Should().Contain($"\"{campo}\"");
    }

    [Fact]
    public void Catalogo_LosCanalesDelReporteSonLosDelCatalogoDeTramites()
    {
        CanalesReportePortal.Web.Should().Be(TramiteCanal.PersonaNaturalWeb);
        CanalesReportePortal.Punto.Should().Be(TramiteCanal.PersonaNaturalOt);
        CanalesReportePortal.Aplicacion.Should().Be(TramiteCanal.Aplicacion);
        PersonaNaturalCreador.PrefijoAnulacion.Should().Be(MotivosAnulacionPortal.RechazoEnCreacion);
    }

    // ── E2 · Excel ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<(string? Error, List<string[]> Filas)> Exportar(
        string db, AlcanceReportePortal alcance, SolicitudReportePortal? solicitud = null, int? page = null, int? pageSize = null)
    {
        await using var ctx = Contexto(db);
        var handler = new ExportarReportePortalHandler(new ReportePortalExcelExporter(new ReportePortalReadRepository(ctx)));
        using var stream = new MemoryStream();
        var error = await handler.HandleAsync(stream, alcance, solicitud ?? Todo, page, pageSize, Ct);
        if (error is not null)
            return (error, []);

        stream.Position = 0;
        using var libro = SpreadsheetDocument.Open(stream, false);
        var hoja = libro.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>();
        var filas = hoja.Select(r => r.Descendants<Cell>().Select(c => c.InnerText).ToArray()).ToList();
        return (null, filas);
    }

    [Fact]
    public async Task Excel_TraeElMismoConjuntoYLasColumnasDelReporte()
    {
        var (db, _) = Sembrar();
        var solicitud = new SolicitudReportePortal(["pn_web", "pn_punto"], [], null, null);
        var listado = (await Listar(db, Plataforma, solicitud)).Pagina!;
        var (error, filas) = await Exportar(db, Plataforma, solicitud);

        error.Should().BeNull();
        filas[0].Should().Equal(ReportePortalExcelExporter.Cabeceras);
        filas.Skip(1).Select(f => f[3]).Should().Equal(listado.Items.Select(i => i.Radicado));
        filas[1][0].Should().Be("Persona Natural Web");
        filas[1][1].Should().Be("Organismo A");
        filas[1][6..].Should().OnlyContain(c => c == string.Empty);
    }

    [Fact]
    public async Task Excel_ConPagina_ExportaSoloEsaPagina_YRespetaFiltros()
    {
        var (db, _) = Sembrar();
        var (_, pagina) = await Exportar(db, Plataforma, Todo, 2, 4);
        pagina.Skip(1).Select(f => f[3]).Should().Equal("R-8", "R-10");

        var (_, tipo) = await Exportar(db, Plataforma, new SolicitudReportePortal([], [TipoDos], null, null));
        tipo.Skip(1).Select(f => f[3]).Should().Equal("R-2");
    }

    [Fact]
    public async Task Excel_Organismo_NoSacaTramitesDeOtroOrganismo_Ni_AceptaPedirlos()
    {
        var (db, _) = Sembrar();
        var (error, filas) = await Exportar(db, Organismo(OtA));

        error.Should().BeNull();
        filas.Skip(1).Select(f => f[3]).Should().BeEquivalentTo("R-1", "R-2", "R-10");

        var (ajeno, vacio) = await Exportar(db, Organismo(OtA, OficinaB));
        ajeno.Should().Be(ReportePortalErrores.OtroOrganismo);
        vacio.Should().BeEmpty();

        var (enLista, _) = await Exportar(db, Organismo(OtA, OficinaA, OficinaB));
        enLista.Should().Be(ReportePortalErrores.OtroOrganismo);
    }

    [Fact]
    public async Task Excel_SinFilas_DejaSoloLaCabecera()
    {
        var (db, _) = Sembrar();
        var (error, filas) = await Exportar(db, Organismo(OtA), new SolicitudReportePortal(["aplicacion"], [], null, null));

        error.Should().BeNull();
        filas.Should().HaveCount(1);
    }
}
