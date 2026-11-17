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
/// Catálogo v1 de la familia TRASPASO. La misma definición se repite en los tres códigos de trámite. No es una
/// siembra de base: la tabla sigue sin migración.
/// </summary>
public static class CatalogoReglasDictamen
{
    /// <summary>Versión que un dictamen futuro deberá guardar junto al veredicto.</summary>
    public const int Version = 1;

    private const string Bloquea = "BLOQUEA";
    private const string Advierte = "ADVIERTE";

    /// <summary>Códigos de trámite que comparten estas reglas.</summary>
    public static readonly IReadOnlyList<string> TiposTramite =
    [
        "TRASPASO_STANDARD",
        "TRASPASO_UNILATERAL",
        "TRASPASO_TRANSFERENCIA_DE_DOMINIO",
    ];

    private static readonly (string Codigo, string Efecto, string Plantilla)[] Definiciones =
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

    /// <summary>42 filas: 14 reglas por cada código de la familia.</summary>
    public static IReadOnlyList<FilaReglaDictamen> Filas { get; } =
        TiposTramite
            .SelectMany(tipo => Definiciones.Select(d =>
                new FilaReglaDictamen(Version, tipo, d.Codigo, d.Efecto, d.Plantilla)))
            .ToArray();
}
