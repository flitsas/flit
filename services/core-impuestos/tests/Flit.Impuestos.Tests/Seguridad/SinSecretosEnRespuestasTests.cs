using System.Net;
using System.Net.Http.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Credenciales;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Seguridad;

/// <summary>
/// HU #14694 (M06, EP-CF3b y EP-CF3c) — ninguna respuesta REST lleva la api_key ni el webhook_secret: ni el 204 de la carga, ni
/// el 422 de una llave rechazada, ni el GET de credenciales (que informa existencia y prefijo, no valor), ni <c>/sesion</c>. El
/// log de toda la petición (incluido el <c>HttpClient</c> hacia el API) tampoco, y los tipos que los transportan no los imprimen
/// en <c>ToString</c>.
/// </summary>
/// <example>
/// <code>
/// await using var api = await CargaManualCredencialesTests.ConApiAsync(doble, logs);
/// logs.Todo.Should().NotContain(apiKey);
/// </code>
/// </example>
public sealed class SinSecretosEnRespuestasTests
{
    private const string ApiKey = ClienteApiLiquidacionTests.ApiKey;
    private const string Secreto = ClienteApiLiquidacionTests.WebhookSecret;
    private const string Token = ClienteApiLiquidacionTests.Token;

    [Fact]
    public async Task Ninguna_respuesta_ni_el_log_lleva_la_api_key_ni_el_secreto()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        var logs = new RegistroDeLogs();
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble, logs);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct);
        var sa = api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(api));

        using var carga = await sa.PutAsJsonAsync(CargaManualCredencialesTests.Ruta(empresa), new { apiKey = ApiKey, webhookSecret = Secreto }, ct);
        using var vista = await sa.GetAsync(CargaManualCredencialesTests.Ruta(empresa), ct);
        using var sesion = await api.Cliente(api.Token(empresa: empresa, permisos: ["impuestos.liquidaciones.ver"])).GetAsync(Program.Prefijo + "/sesion", ct);

        carga.StatusCode.Should().Be(HttpStatusCode.NoContent);
        vista.StatusCode.Should().Be(HttpStatusCode.OK);
        sesion.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var respuesta in new[] { carga, vista, sesion })
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct) + string.Join("\n", respuesta.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
            texto.Should().NotContain(ApiKey).And.NotContain(Secreto).And.NotContain(ApiKey[8..]);
        }

        doble.Pedidos.Should().ContainSingle("la carga sí llamó al API, así que su log tiene la cabecera");
        var log = logs.Todo;
        log.Should().Contain("X-API-Key", "el registro del HttpClient escribe las cabeceras (si no, la prueba no prueba nada)");
        log.Should().NotContain(ApiKey).And.NotContain(Secreto).And.NotContain(Token);
    }

    [Fact]
    public async Task El_422_de_una_llave_rechazada_no_la_repite()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba
        {
            Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\"}}"),
        };
        var logs = new RegistroDeLogs();
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble, logs);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct);

        using var r = await api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(api))
            .PutAsJsonAsync(CargaManualCredencialesTests.Ruta(empresa), new { apiKey = ApiKey, webhookSecret = Secreto }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync(ct)).Should().NotContain(ApiKey).And.NotContain(Secreto);
        logs.Todo.Should().NotContain(ApiKey).And.NotContain(Secreto);
    }

    [Fact]
    public void Los_tipos_con_secretos_no_los_imprimen()
    {
        var textos = new object[]
        {
            new CredencialesEnClaro(ApiKey, Secreto, "empresa-api-1"),
            new CargaCredencialesPeticion(ApiKey, Secreto),
            new AltaEmpresaRespuesta(new EmpresaApi("empresa-api-1", "900123456", true, "AbCdEfGh", true), ApiKey, Secreto),
            new OpcionesApiLiquidacion { BaseUrl = ClienteApiLiquidacionTests.Base, IntegrationToken = Token },
            ResultadoEnvio<AltaEmpresaRespuesta>.Exito(201, new AltaEmpresaRespuesta(new EmpresaApi("e", null, null, null, null), ApiKey, Secreto)),
            new AltaEmpresaPeticion("Flota Andina SAS", "900123456", "ops@flota.example"),
        }.Select(o => o.ToString()!);

        textos.Should().OnlyContain(t => !t.Contains(ApiKey) && !t.Contains(Secreto) && !t.Contains(Token) && !t.Contains("ops@flota.example"));
    }

    [Fact]
    public void La_vista_de_credenciales_no_tiene_donde_llevar_un_valor()
    {
        typeof(VistaCredenciales).GetProperties().Select(p => p.Name).Should()
            .BeEquivalentTo(["TieneApiKey", "TieneWebhookSecret", "Origen", "ApiKeyPrefijo", "ActualizadoEn"], "EP-CF3c: existencia, no valor");
    }
}
