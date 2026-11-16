using System.Text.Json;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;

/// <summary>
/// HU #14310 — convierte el campo público <c>validacionFotos</c> (JSON del cliente) en un resultado SANEADO. Es entrada pública
/// y el resultado es solo informativo, así que NUNCA falla ni rechaza la captura: campo ausente, demasiado grande, JSON inválido,
/// estado/motivo desconocido o foto ausente dejan esa foto «no_verificada / sin_resultado». Solo se aceptan las listas cerradas
/// del dominio; nada de texto libre del cliente llega a la entidad.
/// </summary>
public static class ManualPhotoChecksParser
{
    /// <summary>Tope del campo en caracteres (el JSON legítimo mide ~200).</summary>
    public const int MaxLength = 2048;

    public static ManualPhotoChecks Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxLength)
            return ManualPhotoChecks.AllUnknown;

        try
        {
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 4 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ManualPhotoChecks.AllUnknown;

            return new ManualPhotoChecks(
                ParsePhoto(doc.RootElement, "rostro"),
                ParsePhoto(doc.RootElement, "anverso"),
                ParsePhoto(doc.RootElement, "reverso"));
        }
        catch (JsonException)
        {
            return ManualPhotoChecks.AllUnknown;
        }
    }

    private static ManualPhotoCheck ParsePhoto(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var photo) || photo.ValueKind != JsonValueKind.Object)
            return ManualPhotoCheck.Unknown;

        var state = ReadString(photo, "estado");
        if (state == ManualPhotoCheckStates.Verificada)
            return ManualPhotoCheck.Verified;
        if (state != ManualPhotoCheckStates.NoVerificada)
            return ManualPhotoCheck.Unknown;

        var reason = ReadString(photo, "motivo");
        return ManualPhotoCheckReasons.IsValid(reason) ? ManualPhotoCheck.NotVerified(reason!) : ManualPhotoCheck.Unknown;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
