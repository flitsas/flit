using System.Net.Http.Json;
using System.Text.Json;
using Flit.Api.Grpc;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cliente = Flit.Tramites.Grpc.V1.PersonaNaturalService.PersonaNaturalServiceClient;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #14034 (Feature #14022, Épica #14021, ADR-0075) — la fachada gRPC del portal de persona natural, de punta a punta
/// con el servidor OIDC real de los tests: solo la atiende <c>svc-personanatural</c>, solo para la empresa de Persona
/// Natural, viene apagada por defecto y no tiene ningún método que cree trámites. Requiere la base con las migraciones
/// aplicadas (la empresa «Persona Natural» la siembra el DDL 152).
/// </summary>
public sealed class PersonaNaturalGrpcTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Scope = "platform.tramites.persona_natural";
    private const string Portal = "svc-personanatural";

    [Fact]
    public async Task ElPortalConSuEmpresa_EntraYSinTramiteResponde_EncontradoFalso()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: true);
        var cliente = await ClienteAsync(host, Portal, PlatformTenants.PersonaNatural, ct);

        var respuesta = await cliente.ObtenerEstadoTramiteAsync(new ObtenerEstadoTramiteRequest { SolicitudId = $"sin-tramite-{Guid.NewGuid():N}" }, cancellationToken: ct);

        respuesta.Encontrado.Should().BeFalse();
    }

    [Fact]
    public async Task ConUnTramiteDelPortal_ObtenerEstadoLoEncuentraPorSuReferenciaExterna()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: true);
        var solicitud = $"pn-{Guid.NewGuid():N}";
        var id = await SembrarTramiteAsync(host, PlatformTenants.PersonaNatural, solicitud, ct);
        var cliente = await ClienteAsync(host, Portal, PlatformTenants.PersonaNatural, ct);

        var respuesta = await cliente.ObtenerEstadoTramiteAsync(new ObtenerEstadoTramiteRequest { SolicitudId = solicitud }, cancellationToken: ct);

        respuesta.Encontrado.Should().BeTrue();
        respuesta.ProcedureInstanceId.Should().Be(id.ToString());
        respuesta.Estado.Should().Be("entregado");
        respuesta.SubsanacionActiva.Should().BeFalse();
    }

    [Fact]
    public async Task UnTramiteDeOtraEmpresaConLaMismaReferencia_NoSeVe()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: true);
        var solicitud = $"pn-ajeno-{Guid.NewGuid():N}";
        await SembrarTramiteAsync(host, empresa: null, solicitud, ct);
        var cliente = await ClienteAsync(host, Portal, PlatformTenants.PersonaNatural, ct);

        var respuesta = await cliente.ObtenerEstadoTramiteAsync(new ObtenerEstadoTramiteRequest { SolicitudId = solicitud }, cancellationToken: ct);

        respuesta.Encontrado.Should().BeFalse("la fachada solo mira los trámites de la empresa de Persona Natural");
    }

    [Fact]
    public async Task ConOtraEmpresa_RespondeInvalidTenant_YNoEjecutaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: true);
        foreach (var empresa in new[] { Guid.NewGuid(), PlatformTenants.Plataforma })
        {
            var cliente = await ClienteAsync(host, Portal, empresa, ct);

            var llamada = () => cliente.ObtenerEstadoTramiteAsync(new ObtenerEstadoTramiteRequest { SolicitudId = "x" }, cancellationToken: ct).ResponseAsync;

            var error = (await llamada.Should().ThrowAsync<RpcException>()).Which;
            error.StatusCode.Should().Be(StatusCode.InvalidArgument);
            PlatformRpcErrors.Reason(error).Should().Be("invalid_tenant");
        }
    }

    [Fact]
    public async Task SinLaEmpresaEnLaMetadata_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: true);
        var cliente = await ClienteAsync(host, Portal, empresa: null, ct);

        var llamada = () => cliente.ObtenerEstadoTramiteAsync(new ObtenerEstadoTramiteRequest { SolicitudId = "x" }, cancellationToken: ct).ResponseAsync;

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task OtroClienteDeServicio_AunqueTengaElScope_NoEntra()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: true);
        foreach (var otro in new[] { "svc-otro-prueba", "svc-ict-prueba" })
        {
            var cliente = await ClienteAsync(host, otro, PlatformTenants.PersonaNatural, ct);

            var llamada = () => cliente.ObtenerEstadoTramiteAsync(new ObtenerEstadoTramiteRequest { SolicitudId = "x" }, cancellationToken: ct).ResponseAsync;

            (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().BeOneOf(StatusCode.Unauthenticated, StatusCode.PermissionDenied);
        }
    }

    [Fact]
    public async Task SinToken_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: true);
        var cliente = new Cliente(Canal(host, token: null, PlatformTenants.PersonaNatural));

        var llamada = () => cliente.ObtenerEstadoTramiteAsync(new ObtenerEstadoTramiteRequest { SolicitudId = "x" }, cancellationToken: ct).ResponseAsync;

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().BeOneOf(StatusCode.Unauthenticated, StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task ApagadaPorDefecto_NoSeAtiende_NiConElTokenCorrecto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: false);
        var cliente = await ClienteAsync(host, Portal, PlatformTenants.PersonaNatural, ct);

        var llamada = () => cliente.ObtenerEstadoTramiteAsync(new ObtenerEstadoTramiteRequest { SolicitudId = "x" }, cancellationToken: ct).ResponseAsync;

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().BeOneOf([StatusCode.Unimplemented, StatusCode.Unauthenticated, StatusCode.PermissionDenied], "el servicio ni siquiera se mapea: no lo atiende nadie, ni con el token correcto");
    }

    [Fact]
    public async Task ObtenerConfiguracionPortal_YaNoEstaPendiente_PeroElCandadoVaPrimero()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: true);
        var bueno = await ClienteAsync(host, Portal, PlatformTenants.PersonaNatural, ct);
        var ajeno = await ClienteAsync(host, Portal, Guid.NewGuid(), ct);

        // HU #14073: ya se implementa (ya no responde NO_IMPLEMENTADO); sin contenido publicado la versión es «0».
        var respuesta = await bueno.ObtenerConfiguracionPortalAsync(new ObtenerConfiguracionPortalRequest(), cancellationToken: ct);
        respuesta.Version.Should().NotBeNullOrWhiteSpace();
        respuesta.Enlaces.Should().HaveCount(4, "los cuatro enlaces vienen siempre");

        var conOtraEmpresa = (await ((Func<Task>)(() => ajeno.ObtenerConfiguracionPortalAsync(new ObtenerConfiguracionPortalRequest(), cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;
        PlatformRpcErrors.Reason(conOtraEmpresa).Should().Be("invalid_tenant", "el candado va antes: un llamante ajeno no distingue qué existe");
    }

    [Fact]
    public async Task EvaluarRequisitos_ValidaLaEntrada_YUnTipoDesconocidoNoEstaDisponible()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(habilitada: true);
        var cliente = await ClienteAsync(host, Portal, PlatformTenants.PersonaNatural, ct);

        var sinTipo = (await ((Func<Task>)(() => cliente.EvaluarRequisitosAsync(new EvaluarRequisitosRequest { Placa = "ABC123" }, cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;
        sinTipo.StatusCode.Should().Be(StatusCode.InvalidArgument);

        var organismoMalo = (await ((Func<Task>)(() => cliente.EvaluarRequisitosAsync(new EvaluarRequisitosRequest { TipoTramiteCodigo = "TRASPASO_STANDARD", Placa = "ABC123", OrganismoId = "no-es-guid" }, cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;
        organismoMalo.StatusCode.Should().Be(StatusCode.InvalidArgument);

        var desconocido = (await ((Func<Task>)(() => cliente.EvaluarRequisitosAsync(new EvaluarRequisitosRequest { TipoTramiteCodigo = "NO_EXISTE_ESTE_TIPO", Placa = "ABC123" }, cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;
        desconocido.StatusCode.Should().Be(StatusCode.NotFound);
        PlatformRpcErrors.Reason(desconocido).Should().Be("tipo_no_disponible");
    }

    // ── Traducción de bloqueos y estados (sin base ni red) ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ok", "cumple")]
    [InlineData("warn", "falta")]
    [InlineData("fail", "falta")]
    [InlineData("error", "falta")]
    [InlineData("skip", "no_aplica")]
    [InlineData("", "no_aplica")]
    public void ElEstadoDeUnChequeo_SeTraduceALosTresEstadosDelPortal(string status, string esperado) =>
        PersonaNaturalGrpcService.EstadoRequisito(status).Should().Be(esperado);

    [Fact]
    public void LosBloqueosDelPaso1_SonRequisitosQueFaltan_ConElTextoDeGestorSinDatosInternos()
    {
        foreach (var error in new[] { InitialProcedureValidationGate.DuplicateActiveProcedure, VehicleStatePolicy.ErrorCode, VehicleBodyTypePolicy.ErrorCode })
        {
            var respuesta = PersonaNaturalGrpcService.TraducirBloqueo(error);

            respuesta.PuedeContinuar.Should().BeFalse();
            var requisito = respuesta.Requisitos.Should().ContainSingle().Which;
            requisito.Estado.Should().Be("falta");
            requisito.Codigo.Should().Be(error);
            requisito.Descripcion.Should().NotBeNullOrWhiteSpace().And.NotContainAny("procedureInstanceId", "vin", "propietario");
        }
    }

    [Theory]
    [InlineData("procedure_type_not_found", StatusCode.NotFound, "tipo_no_disponible")]
    [InlineData("modalidad_not_available", StatusCode.NotFound, "tipo_no_disponible")]
    [InlineData("transit_office_required", StatusCode.FailedPrecondition, "organismo_no_habilitado")]
    [InlineData("inventado_que_no_conocemos", StatusCode.Internal, "preflight_no_disponible")]
    public void LosErroresDeEntradaOTipo_SeTraducenAEstadosGrpcConSuRazon(string error, StatusCode estado, string razon)
    {
        var codigoReal = error == "transit_office_required" ? TransitOfficeSelectionPolicy.RequiredErrorCode : error;

        var llamada = () => PersonaNaturalGrpcService.TraducirBloqueo(codigoReal);

        var rpc = llamada.Should().Throw<RpcException>().Which;
        rpc.StatusCode.Should().Be(estado);
        PlatformRpcErrors.Reason(rpc).Should().Be(razon);
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> Host(bool habilitada) =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("PersonaNatural:Fachada:Habilitada", habilitada ? "true" : "false");
            foreach (var (cliente, scope) in new[] { (Portal, Scope), ("svc-otro-prueba", Scope), ("svc-ict-prueba", "platform.tramites.ict") })
            {
                b.UseSetting($"Suite:Oidc:ServiceClients:{cliente}:Secret", OidcServerTests.ServiceSecret);
                b.UseSetting($"Suite:Oidc:ServiceClients:{cliente}:Scopes:0", scope);
            }
        }).ConEmpresaPersonaNatural();

    private static async Task<Cliente> ClienteAsync(WebApplicationFactory<Program> host, string clientId, Guid? empresa, CancellationToken ct)
    {
        var scope = clientId == "svc-ict-prueba" ? "platform.tramites.ict" : Scope;
        return new Cliente(Canal(host, await TokenAsync(host, clientId, scope, ct), empresa));
    }

    private static CallInvoker Canal(WebApplicationFactory<Program> host, string? token, Guid? empresa)
    {
        var canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        return canal.CreateCallInvoker().Intercept(metadata =>
        {
            if (token is not null)
                metadata.Add("authorization", $"Bearer {token}");
            if (empresa is { } e)
                metadata.Add("x-flit-tenant-id", e.ToString());
            return metadata;
        });
    }

    private static async Task<string> TokenAsync(WebApplicationFactory<Program> host, string clientId, string scope, CancellationToken ct)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = OidcServerTests.ServiceSecret,
            ["scope"] = scope,
        }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>Un trámite mínimo en la base real, con el tipo que dejan las migraciones y sin organismo.</summary>
    private static async Task<Guid> SembrarTramiteAsync(WebApplicationFactory<Program> host, Guid? empresa, string referenciaExterna, CancellationToken ct)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var tipo = db.ProcedureTypes.Where(t => !t.Code.StartsWith("PN_")).OrderBy(t => t.Code).First();
        var tenant = empresa ?? db.Tenants.Where(t => t.Id != PlatformTenants.PersonaNatural).OrderBy(t => t.Code).Select(t => t.Id).First();
        var instancia = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureTypeId = tipo.Id,
            TransitOfficeId = null, // sin organismo: otras pruebas en paralelo crean y borran organismos y una base limpia no trae catálogo
            CreatedByUserId = db.Users.OrderBy(u => u.Email).Select(u => u.Id).First(),
            Status = "entregado",
            ExternalRef = referenciaExterna,
            Origin = "persona_natural",
        };
        db.Add(instancia);
        await db.SaveChangesAsync(ct);
        return instancia.Id;
    }
}
