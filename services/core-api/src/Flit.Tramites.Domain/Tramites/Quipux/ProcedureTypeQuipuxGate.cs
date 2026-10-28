using System.Text.Json;

namespace Flit.Tramites.Domain.Tramites.Quipux;

/// <summary>
/// Reglas de canal Quipux en el submit (Bug #13912), alineadas con
/// <c>ListElegiblesSinSubmissionAsync</c>: el tipo declara el bloque
/// <c>procedure_types.external_refs.quipux</c> <b>y</b> la secretaría destino tiene DIVIPO +
/// bandera de familia. Sin dependencia del módulo Quipux.
/// </summary>
public static class ProcedureTypeQuipuxGate
{
    public const string FamiliaMatricula = "MATRICULA";
    public const string FamiliaTraspaso = "TRASPASO";
    public const string FamiliaOtros = "OTROS";

    /// <summary>¿El JSON de <c>external_refs</c> declara un objeto <c>quipux</c>?</summary>
    public static bool EstaParametrizadoParaQuipux(string? externalRefs) =>
        TryGetBloqueQuipux(externalRefs, out _);

    /// <summary>
    /// Familia Quipux (<c>MATRICULA</c>/<c>TRASPASO</c>/<c>OTROS</c>) o <c>null</c> si no hay
    /// bloque o la familia es desconocida.
    /// </summary>
    public static string? FamiliaQuipux(string? externalRefs)
    {
        if (!TryGetBloqueQuipux(externalRefs, out var bloque))
            return null;

        if (!bloque.TryGetProperty("familia", out var famEl) || famEl.ValueKind != JsonValueKind.String)
            return null;

        var fam = famEl.GetString();
        return fam is FamiliaMatricula or FamiliaTraspaso or FamiliaOtros ? fam : null;
    }

    /// <summary>
    /// ¿Este trámite debe quedar en <c>preparado</c> para el worker Quipux? Misma regla que el
    /// encolado: tipo con bloque + DIVIPO no vacío + bandera de familia de la secretaría.
    /// Si el OT no está integrado, el submit usa <c>DestinoDeRadicacion</c> (entregado/preasignacion).
    /// </summary>
    public static bool CanalActivoParaSecretaria(
        string? externalRefs,
        string? divipoCode,
        bool quipuxRegistration,
        bool quipuxTransfer,
        bool quipuxOther)
    {
        if (string.IsNullOrWhiteSpace(divipoCode))
            return false;

        var familia = FamiliaQuipux(externalRefs);
        return familia switch
        {
            FamiliaMatricula => quipuxRegistration,
            FamiliaTraspaso => quipuxTransfer,
            FamiliaOtros => quipuxOther,
            _ => false,
        };
    }

    private static bool TryGetBloqueQuipux(string? externalRefs, out JsonElement bloque)
    {
        bloque = default;
        if (string.IsNullOrWhiteSpace(externalRefs))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(externalRefs);
            if (!doc.RootElement.TryGetProperty("quipux", out var q) || q.ValueKind != JsonValueKind.Object)
                return false;

            bloque = q.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
