using System.Globalization;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;

/// <summary>Un archivo del multipart: el contenido y el tamaño declarado (para rechazar antes de leer).</summary>
public sealed record ManualCaptureUpload(Stream Content, long Length);

/// <summary>Orden de enviar la captura. Los 4 campos son los del multipart (<c>rostro</c>, <c>anverso</c>, <c>reverso</c>, <c>firma</c>).</summary>
public sealed record EnviarCapturaManualCommand(
    string Token,
    ManualCaptureUpload? Rostro,
    ManualCaptureUpload? Anverso,
    ManualCaptureUpload? Reverso,
    ManualCaptureUpload? Firma,
    string? UserAgent);

/// <summary>Resultado del envío: el estado en el que quedó la validación.</summary>
public sealed record EnviarCapturaManualResult(string Status);

/// <summary>
/// HU #13290 (Feature #13281 B, Épica #13202) — <c>POST /public/manual-capture/{token}/submit</c>: recibe y almacena rostro,
/// anverso, reverso y firma, y pasa la validación a <c>pendiente_revision_manual</c> (el enlace queda consumido).
/// <para>
/// Orden de validación (nada se guarda hasta que todo pasa): token/estado como B1 (404/410/409), consentimiento del ciclo
/// actual (409 <c>consentimiento_requerido</c>), presencia (422: <c>firma_requerida</c> / <c>archivo_requerido</c>), tamaño
/// (413) y tipo POR CONTENIDO (415; la firma debe ser PNG).
/// </para>
/// <para>
/// Almacenamiento: <see cref="IAttachmentStorage"/> con la clave estable <c>Id de la validación</c> (el
/// <c>instanceId = Guid.Empty</c> de las validaciones standalone no distingue a nadie; ver
/// <see cref="StorageKey"/>) y nombre con sufijo UTC del intento: cada guardado crea un archivo nuevo, jamás se sobrescribe ni
/// se borra el de un intento previo (decisión del PO: las imágenes se conservan siempre); las rutas previas quedan en la
/// auditoría. Si el guardado falla a mitad se limpia lo ya subido y la fila no cambia; el UPDATE final es atómico
/// (<see cref="IProcedureInstanceRepository.TryPersistManualCaptureAsync"/>), así dos envíos simultáneos no consumen el enlace
/// dos veces.
/// </para>
/// </summary>
public sealed class EnviarCapturaManualHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage,
    IIdentityValidationAuditLog audit,
    TimeProvider? clock = null)
{
    internal const string TipoRostro = "manual_rostro";
    internal const string TipoAnverso = "manual_anverso";
    internal const string TipoReverso = "manual_reverso";

    /// <summary>Mismo tipo que la rúbrica Kyverum (ADR-0054): quien lea <c>SignatureImagePath</c> no distingue el origen.</summary>
    internal const string TipoFirma = "identity_signature";

    /// <summary>
    /// Clave de almacenamiento de las imágenes de una validación. Se usa el id de la validación (no el del trámite): es único
    /// por validación y existe tanto en trámite como en prevalidación standalone, donde el id de instancia es
    /// <c>Guid.Empty</c> y mezclaría los archivos de todos los clientes bajo la misma clave.
    /// </summary>
    internal static Guid StorageKey(ProcedureInstanceBiometricValidation v) => v.Id;

    public async Task<(EnviarCapturaManualResult? Result, string? Error)> HandleAsync(
        EnviarCapturaManualCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var (v, error) = await ManualCaptureSessionResolver.ResolveAsync(repo, command.Token, now, ct).ConfigureAwait(false);
        if (v is null)
            return (null, error);

        if (!v.TieneConsentimientoManualVigente)
            return (null, ManualCaptureErrors.ConsentimientoRequerido);

        // Presencia: la firma es obligatoria y tiene su propio código; un archivo de longitud 0 cuenta como ausente.
        if (command.Firma is not { Length: > 0 })
            return (null, ManualCaptureErrors.FirmaRequerida);
        if (command.Rostro is not { Length: > 0 } || command.Anverso is not { Length: > 0 } || command.Reverso is not { Length: > 0 })
            return (null, ManualCaptureErrors.ArchivoRequerido);

        // Tamaño declarado primero (no se lee nada de un archivo enorme), luego lectura acotada.
        if (command.Rostro.Length > ManualCaptureImages.MaxImageBytes
            || command.Anverso.Length > ManualCaptureImages.MaxImageBytes
            || command.Reverso.Length > ManualCaptureImages.MaxImageBytes
            || command.Firma.Length > ManualCaptureImages.MaxSignatureBytes)
            return (null, ManualCaptureErrors.ArchivoDemasiadoGrande);

        var rostro = await ReadBoundedAsync(command.Rostro, ManualCaptureImages.MaxImageBytes, ct).ConfigureAwait(false);
        var anverso = await ReadBoundedAsync(command.Anverso, ManualCaptureImages.MaxImageBytes, ct).ConfigureAwait(false);
        var reverso = await ReadBoundedAsync(command.Reverso, ManualCaptureImages.MaxImageBytes, ct).ConfigureAwait(false);
        var firma = await ReadBoundedAsync(command.Firma, ManualCaptureImages.MaxSignatureBytes, ct).ConfigureAwait(false);
        if (rostro is null || anverso is null || reverso is null || firma is null)
            return (null, ManualCaptureErrors.ArchivoDemasiadoGrande);

        var fRostro = ManualCaptureImages.Detect(rostro);
        var fAnverso = ManualCaptureImages.Detect(anverso);
        var fReverso = ManualCaptureImages.Detect(reverso);
        var fFirma = ManualCaptureImages.Detect(firma);
        if (!ManualCaptureImages.IsAllowedPhoto(fRostro) || !ManualCaptureImages.IsAllowedPhoto(fAnverso)
            || !ManualCaptureImages.IsAllowedPhoto(fReverso) || !ManualCaptureImages.IsAllowedSignature(fFirma))
            return (null, ManualCaptureErrors.TipoNoSoportado);

        // Rutas del intento anterior (rechazado y reactivado): se conservan en storage y se dejan en la auditoría.
        var previas = DescribirPrevias(v);

        var guardados = new List<string>(4);
        StoredFile sRostro, sAnverso, sReverso, sFirma;
        try
        {
            var key = StorageKey(v);
            // Sufijo UTC con milisegundos: un intento nuevo nunca reutiliza el nombre de uno previo.
            var sello = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            sRostro = await SaveAsync(key, TipoRostro, $"rostro-{sello}.{ManualCaptureImages.Extension(fRostro)}", rostro, guardados, ct)
                .ConfigureAwait(false);
            sAnverso = await SaveAsync(key, TipoAnverso, $"anverso-{sello}.{ManualCaptureImages.Extension(fAnverso)}", anverso, guardados, ct)
                .ConfigureAwait(false);
            sReverso = await SaveAsync(key, TipoReverso, $"reverso-{sello}.{ManualCaptureImages.Extension(fReverso)}", reverso, guardados, ct)
                .ConfigureAwait(false);
            // El hash de la firma lo calcula el storage sobre los bytes guardados, igual que la rúbrica Kyverum (ADR-0054).
            sFirma = await SaveAsync(key, TipoFirma, $"firma-{sello}.png", firma, guardados, ct).ConfigureAwait(false);

            v.RegistrarCapturaManual(sRostro.StoragePath, sAnverso.StoragePath, sReverso.StoragePath, sFirma.StoragePath, sFirma.Sha256, now);
            if (!await repo.TryPersistManualCaptureAsync(v, ct).ConfigureAwait(false))
            {
                // Otro envío del mismo token ya consumió el enlace: lo subido por este queda huérfano, se limpia.
                Limpiar(guardados);
                return (null, ManualCaptureErrors.EstadoInvalido);
            }
        }
        catch (ManualCaptureStateException ex)
        {
            Limpiar(guardados);
            return (null, ex.Code);
        }
        catch
        {
            // Todo o nada: ni la fila cambió (el UPDATE es lo último) ni quedan archivos de este intento.
            Limpiar(guardados);
            throw;
        }

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.ManualCapturaRecibida, IdentityValidationAuditOutcomes.Ok,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id, PartyRole: v.PartyRole,
            Message: "Captura manual recibida (rostro, anverso, reverso y firma); pasa a revisión.",
            Detail: $"sha256_rostro={sRostro.Sha256}; sha256_anverso={sAnverso.Sha256}; sha256_reverso={sReverso.Sha256}; "
                + $"sha256_firma={sFirma.Sha256}; previas=[{previas}]; user_agent={LimpiarUserAgent(command.UserAgent)}"), ct)
            .ConfigureAwait(false);

        return (new EnviarCapturaManualResult(v.Status), null);
    }

    private async Task<StoredFile> SaveAsync(
        Guid key, string tipo, string filename, byte[] bytes, List<string> guardados, CancellationToken ct)
    {
        await using var content = new MemoryStream(bytes, writable: false);
        var stored = await storage.SaveAsync(key, tipo, filename, content, ct).ConfigureAwait(false);
        guardados.Add(stored.StoragePath);
        return stored;
    }

    private void Limpiar(List<string> guardados)
    {
        foreach (var path in guardados)
        {
            try
            {
                storage.Delete(path);
            }
            catch (Exception)
            {
                // Mejor esfuerzo: limpiar nunca debe tapar el error original (el file-manager además no expone borrado).
            }
        }

        guardados.Clear();
    }

    /// <summary>Rutas opacas de storage del intento anterior (vacío si no hubo). Ninguna es PII.</summary>
    private static string DescribirPrevias(ProcedureInstanceBiometricValidation v)
    {
        var partes = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(v.FacePhotoPath)) partes.Add($"rostro:{v.FacePhotoPath}");
        if (!string.IsNullOrWhiteSpace(v.IdFrontPhotoPath)) partes.Add($"anverso:{v.IdFrontPhotoPath}");
        if (!string.IsNullOrWhiteSpace(v.IdBackPhotoPath)) partes.Add($"reverso:{v.IdBackPhotoPath}");
        if (!string.IsNullOrWhiteSpace(v.SignatureImagePath)) partes.Add($"firma:{v.SignatureImagePath}");
        return string.Join(",", partes);
    }

    /// <summary>Agente de usuario sin saltos de línea ni caracteres de control y acotado (trazabilidad, no PII).</summary>
    private static string LimpiarUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "n/d";
        var limpio = new string(userAgent.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return limpio.Length > 200 ? limpio[..200] : limpio;
    }

    /// <summary>Lee hasta <paramref name="max"/> bytes; null si el contenido real excede el máximo.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(ManualCaptureUpload upload, long max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await upload.Content.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + read > max)
                return null;
            ms.Write(buffer, 0, read);
        }

        return ms.ToArray();
    }
}
