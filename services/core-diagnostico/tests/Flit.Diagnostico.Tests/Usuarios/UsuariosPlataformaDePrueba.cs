using System.Collections.Concurrent;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Diagnostico.Tests.Usuarios;

/// <summary>Una llamada que recibió el doble: método gRPC, petición y metadata (para verificar empresa y actor).</summary>
public sealed record LlamadaUsuarios(string Metodo, IMessage Peticion, Metadata Cabeceras)
{
    public string? Empresa => Cabeceras.GetValue(PlatformServiceCallInterceptor.TenantMetadata);

    public string? Actor => Cabeceras.GetValue("x-flit-actor-id");
}

/// <summary>
/// Doble del cliente gRPC de <c>flit.platform.v1.UsuariosProductoService</c> (HU #14587, ADR-0089): lo consume el adaptador real de
/// core-diagnostico sin core-api. Graba cada llamada con su metadata y responde <see cref="Lista"/> o <see cref="Fila"/>; con
/// <see cref="Falla"/> lanza esa excepción en toda llamada. Datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// var falso = new UsuariosProductoFalso();
/// falso.Falla = PlatformRpcErrors.ToRpcException("ULTIMO_ADMINISTRADOR", "x", StatusCode.FailedPrecondition);
/// await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
/// </code>
/// </example>
public sealed class UsuariosProductoFalso : UsuariosProductoService.UsuariosProductoServiceClient
{
    public ConcurrentQueue<LlamadaUsuarios> Llamadas { get; } = new();

    public ListarUsuariosProductoResponse Lista { get; set; } = new();

    public UsuarioProducto Fila { get; set; } = UsuariosDePrueba.Fila(Guid.NewGuid());

    public Exception? Falla { get; set; }

    /// <summary>Registra este doble y el de conteos (<paramref name="conteo"/> o uno vacío) antes que los del servicio.</summary>
    public void Registrar(WebApplicationBuilder b, UsuariosConteoFalso? conteo = null)
    {
        ArgumentNullException.ThrowIfNull(b);
        b.Services.AddSingleton<UsuariosProductoService.UsuariosProductoServiceClient>(this);
        b.Services.AddSingleton<UsuariosProductoConteoService.UsuariosProductoConteoServiceClient>(conteo ?? new UsuariosConteoFalso());
    }

    public void Registrar(WebApplicationBuilder b) => Registrar(b, null);

    public override AsyncUnaryCall<ListarUsuariosProductoResponse> ListarUsuariosProductoAsync(ListarUsuariosProductoRequest request, CallOptions options) =>
        Responder(nameof(ListarUsuariosProductoAsync), request, options, () => Lista);

    public override AsyncUnaryCall<InvitarUsuarioProductoResponse> InvitarUsuarioProductoAsync(InvitarUsuarioProductoRequest request, CallOptions options) =>
        Responder(nameof(InvitarUsuarioProductoAsync), request, options, () => new InvitarUsuarioProductoResponse());

    public override AsyncUnaryCall<CambiarRolProductoResponse> CambiarRolProductoAsync(CambiarRolProductoRequest request, CallOptions options) =>
        Responder(nameof(CambiarRolProductoAsync), request, options, () => new CambiarRolProductoResponse { Usuario = Fila });

    public override AsyncUnaryCall<SuspenderEnProductoResponse> SuspenderEnProductoAsync(SuspenderEnProductoRequest request, CallOptions options) =>
        Responder(nameof(SuspenderEnProductoAsync), request, options, () => new SuspenderEnProductoResponse { Usuario = Fila });

    public override AsyncUnaryCall<ReactivarEnProductoResponse> ReactivarEnProductoAsync(ReactivarEnProductoRequest request, CallOptions options) =>
        Responder(nameof(ReactivarEnProductoAsync), request, options, () => new ReactivarEnProductoResponse { Usuario = Fila });

    private AsyncUnaryCall<T> Responder<T>(string metodo, IMessage request, CallOptions options, Func<T> respuesta)
    {
        Llamadas.Enqueue(new LlamadaUsuarios(metodo.Replace("Async", string.Empty, StringComparison.Ordinal), request, options.Headers ?? []));
        var tarea = Falla is { } falla ? Task.FromException<T>(falla) : Task.FromResult(respuesta());
        return new AsyncUnaryCall<T>(tarea, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }
}

/// <summary>Doble de <c>flit.platform.v1.UsuariosProductoConteoService</c>: responde <see cref="Conteos"/> o lanza <see cref="Falla"/>.</summary>
public sealed class UsuariosConteoFalso : UsuariosProductoConteoService.UsuariosProductoConteoServiceClient
{
    public ConcurrentQueue<LlamadaUsuarios> Llamadas { get; } = new();

    public List<ConteoEmpresa> Conteos { get; } = [];

    public Exception? Falla { get; set; }

    public override AsyncUnaryCall<ContarPorEmpresaResponse> ContarPorEmpresaAsync(ContarPorEmpresaRequest request, CallOptions options)
    {
        Llamadas.Enqueue(new LlamadaUsuarios("ContarPorEmpresa", request, options.Headers ?? []));
        var r = new ContarPorEmpresaResponse();
        r.Conteos.AddRange(Conteos);
        var tarea = Falla is { } falla ? Task.FromException<ContarPorEmpresaResponse>(falla) : Task.FromResult(r);
        return new AsyncUnaryCall<ContarPorEmpresaResponse>(tarea, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }
}

/// <summary>Datos y tokens de prueba del módulo Usuarios (ficticios, Ley 1581).</summary>
public static class UsuariosDePrueba
{
    public const string Gestionar = "diagnostico.usuarios.gestionar";
    public const string Correo = "persona.ficticia@empresa.test";

    /// <summary>Token de un admin_diagnostico de <paramref name="empresa"/> con el permiso nuevo.</summary>
    public static string Admin(ApiDePrueba api, Guid empresa, string? sujeto = null) =>
        api.Token(empresa: empresa, roles: ["admin_diagnostico"], permisos: [Gestionar, "diagnostico.diagnosticos.ver"], tipoEmpresa: "CLIENTE", sujeto: sujeto);

    /// <summary>Token de un usuario_diagnostico (sin el permiso).</summary>
    public static string Usuario(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, roles: ["usuario_diagnostico"], permisos: ["diagnostico.diagnosticos.ver", "diagnostico.diagnosticos.crear"], tipoEmpresa: "CLIENTE");

    /// <summary>Token de un SuperAdmin de una compañía cualquiera.</summary>
    public static string SuperAdmin(ApiDePrueba api, Guid? empresa = null, string? tipoEmpresa = "FLIT", string? sujeto = null) =>
        api.Token(empresa: empresa, roles: ["SuperAdmin"], permisos: [], tipoEmpresa: tipoEmpresa, sujeto: sujeto);

    public static UsuarioProducto Fila(
        Guid id,
        string rol = "usuario_diagnostico",
        EstadoUsuarioProducto estado = EstadoUsuarioProducto.Activo,
        TipoFila tipo = TipoFila.Usuario,
        MotivoSinAcciones motivo = MotivoSinAcciones.Unspecified,
        params AccionUsuario[] acciones)
    {
        var fila = new UsuarioProducto
        {
            Id = id.ToString(), Tipo = tipo, Nombre = "Persona Ficticia", Email = Correo, RolCodigo = rol, Estado = estado, MotivoSinAcciones = motivo,
        };
        fila.Acciones.AddRange(acciones);
        return fila;
    }

    public static RpcException Error(string codigo, StatusCode estado) => PlatformRpcErrors.ToRpcException(codigo, "rechazo de prueba", estado);
}

/// <summary>Proveedor de logs que guarda cada mensaje ya formateado y los valores estructurados (para buscar PII).</summary>
public sealed class CapturaDeLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lineas = new();

    public IReadOnlyCollection<string> Lineas => _lineas;

    public ILogger CreateLogger(string categoryName) => new Registro(_lineas);

    public void Registrar(WebApplicationBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);
        b.Logging.AddProvider(this);
        b.Logging.AddFilter<CapturaDeLogs>(null, LogLevel.Trace);
    }

    public void Dispose()
    {
    }

    private sealed class Registro(ConcurrentQueue<string> lineas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var valores = state is IEnumerable<KeyValuePair<string, object?>> pares ? string.Join('|', pares.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
            lineas.Enqueue($"{formatter(state, exception)} {valores} {exception}");
        }
    }
}
