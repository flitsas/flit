using System.Text;
using Flit.Consultas.Api.Persistence;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Flit.Consultas.Api.Avisos;

/// <summary>
/// Receptor de avisos de Kyverum Verify (HU #13351, ADR-0065 §6). Kyverum hace POST a la URL registrada al crear la
/// validación, con el id de la validación en la ruta (el cuerpo no lo repite) y la firma HMAC en <c>x-kv-signature</c>.
/// Firma válida: se guarda el cuerpo tal como llegó, se publica <see cref="TipoAviso"/> para el servicio que pidió la
/// validación y se responde 200 (AC1). Firma inválida: 401, queda en <c>consultas.avisos</c> y no se publica nada (AC2).
/// La ruta es distinta a la de core-api para que, durante la transición, cada validación vuelva a quien la creó.
/// </summary>
internal static class AvisosKyverumEndpoints
{
    public const string Ruta = "/api/v1/consultas/avisos/kyverum-verify/{validacionId:guid}";
    public const string Proveedor = "kyverum_verify";
    public const string TipoAviso = "consultas.aviso.kyverum_verify";

    /// <summary>Tope del cuerpo: un aviso de Kyverum son unos pocos KB.</summary>
    private const int MaxCuerpo = 256 * 1024;

    private const string Proposito = "Flit.Consultas.Kyverum.SecretoAviso.v1";

    public static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector(Proposito);

    public static IEndpointRouteBuilder MapAvisosKyverum(this IEndpointRouteBuilder app)
    {
        app.MapPost(Ruta, RecibirAsync).AllowAnonymous().DisableAntiforgery();
        return app;
    }

    internal static async Task<IResult> RecibirAsync(
        Guid validacionId,
        HttpRequest request,
        ConsultasDb db,
        IPlatformOutbox outbox,
        IDataProtectionProvider protection,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var logger = loggers.CreateLogger(typeof(AvisosKyverumEndpoints));
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct).ConfigureAwait(false);
        if (buffer.Length is 0 or > MaxCuerpo)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "Cuerpo del aviso vacío o demasiado grande.");
        var cuerpo = buffer.ToArray();
        var firma = request.Headers.TryGetValue(KyverumWebhookVerifier.SignatureHeader, out var f) ? f.ToString() : null;

        var validacion = await db.ValidacionesKyverum.AsNoTracking().FirstOrDefaultAsync(v => v.ValidacionId == validacionId, ct).ConfigureAwait(false);
        string resultado;
        if (validacion is null)
            resultado = AvisoResultados.ReferenciaDesconocida;
        else
            resultado = KyverumWebhookVerifier.IsValid(cuerpo, firma, Secreto(validacion, protection, logger))
                ? AvisoResultados.Publicado
                : AvisoResultados.FirmaInvalida;

        var aviso = new AvisoProveedor
        {
            Id = Guid.CreateVersion7(),
            Proveedor = Proveedor,
            ReferenciaId = validacionId,
            TenantId = validacion?.TenantId,
            Resultado = resultado,
            Cuerpo = Encoding.UTF8.GetString(cuerpo),
            RecibidoEn = time.GetUtcNow(),
        };
        db.Avisos.Add(aviso);
        if (resultado == AvisoResultados.Publicado)
        {
            outbox.Enqueue(TipoAviso, 1, validacion!.TenantId, new AvisoKyverumVerify(
                validacionId, validacion.VerificationId, validacion.Producto, aviso.Id, aviso.Cuerpo));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        switch (resultado)
        {
            case AvisoResultados.Publicado:
                return Results.Ok(new { ok = true });
            case AvisoResultados.FirmaInvalida:
                AvisosLog.FirmaInvalida(logger, validacionId, firma is not null);
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Firma del aviso inválida.");
            default:
                AvisosLog.ReferenciaDesconocida(logger, validacionId);
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: "Validación no encontrada.");
        }
    }

    /// <summary>El secreto en claro, solo en memoria; null si no hay o no se puede descifrar (la firma no verificará).</summary>
    private static string? Secreto(ValidacionKyverum validacion, IDataProtectionProvider protection, ILogger logger)
    {
        if (validacion.SecretoCifrado is null)
            return null;
        try
        {
            return Protector(protection).Unprotect(validacion.SecretoCifrado);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            AvisosLog.SecretoIndescifrable(logger, validacion.ValidacionId);
            return null;
        }
    }
}

/// <summary>Datos de <c>consultas.aviso.kyverum_verify</c> (contracts/asyncapi/consultas-events.v1.yaml).</summary>
public sealed record AvisoKyverumVerify(Guid ValidacionId, string VerificationId, string Producto, Guid AvisoId, string Cuerpo);

internal static partial class AvisosLog
{
    [LoggerMessage(EventId = 7421, Level = LogLevel.Warning, Message = "Aviso de Kyverum con firma inválida para la validación {ValidacionId} (firma presente: {FirmaPresente}); no se publica")]
    public static partial void FirmaInvalida(ILogger logger, Guid validacionId, bool firmaPresente);

    [LoggerMessage(EventId = 7422, Level = LogLevel.Warning, Message = "Aviso de Kyverum para una validación desconocida {ValidacionId}")]
    public static partial void ReferenciaDesconocida(ILogger logger, Guid validacionId);

    [LoggerMessage(EventId = 7423, Level = LogLevel.Error, Message = "El secreto del aviso de la validación {ValidacionId} no se pudo descifrar (llaves de Data Protection)")]
    public static partial void SecretoIndescifrable(ILogger logger, Guid validacionId);
}
