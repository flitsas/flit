using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Identidad.Grpc.V1;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Tests.Api;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Impuestos.Tests.Plataforma;

/// <summary>
/// HU #14694 (M05, F6-CF2) — bloqueo del producto: toda ruta bajo <see cref="Program.Prefijo"/> pregunta si la compañía del
/// token tiene Impuestos activo y, si no o si no se puede saber (Identidad caída, sin Identidad configurada), responde 403
/// <c>PRODUCTO_NO_HABILITADO</c>. Sin un permiso de Impuestos (sin rol del producto) la sesión tampoco abre. Las rutas de salud
/// no pasan por el guard.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-impuestos/tests/Flit.Impuestos.Tests --no-build --filter FullyQualifiedName~GuardSuscripcionTests
/// </code>
/// </example>
public sealed class GuardSuscripcionTests
{
    private const string Sesion = Program.Prefijo + "/sesion";

    private static readonly string[] PermisosGestor = [Permisos.DashboardVer, Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear];

    [Fact]
    public async Task Producto_suspendido_responde_403_con_sesion_valida()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var empresa = Guid.NewGuid();
        api.Productos.Apagar(empresa);
        using var cliente = api.Cliente(api.Token(empresa: empresa, permisos: PermisosGestor));

        var sesion = await cliente.GetAsync(Sesion, ct);
        var otraRuta = await cliente.GetAsync(Program.Prefijo + "/liquidaciones", ct);

        sesion.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Impuestos suspendido para la compañía");
        (await Codigo(sesion, ct)).Should().Be(GuardSuscripcion.CodigoNoHabilitado);
        otraRuta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "cualquier ruta de negocio, también las que aún no existen");
        api.Productos.Consultas(empresa).Should().BeGreaterThan(0, "el guard pregunta por la compañía del token");
    }

    [Fact]
    public async Task Sin_el_producto_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.Productos.Todas = false;
        var empresa = Guid.NewGuid();

        var respuesta = await api.Cliente(api.Token(empresa: empresa, permisos: PermisosGestor)).GetAsync(Sesion, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Codigo(respuesta, ct)).Should().Be(GuardSuscripcion.CodigoNoHabilitado);
        api.Productos.Consultas(empresa).Should().Be(1);
    }

    [Fact]
    public async Task Sin_rol_ni_permisos_de_impuestos_responde_403_aunque_la_compania_tenga_el_producto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        var otroProducto = await api.Cliente(api.Token(roles: ["usuario_diagnostico"], permisos: ["diagnostico.diagnosticos.ver", "users.read"])).GetAsync(Sesion, ct);
        var sinToken = await api.Anonimo().GetAsync(Sesion, ct);
        var otraAudiencia = await api.Cliente(api.Token(audiencia: "diagnostico", permisos: PermisosGestor)).GetAsync(Sesion, ct);

        otroProducto.StatusCode.Should().Be(HttpStatusCode.Forbidden, "un rol de otro producto no abre la sesión de Impuestos");
        sinToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        otraAudiencia.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "el token de otro producto no es para Impuestos");
    }

    [Fact]
    public async Task Producto_activo_deja_pasar_y_pregunta_en_cada_peticion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Productos.Todas = false;
        var empresa = Guid.NewGuid();
        api.Productos.Encender(empresa);
        using var cliente = api.Cliente(api.Token(empresa: empresa, permisos: PermisosGestor));

        var primera = await cliente.GetAsync(Sesion, ct);
        var segunda = await cliente.GetAsync(Sesion, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        (await primera.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(empresa);
        api.Productos.Consultas(empresa).Should().Be(2, "la suscripción se verifica en cada petición de negocio");
    }

    [Fact]
    public async Task Identidad_caida_niega_fail_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba { Error = new RpcException(new Status(StatusCode.Unavailable, "identidad caída")) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            b.Services.RemoveAll<IProductosHabilitados>();
            b.Services.AddSingleton<IProductosHabilitados>(identidad.Productos());
        });

        var respuesta = await api.Cliente(api.Token(permisos: PermisosGestor)).GetAsync(Sesion, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "si no se puede saber, la respuesta es no");
        (await Codigo(respuesta, ct)).Should().Be(GuardSuscripcion.CodigoNoHabilitado);
        identidad.Llamadas.Should().Be(1);
    }

    [Fact]
    public async Task La_caida_no_se_cachea_y_la_respuesta_de_identidad_si()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba { Error = new RpcException(new Status(StatusCode.DeadlineExceeded, "sin respuesta")) };
        var productos = identidad.Productos();
        var empresa = Guid.NewGuid();

        (await productos.ImpuestosHabilitadoAsync(empresa, ct)).Should().BeFalse("plazo vencido = no");
        identidad.Error = null;
        identidad.Respuesta = ["tramites", ProductosHabilitadosIdentidad.Producto];
        (await productos.ImpuestosHabilitadoAsync(empresa, ct)).Should().BeTrue("la negación por caída no queda en caché");
        identidad.Error = new RpcException(new Status(StatusCode.Unavailable, "otra vez caída"));
        (await productos.ImpuestosHabilitadoAsync(empresa, ct)).Should().BeTrue("la respuesta vigente sí queda en caché (caché corta)");
        identidad.Llamadas.Should().Be(2);
        identidad.UltimaEmpresa.Should().Be(empresa.ToString(), "se pregunta por la compañía del token, en la metadata x-flit-tenant-id");
        ProductosHabilitadosIdentidad.Producto.Should().Be("impuestos");
    }

    [Fact]
    public async Task Respuesta_sin_impuestos_o_error_inesperado_niega()
    {
        var ct = TestContext.Current.CancellationToken;
        var apagado = new IdentidadDePrueba { Respuesta = ["tramites", "diagnostico"] };
        var roto = new IdentidadDePrueba { Error = new HttpRequestException("no se pudo obtener el token de servicio") };

        (await apagado.Productos().ImpuestosHabilitadoAsync(Guid.NewGuid(), ct)).Should().BeFalse("tener Diagnóstico no habilita Impuestos");
        (await roto.Productos().ImpuestosHabilitadoAsync(Guid.NewGuid(), ct)).Should().BeFalse("cualquier fallo al preguntar es no");
    }

    [Fact]
    public async Task Sin_identidad_configurada_niega_por_defecto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b => b.Services.RemoveAll<IProductosHabilitados>());

        var respuesta = await api.Cliente(api.Token(permisos: PermisosGestor)).GetAsync(Sesion, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin a quién preguntar, Impuestos está apagado");
    }

    [Fact]
    public async Task Health_no_pasa_por_el_guard()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.Productos.Todas = false;

        var salud = await api.Anonimo().GetAsync("/health", ct);
        var negocio = await api.Cliente(api.Token(permisos: PermisosGestor)).GetAsync(Sesion, ct);

        salud.StatusCode.Should().Be(HttpStatusCode.OK);
        negocio.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        api.Productos.ConsultasTotales.Should().Be(1, "solo la ruta de negocio pregunta por la suscripción");
    }

    private static async Task<string?> Codigo(HttpResponseMessage respuesta, CancellationToken ct) =>
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString();
}

/// <summary>Identidad de prueba por gRPC: responde la lista de productos o lanza el error dado, y cuenta las llamadas.</summary>
internal sealed class IdentidadDePrueba : CallInvoker
{
    public Exception? Error { get; set; }

    public IReadOnlyList<string> Respuesta { get; set; } = [ProductosHabilitadosIdentidad.Producto];

    public int Llamadas { get; private set; }

    public string? UltimaEmpresa { get; private set; }

    public ProductosHabilitadosIdentidad Productos() =>
        new(new IdentidadService.IdentidadServiceClient(this), new MemoryCache(new MemoryCacheOptions()), NullLogger<ProductosHabilitadosIdentidad>.Instance);

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Llamadas++;
        UltimaEmpresa = options.Headers?.GetValue("x-flit-tenant-id");
        var respuesta = Error is { } error
            ? Task.FromException<TResponse>(error)
            : Task.FromResult((TResponse)(object)new ObtenerProductosHabilitadosResponse { Productos = { Respuesta } });
        return new AsyncUnaryCall<TResponse>(respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        throw new NotSupportedException();

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        throw new NotSupportedException();

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException();

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException();
}
