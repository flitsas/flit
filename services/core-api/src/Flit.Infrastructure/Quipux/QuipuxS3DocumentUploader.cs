using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Puertos;
using Flit.Tramites.Application.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Quipux;

/// <summary>
/// <see cref="IQuipuxDocumentUploader"/> sobre el bucket S3 de Quipux (<c>qxinterconnect</c>).
/// <para>
/// El bucket es DE QUIPUX, no de FLIT: Quipux lee el PDF de ahí y por eso el payload de radicación
/// lleva la key y no el binario. Es la ÚNICA parte de FLIT 2.0 que maneja credenciales AWS directas
/// — el file-manager corporativo no aplica porque el destino es de un tercero.
/// </para>
/// <para>
/// Toda la configuración (bucket, prefijo, región y credenciales) sale de <c>admin.quipux_settings</c>
/// vía <see cref="IQuipuxSettingsRepository"/>. Esto corrige el bug de FLIT 1.0
/// (<c>quipuxService.ts:101-103,121</c>), donde access key, secret, región y bucket eran literales en
/// el código: rotar una credencial exigía desplegar. Aquí es un UPDATE.
/// </para>
/// <para>
/// <b>Región (bug 1.0 <c>quipuxService.ts:103</c> vs <c>:139</c>):</b> el cliente S3 se construía con
/// <c>'us-east-1'</c> literal mientras la URL de retorno usaba <c>config.services.aws.region</c>. Si
/// ambas divergían, la key devuelta apuntaba a una región distinta de la real. Aquí hay UNA sola
/// fuente: <see cref="QuipuxSettings.AwsRegion"/>.
/// </para>
/// <para>
/// <b>Nunca se loguean credenciales</b> (bug 1.0 <c>:112</c>: <c>console.log('S3Client config:', s3.config)</c>
/// volcaba access key y secret al log). Aquí solo se registran bucket, key y tamaño.
/// </para>
/// </summary>
internal sealed class QuipuxS3DocumentUploader(
    IQuipuxSettingsRepository settingsRepository,
    IAttachmentStorage attachmentStorage,
    FlitDbContext db,
    ILogger<QuipuxS3DocumentUploader> logger) : IQuipuxDocumentUploader
{
    public async Task<string> UploadAsync(
        Guid attachmentId,
        string documentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        var settings = await settingsRepository.GetAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new QuipuxUploadException(
                "Quipux: no hay fila de configuración (admin.quipux_settings); no se puede subir el documento.",
                isTransient: false);

        ValidarConfiguracionS3(settings);

        // 1) Resolver el binario del adjunto. OpenReadAsync trabaja con el StoragePath (id opaco del
        //    file-manager), no con el id del adjunto, así que hay que traducirlo primero.
        var storagePath = await ResolveStoragePathAsync(attachmentId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            // FALLO RUIDOSO (bug 1.0 `downloadFileIfAvailable`, quipuxService.ts:225-236): sin fileId
            // devolvía Buffer.from('') y subía un PDF de 0 bytes al bucket sin avisar; Quipux radicaba
            // un documento vacío. No es transitorio: reintentar no va a materializar el adjunto.
            QuipuxUploaderLog.AdjuntoNoDisponible(logger, attachmentId, documentName);
            throw new QuipuxUploadException(
                $"Quipux: el adjunto {attachmentId} no existe o no tiene ruta de almacenamiento.",
                isTransient: false);
        }

        var source = await AbrirAdjuntoAsync(storagePath, attachmentId, cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            // OpenReadAsync ya bufferiza en un MemoryStream seekable; solo se copia si el backend
            // devolviera un stream no seekable (necesitamos Length para detectar el PDF vacío y para
            // que el SDK firme el PUT). Nunca se tiene más de un PDF en memoria a la vez.
            MemoryStream? buffer = null;
            try
            {
                var content = source;
                if (!source.CanSeek)
                {
                    buffer = new MemoryStream();
                    await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                    content = buffer;
                }

                if (content.Length == 0)
                {
                    // Mismo bug de 1.0: un stream vacío se subía como PDF de 0 bytes en silencio.
                    QuipuxUploaderLog.AdjuntoVacio(logger, attachmentId, documentName);
                    throw new QuipuxUploadException(
                        $"Quipux: el adjunto {attachmentId} está vacío (0 bytes); no se sube un PDF vacío al bucket.",
                        isTransient: false);
                }

                content.Position = 0;
                var key = $"{settings.S3Prefix}{documentName}.pdf";
                await PutObjectAsync(settings, key, content, cancellationToken).ConfigureAwait(false);

                QuipuxUploaderLog.DocumentoSubido(logger, documentName, settings.Bucket, key, content.Length);
                return key;
            }
            finally
            {
                // Dispose explícito (y no `await using (buffer.ConfigureAwait(false))`): esa forma NO
                // comprueba null y reventaría con NRE en el camino normal, donde `buffer` siempre es
                // null porque el stream del file-manager ya es seekable.
                if (buffer is not null)
                    await buffer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// La configuración S3 debe venir COMPLETA de BD. Sin fallbacks literales a propósito: en 1.0 el
    /// bucket y las credenciales estaban hardcodeados y un despliegue mal configurado escribía en el
    /// bucket equivocado en vez de fallar.
    /// </summary>
    private static void ValidarConfiguracionS3(QuipuxSettings settings)
    {
        var faltantes = new List<string>(4);
        if (string.IsNullOrWhiteSpace(settings.Bucket)) faltantes.Add("bucket");
        if (string.IsNullOrWhiteSpace(settings.AwsRegion)) faltantes.Add("aws_region");
        if (string.IsNullOrWhiteSpace(settings.AwsAccessKeyId)) faltantes.Add("aws_access_key_id");
        if (string.IsNullOrWhiteSpace(settings.AwsSecretAccessKey)) faltantes.Add("aws_secret_access_key");

        // Un secreto ausente aquí puede significar que el keyring de Data Protection no pudo descifrar
        // `aws_secret_access_key_enc` (ver DataProtectionQuipuxSecretProtector). En ambos casos es un
        // problema de configuración, no transitorio: reintentar no lo arregla.
        if (faltantes.Count > 0)
            throw new QuipuxUploadException(
                $"Quipux: configuración S3 incompleta en admin.quipux_settings (falta: {string.Join(", ", faltantes)}).",
                isTransient: false);
    }

    /// <summary>
    /// Traduce el id del adjunto a su <c>StoragePath</c>. Lectura cross-tenant con
    /// <c>SET LOCAL row_security = off</c> (mismo patrón que <c>DbTransitOfficeOperationalStatusReader</c>):
    /// el uploader corre dentro de un worker de sistema, sin <c>app.current_tenant_id</c> en la sesión,
    /// y sin desactivar RLS la política <c>tenant_isolation</c> devolvería 0 filas — el adjunto parecería
    /// inexistente y la radicación moriría con un error engañoso.
    /// </summary>
    private async Task<string?> ResolveStoragePathAsync(Guid attachmentId, CancellationToken ct)
    {
        async Task<string?> LeerAsync() =>
            await db.ProcedureInstanceAttachments
                .AsNoTracking()
                .Where(a => a.Id == attachmentId)
                .Select(a => a.StoragePath)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

        if (!db.Database.IsRelational())
            return await LeerAsync().ConfigureAwait(false);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", ct).ConfigureAwait(false);
                var result = await LeerAsync().ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Abre el binario del adjunto. Un fallo de red contra el file-manager SÍ es transitorio (el
    /// documento existe, la descarga se puede reintentar); un <c>null</c> significa que el binario se
    /// perdió y no lo es.
    /// </summary>
    private async Task<Stream> AbrirAdjuntoAsync(string storagePath, Guid attachmentId, CancellationToken ct)
    {
        Stream? stream;
        try
        {
            stream = await attachmentStorage.OpenReadAsync(storagePath, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new QuipuxUploadException(
                $"Quipux: fallo de red descargando el adjunto {attachmentId} del file-manager.",
                isTransient: true,
                ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout del HttpClient (no cancelación del worker): reintentable.
            throw new QuipuxUploadException(
                $"Quipux: timeout descargando el adjunto {attachmentId} del file-manager.",
                isTransient: true,
                ex);
        }

        if (stream is null)
        {
            QuipuxUploaderLog.AdjuntoNoDisponible(logger, attachmentId, storagePath);
            throw new QuipuxUploadException(
                $"Quipux: el binario del adjunto {attachmentId} no está disponible en el file-manager.",
                isTransient: false);
        }

        return stream;
    }

    /// <summary>
    /// Sube el PDF al bucket de Quipux. El cliente S3 se crea por llamada porque las credenciales viven
    /// en BD y pueden rotar sin reiniciar el proceso (los workers releen los settings en cada ciclo);
    /// el costo es despreciable frente a la cadencia de radicación (minutos).
    /// </summary>
    private static async Task PutObjectAsync(
        QuipuxSettings settings,
        string key,
        Stream content,
        CancellationToken ct)
    {
        // Credenciales explícitas desde BD: NO se usan env vars, perfiles ni roles IAM — el bucket es
        // de un tercero y no hay identidad de FLIT que asumir.
        var credentials = new BasicAWSCredentials(settings.AwsAccessKeyId, settings.AwsSecretAccessKey);
        var config = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(settings.AwsRegion) };

        using var s3 = new AmazonS3Client(credentials, config);
        var request = new PutObjectRequest
        {
            BucketName = settings.Bucket,
            Key = key,
            InputStream = content,
            ContentType = "application/pdf",
            AutoCloseStream = false,
        };

        try
        {
            await s3.PutObjectAsync(request, ct).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex)
        {
            // 5xx / throttling ⇒ transitorio. 403/404 (AccessDenied, InvalidAccessKeyId,
            // SignatureDoesNotMatch, NoSuchBucket) ⇒ configuración mala: reintentar no la arregla.
            throw new QuipuxUploadException(
                $"Quipux: S3 rechazó la subida de '{key}' al bucket '{settings.Bucket}' ({ex.ErrorCode}).",
                isTransient: EsTransitorio(ex),
                ex);
        }
        catch (AmazonServiceException ex)
        {
            // Fallo genérico del servicio/red por debajo del SDK.
            throw new QuipuxUploadException(
                $"Quipux: fallo de servicio subiendo '{key}' al bucket '{settings.Bucket}'.",
                isTransient: true,
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new QuipuxUploadException(
                $"Quipux: fallo de red subiendo '{key}' al bucket '{settings.Bucket}'.",
                isTransient: true,
                ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new QuipuxUploadException(
                $"Quipux: timeout subiendo '{key}' al bucket '{settings.Bucket}'.",
                isTransient: true,
                ex);
        }
    }

    /// <summary>¿El fallo de S3 se puede reintentar? Solo indisponibilidad y throttling.</summary>
    private static bool EsTransitorio(AmazonS3Exception ex) =>
        ex.StatusCode is HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
        || string.Equals(ex.ErrorCode, "SlowDown", StringComparison.Ordinal)
        || string.Equals(ex.ErrorCode, "RequestTimeout", StringComparison.Ordinal)
        || string.Equals(ex.ErrorCode, "InternalError", StringComparison.Ordinal);
}

/// <summary>
/// Logs del uploader (source-generated, CA1848). NUNCA se registran credenciales, secretos ni el objeto
/// de configuración del cliente S3 — en 1.0 (<c>quipuxService.ts:112</c>) se volcaba <c>s3.config</c>
/// completo al log, incluidas access key y secret.
/// </summary>
internal static partial class QuipuxUploaderLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Quipux: documento '{DocumentName}' subido al bucket {Bucket} como '{Key}' ({SizeBytes} bytes).")]
    public static partial void DocumentoSubido(ILogger logger, string documentName, string bucket, string key, long sizeBytes);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Quipux: adjunto {AttachmentId} no disponible ({Referencia}); no se sube un PDF vacío (fallo explícito, no silencioso como en 1.0).")]
    public static partial void AdjuntoNoDisponible(ILogger logger, Guid attachmentId, string referencia);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Quipux: adjunto {AttachmentId} para '{DocumentName}' tiene 0 bytes; se aborta la subida.")]
    public static partial void AdjuntoVacio(ILogger logger, Guid attachmentId, string documentName);
}
