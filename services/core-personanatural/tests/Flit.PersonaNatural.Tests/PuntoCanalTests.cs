using System.Net;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Flit.Consultas.Grpc.V1;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Sesiones;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14216, #14219 y #14221 — canal del punto. El host solo mapea esas rutas (sin Postgres): la cookie, el arranque y
/// las consultas se prueban con fachadas falsas. Ningún servicio de estas rutas recibe el contexto de solicitudes.
/// </summary>
public sealed class PuntoCanalTests
{
    private static readonly Guid OrganismoId = Guid.Parse("0199b7e0-0000-7000-8000-0000000000aa");
    private const string Codigo = "AB1";
    private const string Nombre = "Tránsito de Prueba";
    private const string Base = SesionEndpoints.Base + "/punto";

    [Fact]
    public async Task Encendido_ResuelveYFijaLaSesion_YNoSePuedeCambiarElOrganismo()
    {
        await using var host = Host(Ok());
        var entrada = await host.PostAsync("/entrada", new { codigo = " ab1 " });

        entrada.Status.Should().Be(HttpStatusCode.OK);
        entrada.Json.GetProperty("organismoId").GetGuid().Should().Be(OrganismoId);
        entrada.Json.GetProperty("codigo").GetString().Should().Be(Codigo);
        entrada.Json.GetProperty("nombre").GetString().Should().Be(Nombre);
        entrada.SetCookie.Should().NotBeNullOrEmpty();
        entrada.SetCookie!.ToLowerInvariant().Should().Contain("httponly").And.Contain("samesite=lax");

        var sesion = await host.GetAsync("/sesion", entrada.Cookie);
        sesion.Status.Should().Be(HttpStatusCode.OK);
        sesion.Json.GetProperty("organismoId").GetGuid().Should().Be(OrganismoId);

        var alterada = await host.GetAsync("/sesion", entrada.Cookie + "x");
        alterada.Status.Should().Be(HttpStatusCode.Unauthorized);

        var otro = await host.GetAsync("/sesion?organismoId=0199b7e0-0000-7000-8000-0000000000bb", entrada.Cookie);
        otro.Json.GetProperty("organismoId").GetGuid().Should().Be(OrganismoId);
    }

    [Fact]
    public async Task Apagado_NoAbre_YNoDejaCookie()
    {
        await using var host = Host(new ResolucionCanalPunto(PuntoEndpoints.CanalApagado, OrganismoId, Codigo, Nombre));

        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        entrada.Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        entrada.Json.GetProperty("code").GetString().Should().Be("canal_apagado");
        CookieUtil(entrada.SetCookie).Should().BeNullOrEmpty();
        (await host.GetAsync("/sesion", entrada.Cookie)).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CodigoInvalido_NoAbre()
    {
        await using var host = Host(new ResolucionCanalPunto(PuntoEndpoints.CodigoDesconocido, Guid.Empty, string.Empty, string.Empty));

        var entrada = await host.PostAsync("/entrada", new { codigo = "ZZZ" });

        entrada.Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        entrada.Json.GetProperty("code").GetString().Should().Be("codigo_desconocido");
        CookieUtil(entrada.SetCookie).Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Arranque_Sigue_CuandoLaPlacaEsDelOrganismoDeLaSesion()
    {
        var runt = RuntCon(Nombre);
        var resultado = await Arranque(runt, new OrganismoPunto(OrganismoId, Codigo, Nombre), "ABC123", "1098765432", "CAMBIO_COLOR");

        resultado.Should().BeOfType<ResultadoArranque.Seguir>();
        runt.LlamadasVehiculo.Should().ContainSingle().Which.Placa.Should().Be("ABC123");
    }

    [Fact]
    public async Task Arranque_PlacaConEspacios_SeNormalizaYSigue()
    {
        var runt = RuntCon("tránsito de prueba");
        var resultado = await Arranque(runt, new OrganismoPunto(OrganismoId, Codigo, Nombre), " abc 123 ", "1098765432", "traspaso");

        resultado.Should().BeOfType<ResultadoArranque.Seguir>();
        runt.LlamadasVehiculo.Single().Placa.Should().Be("ABC123");
    }

    [Fact]
    public async Task Arranque_PlacaInexistente_NoSigue()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = ResultadosFalsos.VehiculoInexistente };
        var resultado = await Arranque(runt, Sesion(), "ZZZ999", "1098765432", "CAMBIO_COLOR");

        resultado.Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Be(TiposDelPunto.PlacaInexistente);
    }

    [Fact]
    public async Task Arranque_OtroOrganismo_NoSigue()
    {
        var runt = RuntCon("OT DE OTRO MUNICIPIO");
        var resultado = await Arranque(runt, Sesion(), "ABC123", "1098765432", "TRASPASO_STANDARD");

        resultado.Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Be(TiposDelPunto.OtroOrganismo);
    }

    [Fact]
    public async Task Arranque_RuntCaido_NoSigue()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = ResultadosFalsos.ProveedorCaido };
        var resultado = await Arranque(runt, Sesion(), "ABC123", "1098765432", "DUPLICADO_PLACA");

        resultado.Should().BeOfType<ResultadoArranque.ProveedorCaido>();
    }

    [Fact]
    public async Task Arranque_TipoQueNoAdmite_DevuelveElMotivoDeFlit2_SinConsultarElRunt()
    {
        var runt = RuntCon(Nombre);
        var fachada = FachadaCon(new TipoTramiteDto("MATRICULA_INICIAL", "Matrícula", "MATRICULAS", 1, false, "El tipo de trámite no está habilitado en FLIT 2."));

        var resultado = await new ArranquePuntoService(runt, fachada).EvaluarAsync(Sesion(), "ABC123", "1098765432", "MATRICULA_INICIAL", TestContext.Current.CancellationToken);

        resultado.Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Be("El tipo de trámite no está habilitado en FLIT 2.");
        runt.LlamadasVehiculo.Should().BeEmpty();
    }

    [Fact]
    public async Task Arranque_SinCookie_Responde401_YElRuntCaidoSale503()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = ResultadosFalsos.ProveedorCaido };
        await using var host = Host(Ok(), runt, FachadaCon(new TipoTramiteDto("CAMBIO_COLOR", "Color", "OTROS", 1, true, string.Empty)));

        (await host.PostAsync("/arranque", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" })).Status
            .Should().Be(HttpStatusCode.Unauthorized);

        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
        var caido = await host.PostAsync("/arranque", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie);
        caido.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        caido.Json.GetProperty("puedeSeguir").GetBoolean().Should().BeFalse();
        caido.Json.GetProperty("code").GetString().Should().Be("proveedor_no_disponible");
        caido.Json.GetProperty("message").GetString().Should().Be(TiposDelPunto.RuntCaido);
    }

    [Fact]
    public async Task Consultas_NoCreanTramite_YSinSesionResponden401()
    {
        typeof(ArranquePuntoService).GetConstructors().Single().GetParameters()
            .Should().NotContain(p => p.ParameterType.Name.Contains("Db") || p.ParameterType.Name.Contains("Procedure"));
        typeof(ConsultasPuntoService).GetConstructors().Single().GetParameters()
            .Should().NotContain(p => p.ParameterType.Name.Contains("Db") || p.ParameterType.Name.Contains("Procedure"));

        var runt = new ConsultasRuntFalso
        {
            Vehiculo = () => ResultadosFalsos.VehiculoCompleto(),
            Conductor = () => ResultadosFalsos.Conductor(),
            Multas = MultasConUnComparendo,
        };
        await using var host = Host(Ok(), runt, FachadaCon());

        (await host.PostAsync("/consultas/vehiculo", CuerpoVehiculo())).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await host.PostAsync("/consultas/persona", CuerpoPersona())).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await host.PostAsync("/consultas/multas", CuerpoMultas())).Status.Should().Be(HttpStatusCode.Unauthorized);

        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
        (await host.PostAsync("/consultas/vehiculo", CuerpoVehiculo(), entrada.Cookie)).Status.Should().Be(HttpStatusCode.OK);
        (await host.PostAsync("/consultas/persona", CuerpoPersona(), entrada.Cookie)).Status.Should().Be(HttpStatusCode.OK);
        var multas = await host.PostAsync("/consultas/multas", CuerpoMultas(), entrada.Cookie);
        multas.Status.Should().Be(HttpStatusCode.OK);
        multas.Json.GetProperty("comparendos").GetInt32().Should().Be(1);

        runt.LlamadasVehiculo.Should().ContainSingle();
        runt.LlamadasConductor.Should().ContainSingle();
        runt.LlamadasMultas.Should().ContainSingle();
    }

    private static OrganismoPunto Sesion() => new(OrganismoId, Codigo, Nombre);

    private static ResolucionCanalPunto Ok() => new("ok", OrganismoId, Codigo, Nombre);

    private static ConsultasRuntFalso RuntCon(string organismo)
    {
        return new ConsultasRuntFalso
        {
            Vehiculo = () =>
            {
                var r = ResultadosFalsos.VehiculoCompleto();
                // VehiculoCompleto ya trae transit_office_name; sustituir (no duplicar) o Arranque lee el primero y falla el match.
                var campo = r.Campos.FirstOrDefault(c => c.Clave == TiposDelPunto.CampoOrganismoRunt);
                if (campo is null)
                    r.Campos.Add(new Campo { Clave = TiposDelPunto.CampoOrganismoRunt, ValorTexto = organismo });
                else
                    campo.ValorTexto = organismo;
                return r;
            },
        };
    }

    private static Task<ResultadoArranque> Arranque(ConsultasRuntFalso runt, OrganismoPunto sesion, string placa, string documento, string tipo) =>
        new ArranquePuntoService(runt, FachadaCon(
            new TipoTramiteDto("CAMBIO_COLOR", "Color", "OTROS", 1, true, string.Empty),
            new TipoTramiteDto("TRASPASO_STANDARD", "Traspaso", "TRASPASO", 1, true, string.Empty),
            new TipoTramiteDto("DUPLICADO_PLACA", "Duplicado", "OTROS", 1, true, string.Empty))).EvaluarAsync(sesion, placa, documento, tipo, TestContext.Current.CancellationToken);

    private static FachadaTipos FachadaCon(params TipoTramiteDto[] tipos)
    {
        var f = new FachadaTipos();
        f.Tipos.AddRange(tipos);
        return f;
    }

    private static object CuerpoVehiculo() => new { placa = "ABC123", propietario = new { tipo = "CC", numero = "1098765432" }, consentimiento = true };

    private static object CuerpoPersona() => new { documento = new { tipo = "CC", numero = "1098765432" }, consentimiento = true };

    private static object CuerpoMultas() => new { documento = new { tipo = "CC", numero = "1098765432" }, consentimiento = true };

    private static ResultadoConsulta MultasConUnComparendo()
    {
        var r = new ResultadoConsulta { Semaforo = Semaforo.Verde };
        var chequeo = new Chequeo { Clave = "simit", Estado = EstadoChequeo.Ok };
        chequeo.Comparendos.Add(new Comparendo { Numero = "M1" });
        r.Chequeos.Add(chequeo);
        return r;
    }

    private static string? CookieUtil(string? setCookie)
    {
        if (string.IsNullOrEmpty(setCookie))
            return null;
        var semi = setCookie.IndexOf(';');
        var par = semi < 0 ? setCookie : setCookie[..semi];
        const string prefijo = "pn_punto=";
        return par.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) ? par[prefijo.Length..] : null;
    }

    private static HostPunto Host(ResolucionCanalPunto resolucion, ConsultasRuntFalso? runt = null, FachadaTipos? fachada = null) =>
        new(resolucion, runt ?? new ConsultasRuntFalso(), fachada ?? FachadaCon());

    private sealed class HostPunto : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _http;

        public HostPunto(ResolucionCanalPunto resolucion, ConsultasRuntFalso runt, FachadaTipos fachada)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<ICanalPunto>(new CanalFijo(resolucion));
            builder.Services.AddSingleton<IConsultasRunt>(runt);
            builder.Services.AddSingleton<IFachadaTramites>(fachada);
            builder.Services.AddSingleton(new ConsultaVehiculoService(runt));
            builder.Services.AddSingleton(new ConsultaConductorService(runt));
            builder.Services.AddDataProtection();
            builder.Services.AddSingleton<CookiePunto>();
            builder.Services.AddSingleton<ArranquePuntoService>();
            builder.Services.AddSingleton<ConsultasPuntoService>();
            builder.Services.AddRateLimiter(o => o.AddPolicy(AbusoEndpoints.PoliticaPorIp, _ => RateLimitPartition.GetNoLimiter("punto-test")));
            _app = builder.Build();
            _app.UseRateLimiter();
            _app.MapPuntoEndpoints();
            _app.StartAsync().GetAwaiter().GetResult();
            _http = _app.GetTestClient();
        }

        public async Task<RespuestaPunto> PostAsync(string ruta, object cuerpo, string? cookie = null)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, Base + ruta) { Content = JsonContent.Create(cuerpo) };
            if (cookie is not null)
                peticion.Headers.TryAddWithoutValidation("Cookie", "pn_punto=" + cookie);
            return await LeerAsync(await _http.SendAsync(peticion, TestContext.Current.CancellationToken)).ConfigureAwait(false);
        }

        public async Task<RespuestaPunto> GetAsync(string ruta, string? cookie)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Get, Base + ruta);
            if (!string.IsNullOrEmpty(cookie))
                peticion.Headers.TryAddWithoutValidation("Cookie", "pn_punto=" + cookie);
            return await LeerAsync(await _http.SendAsync(peticion, TestContext.Current.CancellationToken)).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task<RespuestaPunto> LeerAsync(HttpResponseMessage respuesta)
        {
            var texto = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
            var setCookie = respuesta.Headers.TryGetValues("Set-Cookie", out var valores)
                ? valores.FirstOrDefault(v => v.StartsWith("pn_punto=", StringComparison.OrdinalIgnoreCase))
                : null;
            var json = texto.Length == 0 ? default : JsonDocument.Parse(texto).RootElement.Clone();
            return new RespuestaPunto(respuesta.StatusCode, json, setCookie, CookieUtil(setCookie));
        }
    }

    private sealed record RespuestaPunto(HttpStatusCode Status, JsonElement Json, string? SetCookie, string? Cookie);

    private sealed class CanalFijo(ResolucionCanalPunto resolucion) : ICanalPunto
    {
        public Task<ResolucionCanalPunto> ResolverAsync(string codigo, CancellationToken ct) => Task.FromResult(resolucion);
    }

    private sealed class FachadaTipos : IFachadaTramites
    {
        public List<TipoTramiteDto> Tipos { get; } = [];

        public Task<IReadOnlyList<OrganismoDto>> ListarOrganismosAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OrganismoDto>>([]);

        public Task<IReadOnlyList<TipoTramiteDto>> ListarTiposAsync(string? organismoId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TipoTramiteDto>>(Tipos);

        public Task<CotizacionDto> CotizarAsync(string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
