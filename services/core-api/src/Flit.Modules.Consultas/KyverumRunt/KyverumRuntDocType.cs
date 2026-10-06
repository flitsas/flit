namespace Flit.Infrastructure.KyverumRunt;

/// <summary>
/// Normaliza el tipo de documento de FLIT/Verifik (CC, CE, TI, PAS, NIT…) al código que espera Kyverum
/// RUNT (C, T, E, Y, P para personas; N para empresa) — HU #10478. Un código desconocido devuelve
/// <c>null</c>: Kyverum trata <c>tipoDocumento</c> como opcional y prueba C, T, E, Y, P, así que omitirlo
/// es más seguro que enviar un valor inválido.
/// </summary>
internal static class KyverumRuntDocType
{
    public static string? Normalize(string? docType) => docType?.Trim().ToUpperInvariant() switch
    {
        "CC" or "C" => "C",
        "TI" or "T" => "T",
        "CE" or "E" => "E",
        "NIT" or "N" => "N",
        "PAS" or "PA" or "PPT" or "P" => "P",
        _ => null,
    };
}
