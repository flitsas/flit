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
/// HU #14067 (Feature H, Épica #14021) — <c>/api/v1/admin/companies/{id}/external-integrations</c> contra el host real
/// (policy SuperAdmin, esquemas y hasher reales); el repositorio es un doble en memoria con las mismas reglas de unicidad
/// del índice <c>uq_external_clients_company_tipo_activo</c>. El ciclo contra Postgres está en
/// <c>Flit.Integration.Tests</c> (ExternalIntegrationRepositoryTests).
/// </summary>
public sealed class AdminExternalIntegrationsEndpointTests : IClassFixture<AdminExternalIntegrationsEndpointTests.Factory>
{
    private readonly Factory _factory;

    public AdminExternalIntegrationsEndpointTests(Factory factory) => _factory = factory;

    private static string Base(Guid companyId) => $"/api/v1/admin/companies/{companyId}/external-integrations";

    [Fact]
    public async Task AC1_AC6_ElAltaDevuelveElSecretoUnaVezConElAlcanceDeLaFachadaYElListadoNoLoMuestra()
    {
        var compania = _factory.Compania();

        var alta = await Enviar(HttpMethod.Post, Base(compania), "SuperAdmin", new { tipo = "persona_natural" });

        alta.StatusCode.Should().Be(HttpStatusCode.Created);
        alta.Headers.CacheControl!.NoStore.Should().BeTrue();
        var cuerpo = await alta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var secreto = cuerpo.GetProperty("clientSecret").GetString();
        secreto.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        var cliente = cuerpo.GetProperty("client");
        cliente.GetProperty("clientId").GetString().Should().StartWith("pn-");
        cliente.GetProperty("companyId").GetGuid().Should().Be(compania);
        cliente.GetProperty("tipo").GetString().Should().Be("persona_natural");
        cliente.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).Should().Equal("platform.tramites.persona_natural");

        var listado = await Enviar(HttpMethod.Get, Base(compania), "SuperAdmin");
        listado.StatusCode.Should().Be(HttpStatusCode.OK);
        var texto = await listado.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        texto.Should().Contain("pn-").And.NotContain(secreto!).And.NotContainEquivalentOf("secret").And.NotContainEquivalentOf("hash");
    }

    [Fact]
    public async Task AC2_UnaSegundaIntegracionActivaDelMismoTipoEs409_YTrasDesactivarSePuedeCrearOtra()
    {
        var compania = _factory.Compania();
        var primera = await Crear(compania);

        var repetida = await Enviar(HttpMethod.Post, Base(compania), "SuperAdmin", new { tipo = "persona_natural" });

        repetida.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Error(repetida)).Should().Be("integracion_ya_activa");

        (await Enviar(HttpMethod.Post, $"{Base(compania)}/{primera}/deactivate", "SuperAdmin")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Enviar(HttpMethod.Post, Base(compania), "SuperAdmin", new { tipo = "persona_natural" })).StatusCode
            .Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AC2_OtraCompaniaSiPuedeTenerSuPropiaIntegracion()
    {
        await Crear(_factory.Compania());

        (await Enviar(HttpMethod.Post, Base(_factory.Compania()), "SuperAdmin", new { tipo = "persona_natural" })).StatusCode
            .Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AC3_RotarDevuelveUnSecretoNuevoYRespetaRevocarAnterior()
    {
        var compania = _factory.Compania();
        var id = await Crear(compania);

        var rotar = await Enviar(HttpMethod.Post, $"{Base(compania)}/{id}/rotate-secret", "SuperAdmin", new { revocarAnterior = true });

        rotar.StatusCode.Should().Be(HttpStatusCode.OK);
        rotar.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await rotar.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("clientSecret").GetString().Should().HaveLength(43);
        _factory.Clients.UltimoRevocar.Should().BeTrue();

        // Sin cuerpo: rotación planificada, el anterior sigue valiendo durante la ventana de gracia.
        (await Enviar(HttpMethod.Post, $"{Base(compania)}/{id}/rotate-secret", "SuperAdmin")).StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Clients.UltimoRevocar.Should().BeFalse();
    }

    [Fact]
    public async Task AC4_BloquearDesbloquearYDesactivarCambianElEstado()
    {
        var compania = _factory.Compania();
        var id = await Crear(compania);

        var bloquear = await Enviar(HttpMethod.Post, $"{Base(compania)}/{id}/block", "SuperAdmin");
        bloquear.StatusCode.Should().Be(HttpStatusCode.OK);
        (await bloquear.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("lockedUntil").GetDateTimeOffset().Year.Should().Be(9999);

        var rotarBloqueada = await Enviar(HttpMethod.Post, $"{Base(compania)}/{id}/rotate-secret", "SuperAdmin");
        rotarBloqueada.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Error(rotarBloqueada)).Should().Be("integracion_bloqueada");

        var desbloquear = await Enviar(HttpMethod.Post, $"{Base(compania)}/{id}/unblock", "SuperAdmin");
        (await desbloquear.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("lockedUntil").ValueKind.Should().Be(JsonValueKind.Null);

        var desactivar = await Enviar(HttpMethod.Post, $"{Base(compania)}/{id}/deactivate", "SuperAdmin");
        (await desactivar.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("isActive").GetBoolean().Should().BeFalse();
        var bloquearInactiva = await Enviar(HttpMethod.Post, $"{Base(compania)}/{id}/block", "SuperAdmin");
        bloquearInactiva.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CompaniaOIntegracionInexistente_404_YTipoInvalido_400()
    {
        var compania = _factory.Compania();

        (await Error(await Enviar(HttpMethod.Get, Base(Guid.NewGuid()), "SuperAdmin"))).Should().Be("company_not_found");
        (await Error(await Enviar(HttpMethod.Post, $"{Base(compania)}/{Guid.NewGuid()}/block", "SuperAdmin")))
            .Should().Be("integration_not_found");
        var invalido = await Enviar(HttpMethod.Post, Base(compania), "SuperAdmin", new { tipo = "externa" });
        invalido.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Error(invalido)).Should().Be("invalid_integration_type");
    }

    [Fact]
    public async Task UnaIntegracionDeOtraCompania_NoSeRotaNiSeBloqueaDesdeEstaFicha()
    {
        var duena = _factory.Compania();
        var otra = _factory.Compania();
        var id = await Crear(duena);

        (await Enviar(HttpMethod.Post, $"{Base(otra)}/{id}/rotate-secret", "SuperAdmin")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Enviar(HttpMethod.Post, $"{Base(otra)}/{id}/block", "SuperAdmin")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LaRutaGenericaDeClientesExternosNoListaNiEditaLasIntegracionesDeCompania()
    {
        var compania = _factory.Compania();
        var id = await Crear(compania);

        var listado = await Enviar(HttpMethod.Get, "/api/v1/admin/external-clients", "SuperAdmin");
        (await listado.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain(id.ToString());
        (await Enviar(HttpMethod.Get, $"/api/v1/admin/external-clients/{id}", "SuperAdmin")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Enviar(HttpMethod.Patch, $"/api/v1/admin/external-clients/{id}", "SuperAdmin", new { scopes = new[] { ExternalScopes.TramitesRead } }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "no se le pueden agregar permisos de lectura externa por la ruta genérica");
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("POST", "/00000000-0000-4000-8000-000000000001/rotate-secret")]
    [InlineData("POST", "/00000000-0000-4000-8000-000000000001/block")]
    [InlineData("POST", "/00000000-0000-4000-8000-000000000001/unblock")]
    [InlineData("POST", "/00000000-0000-4000-8000-000000000001/deactivate")]
    public async Task AC5_UnUsuarioQueNoEsSuperAdmin_Recibe403(string metodo, string ruta)
    {
        var escriturasAntes = _factory.Clients.Escrituras;

        foreach (var rol in new[] { "Operador", "AdminCompany" })
        {
            var response = await Enviar(new HttpMethod(metodo), Base(_factory.Compania()) + ruta, rol, metodo == "GET" ? null : new { tipo = "persona_natural" });
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, rol);
        }

        _factory.Clients.Escrituras.Should().Be(escriturasAntes, "la autorización corta antes del handler");
    }

    [Fact]
    public async Task AC5_SinTokenRecibe401()
    {
        var response = await _factory.CreateClient().GetAsync(Base(_factory.Compania()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> Crear(Guid compania)
    {
        var alta = await Enviar(HttpMethod.Post, Base(compania), "SuperAdmin", new { tipo = "persona_natural" });
        alta.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await alta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("client").GetProperty("id").GetGuid();
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
        public InMemoryIntegrations Clients { get; } = new();

        /// <summary>Una compañía nueva (existente para el doble).</summary>
        public Guid Compania() => Clients.AddCompany();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExternalClientRepository>();
                services.AddSingleton<IExternalClientRepository>(Clients);
            });
    }

    /// <summary>Doble en memoria con la unicidad «una activa por (compañía, tipo)» del índice parcial.</summary>
    public sealed class InMemoryIntegrations : IExternalClientRepository
    {
        private readonly ConcurrentDictionary<Guid, ExternalClientView> _byId = new();
        private readonly ConcurrentDictionary<Guid, byte> _companies = new();
        private readonly object _gate = new();

        public int Escrituras { get; private set; }

        public bool? UltimoRevocar { get; private set; }

        public Guid AddCompany()
        {
            var id = Guid.NewGuid();
            _companies[id] = 0;
            return id;
        }

        public Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.ContainsKey(companyId));

        public Task<ExternalClientView> CreateAsync(NewExternalClient client, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_byId.Values.Any(c => c.ClientId == client.ClientId))
                {
                    throw new ExternalClientAlreadyExistsException(client.ClientId);
                }

                if (client.CompanyId is { } company
                    && _byId.Values.Any(c => c.CompanyId == company && c.Tipo == client.Tipo && c.IsActive))
                {
                    throw new ExternalIntegrationAlreadyActiveException(company, client.Tipo);
                }

                Escrituras++;
                var view = new ExternalClientView(Guid.CreateVersion7(), client.ClientId, client.DisplayName, client.Purpose,
                    client.Scopes, true, false, null, null, DateTimeOffset.UtcNow, client.Tipo, client.CompanyId);
                _byId[view.Id] = view;
                return Task.FromResult(view);
            }
        }

        public Task<ExternalClientView?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byId.TryGetValue(id, out var c) ? c : null);

        public Task<IReadOnlyList<ExternalClientView>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExternalClientView>>(
                _byId.Values.Where(c => c.Tipo == ExternalIntegrationTypes.Externa).OrderBy(c => c.ClientId, StringComparer.Ordinal).ToList());

        public Task<IReadOnlyList<ExternalClientView>> ListByCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExternalClientView>>(
                _byId.Values.Where(c => c.CompanyId == companyId).OrderByDescending(c => c.CreatedAt).ToList());

        public Task<ExternalClientView?> GetByCompanyAsync(Guid companyId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byId.TryGetValue(id, out var c) && c.CompanyId == companyId ? c : null);

        public Task<ExternalClientView?> UpdateAsync(
            Guid id, ExternalClientChanges changes, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Mutar(id, c => c with
            {
                DisplayName = changes.DisplayName ?? c.DisplayName,
                Purpose = changes.Purpose ?? c.Purpose,
                Scopes = changes.Scopes ?? c.Scopes,
                IsActive = changes.IsActive ?? c.IsActive,
                MustRotate = changes.MustRotate ?? c.MustRotate,
            });

        public Task<ExternalClientView?> ReplaceSecretAsync(
            Guid id, string newSecretHash, bool revokePrevious, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            UltimoRevocar = revokePrevious;
            return Mutar(id, c => c with { MustRotate = false, LockedUntil = null });
        }

        public Task<ExternalClientView?> BlockAsync(Guid id, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Mutar(id, c => c with { LockedUntil = ExternalIntegrationRules.IndefiniteBlock });

        public Task<ExternalClientView?> UnlockAsync(Guid id, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Mutar(id, c => c with { LockedUntil = null });

        public Task<ExternalClientCredentials?> GetCredentialsByClientIdAsync(string clientId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DateTimeOffset?> RegisterFailedAttemptAsync(
            Guid id, int maxFailedAttempts, TimeSpan lockDuration, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RegisterTokenIssuedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private Task<ExternalClientView?> Mutar(Guid id, Func<ExternalClientView, ExternalClientView> cambio)
        {
            if (!_byId.TryGetValue(id, out var c))
            {
                return Task.FromResult<ExternalClientView?>(null);
            }

            Escrituras++;
            var updated = cambio(c);
            _byId[id] = updated;
            return Task.FromResult<ExternalClientView?>(updated);
        }
    }
}
