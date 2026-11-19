using System.Security.Cryptography;
using System.Text;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;

/// <summary>
/// <c>Diagnostico:Ia:Origen=plataforma</c> (ADR-0091 §2.4, §2.7, §3.2): pide a core-api <c>ObtenerConfiguracionIa</c> para
/// <see cref="ToolCode"/> con un nonce nuevo por llamada, valida el eco (nonce, kid, herramienta) en tiempo constante y el reloj
/// (±120 s), recompone el AAD y abre el sobre con la privada. La credencial vive solo en memoria, vigente
/// <c>min(ttl_segundos, 300 s)</c>; la negativa (<c>disponible=false</c> y rechazos permanentes) 60 s; una sola llamada en vuelo.
/// <para>
/// Mapeo (SP-12): core-api caído, token, eco, reloj o tag → <see cref="FallaCredencial.Tecnica"/> (reintento de la cola, sin caché);
/// <c>disponible=false</c>, cliente o herramienta no permitidos, pedido inválido, kid desconocido, <c>alg</c> desconocido, proveedor
/// fuera de la allowlist local (<see cref="ProveedorPermitido"/>) o modelo fuera de <c>ModelosPermitidos</c> (SP-14 = a) →
/// <see cref="FallaCredencial.NoConfigurado"/>. Nunca registra la llave, el sobre, la respuesta ni el mensaje de una excepción.
/// </para>
/// </summary>
public sealed partial class FuenteCredencialPlataforma : IFuenteCredencialRedactor, IDisposable
{
    /// <summary>Única herramienta que svc-diagnostico puede pedir.</summary>
    public const string ToolCode = "diagnostico_dictamen";

    /// <summary>Allowlist local de proveedores (P1 = a): los demás se saltan aunque vengan en la cola.</summary>
    public const string ProveedorPermitido = "anthropic";

    public const string ReasonKidDesconocido = "CLAVE_DESTINATARIO_DESCONOCIDA";

    public static readonly TimeSpan VigenciaMaxima = TimeSpan.FromSeconds(300);

    public static readonly TimeSpan VigenciaNegativa = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan DesfaseMaximo = TimeSpan.FromSeconds(120);

    private readonly Func<ConfiguracionIaService.ConfiguracionIaServiceClient> _cliente;
    private readonly ClavesPrivadasIa _claves;
    private readonly OpcionesIa _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<FuenteCredencialPlataforma> _log;
    private readonly SemaphoreSlim _vuelo = new(1, 1);
    private Instantanea? _instantanea;
    private long? _ultimaRowVersion;

    public FuenteCredencialPlataforma(
        Func<ConfiguracionIaService.ConfiguracionIaServiceClient> cliente,
        ClavesPrivadasIa claves,
        IOptions<OpcionesIa> opciones,
        TimeProvider reloj,
        ILogger<FuenteCredencialPlataforma> log)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(claves);
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentNullException.ThrowIfNull(log);
        _cliente = cliente;
        _claves = claves;
        _opciones = opciones.Value;
        _reloj = reloj;
        _log = log;
    }

    public async Task<ResultadoCredencial> ObtenerAsync(CancellationToken ct)
    {
        if (Vigente() is { } guardada)
            return guardada;

        await _vuelo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Vigente() is { } otra)
                return otra;

            var paso = await IntentoAsync(_claves.Kid, ct).ConfigureAwait(false);
            if (paso.KidDesconocido && _claves.KidAnterior is { } anterior)
                paso = await IntentoAsync(anterior, ct).ConfigureAwait(false);
            Volatile.Write(ref _instantanea, paso.Vigencia is { } vigencia ? new Instantanea(paso.Resultado, _reloj.GetUtcNow() + vigencia) : null);
            return paso.Resultado;
        }
        finally
        {
            _vuelo.Release();
        }
    }

    public void Invalidar() => Volatile.Write(ref _instantanea, null);

    public void Dispose() => _vuelo.Dispose();

    private ResultadoCredencial? Vigente() =>
        Volatile.Read(ref _instantanea) is { } i && _reloj.GetUtcNow() < i.Vence ? i.Resultado : null;

    private async Task<Paso> IntentoAsync(string kid, CancellationToken ct)
    {
        var nonce = RandomNumberGenerator.GetBytes(SobreLlaveIa.BytesNonce);
        var pedido = new ObtenerConfiguracionIaRequest { ToolCode = ToolCode, Kid = kid, Nonce = ByteString.CopyFrom(nonce) };
        ObtenerConfiguracionIaResponse respuesta;
        try
        {
            var plazo = TimeSpan.FromSeconds(Math.Clamp(_opciones.PlazoRpcSegundos, 1, 60));
            respuesta = await _cliente().ObtenerConfiguracionIaAsync(pedido, deadline: DateTime.UtcNow + plazo, cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            return Rechazo(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Token de Identidad, red, canal: técnico. Solo el tipo; nunca ex.Message (SP-6).
            SinRespuesta(_log, ex.GetType());
            return Paso.Tecnica;
        }

        return Abrir(respuesta, kid, nonce);
    }

    private Paso Rechazo(RpcException ex)
    {
        var reason = PlatformRpcErrors.Reason(ex) ?? "-";
        switch (ex.StatusCode)
        {
            case StatusCode.PermissionDenied:
            case StatusCode.InvalidArgument:
                Rechazada(_log, ex.StatusCode, reason);
                return Paso.NoConfigurado;
            case StatusCode.FailedPrecondition:
                Rechazada(_log, ex.StatusCode, reason);
                return string.Equals(reason, ReasonKidDesconocido, StringComparison.Ordinal) ? Paso.NoConfigurado with { KidDesconocido = true } : Paso.NoConfigurado;
            default:
                TecnicaRpc(_log, ex.StatusCode, reason);
                return Paso.Tecnica;
        }
    }

    private Paso Abrir(ObtenerConfiguracionIaResponse r, string kid, byte[] nonce)
    {
        if (!string.Equals(r.ToolCode, ToolCode, StringComparison.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(r.Nonce.Span, nonce)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(r.Kid ?? string.Empty), Encoding.UTF8.GetBytes(kid)))
        {
            Tecnica(_log, "ECO_DISTINTO", "-");
            return Paso.Tecnica;
        }

        if (r.EmitidaEn is null || (_reloj.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(r.EmitidaEn.Seconds)).Duration() > DesfaseMaximo)
        {
            Tecnica(_log, "RELOJ", "-");
            return Paso.Tecnica;
        }

        if (_ultimaRowVersion is { } ultima && r.RowVersion < ultima)
        {
            Tecnica(_log, "ROW_VERSION_ANTERIOR", "-");
            return Paso.Tecnica;
        }

        if (!r.Disponible)
        {
            NoDisponibleMotivo(_log, r.Motivo, r.RowVersion);
            return Paso.NoConfigurado;
        }

        var proveedor = r.Cola.FirstOrDefault(p => string.Equals(p.Proveedor, ProveedorPermitido, StringComparison.Ordinal));
        if (proveedor is null)
        {
            NoDisponible(_log, "SIN_PROVEEDOR_PERMITIDO", r.RowVersion);
            return Paso.NoConfigurado;
        }

        if (!_opciones.ModelosEfectivos.Contains(proveedor.Modelo, StringComparer.Ordinal))
        {
            ModeloNoPermitido(_log, proveedor.Modelo, r.RowVersion);
            return Paso.NoConfigurado;
        }

        string llave;
        try
        {
            if (proveedor.Llave is null)
                throw new SobreLlaveIaInvalidoException(MotivoSobreInvalido.Tamano);
            var aad = SobreLlaveIa.ComponerAad(ToolCode, proveedor.Proveedor, proveedor.Modelo, r.RowVersion, kid, nonce, r.EmitidaEn.Seconds);
            llave = DescifradorLlaveIa.Descifrar(proveedor.Llave, kid, _claves.Para(kid)!, aad);
        }
        catch (SobreLlaveIaInvalidoException ex) when (ex.Motivo == MotivoSobreInvalido.AlgDesconocido)
        {
            NoDisponible(_log, "ALG_DESCONOCIDO", r.RowVersion);
            return Paso.NoConfigurado;
        }
        catch (Exception ex) when (ex is SobreLlaveIaInvalidoException or CryptographicException or ArgumentException)
        {
            // Mismo camino para todo fallo de apertura (sin oráculo): un código fijo, sin tipo ni mensaje (SP-18).
            Tecnica(_log, "SOBRE_INVALIDO", "-");
            return Paso.Tecnica;
        }

        if (string.IsNullOrWhiteSpace(llave))
        {
            Tecnica(_log, "SOBRE_INVALIDO", "-");
            return Paso.Tecnica;
        }

        _ultimaRowVersion = r.RowVersion;
        var vigencia = r.TtlSegundos > 0 ? TimeSpan.FromSeconds(Math.Min(r.TtlSegundos, VigenciaMaxima.TotalSeconds)) : VigenciaMaxima;
        Entregada(_log, proveedor.Proveedor, proveedor.Modelo, r.RowVersion, (int)vigencia.TotalSeconds);
        return new Paso(ResultadoCredencial.Con(new CredencialRedactor(proveedor.Proveedor, proveedor.Modelo, llave, r.RowVersion)), vigencia, false);
    }

    [LoggerMessage(EventId = 126900, Level = LogLevel.Information,
        Message = "Configuración de IA de plataforma vigente: proveedor={Proveedor}, modelo={Modelo}, row_version={RowVersion}, vigencia={VigenciaSegundos}s")]
    private static partial void Entregada(ILogger logger, string proveedor, string modelo, long rowVersion, int vigenciaSegundos);

    [LoggerMessage(EventId = 126901, Level = LogLevel.Warning,
        Message = "Configuración de IA de plataforma no disponible por un fallo técnico ({Motivo}, {Detalle}); la redacción se reintenta")]
    private static partial void Tecnica(ILogger logger, string motivo, string detalle);

    [LoggerMessage(EventId = 126902, Level = LogLevel.Error,
        Message = "core-api rechazó la configuración de IA de Diagnóstico ({Estado}, {Reason}); el redactor queda sin configurar")]
    private static partial void Rechazada(ILogger logger, StatusCode estado, string reason);

    [LoggerMessage(EventId = 126908, Level = LogLevel.Warning,
        Message = "Configuración de IA de plataforma no disponible por un fallo técnico ({Estado}, {Reason}); la redacción se reintenta")]
    private static partial void TecnicaRpc(ILogger logger, StatusCode estado, string reason);

    [LoggerMessage(EventId = 126909, Level = LogLevel.Warning,
        Message = "Configuración de IA de plataforma sin respuesta ({Tipo}); la redacción se reintenta")]
    private static partial void SinRespuesta(ILogger logger, Type tipo);

    [LoggerMessage(EventId = 126910, Level = LogLevel.Information,
        Message = "Configuración de IA de plataforma sin proveedor para Diagnóstico ({Motivo}, row_version={RowVersion}); sale la plantilla")]
    private static partial void NoDisponibleMotivo(ILogger logger, MotivoNoDisponible motivo, long rowVersion);

    [LoggerMessage(EventId = 126903, Level = LogLevel.Information,
        Message = "Configuración de IA de plataforma sin proveedor para Diagnóstico ({Motivo}, row_version={RowVersion}); sale la plantilla")]
    private static partial void NoDisponible(ILogger logger, string motivo, long rowVersion);

    [LoggerMessage(EventId = 126904, Level = LogLevel.Error,
        Message = "core-api entregó un modelo fuera de Diagnostico:Ia:ModelosPermitidos (modelo={Modelo}, row_version={RowVersion}); el redactor queda sin configurar")]
    private static partial void ModeloNoPermitido(ILogger logger, string modelo, long rowVersion);

    private sealed class Instantanea(ResultadoCredencial resultado, DateTimeOffset vence)
    {
        public ResultadoCredencial Resultado { get; } = resultado;

        public DateTimeOffset Vence { get; } = vence;
    }

    private readonly record struct Paso(ResultadoCredencial Resultado, TimeSpan? Vigencia, bool KidDesconocido)
    {
        public static Paso Tecnica => new(ResultadoCredencial.Tecnica, null, false);

        public static Paso NoConfigurado => new(ResultadoCredencial.NoConfigurado, VigenciaNegativa, false);
    }
}
