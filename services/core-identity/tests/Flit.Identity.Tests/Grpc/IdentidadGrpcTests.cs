using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Identidad.Grpc.V1;
using Flit.Identity.Api;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Identity.Tests.Grpc;

/// <summary>
/// HU #13334 (Epic #13316) — <c>flit.identidad.v1.IdentidadService</c> contra core-identity real y PostgreSQL
/// (<c>ConnectionStrings__Core</c>), con tokens de servicio emitidos por su propio <c>/connect/token</c>.
/// </summary>
public sealed class IdentidadGrpcTests : IClassFixture<IdentidadGrpcTests.Host>, IDisposable
{
    private const int GrpcPort = 5925;
    private const string Secret = "secreto-de-prueba-grpc-identidad";

    private readonly Host _host;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _otraEmpresa = Guid.NewGuid();
    private readonly Guid _activo = Guid.NewGuid();
    private readonly Guid _pendiente = Guid.NewGuid();
    private readonly Guid _deOtraEmpresa = Guid.NewGuid();
    private readonly Guid _asignacionBorrada = Guid.NewGuid();
    private readonly Guid _rolTramites = Guid.NewGuid();
    private readonly Guid _rolPlataforma = Guid.NewGuid();

    public IdentidadGrpcTests(Host host)
    {
        _host = host;
        SeedAsync().GetAwaiter().GetResult();
    }

    // ── AC1: consulta válida ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarUsuariosEmpresa_ConTokenDeServicio_DevuelveSoloLosDeEsaEmpresa()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await Client(await ServiceTokenAsync(ct)).ListarUsuariosEmpresaAsync(
            new ListarUsuariosEmpresaRequest(), Metadata(_empresa), cancellationToken: ct);

        response.Usuarios.Select(u => u.Id).Should().Equal(_activo.ToString());
        var usuario = response.Usuarios.Single();
        usuario.Email.Should().Be(Email("activo"));
        usuario.Estado.Should().Be(EstadoUsuario.Activo);
        usuario.Roles.Select(r => (r.Producto, r.Codigo)).Should().Equal(("plataforma", $"GrpcAdmin-{_suffix}"), ("tramites", $"GrpcRadicador-{_suffix}"));
        response.NextPageToken.Should().BeEmpty();
    }

    [Fact]
    public async Task ListarUsuariosEmpresa_ConNoActivosYProducto_FiltraRolesPorProducto()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await Client(await ServiceTokenAsync(ct)).ListarUsuariosEmpresaAsync(
            new ListarUsuariosEmpresaRequest { IncluirNoActivos = true, Producto = "tramites" }, Metadata(_empresa), cancellationToken: ct);

        response.Usuarios.Select(u => u.Id).Should().BeEquivalentTo([_activo.ToString(), _pendiente.ToString()]);
        response.Usuarios.Should().OnlyContain(u => u.Roles.All(r => r.Producto == "tramites"));
        response.Usuarios.Single(u => u.Id == _pendiente.ToString()).Estado.Should().Be(EstadoUsuario.Pendiente);
    }

    [Fact]
    public async Task ListarUsuariosEmpresa_Pagina()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = Client(await ServiceTokenAsync(ct));

        var first = await client.ListarUsuariosEmpresaAsync(new ListarUsuariosEmpresaRequest { IncluirNoActivos = true, PageSize = 1 }, Metadata(_empresa), cancellationToken: ct);
        first.Usuarios.Should().HaveCount(1);
        first.NextPageToken.Should().NotBeEmpty();

        var second = await client.ListarUsuariosEmpresaAsync(
            new ListarUsuariosEmpresaRequest { IncluirNoActivos = true, PageSize = 1, PageToken = first.NextPageToken }, Metadata(_empresa), cancellationToken: ct);
        second.Usuarios.Should().HaveCount(1);
        second.Usuarios[0].Id.Should().NotBe(first.Usuarios[0].Id);
        second.NextPageToken.Should().BeEmpty();
    }

    [Fact]
    public async Task ObtenerUsuario_DeOtraEmpresa_NotFound_YDeLaEmpresa_ConSusRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = Client(await ServiceTokenAsync(ct));

        var ajeno = async () => await client.ObtenerUsuarioAsync(new ObtenerUsuarioRequest { UsuarioId = _deOtraEmpresa.ToString() }, Metadata(_empresa), cancellationToken: ct);
        (await ajeno.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);

        var borrado = async () => await client.ObtenerUsuarioAsync(new ObtenerUsuarioRequest { UsuarioId = _asignacionBorrada.ToString() }, Metadata(_empresa), cancellationToken: ct);
        (await borrado.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);

        var propio = await client.ObtenerUsuarioAsync(new ObtenerUsuarioRequest { UsuarioId = _activo.ToString(), Producto = "plataforma" }, Metadata(_empresa), cancellationToken: ct);
        propio.Usuario.Roles.Select(r => r.Codigo).Should().Equal($"GrpcAdmin-{_suffix}");
    }

    [Fact]
    public async Task ObtenerProductosHabilitados_ConLaReglaDeLaJerarquia()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = Client(await ServiceTokenAsync(ct));

        (await client.ObtenerProductosHabilitadosAsync(new ObtenerProductosHabilitadosRequest(), Metadata(_empresa), cancellationToken: ct))
            .Productos.Should().Equal("plataforma", "tramites");
        (await client.ObtenerProductosHabilitadosAsync(new ObtenerProductosHabilitadosRequest(), Metadata(_otraEmpresa), cancellationToken: ct))
            .Productos.Should().Equal("plataforma");
    }

    // ── AC2: token de usuario ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenDeUsuario_Unauthenticated_AunqueElMismoTokenSirvaEnElRest()
    {
        var ct = TestContext.Current.CancellationToken;
        var userToken = UserToken();

        // El token es válido: la API REST lo acepta. Lo que el gRPC rechaza es que sea de usuario.
        using var rest = _host.CreateClient();
        rest.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        (await rest.GetAsync("/api/v1/platform/me/apps", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var call = async () => await Client(userToken).ListarUsuariosEmpresaAsync(new ListarUsuariosEmpresaRequest(), Metadata(_empresa), cancellationToken: ct);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task SinToken_Unauthenticated()
    {
        var ct = TestContext.Current.CancellationToken;
        var call = async () => await Client(token: null).ObtenerProductosHabilitadosAsync(new ObtenerProductosHabilitadosRequest(), Metadata(_empresa), cancellationToken: ct);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task TokenDeServicioSinElScope_PermissionDenied()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = await ServiceTokenAsync(ct, "svc-manifiesto-grpc", "platform.manifest");
        var call = async () => await Client(token).ObtenerProductosHabilitadosAsync(new ObtenerProductosHabilitadosRequest(), Metadata(_empresa), cancellationToken: ct);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    // ── AC3: sin empresa ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinEmpresa_InvalidArgument()
    {
        var ct = TestContext.Current.CancellationToken;
        var call = async () => await Client(await ServiceTokenAsync(ct)).ListarUsuariosEmpresaAsync(new ListarUsuariosEmpresaRequest(), new Metadata(), cancellationToken: ct);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task ProductoDesconocido_InvalidArgument()
    {
        var ct = TestContext.Current.CancellationToken;
        var call = async () => await Client(await ServiceTokenAsync(ct)).ListarUsuariosEmpresaAsync(
            new ListarUsuariosEmpresaRequest { Producto = "flotas" }, Metadata(_empresa), cancellationToken: ct);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    // ── Solo en el puerto gRPC ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElServicioSoloRespondeEnSuPuerto_NoEnElDelRest()
    {
        var ct = TestContext.Current.CancellationToken;
        var call = async () => await Client(await ServiceTokenAsync(ct), port: 4025).ObtenerProductosHabilitadosAsync(
            new ObtenerProductosHabilitadosRequest(), Metadata(_empresa), cancellationToken: ct);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unimplemented);
    }

    [Fact]
    public async Task ExponeGrpcHealthEnSuPuerto()
    {
        var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}", new GrpcChannelOptions { HttpHandler = _host.Server.CreateHandler() });
        (await new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest(), cancellationToken: TestContext.Current.CancellationToken))
            .Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")] // lo que pasa el compose cuando CORE_IDENTITY_GRPC_PORT no está definida
    public void SinPuertoConfigurado_NoHayGrpc(string? port)
    {
        using var host = new IdentityHostTests.Host();
        using var sinGrpc = port is null ? host : host.WithWebHostBuilder(b => b.UseSetting("Identidad:GrpcPort", port));
        sinGrpc.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints)
            .Should().NotContain(e => (e.DisplayName ?? string.Empty).Contains("IdentidadService", StringComparison.Ordinal));
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private IdentidadService.IdentidadServiceClient Client(string? token, int port = GrpcPort)
    {
        var channel = GrpcChannel.ForAddress($"http://localhost:{port}", new GrpcChannelOptions { HttpHandler = _host.Server.CreateHandler() });
        var invoker = token is null
            ? channel.CreateCallInvoker()
            : channel.CreateCallInvoker().Intercept(metadata =>
            {
                metadata.Add("authorization", $"Bearer {token}");
                return metadata;
            });
        return new IdentidadService.IdentidadServiceClient(invoker);
    }

    private static Metadata Metadata(Guid tenant) => new() { { "x-flit-tenant-id", tenant.ToString() } };

    private async Task<string> ServiceTokenAsync(CancellationToken ct, string clientId = "svc-consultas-grpc", string scope = "platform.identidad.read")
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = Secret, ["scope"] = scope,
        }), ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString()!;
    }

    /// <summary>JWT de usuario de siempre, emitido por el mismo emisor que usa el login.</summary>
    private string UserToken()
    {
        using var scope = _host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenIssuer>().IssueToken(
            _activo, Email("activo"), _empresa, "Empresa gRPC", string.Empty, "COMPANY", "RENTING", false,
            [new UserRoleSnapshot(_rolTramites, $"GrpcRadicador-{_suffix}")], [], "flit").Token;
    }

    private string Email(string who) => $"grpc-{who}-{_suffix}@flit.local";

    private async Task SeedAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, code) in new[] { (_empresa, "A"), (_otraEmpresa, "B") })
        {
            db.Tenants.Add(new Tenant
            {
                Id = id, Code = $"IT-GRPC-{code}-{_suffix}", LegalName = $"Empresa gRPC {code}",
                TaxId = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}", TenantType = "RENTING", IsActive = true, CreatedAt = now,
            });
        }

        await db.SaveChangesAsync();

        foreach (var (id, who, status) in new[] { (_activo, "activo", "active"), (_pendiente, "pendiente", "pending"), (_deOtraEmpresa, "otra", "active"), (_asignacionBorrada, "borrada", "active") })
            db.Users.Add(new User { Id = id, Email = Email(who), DisplayName = who, Status = status, CreatedAt = now });

        db.Roles.Add(new Role { Id = _rolTramites, Code = $"GrpcRadicador-{_suffix}", Name = "Radicador gRPC", TargetEntityType = "COMPANY", ProductCode = "tramites", IsActive = true, CreatedAt = now });
        db.Roles.Add(new Role { Id = _rolPlataforma, Code = $"GrpcAdmin-{_suffix}", Name = "Admin gRPC", TargetEntityType = "COMPANY", ProductCode = "plataforma", IsActive = true, CreatedAt = now });
        await db.SaveChangesAsync();

        // La base crea las filas de productos al dar de alta la empresa: aquí solo se fija su estado.
        await SetProductAsync(db, _empresa, "tramites", true, now);
        await SetProductAsync(db, _otraEmpresa, "tramites", false, now);

        void Assign(Guid tenant, Guid user, Guid role, DateTimeOffset? deletedAt = null) =>
            db.UserRoleAssignments.Add(new UserRoleAssignment { Id = Guid.CreateVersion7(), TenantId = tenant, UserId = user, RoleId = role, AssignedAt = now, CreatedAt = now, DeletedAt = deletedAt });
        Assign(_empresa, _activo, _rolTramites);
        Assign(_empresa, _activo, _rolPlataforma);
        Assign(_empresa, _pendiente, _rolTramites);
        Assign(_otraEmpresa, _deOtraEmpresa, _rolTramites);
        Assign(_empresa, _asignacionBorrada, _rolTramites, now);
        await db.SaveChangesAsync();
    }

    private static async Task SetProductAsync(IdentityDbContext db, Guid tenant, string product, bool enabled, DateTimeOffset now)
    {
        var row = await db.Set<TenantProductEntity>().FirstOrDefaultAsync(r => r.TenantId == tenant && r.ProductCode == product);
        if (row is null)
            db.Set<TenantProductEntity>().Add(new TenantProductEntity { Id = Guid.CreateVersion7(), TenantId = tenant, ProductCode = product, Enabled = enabled, CreatedAt = now, UpdatedAt = now });
        else
            row.Enabled = enabled;
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Guid[] users = [_activo, _pendiente, _deOtraEmpresa, _asignacionBorrada];
        Guid[] tenants = [_empresa, _otraEmpresa];
        db.UserRoleAssignments.Where(a => users.Contains(a.UserId)).ExecuteDelete();
        db.Roles.Where(r => r.Id == _rolTramites || r.Id == _rolPlataforma).ExecuteDelete();
        db.Set<TenantProductEntity>().Where(r => tenants.Contains(r.TenantId)).ExecuteDelete();
        db.Users.Where(u => users.Contains(u.Id)).ExecuteDelete();
        db.Tenants.Where(t => tenants.Contains(t.Id)).ExecuteDelete();
    }

    /// <summary>core-identity con OIDC, el gRPC encendido y dos clientes de servicio de prueba.</summary>
    public sealed class Host : WebApplicationFactory<IdentityApiEntryPoint>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Suite:Oidc:Enabled", "true");
            builder.UseSetting("Suite:Hosts:Environment", "dev");
            builder.UseSetting("Identidad:GrpcPort", GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Suite:Oidc:ServiceClients:svc-consultas-grpc:Secret", Secret);
            builder.UseSetting("Suite:Oidc:ServiceClients:svc-consultas-grpc:Scopes:0", "platform.identidad.read");
            builder.UseSetting("Suite:Oidc:ServiceClients:svc-manifiesto-grpc:Secret", Secret);
            builder.UseSetting("Suite:Oidc:ServiceClients:svc-manifiesto-grpc:Scopes:0", "platform.manifest");
        }
    }
}
