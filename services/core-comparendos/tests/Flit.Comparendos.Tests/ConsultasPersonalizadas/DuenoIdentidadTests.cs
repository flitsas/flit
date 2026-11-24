using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Tests.Api;
using Flit.Identidad.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Comparendos.Tests.ConsultasPersonalizadas;

/// <summary>Reportes 2.0 — la verificación del dueño de un envío programado contra Identidad (<see cref="UsuariosEmpresaIdentidad"/>).</summary>
public sealed class DuenoIdentidadTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();

    /// <summary>Identidad falsa: responde lo que se le diga a ObtenerUsuario y anota con qué se le preguntó.</summary>
    private sealed class IdentidadFalsa(Func<ObtenerUsuarioRequest, ObtenerUsuarioResponse> responder) : IdentidadService.IdentidadServiceClient
    {
        public List<(ObtenerUsuarioRequest Pedido, string? Tenant)> Pedidos { get; } = [];

        public override AsyncUnaryCall<ObtenerUsuarioResponse> ObtenerUsuarioAsync(ObtenerUsuarioRequest request, Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            Pedidos.Add((request, headers?.GetValue("x-flit-tenant-id")));
            return new AsyncUnaryCall<ObtenerUsuarioResponse>(Task.Run(() => responder(request)), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }
    }

    private static Task<bool?> PreguntarAsync(Func<ObtenerUsuarioRequest, ObtenerUsuarioResponse> responder, out IdentidadFalsa cliente)
    {
        cliente = new IdentidadFalsa(responder);
        return new UsuariosEmpresaIdentidad(cliente, NullLogger<UsuariosEmpresaIdentidad>.Instance)
            .SigueEnLaEmpresaAsync(Tenant, Usuario, TestContext.Current.CancellationToken);
    }

    private static ObtenerUsuarioResponse Con(EstadoUsuario estado, params string[] roles)
    {
        var u = new Flit.Identidad.Grpc.V1.Usuario { Id = Usuario.ToString(), Email = "dueno@empresa.test", Estado = estado };
        u.Roles.AddRange(roles.Select(r => new Rol { Id = Guid.NewGuid().ToString(), Codigo = r, Producto = "comparendos" }));
        return new ObtenerUsuarioResponse { Usuario = u };
    }

    [Fact]
    public async Task Vigente_EsSi_YPreguntaPorEseUsuarioEnEsaEmpresa()
    {
        (await PreguntarAsync(_ => Con(EstadoUsuario.Activo, "admin_comparendos"), out var cliente)).Should().BeTrue();
        cliente.Pedidos.Should().ContainSingle().Which.Should().Match<(ObtenerUsuarioRequest Pedido, string? Tenant)>(p =>
            p.Pedido.UsuarioId == Usuario.ToString() && p.Tenant == Tenant.ToString());
    }

    [Fact]
    public async Task VigenteSinElRolPorDefecto_ConElPermisoIndividual_EsSi()
    {
        // El caso que la deducción por roles por defecto pausaba por error: un operador con el permiso dado a él.
        (await PreguntarAsync(_ => Con(EstadoUsuario.Activo, "operador_comparendos"), out _)).Should().BeTrue();
        (await PreguntarAsync(_ => Con(EstadoUsuario.Activo, "rol_personalizado"), out _)).Should().BeTrue();
    }

    [Fact]
    public async Task FueraDeLaEmpresa_OInactivo_EsNo()
    {
        (await PreguntarAsync(_ => throw new RpcException(new Status(StatusCode.NotFound, "El usuario no existe en esta empresa.")), out _)).Should().BeFalse();
        (await PreguntarAsync(_ => Con(EstadoUsuario.Inactivo, "admin_comparendos"), out _)).Should().BeFalse();
    }

    [Fact]
    public async Task SiIdentidadNoResponde_EsNoSe_NoUnNo()
    {
        (await PreguntarAsync(_ => throw new RpcException(new Status(StatusCode.Unavailable, "caído")), out _)).Should().BeNull();
        (await PreguntarAsync(_ => throw new RpcException(new Status(StatusCode.DeadlineExceeded, "lento")), out _)).Should().BeNull();
    }
}

/// <summary>Con Identidad configurada (DEV, QA, PDN) se resuelve la implementación real, no la que por defecto no bloquea.</summary>
public sealed class ApiConIdentidad : ApiDePrueba
{
    protected override IDictionary<string, string?> Configuracion() => new Dictionary<string, string?> { ["Comparendos:Identidad:Address"] = "http://127.0.0.1:1" };
}

public sealed class DuenoIdentidadDiTests(ApiConIdentidad api) : IClassFixture<ApiConIdentidad>
{
    [Fact]
    public async Task ConIdentidadConfigurada_SeResuelveUsuariosEmpresaIdentidad()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await using var scope = api.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IUsuariosEmpresa>().Should().BeOfType<UsuariosEmpresaIdentidad>();
    }
}
