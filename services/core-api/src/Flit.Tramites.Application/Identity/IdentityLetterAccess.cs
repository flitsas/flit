using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// HU #14302 (Feature #14300, Épica #14271) — acceso a la carta de identidad de una validación MANUAL desde el expediente, el
/// consolidado, el sello del FUR y la descarga. La outbox genera la carta al aprobar pero se rinde tras 5 intentos (~25 s), así que
/// quien la necesita la pide aquí: si falta se genera en el momento (<see cref="IIdentityLetterService.EnsureForValidationAsync"/>
/// es idempotente) y, si sigue sin poder, devuelve <c>null</c> para que cada llamador aplique SU criterio de fallo.
/// </summary>
public static class IdentityLetterAccess
{
    /// <summary>
    /// Devuelve la validación con la carta ya registrada, generándola si falta. <c>null</c> si no le corresponde carta o no se
    /// pudo generar. Nunca lanza por un fallo de generación (<see cref="IdentityLetterGenerationException"/>): ese fallo se
    /// reintenta; no revierte nada.
    /// </summary>
    public static async Task<ProcedureInstanceBiometricValidation?> EnsureAsync(
        IIdentityLetterService? letters,
        IProcedureInstanceRepository repo,
        ProcedureInstanceBiometricValidation validation,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(validation);

        if (!validation.CorrespondeCartaIdentidad)
            return null;
        if (validation.TieneCartaIdentidad)
            return validation;
        if (letters is null)
            return null;

        try
        {
            await letters.EnsureForValidationAsync(validation.Id, ct).ConfigureAwait(false);
        }
        catch (IdentityLetterGenerationException)
        {
            return null;
        }

        // La validación recibida puede ser una copia sin seguimiento (identidad apalancada): se relee la rastreada, que es la que
        // registró la carta.
        var fresh = await repo.GetBiometricByIdAsync(validation.Id, ct).ConfigureAwait(false);
        return fresh is { TieneCartaIdentidad: true } && fresh.TenantId == validation.TenantId ? fresh : null;
    }

    /// <summary>Lee el PDF de la carta del storage. <c>null</c> si la ruta no existe o el archivo está vacío.</summary>
    public static async Task<byte[]?> ReadPdfAsync(
        IAttachmentStorage storage, ProcedureInstanceBiometricValidation validation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(validation);
        if (string.IsNullOrWhiteSpace(validation.CartaPath))
            return null;

        var stream = await storage.OpenReadAsync(validation.CartaPath, ct).ConfigureAwait(false);
        if (stream is null)
            return null;
        await using (stream.ConfigureAwait(false))
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            return ms.Length == 0 ? null : ms.ToArray();
        }
    }

    /// <summary>Nombre del PDF de la carta: serie del código de integridad (sin PII).</summary>
    public static string FileName(ProcedureInstanceBiometricValidation validation) =>
        string.IsNullOrWhiteSpace(validation.CartaSerie)
            ? $"carta_identidad_{validation.Id:N}.pdf"
            : $"carta_identidad_{validation.CartaSerie}.pdf";
}
