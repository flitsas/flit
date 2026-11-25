using System.Collections.Concurrent;
using Flit.Identidad.Grpc.V1;
using Flit.Impuestos.Api.Plataforma;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Impuestos.Tests.Plataforma;

/// <summary>
/// HU #14694 (corrección 4) — nombre de quien actúa para <c>creada_por_nombre</c> y <c>cerrada_por_nombre</c>: el claim <c>name</c>
/// si el token lo trae; si no, Identidad (<c>ObtenerUsuario</c> con <c>x-flit-tenant-id</c> de la compañía del usuario), con caché
/// por (compañía, usuario) de <see cref="ResolvedorNombreActor.Vigencia"/>. Si Identidad no da el nombre (no conectada, NOT_FOUND,
/// plazo vencido o nombre vacío), el correo, con un log Debug que no lleva el correo.
/// </summary>
/// <example>
/// <code>
/// var nombre = await resolvedor.ResolverAsync(empresa, usuario, nombreDelToken: null, correo: "ana@empresa.test", ct);
/// </code>
/// </example>
public sealed class ResolvedorNombreActorTests
{
    private const string Correo = "ana@empresa.test";

    [Fact]
    public async Task Con_el_claim_name_lo_usa_sin_preguntar_a_Identidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadUsuarios { Nombre = "Otro Nombre" };
        var resolvedor = identidad.Resolvedor(new LogsConNivel());

        var nombre = await resolvedor.ResolverAsync(Guid.NewGuid(), Guid.NewGuid(), "  Ana Token  ", Correo, ct);

        nombre.Should().Be("Ana Token");
        identidad.Llamadas.Should().Be(0, "el token ya trae el nombre");
    }

    [Fact]
    public async Task Sin_claim_pregunta_a_Identidad_con_la_compania_y_el_usuario()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadUsuarios { Nombre = "  Ana Pérez " };
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();

        var nombre = await identidad.Resolvedor(new LogsConNivel()).ResolverAsync(empresa, usuario, null, Correo, ct);

        nombre.Should().Be("Ana Pérez");
        identidad.Llamadas.Should().Be(1);
        identidad.UltimaEmpresa.Should().Be(empresa.ToString(), "x-flit-tenant-id de la compañía");
        identidad.UltimoUsuario.Should().Be(usuario.ToString());
    }

    public static TheoryData<string> Fallos => ["UNAVAILABLE", "NOT_FOUND", "DEADLINE", "VACIO", "RED"];

    [Theory]
    [MemberData(nameof(Fallos))]
    public async Task Si_Identidad_falla_usa_el_correo_y_deja_un_Debug_sin_el_correo(string fallo)
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadUsuarios
        {
            Nombre = fallo == "VACIO" ? "   " : "No se usa",
            Error = fallo switch
            {
                "UNAVAILABLE" => new RpcException(new Status(StatusCode.Unavailable, "caída")),
                "NOT_FOUND" => new RpcException(new Status(StatusCode.NotFound, "no existe")),
                "DEADLINE" => new RpcException(new Status(StatusCode.DeadlineExceeded, "plazo")),
                "RED" => new HttpRequestException("sin token de servicio"),
                _ => null,
            },
        };
        var logs = new LogsConNivel();

        var nombre = await identidad.Resolvedor(logs).ResolverAsync(Guid.NewGuid(), Guid.NewGuid(), null, Correo, ct);

        nombre.Should().Be(Correo, "respaldo: el correo");
        logs.Lineas.Should().ContainSingle().Which.Nivel.Should().Be(LogLevel.Debug);
        logs.Lineas.Should().OnlyContain(l => !l.Mensaje.Contains(Correo, StringComparison.OrdinalIgnoreCase), "el log no lleva el correo");
    }

    [Fact]
    public async Task Sin_Identidad_conectada_usa_el_correo_sin_llamar()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsConNivel();
        var resolvedor = new ResolvedorNombreActor(null, new MemoryCache(new MemoryCacheOptions()), logs.Logger<ResolvedorNombreActor>());

        var nombre = await resolvedor.ResolverAsync(Guid.NewGuid(), Guid.NewGuid(), null, Correo, ct);

        nombre.Should().Be(Correo);
        logs.Lineas.Should().ContainSingle().Which.Nivel.Should().Be(LogLevel.Debug);
        logs.Lineas.Should().OnlyContain(l => !l.Mensaje.Contains(Correo, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sin_compania_usa_el_correo_sin_llamar()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadUsuarios { Nombre = "Ana Pérez" };

        var nombre = await identidad.Resolvedor(new LogsConNivel()).ResolverAsync(null, Guid.NewGuid(), null, Correo, ct);

        nombre.Should().Be(Correo);
        identidad.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task El_nombre_resuelto_queda_en_cache_por_compania_y_usuario_y_el_fallo_no()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadUsuarios { Error = new RpcException(new Status(StatusCode.Unavailable, "caída")) };
        var resolvedor = identidad.Resolvedor(new LogsConNivel());
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();

        (await resolvedor.ResolverAsync(empresa, usuario, null, Correo, ct)).Should().Be(Correo);
        identidad.Error = null;
        identidad.Nombre = "Ana Pérez";
        (await resolvedor.ResolverAsync(empresa, usuario, null, Correo, ct)).Should().Be("Ana Pérez", "el fallo no quedó en caché");
        identidad.Nombre = "Nombre Nuevo";
        (await resolvedor.ResolverAsync(empresa, usuario, null, Correo, ct)).Should().Be("Ana Pérez", "el nombre resuelto sí queda en caché");
        (await resolvedor.ResolverAsync(Guid.NewGuid(), usuario, null, Correo, ct)).Should().Be("Nombre Nuevo", "la clave incluye la compañía");
        identidad.Llamadas.Should().Be(3);
        ResolvedorNombreActor.Vigencia.Should().Be(TimeSpan.FromMinutes(10));
    }
}

/// <summary>Identidad de prueba por gRPC para <c>ObtenerUsuario</c>: responde <see cref="Nombre"/> o lanza <see cref="Error"/>.</summary>
internal sealed class IdentidadUsuarios : CallInvoker
{
    public Exception? Error { get; set; }

    public string Nombre { get; set; } = "Ana Pérez";

    public int Llamadas { get; private set; }

    public string? UltimaEmpresa { get; private set; }

    public string? UltimoUsuario { get; private set; }

    public IdentidadService.IdentidadServiceClient Cliente() => new(this);

    public ResolvedorNombreActor Resolvedor(LogsConNivel logs) =>
        new(Cliente(), new MemoryCache(new MemoryCacheOptions()), logs.Logger<ResolvedorNombreActor>());

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Llamadas++;
        UltimaEmpresa = options.Headers?.GetValue("x-flit-tenant-id");
        UltimoUsuario = (request as ObtenerUsuarioRequest)?.UsuarioId;
        var respuesta = Error is { } error
            ? Task.FromException<TResponse>(error)
            : Task.FromResult((TResponse)(object)new ObtenerUsuarioResponse { Usuario = new Usuario { Id = UltimoUsuario ?? string.Empty, Nombre = Nombre } });
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

/// <summary>Logs con su nivel, para comprobar el Debug y que el mensaje no lleve PII.</summary>
internal sealed class LogsConNivel
{
    private readonly ConcurrentQueue<(LogLevel Nivel, string Mensaje)> _lineas = new();

    public IReadOnlyList<(LogLevel Nivel, string Mensaje)> Lineas => [.. _lineas];

    public ILogger<T> Logger<T>() => new Registro<T>(_lineas);

    private sealed class Registro<T>(ConcurrentQueue<(LogLevel, string)> lineas) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lineas.Enqueue((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception)));
    }
}
