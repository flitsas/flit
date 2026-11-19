using System.Security.Cryptography;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Política de baúl «congelada»: responde con la COPIA que el trámite guardó al radicar
/// (<see cref="ProcedureInstanceFirmaBaulCopia"/>) y NUNCA consulta el baúl en vivo. Una persona sin copia
/// devuelve <c>null</c> (sin firma de baúl), aunque hoy tenga una vigente en el baúl: el trámite ya radicado
/// no la adopta a posteriori. La firma del MANDATARIO no es de una parte del trámite y sigue yendo al baúl en
/// vivo.
/// </summary>
internal sealed class FirmaBaulCopiaPolicy(
    IReadOnlyList<ProcedureInstanceFirmaBaulCopia> copias,
    ISignatureVaultPolicy live) : ISignatureVaultPolicy
{
    public Task<SignatureVaultMatch?> ResolveAsync(
        Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
    {
        var copia = copias.FirstOrDefault(c =>
            c.TenantId == tenantId
            && string.Equals(c.DocumentType?.Trim(), documentType?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.DocumentNumber?.Trim(), documentNumber?.Trim(), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(copia is null ? null : FirmaBaulCopias.ToMatch(copia));
    }

    public Task<SignatureVaultMatch?> ResolveMandatarioAsync(
        Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default) =>
        live.ResolveMandatarioAsync(tenantId, documentType, documentNumber, cancellationToken);
}

/// <summary>
/// Punto único para decidir si un trámite lee el baúl EN VIVO (borrador / anterior a la mejora) o su COPIA
/// (radicado: <see cref="ProcedureInstance.FirmaBaulCopiadaAt"/> no nulo). Todo consumidor de la firma de baúl
/// de las PARTES de un trámite pasa por <see cref="EfectivaAsync"/> en vez de usar la política inyectada.
/// </summary>
internal static class FirmaBaulCopias
{
    /// <summary>
    /// Política efectiva del trámite: la copia si ya se tomó al radicar; si no, el baúl en vivo. Una consulta
    /// (por trámite) solo cuando hay copia.
    /// </summary>
    public static async Task<ISignatureVaultPolicy> EfectivaAsync(
        IProcedureInstanceRepository repo, ISignatureVaultPolicy live, ProcedureInstance instance, CancellationToken ct)
    {
        if (instance.FirmaBaulCopiadaAt is null)
            return live;

        var copias = await repo.ListFirmaBaulCopiasAsync(instance.Id, instance.TenantId, ct).ConfigureAwait(false);
        return new FirmaBaulCopiaPolicy(copias ?? [], live);
    }

    public static SignatureVaultMatch ToMatch(ProcedureInstanceFirmaBaulCopia c) =>
        new(
            c.SignatureVaultId,
            c.FullName,
            c.SignatureHash,
            c.StoragePath,
            c.StorageSha256,
            c.VigenciaDesde,
            c.VigenciaHasta,
            c.DocumentNumber,
            c.CodigoHash);

    /// <summary>Llave de persona (tenant|TIPO|NÚMERO) de las copias, para el listado.</summary>
    public static IReadOnlyDictionary<string, bool> ToVigenciaKeys(
        Guid tenantId, IEnumerable<ProcedureInstanceFirmaBaulCopia> copias)
    {
        // Una persona puede ocupar varios slots: la última escritura da igual (todas son «vigentes»).
        var keys = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var c in copias)
            keys[BiometricRules.IdentidadKey(tenantId, c.DocumentType, c.DocumentNumber)] = true;
        return keys;
    }
}

/// <summary>
/// Toma la copia de la firma de baúl de cada parte al RADICAR (salir de borrador). Por cada actor jurídico al
/// que le aplica el baúl (<see cref="FirmaBaulCobertura.Aplica"/>) y con firma vigente hoy en el baúl, guarda
/// una fila (<see cref="ProcedureInstanceFirmaBaulCopia"/>) y COPIA el archivo de la imagen a almacenamiento
/// propio del trámite. Siempre marca <see cref="ProcedureInstance.FirmaBaulCopiadaAt"/>, incluso sin ninguna
/// firma de baúl: desde ahí el baúl en vivo ya no cuenta para este trámite. Corre dentro de la unidad de
/// trabajo de la transición (no guarda por su cuenta).
/// </summary>
public sealed class FirmaBaulCopiaCapturer(
    IProcedureInstanceRepository repo,
    ISignatureVaultPolicy vaultPolicy,
    IAttachmentStorage storage,
    ILogger<FirmaBaulCopiaCapturer>? logger = null)
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>Tipo de archivo con el que se guarda la imagen copiada en el almacenamiento del trámite.</summary>
    public const string TipoArchivoCopia = "firma_baul_copia";

    public async Task CapturarAsync(ProcedureInstance instance, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.FirmaBaulCopiadaAt is not null)
            return;

        // Reemplaza copias previas (el trámite volvió a borrador y se radica de nuevo).
        var previas = await repo.ListFirmaBaulCopiasAsync(instance.Id, instance.TenantId, ct).ConfigureAwait(false);
        var ahora = DateTimeOffset.UtcNow;
        foreach (var previa in previas)
            previa.DeletedAt = ahora;

        var nuevas = new List<ProcedureInstanceFirmaBaulCopia>();
        foreach (var (rol, slotKey, actor) in Slots(instance))
        {
            if (!FirmaBaulCobertura.Aplica(actor))
                continue;

            var sujeto = IdentitySubjectResolver.For(actor);
            if (string.IsNullOrWhiteSpace(sujeto.TipoDocumento) || string.IsNullOrWhiteSpace(sujeto.NumeroDocumento))
                continue;

            var tipo = sujeto.TipoDocumento.Trim();
            var numero = sujeto.NumeroDocumento.Trim();
            var match = await vaultPolicy.ResolveAsync(instance.TenantId, tipo, numero, ct).ConfigureAwait(false);
            if (match is null)
                continue;

            var (path, sha) = await CopiarArchivoAsync(instance, slotKey, match, ct).ConfigureAwait(false);
            nuevas.Add(new ProcedureInstanceFirmaBaulCopia
            {
                TenantId = instance.TenantId,
                ProcedureInstanceId = instance.Id,
                SlotKey = slotKey,
                PartyRole = rol,
                DocumentType = Truncar(tipo, 10),
                DocumentNumber = Truncar(numero, 40),
                SignatureVaultId = match.SignatureVaultId,
                FullName = Truncar(match.FullName, 200),
                SignatureHash = Truncar(match.SignatureHash ?? string.Empty, 200),
                CodigoHash = string.IsNullOrWhiteSpace(match.CodigoHash) ? null : Truncar(match.CodigoHash, 100),
                VigenciaDesde = match.VigenciaDesde,
                VigenciaHasta = match.VigenciaHasta,
                CapturedAt = ahora,
                StoragePath = path,
                StorageSha256 = sha,
                SourceStoragePath = match.StoragePath,
                CreatedAt = ahora,
            });
        }

        if (nuevas.Count > 0)
            await repo.AddFirmaBaulCopiasAsync(nuevas, ct).ConfigureAwait(false);

        instance.FirmaBaulCopiadaAt = ahora;
    }

    /// <summary>
    /// Copia el archivo de la firma al almacenamiento del trámite. Si no se puede leer/guardar (best-effort,
    /// igual que el FUR), conserva la referencia original: la fila se crea de todos modos para que la parte
    /// siga acreditada por baúl, y la imagen caerá al sello de texto si el archivo ya no existe.
    /// </summary>
    private async Task<(string Path, string Sha256)> CopiarArchivoAsync(
        ProcedureInstance instance, string slotKey, SignatureVaultMatch match, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(match.StoragePath))
            return (string.Empty, match.StorageSha256 ?? string.Empty);

        try
        {
            var stream = await storage.OpenReadAsync(match.StoragePath, ct).ConfigureAwait(false);
            if (stream is null)
                return (match.StoragePath, match.StorageSha256 ?? string.Empty);

            await using (stream.ConfigureAwait(false))
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                if (ms.Length == 0)
                    return (match.StoragePath, match.StorageSha256 ?? string.Empty);

                ms.Position = 0;
                var guardado = await storage
                    .SaveAsync(instance.Id, TipoArchivoCopia, $"firma-baul-{slotKey}.png", ms, ct)
                    .ConfigureAwait(false);
                var sha = string.IsNullOrWhiteSpace(guardado.Sha256)
                    ? Convert.ToHexStringLower(SHA256.HashData(ms.ToArray()))
                    : guardado.Sha256;
                return (guardado.StoragePath, sha);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin PII: solo el tipo de excepción y el trámite.
            FirmaBaulCopiaLog.CopiaArchivoFallida(_logger, instance.Id, ex.GetType().Name);
            return (match.StoragePath, match.StorageSha256 ?? string.Empty);
        }
    }

    private static IEnumerable<(string Rol, string SlotKey, ProcedureInstanceActor Actor)> Slots(ProcedureInstance instance)
    {
        var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        var roles = PartesDeclaradas.Firma(profile);
        foreach (var rol in roles)
        {
            foreach (var actor in instance.Actors
                         .Where(a => string.Equals(a.ActorType, rol, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(a => a.Ordinal))
            {
                yield return (rol, FurOverlayPartyKey.For(rol, actor.Ordinal), actor);
            }
        }
    }

    private static string Truncar(string valor, int max) => valor.Length <= max ? valor : valor[..max];
}

internal static partial class FirmaBaulCopiaLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo copiar el archivo de la firma de baúl del trámite {InstanceId} ({Error}); se conserva la referencia original.")]
    public static partial void CopiaArchivoFallida(ILogger logger, Guid instanceId, string error);
}
