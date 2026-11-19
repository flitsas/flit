using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using Flit.Identidad.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #13726 (F00.1-H4), escenarios 1 a 3 — guard de suscripción por petición: toda ruta bajo
/// <see cref="Program.Prefijo"/> pregunta si la compañía del token tiene Diagnóstico activo y, si no o si no se puede
/// saber (Identidad caída, sin Identidad configurada), responde 403. Las rutas de salud no pasan por el guard.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~GuardSuscripcionTests
/// </code>
/// </example>
public sealed class GuardSuscripcionTests
{
    private const string Sesion = Program.Prefijo + "/sesion";

    private static readonly string[] PermisosUsuario =
    [
        Permisos.DiagnosticosCrear, Permisos.DiagnosticosVer, Permisos.ReportesDescargar, Permisos.DerivacionCrear, Permisos.ConfiguracionVer,
    ];

    // Escenario 1 — sesión válida y permisos de Diagnóstico, pero la compañía tiene Diagnóstico suspendido.
    [Fact]
    public async Task Suscripcion_suspendida_responde_403_con_sesion_valida()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var empresa = Guid.NewGuid();
        api.Productos.Apagar(empresa);
        using var cliente = api.Cliente(api.Token(empresa: empresa, permisos: PermisosUsuario, roles: ["usuario_diagnostico"]));

        var sesion = await cliente.GetAsync(Sesion, ct);
        var otraRuta = await cliente.GetAsync(Program.Prefijo + "/diagnosticos", ct);

        sesion.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Diagnóstico suspendido para la compañía");
        (await Codigo(sesion, ct)).Should().Be(GuardSuscripcion.CodigoNoHabilitado);
        otraRuta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "cualquier ruta de negocio, también las que aún no existen");
        api.Productos.Consultas(empresa).Should().BeGreaterThan(0, "el guard pregunta por la compañía del token");
    }

    // Escenario 1 — la compañía nunca tuvo la suscripción.
    [Fact]
    public async Task Suscripcion_inexistente_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.Productos.Todas = false;
        var empresa = Guid.NewGuid();

        var respuesta = await api.Cliente(api.Token(empresa: empresa, permisos: PermisosUsuario)).GetAsync(Sesion, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Codigo(respuesta, ct)).Should().Be(GuardSuscripcion.CodigoNoHabilitado);
        api.Productos.Consultas(empresa).Should().Be(1);
    }

    // Escenario 2 — compañía activa y usuario con rol de Diagnóstico: GET /sesion responde 200 con el mismo token.
    [Fact]
    public async Task Suscripcion_activa_deja_pasar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.Productos.Todas = false;
        var empresa = Guid.NewGuid();
        api.Productos.Encender(empresa);

        var respuesta = await api.Cliente(api.Token(empresa: empresa, permisos: PermisosUsuario, roles: ["usuario_diagnostico"])).GetAsync(Sesion, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, "sin pedir otro login");
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(empresa);
        api.Productos.Consultas(empresa).Should().Be(1, "la suscripción se verifica en cada petición de negocio");
    }

    // Escenario 3 — gRPC de Identidad caído y sin entrada en caché: 403 (fail-closed), con la implementación real.
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
        var empresa = Guid.NewGuid();

        var respuesta = await api.Cliente(api.Token(empresa: empresa, permisos: PermisosUsuario)).GetAsync(Sesion, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "si no se puede saber, la respuesta es no");
        (await Codigo(respuesta, ct)).Should().Be(GuardSuscripcion.CodigoNoHabilitado);
        identidad.Llamadas.Should().Be(1);
    }

    // Escenario 3 — la caída no se guarda en caché: cuando Identidad vuelve, la siguiente petición sí pasa.
    [Fact]
    public async Task Identidad_caida_no_se_cachea_y_al_volver_responde_lo_que_dice_identidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba { Error = new RpcException(new Status(StatusCode.DeadlineExceeded, "sin respuesta")) };
        var productos = identidad.Productos();
        var empresa = Guid.NewGuid();

        (await productos.DiagnosticoHabilitadoAsync(empresa, ct)).Should().BeFalse("plazo vencido = no");
        identidad.Error = null;
        identidad.Respuesta = ["tramites", ProductosHabilitadosIdentidad.Producto];
        (await productos.DiagnosticoHabilitadoAsync(empresa, ct)).Should().BeTrue("la negación por caída no queda en caché");
        identidad.Error = new RpcException(new Status(StatusCode.Unavailable, "otra vez caída"));
        (await productos.DiagnosticoHabilitadoAsync(empresa, ct)).Should().BeTrue("la respuesta vigente sí queda en caché (caché corta)");
        identidad.Llamadas.Should().Be(2);
        identidad.UltimaEmpresa.Should().Be(empresa.ToString(), "se pregunta por la compañía del token, en la metadata x-flit-tenant-id");
    }

    // Escenario 1 y 3 — Identidad responde sin diagnostico (apagado) o falla con un error que no es gRPC: no.
    [Fact]
    public async Task Respuesta_sin_diagnostico_o_error_inesperado_niega()
    {
        var ct = TestContext.Current.CancellationToken;
        var apagado = new IdentidadDePrueba { Respuesta = ["tramites", "comparendos"] };
        var roto = new IdentidadDePrueba { Error = new HttpRequestException("no se pudo obtener el token de servicio") };

        (await apagado.Productos().DiagnosticoHabilitadoAsync(Guid.NewGuid(), ct)).Should().BeFalse();
        (await roto.Productos().DiagnosticoHabilitadoAsync(Guid.NewGuid(), ct)).Should().BeFalse("cualquier fallo al preguntar es no");
    }

    // Escenario 3 — sin Identidad configurada (Diagnostico:Identidad:Address vacío) el servicio niega por defecto.
    [Fact]
    public async Task Sin_identidad_configurada_niega_por_defecto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b => b.Services.RemoveAll<IProductosHabilitados>());

        var respuesta = await api.Cliente(api.Token(permisos: PermisosUsuario)).GetAsync(Sesion, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin a quién preguntar, Diagnóstico está apagado");
    }

    // Las rutas de salud no pasan por el guard: con el producto apagado y sin token siguen en 200.
    [Fact]
    public async Task Health_no_pasa_por_el_guard()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.Productos.Todas = false;

        var salud = await api.Anonimo().GetAsync("/health", ct);
        var negocio = await api.Cliente(api.Token(permisos: PermisosUsuario)).GetAsync(Sesion, ct);

        salud.StatusCode.Should().Be(HttpStatusCode.OK);
        negocio.StatusCode.Should().Be(HttpStatusCode.Forbidden, "la misma API sí niega la ruta de negocio");
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
