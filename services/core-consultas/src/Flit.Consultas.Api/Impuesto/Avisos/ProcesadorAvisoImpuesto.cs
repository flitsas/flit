using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>Cabeceras del aviso, tal como llegaron.</summary>
internal sealed record CabecerasAvisoImpuesto(string? Timestamp, string? Firma, string? Delivery, string? Evento)
{
    public override string ToString() => $"CabecerasAvisoImpuesto {{ ConFirma = {Firma is not null}, ConDelivery = {Delivery is not null}, Evento = {Evento} }}";
}

/// <summary>
/// Decide la respuesta a un aviso del proveedor de impuesto (HU #14711, plan §3.3, guía §6) y lo deja al sondeo que espera la
/// consulta. No confía en el cuerpo antes de la firma:
/// <list type="number">
///   <item>Token → empresa (<see cref="TokenAvisoImpuesto"/>); sin llave, sin forma o alterado: <c>401</c>.</item>
///   <item>Secreto de la empresa (<see cref="IFuenteSecretoWebhookImpuesto"/>); sin secreto: <c>401</c>; Identidad caída: <c>503</c>
///   (el proveedor reintenta).</item>
///   <item>Firma con el secreto actual y, si falla, con el anterior vigente, timestamp a ±300 s. Si no coincide con la versión
///   cacheada se relee el secreto una vez (rotación) y se vuelve a verificar. Sigue sin coincidir: <c>401</c> sin detalle.</item>
///   <item>Firma válida: otro evento que <c>consulta.finalizada</c>, o un cuerpo sin <c>consulta.id</c>/<c>attempt</c>/estado
///   terminal, se confirma con <c>200</c> y se descarta.</item>
///   <item>Deduplica por <c>X-Flit-Delivery</c> (sin él, <c>sin-delivery:{consulta}:{attempt}</c>): duplicado, <c>200</c> sin repetir.</item>
///   <item>La consulta debe ser de esa empresa y seguir abierta; si es de otra o ya se cerró, <c>200</c> y se descarta (log sin
///   datos de la consulta). Si sigue abierta, el aviso va al <see cref="BuzonAvisosImpuesto"/> (gana el mayor <c>attempt</c>) y el
///   sondeo la cierra por el mismo camino del polling.</item>
/// </list>
/// Responde en cuanto deja el aviso: el cierre y la publicación los hace el sondeo. No registra token, secreto, firma ni cuerpo.
/// </summary>
/// <example>
/// <code>
/// var status = await procesador.ProcesarAsync(token, new CabecerasAvisoImpuesto(ts, firma, delivery, evento), cuerpoCrudo, ct);
/// </code>
/// </example>
internal sealed partial class ProcesadorAvisoImpuesto(
    OpcionesAvisoImpuesto opciones,
    IFuenteSecretoWebhookImpuesto secretos,
    IRegistroAvisosImpuesto registro,
    BuzonAvisosImpuesto buzon,
    TimeProvider time,
    ILogger<ProcesadorAvisoImpuesto> logger)
{
    public const int LargoMaximoDelivery = 120;
    private const string PrefijoSinDelivery = "sin-delivery:";
    private static readonly Regex IdConsulta = IdConsultaRegex();

    public async Task<int> ProcesarAsync(string? token, CabecerasAvisoImpuesto cabeceras, ReadOnlyMemory<byte> cuerpo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cabeceras);
        if (opciones.Llave is not { } llave || !TokenAvisoImpuesto.TryLeer(token, llave, out var tenantId))
        {
            AvisosImpuestoLog.TokenDesconocido(logger);
            return StatusCodes.Status401Unauthorized;
        }

        var verificacion = await VerificarAsync(tenantId, cabeceras, cuerpo, ct).ConfigureAwait(false);
        if (verificacion != StatusCodes.Status200OK)
            return verificacion;

        if (!string.Equals(cabeceras.Evento, VerificadorFirmaImpuesto.EventoFinalizada, StringComparison.Ordinal))
        {
            AvisosImpuestoLog.OtroEvento(logger, tenantId);
            return StatusCodes.Status200OK;
        }

        if (Leer(cuerpo.Span) is not { } aviso)
        {
            AvisosImpuestoLog.SinForma(logger, tenantId);
            return StatusCodes.Status200OK;
        }

        var delivery = Delivery(cabeceras.Delivery, aviso.IdExterno, aviso.Attempt);
        if (!await registro.RegistrarEntregaAsync(tenantId, delivery, aviso.IdExterno, aviso.Attempt, ct).ConfigureAwait(false))
        {
            AvisosImpuestoLog.Duplicado(logger, tenantId);
            return StatusCodes.Status200OK;
        }

        switch (await registro.EstadoConsultaAsync(tenantId, aviso.IdExterno, ct).ConfigureAwait(false))
        {
            case EstadoConsultaAviso.NoEncontrada:
                AvisosImpuestoLog.ConsultaAjena(logger, tenantId);
                return StatusCodes.Status200OK;
            case EstadoConsultaAviso.Cerrada:
                AvisosImpuestoLog.ConsultaCerrada(logger, tenantId, aviso.Attempt);
                return StatusCodes.Status200OK;
            default:
                buzon.Entregar(tenantId, aviso.IdExterno, new AvisoImpuestoVerificado(aviso.Attempt, aviso.Estado, aviso.Consulta, time.GetUtcNow()));
                AvisosImpuestoLog.Recibido(logger, tenantId, aviso.Attempt);
                return StatusCodes.Status200OK;
        }
    }

    private async Task<int> VerificarAsync(Guid tenantId, CabecerasAvisoImpuesto cabeceras, ReadOnlyMemory<byte> cuerpo, CancellationToken ct)
    {
        try
        {
            var secreto = await secretos.ObtenerAsync(tenantId, forzar: false, ct).ConfigureAwait(false);
            if (!secreto.Configurado)
            {
                AvisosImpuestoLog.SinSecreto(logger, tenantId);
                return StatusCodes.Status401Unauthorized;
            }

            var resultado = VerificadorFirmaImpuesto.Verificar(secreto.Vigentes(time.GetUtcNow()), cabeceras.Timestamp, cabeceras.Firma, cuerpo.Span, time.GetUtcNow());
            if (resultado == ResultadoFirmaImpuesto.FirmaInvalida)
            {
                // Puede que el secreto haya rotado después de cachearlo: se relee una vez y se verifica con la versión nueva.
                var releido = await secretos.ObtenerAsync(tenantId, forzar: true, ct).ConfigureAwait(false);
                if (releido.Configurado && releido.Version != secreto.Version)
                    resultado = VerificadorFirmaImpuesto.Verificar(releido.Vigentes(time.GetUtcNow()), cabeceras.Timestamp, cabeceras.Firma, cuerpo.Span, time.GetUtcNow());
            }

            if (resultado != ResultadoFirmaImpuesto.Valida)
            {
                AvisosImpuestoLog.FirmaRechazada(logger, tenantId, resultado.ToString());
                return StatusCodes.Status401Unauthorized;
            }

            return StatusCodes.Status200OK;
        }
        catch (FalloSecretoWebhookImpuestoException)
        {
            AvisosImpuestoLog.IdentidadNoDisponible(logger, tenantId);
            return StatusCodes.Status503ServiceUnavailable;
        }
    }

    /// <summary><c>X-Flit-Delivery</c> recortado; sin él, <c>sin-delivery:{consulta}:{attempt}</c>; uno muy largo se resume con SHA-256.</summary>
    internal static string Delivery(string? cabecera, string idExterno, int attempt)
    {
        var valor = cabecera?.Trim();
        if (string.IsNullOrEmpty(valor))
            return PrefijoSinDelivery + idExterno + ":" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return valor.Length <= LargoMaximoDelivery
            ? valor
            : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
    }

    private sealed record AvisoLeido(string IdExterno, int Attempt, EstadoTrabajoExterno Estado, string Consulta);

    /// <summary><c>consulta</c> con id (forma estricta), attempt ≥ 0 y estado terminal; null si no.</summary>
    private static AvisoLeido? Leer(ReadOnlySpan<byte> cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo.ToArray());
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("consulta", out var consulta) || consulta.ValueKind != JsonValueKind.Object)
                return null;
            var id = consulta.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            if (id is null || !IdConsulta.IsMatch(id))
                return null;
            if (!consulta.TryGetProperty("attempt", out var a) || a.ValueKind != JsonValueKind.Number || !a.TryGetInt32(out var attempt) || attempt < 0)
                return null;
            var status = consulta.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var estado = status?.ToLowerInvariant() switch
            {
                "succeeded" => EstadoTrabajoExterno.Exitoso,
                "partial" => EstadoTrabajoExterno.Parcial,
                "failed" => EstadoTrabajoExterno.Fallido,
                _ => (EstadoTrabajoExterno?)null,
            };
            return estado is null ? null : new AvisoLeido(id, attempt, estado.Value, consulta.GetRawText());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdConsultaRegex();
}

/// <summary>Logs del receptor: solo la empresa, el attempt y códigos fijos; nunca token, secreto, firma, id de consulta ni cuerpo.</summary>
internal static partial class AvisosImpuestoLog
{
    [LoggerMessage(EventId = 7450, Level = LogLevel.Warning, Message = "Aviso de impuesto rechazado para la empresa {TenantId}: {Motivo}.")]
    public static partial void FirmaRechazada(ILogger logger, Guid tenantId, string motivo);

    [LoggerMessage(EventId = 7451, Level = LogLevel.Warning, Message = "Aviso de impuesto con un token que no es de ninguna empresa.")]
    public static partial void TokenDesconocido(ILogger logger);

    [LoggerMessage(EventId = 7452, Level = LogLevel.Information, Message = "Aviso de impuesto duplicado para la empresa {TenantId}; se confirma sin repetir.")]
    public static partial void Duplicado(ILogger logger, Guid tenantId);

    [LoggerMessage(EventId = 7453, Level = LogLevel.Warning, Message = "Aviso de impuesto de una consulta que no es de la empresa {TenantId}; se descarta.")]
    public static partial void ConsultaAjena(ILogger logger, Guid tenantId);

    [LoggerMessage(EventId = 7454, Level = LogLevel.Information, Message = "Aviso de impuesto (attempt {Attempt}) de una consulta ya cerrada de la empresa {TenantId}; se descarta.")]
    public static partial void ConsultaCerrada(ILogger logger, Guid tenantId, int attempt);

    [LoggerMessage(EventId = 7455, Level = LogLevel.Warning, Message = "Aviso de impuesto firmado sin la forma esperada para la empresa {TenantId}; se descarta.")]
    public static partial void SinForma(ILogger logger, Guid tenantId);

    [LoggerMessage(EventId = 7456, Level = LogLevel.Information, Message = "Aviso de impuesto (attempt {Attempt}) recibido para la empresa {TenantId}.")]
    public static partial void Recibido(ILogger logger, Guid tenantId, int attempt);

    [LoggerMessage(EventId = 7457, Level = LogLevel.Warning, Message = "Aviso de impuesto para la empresa {TenantId} sin secreto del webhook.")]
    public static partial void SinSecreto(ILogger logger, Guid tenantId);

    [LoggerMessage(EventId = 7458, Level = LogLevel.Warning, Message = "Aviso de impuesto para la empresa {TenantId}: Identidad no entregó el secreto; responde 503.")]
    public static partial void IdentidadNoDisponible(ILogger logger, Guid tenantId);

    [LoggerMessage(EventId = 7459, Level = LogLevel.Information, Message = "Aviso de impuesto con otro evento para la empresa {TenantId}; se ignora.")]
    public static partial void OtroEvento(ILogger logger, Guid tenantId);
}
