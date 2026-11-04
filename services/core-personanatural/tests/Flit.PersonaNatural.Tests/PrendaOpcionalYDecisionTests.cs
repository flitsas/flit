using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Solicitudes;
using FluentAssertions;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Paridad con PrendaGate de FLIT 2: levantar en un tipo con prenda complementaria no exige acreedor ni NIT; y con prenda o gravamen
/// en el RUNT el traspaso exige una decisión explícita (<c>prenda_decision_requerida</c>) para radicar y para pasar el gate del punto.
/// </summary>
public sealed class PrendaOpcionalYDecisionTests
{
    private static DefinicionTipoDto Complementaria() => DefinicionesDePrueba.UnTitular("TRASPASO_STANDARD") with
    {
        Documentos = [],
        Flags = new FlagsTipoDto("PLATE", false, false, false, true, false, false),
    };

    private static DefinicionTipoDto AccionUnica() => DefinicionesDePrueba.UnTitular("LEVANTAMIENTO_PRENDA") with
    {
        Documentos = [],
        Flags = new FlagsTipoDto("PLATE", false, false, false, false, false, false, AccionPrenda: ReglasBorrador.PrendaLevantar),
    };

    private static (PrendaDatos? Prenda, IReadOnlyDictionary<string, string[]> Errores) Validar(DefinicionTipoDto def, string decision, string? acreedor = null, string? nit = null, bool tiene = true)
    {
        var errores = new ErroresDeCampo();
        var (_, p) = ReglasBorrador.ValidarComercial(def, new ComercialRequest(null, null, new PrendaRequest(tiene, acreedor, nit, decision, "Entidad X")), errores);
        return (p, errores.Todos().ToDictionary(k => k.Key, k => k.Value.ToArray()));
    }

    // ── Acreedor opcional al levantar ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Levantar_EnTraspaso_SinAcreedor_Pasa()
    {
        var (prenda, errores) = Validar(Complementaria(), "levantar");

        errores.Should().BeEmpty();
        prenda.Should().NotBeNull();
        prenda!.Decision.Should().Be("levantar");
    }

    [Fact]
    public void Levantar_EnTraspaso_ConAcreedorYNit_LosGuarda()
    {
        var (prenda, errores) = Validar(Complementaria(), "levantar", "Banco X", "900.123.456-7");

        errores.Should().BeEmpty();
        prenda!.Acreedor.Should().Be("Banco X");
        prenda.NitAcreedor.Should().Be("9001234567");
    }

    [Fact]
    public void Levantar_EnTraspaso_ConNitInvalido_LoValida()
    {
        var (_, errores) = Validar(Complementaria(), "levantar", "Banco X", "12");

        errores.Should().ContainKey("prenda.nitAcreedor");
    }

    [Fact]
    public void Registrar_EnTraspaso_SinAcreedor_NoPasa()
    {
        var (_, errores) = Validar(Complementaria(), "registrar");

        errores.Should().ContainKey("prenda.acreedor").And.ContainKey("prenda.nitAcreedor");
    }

    [Fact]
    public void Levantar_EnAccionUnica_SinAcreedor_NoPasa()
    {
        var (_, errores) = Validar(AccionUnica(), "levantar");

        errores.Should().ContainKey("prenda.acreedor").And.ContainKey("prenda.nitAcreedor");
    }

    private static List<string> Pendientes(DefinicionTipoDto def, PrendaDatos prenda) =>
        ReglasBorrador.Completitud(def, new Dictionary<string, string>(), new DatosSolicitud(null, prenda, null), [], new HashSet<string>()).Pendientes.Select(p => p.Codigo).ToList();

    [Fact]
    public void Completitud_LevantarEnTraspasoSinAcreedor_NoPideAcreedorNiNit()
    {
        var pendientes = Pendientes(Complementaria(), new PrendaDatos(true, null, null, "levantar"));

        pendientes.Should().NotContain(["prenda_acreedor_requerido", "prenda_nit_requerido"]);
    }

    [Fact]
    public void Completitud_RegistrarSinAcreedor_LoPide_YLevantarDeAccionUnicaTambien()
    {
        Pendientes(Complementaria(), new PrendaDatos(true, null, null, "registrar")).Should().Contain(["prenda_acreedor_requerido", "prenda_nit_requerido"]);
        Pendientes(AccionUnica(), new PrendaDatos(true, null, null, "levantar")).Should().Contain(["prenda_acreedor_requerido", "prenda_nit_requerido"]);
    }

    // ── Decisión obligatoria con gravamen: gate del punto ────────────────────────────────────────────────────────────

    private sealed class DefinicionesFalsas(DefinicionTipoDto def) : IFachadaDefiniciones
    {
        public Task<DefinicionTipoDto> ObtenerDefinicionAsync(string codigo, string? organismoId, CancellationToken ct) => Task.FromResult(def);
    }

    private sealed class BorradorFalso(DatosBorradorPunto datos) : IBorradorPunto
    {
        public Task<Guid?> ResolverSesionAsync(string? token, CancellationToken ct) => Task.FromResult<Guid?>(datos.SolicitudId);

        public Task<DatosBorradorPunto?> LeerAsync(Guid solicitudId, CancellationToken ct) => Task.FromResult<DatosBorradorPunto?>(datos);
    }

    private sealed class GravamenFalso(bool tienePrenda, bool proveedorCaido = false) : IGravamenDelBorrador
    {
        public int Llamadas { get; private set; }

        public Task<ResultadoVehiculoBorrador> GravamenAsync(Guid solicitudId, CancellationToken ct)
        {
            Llamadas++;
            if (proveedorCaido)
                throw new ProveedorNoDisponibleException("caído");
            return Task.FromResult(new ResultadoVehiculoBorrador(EstadoVehiculoBorrador.Ok, null, new GravamenBorradorDto(tienePrenda, null, null, tienePrenda ? null : "sin_gravamen")));
        }
    }

    private static async Task<ResultadoCompletitudPunto> Punto(DefinicionTipoDto def, PrendaDatos? prenda, GravamenFalso gravamen)
    {
        var organismo = Guid.CreateVersion7();
        var datos = new DatosBorradorPunto(Guid.CreateVersion7(), organismo, "TRASPASO_STANDARD", true, new DatosSolicitud(null, prenda, null), new Dictionary<string, string>(), new HashSet<string>(), false);
        var servicio = new CompletitudRequisitosPuntoService(new DefinicionesFalsas(def), new ConsultasRuntFalso(), new BorradorFalso(datos), null, gravamen);
        return await servicio.EvaluarAsync(new OrganismoPunto(organismo, "AB1", "Org"), "t", "TRASPASO_STANDARD", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Punto_ConGravamenSinDecision_NoPasaElGate_ConElCodigoYLaReferencia()
    {
        var r = await Punto(Complementaria(), null, new GravamenFalso(true));

        var c = r.Should().BeOfType<ResultadoCompletitudPunto.Ok>().Subject.Completitud;
        c.PuedeContinuarPago.Should().BeFalse();
        c.Pendientes.Should().ContainSingle().Which.Should().Be(new PendienteCompletitudPuntoDto(
            "prenda_decision_requerida", "El RUNT reporta prenda o gravamen: elige qué se hará con ella para continuar.", "prenda.decision"));
    }

    [Fact]
    public async Task Punto_BorradorViejoConPrendaSinDecision_ConGravamen_NoPasa()
    {
        var r = await Punto(Complementaria(), new PrendaDatos(true, "Banco", "9001234567"), new GravamenFalso(true));

        r.Should().BeOfType<ResultadoCompletitudPunto.Ok>().Subject.Completitud.PuedeContinuarPago.Should().BeFalse();
    }

    [Theory]
    [InlineData("registrar")]
    [InlineData("levantar")]
    [InlineData("sin_prenda")]
    public async Task Punto_ConGravamenYCadaDecision_Pasa(string decision)
    {
        var r = await Punto(Complementaria(), new PrendaDatos(decision != "sin_prenda", null, null, decision), new GravamenFalso(true));

        r.Should().BeOfType<ResultadoCompletitudPunto.Ok>().Subject.Completitud.PuedeContinuarPago.Should().BeTrue();
    }

    [Fact]
    public async Task Punto_SinGravamen_NoExigeDecision()
    {
        var r = await Punto(Complementaria(), null, new GravamenFalso(false));

        r.Should().BeOfType<ResultadoCompletitudPunto.Ok>().Subject.Completitud.PuedeContinuarPago.Should().BeTrue();
    }

    [Fact]
    public async Task Punto_TipoSinPrendaComplementaria_NoConsultaElRunt()
    {
        var gravamen = new GravamenFalso(true);
        var r = await Punto(DefinicionesDePrueba.UnTitular() with { Documentos = [] }, null, gravamen);

        r.Should().BeOfType<ResultadoCompletitudPunto.Ok>().Subject.Completitud.PuedeContinuarPago.Should().BeTrue();
        gravamen.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Punto_ProveedorCaido_NoDecideEnFalso_Responde503()
    {
        var r = await Punto(Complementaria(), null, new GravamenFalso(true, proveedorCaido: true));

        var rechazo = r.Should().BeOfType<ResultadoCompletitudPunto.Rechazo>().Subject;
        rechazo.Http.Should().Be(503);
        rechazo.Codigo.Should().Be(ConsultasEndpoints.CodigoProveedor);
    }
}
