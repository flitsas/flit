using System.Text.Json;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Solicitudes;
using FluentAssertions;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Paridad con FLIT 2: el valor nuevo de una transformación (color, combustible, carrocería, también los simultáneos) debe ser
/// distinto del que el RUNT tiene hoy, comparando sin mayúsculas, tildes ni espacios de más. Sin RUNT no se bloquea.
/// </summary>
public sealed class DistintoDelRuntTests
{
    private static readonly ValoresActualesVehiculo Moto = new("AZUL MATE", "GASOLINA", "Sin Carrocería");

    private static JsonElement J(object valor) => JsonSerializer.SerializeToElement(valor);

    private static DeclaracionTipoDto Color(string clave = "transformacion_color", string activadaPor = "") => new(
        clave, "seleccion_catalogo", "Nuevo color", true, [], "vehicle_color", ["cambio_color"], "soporte_cambio_color", false, "colores", activadaPor);

    private static DeclaracionTipoDto Combustible() => new(
        "transformacion_combustible", "seleccion_catalogo", "Nuevo combustible", true, [], "vehicle_fuel", ["cambio_combustible"], "soporte", false, "combustibles", string.Empty);

    private static DeclaracionTipoDto Carroceria() => new(
        "transformacion_carroceria", "seleccion_catalogo", "Nueva carrocería", true, [], "vehicle_body_type", ["cambio_carroceria"], "soporte", false, "carrocerias", string.Empty);

    private static DefinicionTipoDto Def(params DeclaracionTipoDto[] declaraciones) => DefinicionesDePrueba.UnTitular() with
    {
        Pasos = [new PasoDto("consulta", "Consulta", ["vehicle_query"], []), new PasoDto("documentos", "Documentos", ["document_checklist"], [])],
        Documentos = [],
        Declaraciones = declaraciones,
    };

    private static ErroresDeCampo Validar(DefinicionTipoDto def, ValoresActualesVehiculo? runt, string clave, string valor)
    {
        var errores = new ErroresDeCampo();
        ReglasBorrador.ValidarCampos(def, new Dictionary<string, JsonElement> { [clave] = J(valor) }, errores, runt);
        return errores;
    }

    [Theory]
    [InlineData("AZUL MATE")]
    [InlineData("azul mate")]
    [InlineData("  Azul   Mate ")]
    [InlineData("AZÚL MATÉ")]
    public void Color_IgualAlRunt_Rechaza_ConMensajeEnElCampo(string valor)
    {
        var errores = Validar(Def(Color()), Moto, "vehicle_color", valor);

        errores.Todos().Should().ContainKey("valores.vehicle_color").WhoseValue.Should().Equal("El nuevo color debe ser distinto al registrado en el RUNT.");
    }

    [Fact]
    public void Color_Distinto_Pasa_YSeGuarda()
    {
        var errores = new ErroresDeCampo();

        var salida = ReglasBorrador.ValidarCampos(Def(Color()), new Dictionary<string, JsonElement> { ["vehicle_color"] = J("ROJO") }, errores, Moto);

        errores.Hay.Should().BeFalse();
        salida["vehicle_color"].Should().Be("ROJO");
        salida["cambio_color"].Should().Be("true");
    }

    [Fact]
    public void Combustible_YCarroceria_IgualesAlRunt_Rechazan()
    {
        Validar(Def(Combustible()), Moto, "vehicle_fuel", "gasolina").Todos()["valores.vehicle_fuel"]
            .Should().Equal("El nuevo combustible debe ser distinto al registrado en el RUNT.");
        Validar(Def(Carroceria()), Moto, "vehicle_body_type", "SIN CARROCERIA").Todos()["valores.vehicle_body_type"]
            .Should().Equal("La nueva carrocería debe ser distinta a la registrada en el RUNT.");
    }

    [Fact]
    public void Simultaneo_IgualAlRunt_Rechaza()
    {
        Validar(Def(Color("simultaneo_color", "CAMBIO_COLOR")), Moto, "vehicle_color", "AZUL MATE").Hay.Should().BeTrue();
    }

    [Fact]
    public void SinRunt_NoBloquea()
    {
        Validar(Def(Color()), null, "vehicle_color", "AZUL MATE").Hay.Should().BeFalse();
        Validar(Def(Color()), new ValoresActualesVehiculo(null, null, null), "vehicle_color", "AZUL MATE").Hay.Should().BeFalse();
    }

    [Fact]
    public void Completitud_IgualAlRunt_DejaPendienteLaDeclaracion_ConElMotivo()
    {
        var def = Def(Color());
        var datos = new DatosSolicitud(null, null, null);

        var (_, igual) = ReglasBorrador.Completitud(def, new Dictionary<string, string> { ["vehicle_color"] = "AZUL MATE" }, datos, [("comprador", 1)], new HashSet<string>(), default, Moto);
        var (_, distinto) = ReglasBorrador.Completitud(def, new Dictionary<string, string> { ["vehicle_color"] = "ROJO" }, datos, [("comprador", 1)], new HashSet<string>(), default, Moto);
        var (_, sinRunt) = ReglasBorrador.Completitud(def, new Dictionary<string, string> { ["vehicle_color"] = "AZUL MATE" }, datos, [("comprador", 1)], new HashSet<string>(), default, null);

        igual.Should().ContainSingle().Which.Should().Be(new PendienteDto("declaracion_requerida", "transformacion_color", "documentos", "El nuevo color debe ser distinto al registrado en el RUNT."));
        distinto.Should().BeEmpty();
        sinRunt.Should().BeEmpty();
    }

    // ── Punto ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class DefinicionesFalsas(DefinicionTipoDto def) : IFachadaDefiniciones
    {
        public Task<DefinicionTipoDto> ObtenerDefinicionAsync(string codigo, string? organismoId, CancellationToken ct) => Task.FromResult(def);
    }

    private sealed class BorradorFalso(DatosBorradorPunto datos) : IBorradorPunto
    {
        public Task<Guid?> ResolverSesionAsync(string? token, CancellationToken ct) => Task.FromResult<Guid?>(datos.SolicitudId);

        public Task<DatosBorradorPunto?> LeerAsync(Guid solicitudId, CancellationToken ct) => Task.FromResult<DatosBorradorPunto?>(datos);
    }

    private sealed class RuntFalso(ValoresActualesVehiculo? valores) : IValoresRuntBorrador
    {
        public int Llamadas { get; private set; }

        public Task<ValoresActualesVehiculo?> ObtenerAsync(Guid solicitudId, CancellationToken ct)
        {
            Llamadas++;
            return Task.FromResult(valores);
        }
    }

    private static async Task<CompletitudRequisitosPuntoDto> Punto(string color, ValoresActualesVehiculo? runt)
    {
        var organismo = Guid.CreateVersion7();
        var datos = new DatosBorradorPunto(
            Guid.CreateVersion7(), organismo, "CAMBIO_COLOR", true, new DatosSolicitud(null, null, null),
            new Dictionary<string, string> { ["vehicle_color"] = color }, new HashSet<string>(), false);
        var servicio = new CompletitudRequisitosPuntoService(new DefinicionesFalsas(Def(Color())), new ConsultasRuntFalso(), new BorradorFalso(datos), new RuntFalso(runt));

        var r = await servicio.EvaluarAsync(new OrganismoPunto(organismo, "AB1", "Org"), "t", "CAMBIO_COLOR", TestContext.Current.CancellationToken);

        return r.Should().BeOfType<ResultadoCompletitudPunto.Ok>().Subject.Completitud;
    }

    [Fact]
    public async Task Punto_IgualAlRunt_NoPuedeContinuar_ConElMotivo()
    {
        var c = await Punto("azul mate", Moto);

        c.PuedeContinuarPago.Should().BeFalse();
        c.Pendientes.Should().ContainSingle().Which.Should().Be(new PendienteCompletitudPuntoDto("declaracion_requerida", "El nuevo color debe ser distinto al registrado en el RUNT."));
    }

    [Fact]
    public async Task Punto_DistintoORuntCaido_PuedeContinuar()
    {
        (await Punto("ROJO", Moto)).PuedeContinuarPago.Should().BeTrue();
        (await Punto("AZUL MATE", null)).PuedeContinuarPago.Should().BeTrue();
    }
}
