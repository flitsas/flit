using System.Text.Json;
using Flit.Modules.Quipux.Domain.Trazabilidad;

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarLog;

/// <summary>
/// Proyección del <c>detail</c> de un evento del LOG QX: enmascara los datos sensibles y extrae las
/// claves técnicas (duración, origen y código) que la interfaz muestra sueltas.
/// </summary>
/// <remarks>
/// Vive aparte porque lo consumen DOS pantallas —la búsqueda de la HU #10793 y el log completo de la
/// HU #11787— y el enmascarado es una barrera de seguridad: dos copias de la misma transformación
/// son dos sitios donde se puede corregir una y olvidar la otra.
/// </remarks>
internal static class LogQxDetailProjector
{
    internal readonly record struct Projection(
        JsonElement? Detail,
        long? DurationMs,
        string? Origin,
        int? ResponseCode);

    /// <summary>
    /// Devuelve el detail ENMASCARADO más los campos técnicos extraídos del original. Un detail
    /// ausente o no-JSON se trata como «sin payload disponible» y nunca rompe la página.
    /// </summary>
    public static Projection Project(string? rawDetail, string stage)
    {
        JsonElement? detail = null;
        long? durationMs = null;
        string? origin = null;
        int? responseCode = null;

        if (!string.IsNullOrWhiteSpace(rawDetail))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawDetail);

                // Clonar: el JsonDocument se libera al salir del using, pero el JsonElement clonado
                // sobrevive. El detail ya viene sanitizado desde captura; el enmascarado (HU #10794)
                // es una segunda barrera sobre cualquier PII que se hubiera colado a una clave
                // sensible. Las claves técnicas no casan con la lista sensible, así que se extraen
                // del original y sobreviven al enmascarado.
                var root = doc.RootElement.Clone();
                detail = LogQxSensitiveDataMasker.Mask(root);

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("duration_ms", out var d)
                        && d.ValueKind == JsonValueKind.Number
                        && d.TryGetInt64(out var ms))
                    {
                        durationMs = ms;
                    }

                    if (root.TryGetProperty("origen", out var o) && o.ValueKind == JsonValueKind.String)
                    {
                        origin = o.GetString();
                    }

                    if (root.TryGetProperty("codigo", out var c)
                        && c.ValueKind == JsonValueKind.Number
                        && c.TryGetInt32(out var code))
                    {
                        responseCode = code;
                    }
                }
            }
            catch (JsonException)
            {
                detail = null;
            }
        }

        return new Projection(detail, durationMs, origin ?? DeriveOrigin(stage), responseCode);
    }

    /// <summary>
    /// Origen best-effort para eventos previos a la instrumentación (sin <c>origen</c> en el detail):
    /// las etapas <c>registro_*</c> las genera el worker registrador; las <c>consulta_*</c>, el
    /// sondeo. Los eventos nuevos traen el origen explícito y esta derivación no aplica.
    /// </summary>
    private static string? DeriveOrigin(string stage)
    {
        if (stage.StartsWith("registro", StringComparison.Ordinal))
        {
            return QuipuxJobNames.Register;
        }

        return stage.StartsWith("consulta", StringComparison.Ordinal) ? QuipuxJobNames.StatusPoll : null;
    }
}
