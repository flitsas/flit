namespace Flit.Diagnostico.Domain.Dictamen;

/// <summary>
/// Una fila de <c>diagnostico.reglas_dictamen</c> todavía en memoria. La condición no viaja aquí: la evalúa
/// <see cref="EvaluadorDictamen"/>. La plantilla es el texto que ve el cliente cuando la redacción automática está pendiente.
/// </summary>
public sealed record FilaReglaDictamen(
    int CatalogoVersion,
    string TipoTramiteCodigo,
    string Codigo,
    string Efecto,
    string PlantillaTexto);

/// <summary>
/// Catálogo de reglas de la familia TRASPASO. <see cref="Filas"/> es la versión vigente (<see cref="Version"/>), la que
/// usa el evaluador para los dictámenes nuevos. <see cref="FilasV1"/> se conserva intacta: los dictámenes ya emitidos
/// guardan <c>catalogo_version = 1</c> y no se reevalúan. <see cref="Historico"/> es lo que se siembra en
/// <c>reglas_dictamen</c>.
/// </summary>
/// <example>
/// <code>
/// var fila = CatalogoReglasDictamen.FilasDe(dictamen.CatalogoVersion)
///     .Single(f => f.TipoTramiteCodigo == "TRASPASO_STANDARD" &amp;&amp; f.Codigo == "SIMIT_VEHICULO");
/// </code>
/// </example>
public static class CatalogoReglasDictamen
{
    /// <summary>Versión con la que se emite un dictamen nuevo y que se guarda junto al veredicto.</summary>
    public const int Version = 2;

    private const string Bloquea = "BLOQUEA";
    private const string Advierte = "ADVIERTE";
    private const string Unilateral = "TRASPASO_UNILATERAL";

    /// <summary>Códigos de trámite que comparten estas reglas.</summary>
    public static readonly IReadOnlyList<string> TiposTramite =
    [
        "TRASPASO_STANDARD",
        Unilateral,
        "TRASPASO_TRANSFERENCIA_DE_DOMINIO",
    ];

    // ---- Versión 1 (2026-11-03). No editar: es la de los dictámenes ya emitidos. ----
    private static readonly (string Codigo, string Efecto, string Plantilla)[] DefinicionesV1 =
    [
        ("SOAT_NO_VIGENTE", Bloquea, "El SOAT no está vigente, así que el traspaso queda bloqueado."),
        ("RTM_NO_VIGENTE", Bloquea,
            "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, "
            + "así que el traspaso queda bloqueado."),
        ("ESTADO_VEHICULO", Advierte,
            "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable."),
        ("SIMIT_VEHICULO", Advierte,
            "El vehículo tiene comparendos, no está en paz y salvo, figura suspendido o cancelado, "
            + "o tiene un acuerdo de pago que no está al día. Es una advertencia."),
        ("SIMIT_PROPIETARIO", Advierte,
            "El propietario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, "
            + "o un acuerdo de pago que no está al día. Es una advertencia."),
        ("SIMIT_LOCATARIO", Advierte,
            "El locatario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, "
            + "o un acuerdo de pago que no está al día. Es una advertencia. En leasing, si no hay documento, "
            + "no se pudo consultar su deuda."),
        ("LIMITACION", Advierte, "El vehículo tiene una limitación a la propiedad. Es una advertencia."),
        ("GRAVAMEN", Advierte, "El vehículo tiene un gravamen registrado. Es una advertencia."),
        ("GARANTIA_MOBILIARIA", Advierte, "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia."),
        ("PRENDA", Advierte, "El vehículo tiene una prenda registrada. Es una advertencia."),
        ("INFRACCION_RUNT", Advierte,
            "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT."),
        ("LICENCIA_CONDUCTOR", Advierte,
            "La licencia de conducción está suspendida, vencida o cancelada, "
            + "o no hay certificado de aptitud vigente. Es una advertencia."),
        ("IMPUESTO", Advierte, "El vehículo tiene impuesto departamental pendiente. Es una advertencia."),
        ("FUENTE_PENDIENTE", Advierte,
            "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible "
            + "y esta novedad queda advertida."),
    ];

    /// <summary>
    /// Una regla v2. <see cref="EfectoUnilateral"/> y <see cref="PlantillaUnilateral"/> solo difieren en las reglas de
    /// multas: el traspaso unilateral (Res. 20233040017145, art. 5.3.2.2) no exige paz y salvo, así que ahí advierten.
    /// </summary>
    private sealed record DefinicionV2(
        string Codigo, string Efecto, string Plantilla, string? EfectoUnilateral = null, string? PlantillaUnilateral = null);

    private const string Multas = "multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo.";
    private const string AdvierteUnilateral = " En el traspaso unilateral es una advertencia y el trámite sigue viable.";
    private const string SinVerificar =
        "No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el "
        + "documento del locatario.";

    // ---- Versión 2 (2026-11-11, punto 2 «Multas bloqueantes»). ----
    private static readonly DefinicionV2[] DefinicionesV2 =
    [
        new(DefinicionesV1[0].Codigo, DefinicionesV1[0].Efecto, DefinicionesV1[0].Plantilla),
        new(DefinicionesV1[1].Codigo, DefinicionesV1[1].Efecto, DefinicionesV1[1].Plantilla),
        new(DefinicionesV1[2].Codigo, DefinicionesV1[2].Efecto, DefinicionesV1[2].Plantilla),
        // Las multas del vehículo bloquean por política de FLIT: la norma pide el paz y salvo de las personas.
        new("SIMIT_VEHICULO", Bloquea,
            "El vehículo tiene " + Multas + " Por política de FLIT el traspaso queda bloqueado hasta que se pongan al día.",
            Advierte, "El vehículo tiene " + Multas + AdvierteUnilateral),
        new("SIMIT_PROPIETARIO", Bloquea,
            "El propietario tiene " + Multas + " El traspaso queda bloqueado hasta que se ponga al día.",
            Advierte, "El propietario tiene " + Multas + AdvierteUnilateral),
        new("SIMIT_LOCATARIO", Bloquea,
            "El locatario tiene " + Multas + " El traspaso queda bloqueado hasta que se ponga al día.",
            Advierte, "El locatario tiene " + Multas + AdvierteUnilateral),
        new("SIMIT_SIN_VERIFICAR", Bloquea,
            SinVerificar + " El traspaso queda bloqueado hasta completar la consulta.",
            Advierte, SinVerificar + AdvierteUnilateral),
        new("SIMIT_LICENCIA", Advierte,
            "El SIMIT reporta una licencia de conducción suspendida o cancelada. Es una advertencia y no cambia el veredicto."),
        new("SIMIT_ACUERDO_PAGO", Advierte,
            "Hay un acuerdo de pago con saldo pendiente en el SIMIT. Es una advertencia y no cambia el veredicto."),
        .. DefinicionesV1[6..].Select(d => new DefinicionV2(d.Codigo, d.Efecto, d.Plantilla)),
    ];

    /// <summary>42 filas de la versión 1: 14 reglas por cada código de la familia.</summary>
    public static IReadOnlyList<FilaReglaDictamen> FilasV1 { get; } =
        TiposTramite
            .SelectMany(tipo => DefinicionesV1.Select(d => new FilaReglaDictamen(1, tipo, d.Codigo, d.Efecto, d.Plantilla)))
            .ToArray();

    /// <summary>51 filas de la versión vigente: 17 reglas por cada código de la familia.</summary>
    public static IReadOnlyList<FilaReglaDictamen> Filas { get; } =
        TiposTramite
            .SelectMany(tipo => DefinicionesV2.Select(d => tipo == Unilateral
                ? new FilaReglaDictamen(Version, tipo, d.Codigo, d.EfectoUnilateral ?? d.Efecto, d.PlantillaUnilateral ?? d.Plantilla)
                : new FilaReglaDictamen(Version, tipo, d.Codigo, d.Efecto, d.Plantilla)))
            .ToArray();

    /// <summary>Todas las versiones publicadas, en orden. Es lo que se siembra en <c>reglas_dictamen</c>.</summary>
    public static IReadOnlyList<FilaReglaDictamen> Historico { get; } = [.. FilasV1, .. Filas];

    /// <summary>Las filas de una versión publicada; vacío si la versión no existe.</summary>
    public static IReadOnlyList<FilaReglaDictamen> FilasDe(int catalogoVersion) => catalogoVersion switch
    {
        1 => FilasV1,
        Version => Filas,
        _ => [],
    };
}
