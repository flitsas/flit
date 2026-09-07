using System.Security.Cryptography;
using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Arma el contexto de estampado de impronta manual desde la instancia: dueños actuales
/// (vendedores si <c>RequiresSeller</c>, si no compradores) ordenados por <c>Ordinal</c>,
/// con rúbrica biométrica si existe.
/// </summary>
public static class ImprontaManualStampContextBuilder
{
    public static async Task<ImprontaManualStampContext> BuildAsync(
        ProcedureInstance instance,
        ProcedureInstanceAttachment impronta,
        IAttachmentStorage storage,
        CancellationToken ct = default)
    {
        var fv = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        string? Get(string key) => fv.TryGetValue(key, out var v) ? v : null;

        var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        var rol = profile.RequiresSeller ? "vendedor" : "comprador";

        var actores = instance.Actors
            .Where(a => string.Equals(a.ActorType, rol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Ordinal)
            .ThenBy(a => a.CreatedAt)
            .ToList();

        if (actores.Count == 0)
        {
            // Sin actor del rol esperado: no bloquea el consolidado; el stamper pinta zona 1+3
            // y deja zona 2 vacía de firmantes.
            actores = [];
        }

        var signers = new List<ImprontaManualSigner>(actores.Count);
        foreach (var actor in actores)
        {
            var bio = ResolveBiometric(instance, actor);
            byte[]? imagen = null;
            if (bio?.SignatureImagePath is { Length: > 0 } path)
            {
                try
                {
                    await using var stream = await storage.OpenReadAsync(path, ct).ConfigureAwait(false);
                    if (stream is not null)
                        imagen = await ReadAllAsync(stream, ct).ConfigureAwait(false);
                }
                catch
                {
                    imagen = null;
                }
            }

            var huella = bio?.CertificateHash;
            var hashProp = !string.IsNullOrWhiteSpace(huella)
                ? Sha256Hex(Encoding.UTF8.GetBytes(huella!))
                : Sha256Hex(Encoding.UTF8.GetBytes($"{actor.DocumentNumber}|{actor.FullName}"));

            signers.Add(new ImprontaManualSigner(
                FullName: string.IsNullOrWhiteSpace(actor.FullName) ? actor.DocumentNumber : actor.FullName.Trim(),
                HuellaDigital: huella,
                HashPropietario: hashProp,
                SignatureImage: imagen));
        }

        return new ImprontaManualStampContext(
            ReferenceNumber: instance.ReferenceNumber,
            Placa: Get("plate"),
            Vin: Get("vin"),
            NumMotor: Get("vehicle_engine_number") ?? Get("engine_number") ?? Get("num_motor"),
            NumChasis: Get("vehicle_chassis_number") ?? Get("chassis_number") ?? Get("num_chasis") ?? Get("vin"),
            FechaCargue: impronta.UploadedAt,
            Signers: signers);
    }

    private static ProcedureInstanceBiometricValidation? ResolveBiometric(
        ProcedureInstance instance, ProcedureInstanceActor actor)
    {
        var validations = instance.BiometricValidations;
        if (validations is null || validations.Count == 0)
            return null;

        return validations
            .Where(v =>
                string.Equals(v.DocumentNumber, actor.DocumentNumber, StringComparison.OrdinalIgnoreCase)
                && (v.PartyRole is null
                    || string.Equals(v.PartyRole, actor.ActorType, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault();
    }

    private static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }
}
