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
    /// ADR-0055 (HU #12128/#12129) — familia de acción derivada de la decisión: <see cref="PrendaAccionFamilia.Constitucion"/>
    /// (<c>solicitar</c>/<c>registrar</c>), <see cref="PrendaAccionFamilia.Levantamiento"/> (<c>levantar</c>), o
    /// <c>null</c> en <c>omitir</c>/<c>sin_prenda</c> (declaran AUSENCIA de gravamen, no un hecho que deba
    /// versionarse por familia). Misma derivación, en el mismo orden, que la migración SQL
    /// <c>103-prenda-accion-familia-dual.sql</c> — si esta función cambia, esa migración debe revisarse.
    /// </summary>
    public static string? AccionFamiliaFor(string? decision) => decision?.Trim().ToLowerInvariant() switch
    {
        Solicitar or Registrar => PrendaAccionFamilia.Constitucion,
        Levantar => PrendaAccionFamilia.Levantamiento,
        _ => null,
    };

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

    /// <summary>
    /// HU #11257 (Feature #11254) — traduce la decisión al valor semántico que consume el generador del
    /// FUR: <c>solicitar</c>/<c>registrar</c> constituyen el gravamen (casilla 11), <c>levantar</c> lo
    /// levanta (casilla 12), y <c>omitir</c>/<c>sin_prenda</c>/<c>null</c>/cualquier valor desconocido no
    /// marcan ninguna. Antes de esta HU el FUR transportaba <c>bool ImplicaGravamen(decision)</c>, que
    /// colapsa <c>levantar</c> al mismo <c>false</c> que "sin prenda" — la modalidad se perdía antes de
    /// llegar al generador. <see cref="ImplicaGravamen"/> NO se toca: sigue sirviendo a otros consumidores
    /// con su semántica original (presencia de gravamen, no la modalidad de marcación del FUR).
    /// </summary>
    public static FurPrendaMarking ToFurMarking(string? decision)
    {
        if (string.Equals(decision, Solicitar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(decision, Registrar, StringComparison.OrdinalIgnoreCase))
            return FurPrendaMarking.Constitucion;

        if (string.Equals(decision, Levantar, StringComparison.OrdinalIgnoreCase))
            return FurPrendaMarking.Levantamiento;

        return FurPrendaMarking.Ninguna;
    }

    /// <summary>
    /// ADR-0055 (HU #12129) — traduce el CONJUNTO de decisiones vigentes de la instancia (hasta dos,
    /// una por <c>AccionFamilia</c>: constitución y levantamiento) a la marca combinada del FUR.
    /// Antes de esta HU no existía productor de <see cref="FurPrendaMarking.Ambos"/>: la sobrecarga
    /// de una sola decisión (<see cref="ToFurMarking(string?)"/>) no podía representar dos hechos
    /// simultáneos porque el modelo solo admitía una fila vigente por instancia. Esa sobrecarga NO
    /// se toca — sigue sirviendo a todo consumidor de una sola decisión (matrícula, traspaso).
    /// </summary>
    public static FurPrendaMarking ToFurMarking(IEnumerable<string?> decisionesVigentes)
    {
        ArgumentNullException.ThrowIfNull(decisionesVigentes);

        var marcas = decisionesVigentes.Select(ToFurMarking).ToHashSet();
        var constituye = marcas.Contains(FurPrendaMarking.Constitucion);
        var levanta = marcas.Contains(FurPrendaMarking.Levantamiento);

        return (constituye, levanta) switch
        {
            (true, true) => FurPrendaMarking.Ambos,
            (true, false) => FurPrendaMarking.Constitucion,
            (false, true) => FurPrendaMarking.Levantamiento,
            _ => FurPrendaMarking.Ninguna,
        };
    }
}

/// <summary>
/// HU #11257 (Feature #11254) — valor semántico ya resuelto de la marcación de prenda en el FUR (dominio,
/// no Infrastructure): qué casilla marcar, sin que la capa de dibujo compare strings de
/// <see cref="PrendaDecision"/>. Vive junto a <see cref="PrendaDecision"/> (y no en
/// <c>Flit.Tramites.Application/Documents</c>, como marcaba el plan técnico original) porque
/// <c>Flit.Tramites.Domain</c> no referencia ningún otro proyecto: un enum consumido por
/// <see cref="PrendaDecision.ToFurMarking"/> no puede vivir en una capa que Domain no ve.
/// </summary>
public enum FurPrendaMarking
{
    /// <summary>Sin marcación: <c>omitir</c>, <c>sin_prenda</c>, <c>null</c> o valor desconocido.</summary>
    Ninguna,

    /// <summary>Constituye el gravamen (<c>solicitar</c>/<c>registrar</c>): casilla 11.</summary>
    Constitucion,

    /// <summary>Levanta un gravamen existente (<c>levantar</c>): casilla 12.</summary>
    Levantamiento,

    /// <summary>Levanta e inscribe en el mismo FUR (casillas 11 y 12). Simulador / dictamen art. 5.1.8.</summary>
    Ambos,
}

/// <summary>
/// Valores de <c>AccionFamilia</c> (ADR-0055, HU #12128) — agrupan las cinco decisiones de
/// <see cref="PrendaDecision"/> en dos familias mutuamente excluyentes ENTRE SÍ (nunca dos filas
/// vigentes de la misma familia), pero que SÍ pueden coexistir vigentes entre ellas cuando el tipo
/// admite la acción complementaria (<see cref="Flit.Tramites.Domain.Tramites.Services.ProcedureTypeLayers.PermiteAccionComplementaria"/>).
/// </summary>
public static class PrendaAccionFamilia
{
    public const string Constitucion = "constitucion";
    public const string Levantamiento = "levantamiento";
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
