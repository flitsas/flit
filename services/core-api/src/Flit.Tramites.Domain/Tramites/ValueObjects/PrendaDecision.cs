namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Decisiones posibles sobre la prenda (gravamen) de un trámite — cimiento IT-3 (Feature #10585, R4/R10/R17).
/// El conjunto es cerrado y se comparte con el contrato del front. La prenda es un agregado compañero de la
/// instancia (no una tipología nueva): se declara en matrícula, se gestiona con gate en traspaso y se puede
/// modificar post-registro (versionado por estado).
/// </summary>
public static class PrendaDecision
{
    /// <summary>Se solicita constituir la prenda (requiere documento de solicitud).</summary>
    public const string Solicitar = "solicitar";

    /// <summary>La prenda ya está registrada/constituida (requiere documento de registro).</summary>
    public const string Registrar = "registrar";

    /// <summary>Se levanta un gravamen existente (requiere documento de levantamiento).</summary>
    public const string Levantar = "levantar";

    /// <summary>Existe gravamen pero el gestor decide continuar sin gestionarlo ("asumo el riesgo").</summary>
    public const string Omitir = "omitir";

    /// <summary>El vehículo no tiene prenda (declaración informativa).</summary>
    public const string SinPrenda = "sin_prenda";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Solicitar, Registrar, Levantar, Omitir, SinPrenda,
    };

    /// <summary>Decisiones que exigen adjuntar el documento de soporte correspondiente.</summary>
    public static readonly IReadOnlySet<string> RequierenDocumento = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Solicitar, Registrar, Levantar,
    };

    public static bool IsValid(string? decision) =>
        !string.IsNullOrWhiteSpace(decision) && All.Contains(decision);

    public static bool RequiereDocumento(string? decision) =>
        !string.IsNullOrWhiteSpace(decision) && RequierenDocumento.Contains(decision);

    /// <summary>
    /// Indica presencia de gravamen para reflejarlo en el FUR (HU-F2-08): <c>solicitar</c>/<c>registrar</c>
    /// marcan el gravamen; <c>sin_prenda</c>/<c>omitir</c>/<c>levantar</c> no.
    /// </summary>
    public static bool ImplicaGravamen(string? decision) =>
        string.Equals(decision, Solicitar, StringComparison.OrdinalIgnoreCase)
        || string.Equals(decision, Registrar, StringComparison.OrdinalIgnoreCase);

    /// <summary>DocTipo del adjunto exigido por la decisión (o <c>null</c> si no requiere documento).</summary>
    public static string? DocTipoFor(string? decision) => decision?.Trim().ToLowerInvariant() switch
    {
        Solicitar => PrendaDocTipos.Solicitud,
        Registrar => PrendaDocTipos.Registro,
        Levantar => PrendaDocTipos.Levantamiento,
        _ => null,
    };
}

/// <summary>DocTipos de los adjuntos de prenda (compartidos con <c>AttachmentRules.ValidTipos</c>).</summary>
public static class PrendaDocTipos
{
    public const string Solicitud = "prenda_solicitud";
    public const string Registro = "prenda_registro";
    public const string Levantamiento = "prenda_levantamiento";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Solicitud, Registro, Levantamiento,
    };
}
