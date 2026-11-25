using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Impuestos.Tests.Credenciales;

/// <summary>
/// HU #14694 (M06, D-409, plan §3.1 y §5.1) — <c>PUT /backoffice/empresas/{tenantId}/credenciales</c>: el Super Admin carga
/// <c>{apiKey, webhookSecret?}</c>; se valida con <c>GET /fuentes</c> usando esa api_key; responde 204 sin cuerpo; desde
/// <c>REQUIERE_CREDENCIALES</c> o <c>LISTO_SIN_WEBHOOK</c> pasa a <c>LISTO</c> (con secreto) o <c>LISTO_SIN_WEBHOOK</c> (sin él),
/// con historial y versión. Un 401 del API es 422 <c>CREDENCIAL_INVALIDA</c> y no guarda nada; otro estado es 409 sin llamar al
/// API; solo el Super Admin. <c>GET</c> informa existencia, nunca valores, y queda auditado.
/// </summary>
/// <example>
/// <code>
/// await using var api = await CargaManualCredencialesTests.ConApiAsync(doble);
/// var r = await api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(api)).PutAsJsonAsync(CargaManualCredencialesTests.Ruta(empresa), new { apiKey }, ct);
/// </code>
/// </example>
public sealed class CargaManualCredencialesTests
{
    private const string ApiKey = ClienteApiLiquidacionTests.ApiKey;
    private const string Secreto = ClienteApiLiquidacionTests.WebhookSecret;

    public static readonly Guid ActorSuperAdmin = Guid.Parse("5a000000-0000-0000-0000-0000000000a1");

    [Fact]
    public async Task Con_secreto_valida_con_fuentes_responde_204_y_pasa_a_LISTO()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct);

        using var r = await api.Cliente(TokenSuperAdmin(api)).PutAsJsonAsync(Ruta(empresa), new { apiKey = ApiKey, webhookSecret = Secreto }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await r.Content.ReadAsStringAsync(ct)).Should().BeEmpty("nunca devuelve valores");
        var pedido = doble.Pedidos.Should().ContainSingle().Subject;
        pedido.Uri.Should().EndWith("/api/v1/fuentes");
        pedido.Cabeceras["X-API-Key"].Should().Be(ApiKey, "se valida la llave que se está cargando");

        await using var db = api.Contexto(empresa);
        var aprov = await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleAsync(ct);
        aprov.Estado.Should().Be(EstadoAprovisionamiento.Listo);
        aprov.FirmaConfirmada.Should().BeFalse("firma sin confirmar hasta el primer webhook válido");
        aprov.Version.Should().Be(2);
        var historial = await db.Set<HistorialAprovisionamiento>().AsNoTracking().SingleAsync(ct);
        historial.Desde.Should().Be(EstadoAprovisionamiento.RequiereCredenciales);
        historial.Hacia.Should().Be(EstadoAprovisionamiento.Listo);
        historial.ActorId.Should().Be(ActorSuperAdmin);
        historial.ActorTipo.Should().Be("SUPER_ADMIN");
        historial.MotivoCodigo.Should().Be("CARGA_MANUAL");
        var credencial = await db.Set<CredencialEmpresa>().AsNoTracking().SingleAsync(ct);
        credencial.Origen.Should().Be(OrigenCredencial.CargaManual);
        credencial.WebhookSecretCifrado.Should().NotBeNull();
        credencial.ActualizadoPor.Should().Be(ActorSuperAdmin);
    }

    [Fact]
    public async Task Sin_secreto_queda_LISTO_SIN_WEBHOOK()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct);

        using var r = await api.Cliente(TokenSuperAdmin(api)).PutAsJsonAsync(Ruta(empresa), new { apiKey = ApiKey }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var db = api.Contexto(empresa);
        (await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleAsync(ct)).Estado.Should().Be(EstadoAprovisionamiento.ListoSinWebhook);
        (await db.Set<CredencialEmpresa>().AsNoTracking().SingleAsync(ct)).WebhookSecretCifrado.Should().BeNull();
    }

    [Fact]
    public async Task Desde_LISTO_SIN_WEBHOOK_cargar_el_secreto_pasa_a_LISTO_y_apaga_el_modo_respaldo()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await using (var semilla = api.Contexto(empresa))
        {
            semilla.Add(new AprovisionamientoEmpresa
            {
                TenantId = empresa,
                Estado = EstadoAprovisionamiento.ListoSinWebhook,
                ModoRespaldoFirma = true,
                FirmasInvalidasConsecutivas = 5,
                Version = 4,
            });
            await semilla.SaveChangesAsync(ct);
        }

        using var r = await api.Cliente(TokenSuperAdmin(api)).PutAsJsonAsync(Ruta(empresa), new { apiKey = ApiKey, webhookSecret = Secreto }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var db = api.Contexto(empresa);
        var aprov = await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleAsync(ct);
        aprov.Estado.Should().Be(EstadoAprovisionamiento.Listo);
        aprov.ModoRespaldoFirma.Should().BeFalse("una carga de secreto nueva apaga el modo respaldo (F2b-CF3)");
        aprov.FirmasInvalidasConsecutivas.Should().Be(0);
        aprov.Version.Should().Be(5);
    }

    [Fact]
    public async Task Un_401_de_fuentes_es_422_CREDENCIAL_INVALIDA_y_no_guarda_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba
        {
            Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\"}}"),
        };
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct);

        using var r = await api.Cliente(TokenSuperAdmin(api)).PutAsJsonAsync(Ruta(empresa), new { apiKey = ApiKey, webhookSecret = Secreto }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        cuerpo.RootElement.GetProperty("code").GetString().Should().Be("CREDENCIAL_INVALIDA");
        cuerpo.RootElement.GetProperty("error").GetString().Should().Be(LiteralCredencialInvalida);
        await using var db = api.Contexto(empresa);
        (await db.Set<CredencialEmpresa>().AnyAsync(ct)).Should().BeFalse();
        (await db.Set<HistorialAprovisionamiento>().AnyAsync(ct)).Should().BeFalse();
        (await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleAsync(ct)).Estado.Should().Be(EstadoAprovisionamiento.RequiereCredenciales);
    }

    [Fact]
    public async Task Si_el_API_no_responde_es_503_y_no_guarda_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba
        {
            Responder = _ => throw new HttpRequestException("rechazada", new SocketException((int)SocketError.ConnectionRefused)),
        };
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct);

        using var r = await api.Cliente(TokenSuperAdmin(api)).PutAsJsonAsync(Ruta(empresa), new { apiKey = ApiKey }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        cuerpo.RootElement.GetProperty("code").GetString().Should().Be("PROVEEDOR_NO_DISPONIBLE");
        cuerpo.RootElement.GetProperty("error").GetString().Should().Be("El proveedor no responde. Intenta más tarde.");
        await using var db = api.Contexto(empresa);
        (await db.Set<CredencialEmpresa>().AnyAsync(ct)).Should().BeFalse();
    }

    [Theory]
    [InlineData(EstadoAprovisionamiento.Pendiente)]
    [InlineData(EstadoAprovisionamiento.Listo)]
    [InlineData(EstadoAprovisionamiento.Incierto)]
    [InlineData(EstadoAprovisionamiento.Suspendido)]
    public async Task Un_estado_que_no_admite_carga_es_409_sin_llamar_al_API(EstadoAprovisionamiento estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct, estado);

        using var r = await api.Cliente(TokenSuperAdmin(api)).PutAsJsonAsync(Ruta(empresa), new { apiKey = ApiKey }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        doble.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_aprovisionamiento_es_409_sin_llamar_al_API()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        using var r = await api.Cliente(TokenSuperAdmin(api)).PutAsJsonAsync(Ruta(Guid.NewGuid()), new { apiKey = ApiKey }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        doble.Pedidos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", null, "apiKey")]
    [InlineData("   ", null, "apiKey")]
    [InlineData("con espacio interno 0123456789", null, "apiKey")]
    [InlineData(ApiKey, "sin-prefijo-whsec", "webhookSecret")]
    public async Task Valores_mal_formados_son_422_sin_llamar_al_API(string apiKey, string? webhookSecret, string campo)
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct);

        using var r = await api.Cliente(TokenSuperAdmin(api)).PutAsJsonAsync(Ruta(empresa), new { apiKey, webhookSecret }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        cuerpo.RootElement.GetProperty("code").GetString().Should().Be("CREDENCIAL_INVALIDA");
        cuerpo.RootElement.GetProperty("error").GetString().Should().Be(LiteralCredencialInvalida);
        cuerpo.RootElement.GetProperty("campo").GetString().Should().Be(campo);
        doble.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Solo_el_Super_Admin_carga()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct);
        var admin = api.Token(empresa: empresa, roles: ["admin_impuestos"], permisos: [Permisos.ConfiguracionEditar, Permisos.UsuariosGestionar]);
        var flitSinSa = api.Token(tipoEmpresa: "FLIT", roles: [], permisos: [Permisos.BackofficeVer, Permisos.CredencialesCargar]);

        using var deAdmin = await api.Cliente(admin).PutAsJsonAsync(Ruta(empresa), new { apiKey = ApiKey }, ct);
        using var deFlit = await api.Cliente(flitSinSa).PutAsJsonAsync(Ruta(empresa), new { apiKey = ApiKey }, ct);

        deAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deFlit.StatusCode.Should().Be(HttpStatusCode.Forbidden, "la empresa explícita es solo del Super Admin");
        doble.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task GET_informa_existencia_sin_valores_y_queda_auditado()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct);
        var cliente = api.Cliente(TokenSuperAdmin(api));

        using var antes = await cliente.GetAsync(Ruta(empresa), ct);
        using (var carga = await cliente.PutAsJsonAsync(Ruta(empresa), new { apiKey = ApiKey }, ct))
            carga.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var despues = await cliente.GetAsync(Ruta(empresa), ct);

        antes.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var vacio = JsonDocument.Parse(await antes.Content.ReadAsStringAsync(ct)))
        {
            vacio.RootElement.GetProperty("tieneApiKey").GetBoolean().Should().BeFalse();
            vacio.RootElement.GetProperty("tieneWebhookSecret").GetBoolean().Should().BeFalse();
            vacio.RootElement.GetProperty("origen").ValueKind.Should().Be(JsonValueKind.Null);
        }

        despues.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var vista = JsonDocument.Parse(await despues.Content.ReadAsStringAsync(ct));
        vista.RootElement.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo(["tieneApiKey", "tieneWebhookSecret", "origen", "apiKeyPrefijo", "actualizadoEn"]);
        vista.RootElement.GetProperty("tieneApiKey").GetBoolean().Should().BeTrue();
        vista.RootElement.GetProperty("tieneWebhookSecret").GetBoolean().Should().BeFalse();
        vista.RootElement.GetProperty("origen").GetString().Should().Be("CARGA_MANUAL");
        vista.RootElement.GetProperty("apiKeyPrefijo").GetString().Should().Be("AbCdEfGh");
        await using var db = api.Contexto(empresa);
        (await db.Set<LecturaBackoffice>().CountAsync(l => l.Recurso == "credenciales", ct)).Should().Be(2);
    }

    public static string Ruta(Guid empresa) => $"{Program.Prefijo}/backoffice/empresas/{empresa}/credenciales";

    /// <summary>Literal de <c>CREDENCIAL_INVALIDA</c> en <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.1.0).</summary>
    public const string LiteralCredencialInvalida = "El proveedor rechazó la API key: revisa el valor e intenta de nuevo.";

    public static string TokenSuperAdmin(ApiDePrueba api) =>
        api.Token(roles: [], permisos: [], superAdmin: true, tipoEmpresa: "FLIT", usuario: ActorSuperAdmin);

    /// <summary>La API con base y el cliente del API de liquidación apuntando a <paramref name="doble"/>; con log en memoria si se da.</summary>
    public static Task<ApiDePrueba> ConApiAsync(ApiLiquidacionDePrueba doble, RegistroDeLogs? logs = null) => ApiDePrueba.ConBaseAsync(b =>
    {
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Impuestos:Api:BaseUrl"] = ClienteApiLiquidacionTests.Base,
            ["Impuestos:Api:IntegrationToken"] = ClienteApiLiquidacionTests.Token,
        });
        b.Services.AddHttpClient<ClienteApiLiquidacion>().ConfigurePrimaryHttpMessageHandler(() => doble);
        if (logs is not null)
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<RegistroDeLogs>(null, LogLevel.Trace);
        }
    });
}
