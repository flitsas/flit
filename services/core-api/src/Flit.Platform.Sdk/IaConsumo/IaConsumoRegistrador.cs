using System.Security.Claims;
using System.Threading.Channels;

namespace Flit.Platform.Sdk.IaConsumo;

/// <summary>
/// Configuración del registro. Las banderas se leen de <see cref="IConfiguration"/> en cada uso (no se congelan al
/// arrancar), así que basta cambiar la variable de entorno del servicio para encenderlas o apagarlas.
/// </summary>
public sealed class IaConsumoOpciones(IConfiguration configuration, string aplicativo, string servicio)
{
    /// <summary>Bandera maestra (contrato regla 6). Por defecto <c>true</c>.</summary>
    public const string ClaveLog = "IA_CONSUMO_LOG";
    /// <summary>Prompt y respuesta completos en <c>ia_consumo_detalle</c> (regla 5). Por defecto <c>false</c>.</summary>
    public const string ClaveDetalle = "IA_CONSUMO_DETALLE";
    /// <summary>Días de retención del detalle. Por defecto 30.</summary>
    public const string ClaveDetalleDias = "IA_CONSUMO_DETALLE_DIAS";
    /// <summary>dev | qa | pdn | local. Si falta se usa <c>DR_FLIT_DEPLOY_ENVIRONMENT</c> y, si tampoco, <c>local</c>.</summary>
    public const string ClaveAmbiente = "IA_CONSUMO_AMBIENTE";

    public string Aplicativo { get; } = aplicativo;
    public string Servicio { get; } = servicio;

    public bool Activo => Bandera(ClaveLog, porDefecto: true);
    public bool DetalleActivo => Activo && Bandera(ClaveDetalle, porDefecto: false);

    public int DiasDetalle =>
        int.TryParse(configuration[ClaveDetalleDias], out var dias) && dias > 0 ? dias : 30;

    public string Ambiente => NormalizarAmbiente(configuration[ClaveAmbiente] ?? configuration["DR_FLIT_DEPLOY_ENVIRONMENT"]);

    /// <summary>Cola en memoria (regla 2). Si se llena, se descarta y se cuenta.</summary>
    public int CapacidadCola { get; init; } = 10_000;
    public int TamanoLote { get; init; } = 200;
    /// <summary>Tope para vaciar la cola al apagar.</summary>
    public TimeSpan TopeVaciado { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Cuánto se cachean las tarifas (regla 9).</summary>
    public TimeSpan CacheTarifas { get; init; } = TimeSpan.FromMinutes(10);

    private bool Bandera(string clave, bool porDefecto)
    {
        var valor = configuration[clave];
        return string.IsNullOrWhiteSpace(valor) ? porDefecto : bool.TryParse(valor.Trim(), out var b) ? b : porDefecto;
    }

    internal static string NormalizarAmbiente(string? valor) => valor?.Trim().ToLowerInvariant() switch
    {
        null or "" => "local",
        "pdn" or "prod" or "production" or "produccion" or "producción" => "pdn",
        "qa" or "staging" => "qa",
        "dev" or "development" or "desarrollo" => "dev",
        var otro => otro,
    };
}

/// <summary>Lo que va a la base: la fila y, si el detalle está activo, su prompt y respuesta.</summary>
public sealed record IaConsumoPendiente(IaConsumoLog Fila, IaConsumoDetalle? Detalle);

/// <summary>Cola acotada en memoria entre los clientes HTTP y el escritor en segundo plano.</summary>
public sealed class IaConsumoCola
{
    private readonly Channel<IaConsumoPendiente> _canal;
    private long _descartados;

    public IaConsumoCola(IaConsumoOpciones opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        // FullMode.Wait: TryWrite devuelve false con la cola llena (DropWrite devolvería true y no se podría contar).
        _canal = Channel.CreateBounded<IaConsumoPendiente>(new BoundedChannelOptions(opciones.CapacidadCola)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelReader<IaConsumoPendiente> Lector => _canal.Reader;

    /// <summary>Filas descartadas por cola llena desde que arrancó el servicio.</summary>
    public long Descartados => Interlocked.Read(ref _descartados);

    /// <summary>Nunca espera: si no cabe, devuelve false y cuenta el descarte.</summary>
    public bool Encolar(IaConsumoPendiente pendiente, out long descartados)
    {
        if (_canal.Writer.TryWrite(pendiente))
        {
            descartados = Descartados;
            return true;
        }
        descartados = Interlocked.Increment(ref _descartados);
        return false;
    }
}

/// <summary>Punto de entrada de los clientes HTTP de IA: registra un intento sin bloquear ni fallar nunca.</summary>
public interface IIaConsumoRegistrador
{
    IIaCallContext Contexto { get; }

    /// <summary>false con <c>IA_CONSUMO_LOG=false</c>: el cliente puede ahorrarse armar la llamada.</summary>
    bool Activo { get; }

    /// <summary>true con <c>IA_CONSUMO_DETALLE=true</c>: solo entonces vale la pena armar el texto del prompt completo.</summary>
    bool DetalleActivo { get; }

    void Registrar(IaLlamada llamada);
}

/// <summary>Registrador que no hace nada (clientes construidos a mano o servicios sin registro).</summary>
public sealed class NullIaConsumoRegistrador : IIaConsumoRegistrador
{
    public static NullIaConsumoRegistrador Instancia { get; } = new();

    public IIaCallContext Contexto => IaCallContext.Instancia;
    public bool Activo => false;
    public bool DetalleActivo => false;

    public void Registrar(IaLlamada llamada)
    {
    }
}

internal sealed class IaConsumoRegistrador(
    IaConsumoOpciones opciones,
    IaConsumoCola cola,
    IIaCallContext contexto,
    IHttpContextAccessor http,
    TimeProvider time,
    ILogger<IaConsumoRegistrador> logger) : IIaConsumoRegistrador
{
    public IIaCallContext Contexto => contexto;
    public bool Activo => opciones.Activo;
    public bool DetalleActivo => opciones.DetalleActivo;

    public void Registrar(IaLlamada llamada)
    {
#pragma warning disable CA1031 // El registro nunca rompe la llamada a la IA (contrato regla 2).
        try
        {
            if (llamada is null || !opciones.Activo)
                return;

            var pendiente = Construir(llamada);
            if (!cola.Encolar(pendiente, out var descartados) && (descartados == 1 || descartados % 1000 == 0))
                IaConsumoBitacora.ColaLlena(logger, descartados);
        }
        catch (Exception ex)
        {
            IaConsumoBitacora.NoRegistrado(logger, ex.GetType().Name);
        }
#pragma warning restore CA1031
    }

    internal IaConsumoPendiente Construir(IaLlamada llamada)
    {
        var actual = contexto.Actual;
        var ambito = actual?.Ambito ?? new IaAmbito();
        var fin = llamada.FechaFin ?? time.GetUtcNow();
        var inicio = llamada.FechaInicio;
        var latencia = Math.Clamp((long)(fin - inicio).TotalMilliseconds, 0, int.MaxValue);
        var (tenant, usuarioId, login, autenticado) = Usuario();

        var fila = new IaConsumoLog
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            // Sin ámbito, cada intento es su propia operación.
            CorrelacionId = actual?.CorrelacionId ?? Guid.CreateVersion7(time.GetUtcNow()),
            Intento = llamada.Intento < 1 ? (short)1 : llamada.Intento,
            Pasada = ambito.Pasada is { } p && p > 0 ? p : (short)1,
            FechaInicio = inicio.ToUniversalTime(),
            FechaFin = fin.ToUniversalTime(),
            LatenciaMs = (int)latencia,
            Aplicativo = opciones.Aplicativo,
            Servicio = opciones.Servicio,
            Ambiente = opciones.Ambiente,
            TenantId = ambito.TenantId ?? tenant,
            UsuarioId = ambito.UsuarioId ?? usuarioId,
            UsuarioLogin = ambito.UsuarioLogin ?? login,
            // Criterio común con FLITO: usuario si la petición trae uno; si no, job (procesadores y colas).
            Origen = ambito.Origen ?? (autenticado ? IaOrigenes.Usuario : IaOrigenes.Job),
            Modulo = ambito.Modulo ?? IaValores.Desconocido,
            Proceso = ambito.Proceso ?? IaValores.Desconocido,
            TipoTarea = ambito.TipoTarea ?? llamada.TipoTareaPorDefecto,
            Placa = IaConsumoTexto.NormalizarPlaca(ambito.Placa),
            EntidadTipo = ambito.EntidadTipo,
            EntidadId = ambito.EntidadId,
            ArchivoMime = ambito.ArchivoMime ?? llamada.ArchivoMime,
            ArchivoBytes = ambito.ArchivoBytes ?? llamada.ArchivoBytes,
            ArchivoPaginas = ambito.ArchivoPaginas,
            ArchivoSha256 = ambito.ArchivoSha256,
            Proveedor = llamada.Proveedor,
            ModeloSolicitado = llamada.ModeloSolicitado,
            ModeloRespondido = llamada.ModeloRespondido,
            IdRespuestaProveedor = llamada.IdRespuestaProveedor,
            MaxTokens = llamada.MaxTokens,
            PromptCodigo = ambito.PromptCodigo,
            PromptVersion = ambito.PromptVersion,
            PromptHash = IaConsumoTexto.Sha256Hex(llamada.PromptBase ?? llamada.PromptTexto),
            HttpStatus = llamada.HttpStatus,
            Resultado = llamada.Resultado,
            StopReason = llamada.StopReason,
            ErrorCodigo = llamada.ErrorCodigo,
            ErrorMensaje = IaConsumoTexto.Recortar(IaConsumoTexto.SanearError(llamada.ErrorMensaje), IaConsumoTexto.MaxErrorMensaje),
            TokensEntrada = Math.Max(0, llamada.TokensEntrada),
            TokensSalida = Math.Max(0, llamada.TokensSalida),
            TokensCacheLectura = Math.Max(0, llamada.TokensCacheLectura),
            TokensCacheEscritura = Math.Max(0, llamada.TokensCacheEscritura),
        };

        IaConsumoDetalle? detalle = null;
        if (opciones.DetalleActivo && (llamada.PromptTexto is not null || llamada.RespuestaTexto is not null))
        {
            detalle = new IaConsumoDetalle
            {
                ConsumoId = fila.Id,
                FechaInicio = fila.FechaInicio,
                PromptTexto = IaConsumoTexto.Recortar(llamada.PromptTexto, IaConsumoTexto.MaxDetalle),
                RespuestaTexto = IaConsumoTexto.Recortar(llamada.RespuestaTexto, IaConsumoTexto.MaxDetalle),
            };
        }

        return new IaConsumoPendiente(fila, detalle);
    }

    /// <summary>
    /// Usuario y tenant de la petición en curso (contrato regla 7). El tenant sale del claim <c>tenant_id</c> y, si no
    /// viene (SuperAdmin, token de servicio), de la cabecera <c>X-Tenant-Id</c> / <c>X-Flit-Tenant-Id</c>.
    /// </summary>
    private (string? Tenant, string? UsuarioId, string? Login, bool Autenticado) Usuario()
    {
        var ctx = http.HttpContext;
        if (ctx is null)
            return (null, null, null, false);

        var user = ctx.User;
        var autenticado = user.Identity?.IsAuthenticated == true;
        var tenant = NoVacio(user.FindFirstValue("tenant_id"))
            ?? NoVacio(ctx.Request.Headers["X-Tenant-Id"].ToString())
            ?? NoVacio(ctx.Request.Headers["X-Flit-Tenant-Id"].ToString());
        if (!autenticado)
            return (tenant, null, null, false);

        var usuarioId = NoVacio(user.FindFirstValue("sub")) ?? NoVacio(user.FindFirstValue(ClaimTypes.NameIdentifier));
        var login = NoVacio(user.FindFirstValue("preferred_username"))
            ?? NoVacio(user.FindFirstValue("email"))
            ?? NoVacio(user.FindFirstValue(ClaimTypes.Email))
            ?? NoVacio(user.Identity?.Name);
        return (tenant, usuarioId, login, true);
    }

    private static string? NoVacio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}

internal static partial class IaConsumoBitacora
{
    [LoggerMessage(EventId = 7401, Level = LogLevel.Warning,
        Message = "IA consumo: cola llena, se descartó el registro ({Descartados} descartados desde el arranque)")]
    public static partial void ColaLlena(ILogger logger, long descartados);

    [LoggerMessage(EventId = 7402, Level = LogLevel.Warning, Message = "IA consumo: no se pudo armar el registro ({Tipo})")]
    public static partial void NoRegistrado(ILogger logger, string tipo);

    [LoggerMessage(EventId = 7403, Level = LogLevel.Warning,
        Message = "IA consumo: no se pudo guardar un lote de {Cantidad} registros ({Tipo}); se descarta y se sigue")]
    public static partial void LoteNoGuardado(ILogger logger, int cantidad, string tipo, Exception ex);

    [LoggerMessage(EventId = 7404, Level = LogLevel.Warning, Message = "IA consumo: no se pudieron leer las tarifas ({Tipo}); costo NULL")]
    public static partial void TarifasNoLeidas(ILogger logger, string tipo);

    [LoggerMessage(EventId = 7405, Level = LogLevel.Information, Message = "IA consumo: purgado el detalle anterior a {Limite} ({Cantidad} filas)")]
    public static partial void DetallePurgado(ILogger logger, DateTimeOffset limite, int cantidad);

    [LoggerMessage(EventId = 7406, Level = LogLevel.Warning, Message = "IA consumo: falló la purga del detalle ({Tipo})")]
    public static partial void PurgaFallo(ILogger logger, string tipo);

    [LoggerMessage(EventId = 7407, Level = LogLevel.Warning,
        Message = "IA consumo: al apagar quedaron {Cantidad} registros sin guardar (tope de vaciado)")]
    public static partial void VaciadoIncompleto(ILogger logger, int cantidad);
}
