using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #13731 — <c>ListarTiposTramite</c> de <c>CatalogosService</c>: los tipos de trámite publicados y activos, solo para
/// el cliente de servicio permitido (svc-diagnostico) con el scope <c>platform.tramites.catalogos</c>.
/// <para>Uso de ejemplo:
/// <c>new CatalogosService.CatalogosServiceClient(invoker).ListarTiposTramiteAsync(new ListarTiposTramiteRequest())</c>
/// con un token client_credentials de svc-diagnostico (aud=tramites).</para>
/// </summary>
public sealed class TiposTramiteGrpcTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Catalogos = "platform.tramites.catalogos";
    private const string IdentidadRead = "platform.identidad.read";
    private const string Diagnostico = "svc-diagnostico-prueba";
    private const string Comparendos = "svc-comparendos-prueba";

    [Fact]
    public async Task ClientePermitido_RecibeSoloPublicadosYActivos_ConSusCampos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var prefijo = $"T13731{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var publicado = Tipo($"{prefijo}_B", "Traspaso de prueba", "TRASPASO", PublicationStatus.Published, activo: true, wizard: true);
        var publicadoSinWizard = Tipo($"{prefijo}_A", "Matrícula de prueba", "MATRICULAS", PublicationStatus.Published, activo: true, wizard: false);
        var borrador = Tipo($"{prefijo}_C", "Borrador de prueba", "OTROS", PublicationStatus.Draft, activo: true, wizard: false);
        var archivado = Tipo($"{prefijo}_D", "Archivado de prueba", "OTROS", PublicationStatus.Archived, activo: true, wizard: false);
        var inactivo = Tipo($"{prefijo}_E", "Inactivo de prueba", "TRASPASO", PublicationStatus.Published, activo: false, wizard: true);
        await SembrarAsync(host, [publicado, publicadoSinWizard, borrador, archivado, inactivo], ct);
        try
        {
            var token = await TokenAsync(host, Diagnostico, Catalogos, ct);
            var respuesta = await ListarTiposAsync(host, token, ct);

            respuesta.Tipos.Select(t => t.Codigo).Should().BeInAscendingOrder(StringComparer.Ordinal);
            var propios = respuesta.Tipos.Where(t => t.Codigo.StartsWith(prefijo, StringComparison.Ordinal)).ToList();
            propios.Select(t => t.Codigo).Should().Equal($"{prefijo}_A", $"{prefijo}_B");
            propios[1].Should().BeEquivalentTo(new TipoTramite { Codigo = $"{prefijo}_B", Nombre = "Traspaso de prueba", Familia = "TRASPASO", Activo = true, WizardEnabled = true });
            propios[0].Should().BeEquivalentTo(new TipoTramite { Codigo = $"{prefijo}_A", Nombre = "Matrícula de prueba", Familia = "MATRICULAS", Activo = true, WizardEnabled = false });
            respuesta.Tipos.Should().OnlyContain(t => t.Activo);
        }
        finally
        {
            await BorrarAsync(host, prefijo);
        }
    }

    [Fact]
    public async Task OtroClienteConElScope_PermissionDenied_YSigueLeyendoOrganismos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var token = await TokenAsync(host, Comparendos, Catalogos, ct);

        var call = () => ListarTiposAsync(host, token, ct);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);

        var organismos = await Cliente(host, token).ListarOrganismosTransitoAsync(new ListarOrganismosTransitoRequest(), cancellationToken: ct);
        organismos.Should().NotBeNull();
    }

    [Fact]
    public async Task SinToken_Unauthenticated_YSinElScope_Rechazado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();

        var sinToken = () => ListarTiposAsync(host, token: null, ct);
        (await sinToken.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);

        // Cliente permitido, pero el token no lleva platform.tramites.catalogos.
        var sinScope = await TokenAsync(host, Diagnostico, IdentidadRead, ct);
        var conOtroScope = () => ListarTiposAsync(host, sinScope, ct);
        (await conOtroScope.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().BeOneOf(StatusCode.Unauthenticated, StatusCode.PermissionDenied);
    }

    [Fact]
    public void Configuracion_SoloSvcDiagnosticoEstaPermitido_YTieneElScope()
    {
        var configuracion = factory.Services.GetRequiredService<IConfiguration>();

        configuracion.GetSection("Catalogos:TiposTramite:ClientesPermitidos").Get<string[]>().Should().Equal("svc-diagnostico");
        configuracion.GetSection("Suite:Oidc:ServiceClients:svc-diagnostico:Scopes").Get<string[]>().Should().Contain(Catalogos);
    }

    private WebApplicationFactory<Program> Host() =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("Catalogos:TiposTramite:ClientesPermitidos:0", Diagnostico);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Diagnostico}:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Diagnostico}:Scopes:0", Catalogos);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Diagnostico}:Scopes:1", IdentidadRead);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Comparendos}:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Comparendos}:Scopes:0", Catalogos);
        });

    private static ProcedureType Tipo(string codigo, string nombre, string familia, string estado, bool activo, bool wizard) => new()
    {
        Id = Guid.CreateVersion7(),
        Code = codigo,
        Name = nombre,
        Family = familia,
        IsActive = activo,
        PublicationStatus = estado,
        WizardEnabled = wizard,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task SembrarAsync(WebApplicationFactory<Program> host, IReadOnlyList<ProcedureType> tipos, CancellationToken ct)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        db.ProcedureTypes.AddRange(tipos);
        await db.SaveChangesAsync(ct);
    }

    private static async Task BorrarAsync(WebApplicationFactory<Program> host, string prefijo)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        await db.ProcedureTypes.Where(t => t.Code.StartsWith(prefijo)).ExecuteDeleteAsync();
    }

    private static CatalogosService.CatalogosServiceClient Cliente(WebApplicationFactory<Program> host, string? token)
    {
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        var invoker = token is null
            ? channel.CreateCallInvoker()
            : channel.CreateCallInvoker().Intercept(metadata =>
            {
                metadata.Add("authorization", $"Bearer {token}");
                return metadata;
            });
        return new CatalogosService.CatalogosServiceClient(invoker);
    }

    private static async Task<ListarTiposTramiteResponse> ListarTiposAsync(WebApplicationFactory<Program> host, string? token, CancellationToken ct) =>
        await Cliente(host, token).ListarTiposTramiteAsync(new ListarTiposTramiteRequest(), cancellationToken: ct);

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
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString()!;
    }
}
