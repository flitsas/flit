using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13088 — <c>/api/v1/admin/external-clients</c> contra el host real (policy SuperAdmin, esquemas y
/// hasher reales); el repositorio es un doble en memoria. El ciclo contra Postgres está en
/// <c>Flit.Integration.Tests</c> (ExternalClientAdminRepositoryTests).
/// </summary>
public sealed class AdminExternalClientsEndpointTests : IClassFixture<AdminExternalClientsEndpointTests.Factory>
{
    private const string Base = "/api/v1/admin/external-clients";
    private const string Finalidad = "Sincronización de trámites para procesos Flito";

    private readonly Factory _factory;

    public AdminExternalClientsEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task AC1_ElAltaDevuelveElSecretoUnaVezYElListadoNoLoMuestra()
    {
        var clientId = $"flito-{Guid.NewGuid():N}"[..20];

        var alta = await Enviar(HttpMethod.Post, Base, "SuperAdmin", new
        {
            clientId,
            displayName = "Flito (pruebas)",
            purpose = Finalidad,
            scopes = new[] { ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead },
        });

        alta.StatusCode.Should().Be(HttpStatusCode.Created);
        alta.Headers.CacheControl!.NoStore.Should().BeTrue();
        var cuerpo = await alta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var secreto = cuerpo.GetProperty("clientSecret").GetString();
        secreto.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        cuerpo.GetProperty("client").GetProperty("clientId").GetString().Should().Be(clientId);

        var listado = await Enviar(HttpMethod.Get, Base, "SuperAdmin");
        listado.StatusCode.Should().Be(HttpStatusCode.OK);
        var texto = await listado.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        texto.Should().Contain(clientId);
        texto.Should().NotContain(secreto!).And.NotContainEquivalentOf("secret").And.NotContainEquivalentOf("hash");
    }

    [Fact]
    public async Task AC1_IdentificadorRepetido409YDatosInvalidos400()
    {
        var clientId = $"flito-{Guid.NewGuid():N}"[..20];
        var body = new { clientId, displayName = "Flito", purpose = Finalidad, scopes = new[] { ExternalScopes.TramitesRead } };

        (await Enviar(HttpMethod.Post, Base, "SuperAdmin", body)).StatusCode.Should().Be(HttpStatusCode.Created);
        var repetido = await Enviar(HttpMethod.Post, Base, "SuperAdmin", body);
        var invalido = await Enviar(HttpMethod.Post, Base, "SuperAdmin", body with { clientId = "Flito Dev!" });

        repetido.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Error(repetido)).Should().Be("client_id_taken");
        invalido.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Error(invalido)).Should().Be("invalid_client_id");
    }

    [Fact]
    public async Task AC2_EditarRegenerarYDesbloquearResponden()
    {
        var id = _factory.Existente();

        var editar = await Enviar(HttpMethod.Patch, $"{Base}/{id}", "SuperAdmin", new { isActive = false, mustRotate = true });
        var regenerar = await Enviar(HttpMethod.Post, $"{Base}/{id}/regenerate-secret", "SuperAdmin", new { revocarAnterior = true });
        var desbloquear = await Enviar(HttpMethod.Post, $"{Base}/{id}/unlock", "SuperAdmin");

        editar.StatusCode.Should().Be(HttpStatusCode.OK);
        (await editar.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("isActive").GetBoolean().Should().BeFalse();
        regenerar.StatusCode.Should().Be(HttpStatusCode.OK);
        regenerar.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await regenerar.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("clientSecret").GetString().Should().HaveLength(43);
        _factory.Clients.UltimoRevocar.Should().BeTrue();
        desbloquear.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AC2_ClienteInexistente404()
    {
        var response = await Enviar(HttpMethod.Post, $"{Base}/{Guid.NewGuid()}/unlock", "SuperAdmin");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Error(response)).Should().Be("not_found");
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("PATCH", "/00000000-0000-4000-8000-000000000001")]
    [InlineData("POST", "/00000000-0000-4000-8000-000000000001/regenerate-secret")]
    [InlineData("POST", "/00000000-0000-4000-8000-000000000001/unlock")]
    public async Task AC3_UnUsuarioQueNoEsSuperAdmin_Recibe403(string metodo, string ruta)
    {
        var escriturasAntes = _factory.Clients.Escrituras;

        var response = await Enviar(new HttpMethod(metodo), Base + ruta, "Operador", metodo == "GET" ? null : new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Clients.Escrituras.Should().Be(escriturasAntes, "la autorización corta antes del handler");
    }

    [Fact]
    public async Task AC3_UnPaseDeClienteExterno_Recibe401()
    {
        using var scope = _factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>()
            .Issue("flito-dev", ExternalScopes.Todos).Token;

        using var request = new HttpRequestMessage(HttpMethod.Get, Base);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        var response = await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "un pase externo no es credencial de la plataforma");
    }

    [Fact]
    public async Task AC3_SinToken_Recibe401()
    {
        var response = await _factory.CreateClient().GetAsync(Base, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> Enviar(HttpMethod metodo, string url, string rol, object? body = null)
    {
        using var request = new HttpRequestMessage(metodo, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken(rol));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string?> Error(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("error").GetString();

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public InMemoryAdminClients Clients { get; } = new();

        public Guid Existente()
        {
            var id = Guid.CreateVersion7();
            Clients.Put(new ExternalClientView(id, $"c-{id:N}"[..20], "Flito", Finalidad,
                [ExternalScopes.TramitesRead], true, false, DateTimeOffset.UtcNow.AddMinutes(10), null, DateTimeOffset.UtcNow));
            return id;
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExternalClientRepository>();
                services.AddSingleton<IExternalClientRepository>(Clients);
            });
    }

    /// <summary>Doble en memoria de la parte de administración del repositorio.</summary>
    public sealed class InMemoryAdminClients : IExternalClientRepository
    {
        private readonly ConcurrentDictionary<Guid, ExternalClientView> _byId = new();

        public int Escrituras { get; private set; }

        public bool? UltimoRevocar { get; private set; }

        public void Put(ExternalClientView client) => _byId[client.Id] = client;

        public Task<ExternalClientView> CreateAsync(NewExternalClient client, CancellationToken cancellationToken = default)
        {
            if (_byId.Values.Any(c => c.ClientId == client.ClientId))
            {
                throw new ExternalClientAlreadyExistsException(client.ClientId);
            }

            Escrituras++;
            var view = new ExternalClientView(Guid.CreateVersion7(), client.ClientId, client.DisplayName, client.Purpose,
                client.Scopes, true, false, null, null, DateTimeOffset.UtcNow);
            Put(view);
            return Task.FromResult(view);
        }

        public Task<ExternalClientView?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byId.TryGetValue(id, out var c) ? c : null);

        public Task<IReadOnlyList<ExternalClientView>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExternalClientView>>(_byId.Values.OrderBy(c => c.ClientId, StringComparer.Ordinal).ToList());

        public Task<ExternalClientView?> UpdateAsync(
            Guid id, ExternalClientChanges changes, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (!_byId.TryGetValue(id, out var c))
            {
                return Task.FromResult<ExternalClientView?>(null);
            }

            Escrituras++;
            var updated = c with
            {
                DisplayName = changes.DisplayName ?? c.DisplayName,
                Purpose = changes.Purpose ?? c.Purpose,
                Scopes = changes.Scopes ?? c.Scopes,
                IsActive = changes.IsActive ?? c.IsActive,
                MustRotate = changes.MustRotate ?? c.MustRotate,
            };
            Put(updated);
            return Task.FromResult<ExternalClientView?>(updated);
        }

        public Task<ExternalClientView?> ReplaceSecretAsync(
            Guid id, string newSecretHash, bool revokePrevious, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            UltimoRevocar = revokePrevious;
            if (!_byId.TryGetValue(id, out var c))
            {
                return Task.FromResult<ExternalClientView?>(null);
            }

            Escrituras++;
            var updated = c with { MustRotate = false, LockedUntil = null };
            Put(updated);
            return Task.FromResult<ExternalClientView?>(updated);
        }

        public Task<ExternalClientView?> UnlockAsync(Guid id, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (!_byId.TryGetValue(id, out var c))
            {
                return Task.FromResult<ExternalClientView?>(null);
            }

            Escrituras++;
            var updated = c with { LockedUntil = null };
            Put(updated);
            return Task.FromResult<ExternalClientView?>(updated);
        }

        public Task<ExternalClientCredentials?> GetCredentialsByClientIdAsync(string clientId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DateTimeOffset?> RegisterFailedAttemptAsync(
            Guid id, int maxFailedAttempts, TimeSpan lockDuration, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RegisterTokenIssuedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
