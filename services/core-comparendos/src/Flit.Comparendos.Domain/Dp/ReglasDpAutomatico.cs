namespace Flit.Comparendos.Domain.Dp;

/// <summary>Códigos de las tipologías de derecho de petición que el envío automático sabe decidir (semilla de TipologiasSemilla).</summary>
public static class CodigosTipologiaDp
{
    /// <summary>Caducidad individual.</summary>
    public const string Caducidad = "DCI";

    /// <summary>Mala notificación del comparendo.</summary>
    public const string MalaNotificacion = "DBN";

    /// <summary>Prescripción.</summary>
    public const string Prescripcion = "DPR";

    /// <summary>Responsabilidad del propietario persona jurídica.</summary>
    public const string PersonaJuridica = "DLL";

    /// <summary>Revocatoria por la sentencia C-321 de 2022 (C35 y D02 no se detectan por cámara).</summary>
    public const string RevocatoriaC321 = "DRP";
}

/// <summary>De qué fecha del comparendo corre un plazo.</summary>
public enum BasePlazo
{
    /// <summary>La fecha en que ocurrió la infracción.</summary>
    Imposicion = 1,

    /// <summary>La fecha en que se notificó al propietario.</summary>
    Notificacion = 2,
}

/// <summary>
/// Reglas con las que el envío automático decide qué derecho de petición aplica a un comparendo. Los valores por defecto
/// son los del envío automático de FLIT V1 (BackCrudInfraction_master, 5 reglas por calendario) y NO están validados
/// jurídicamente: cada umbral es un parámetro para que lo confirme o lo ajuste el abogado sin cambiar la lógica
/// (Epic #14606). Las diferencias con el cálculo de plazos de V2 son deliberadas y están anotadas en cada propiedad.
/// </summary>
public sealed record ReglasDpAutomatico
{
    /// <summary>Valores de V1.</summary>
    public static ReglasDpAutomatico V1 { get; } = new();

    /// <summary>
    /// DCI: de dónde cuenta la caducidad. V1 la cuenta desde la imposición; <see cref="Plazos.MotorDePlazos"/> la cuenta
    /// desde la notificación, así que un comparendo puede aplicar aquí y no ser «Caducado» para V2.
    /// </summary>
    public BasePlazo BaseCaducidad { get; init; } = BasePlazo.Imposicion;

    /// <summary>DCI: años de caducidad.</summary>
    public int AniosCaducidad { get; init; } = 1;

    /// <summary>DCI: días que V1 suma al año antes de dar la caducidad por cumplida (más de 1 año y 1 día).</summary>
    public int DiasExtraCaducidad { get; init; } = 1;

    /// <summary>DBN: días calendario entre la imposición y la notificación a partir de los cuales la notificación es tardía.</summary>
    public int DiasNotificacionTardia { get; init; } = 30;

    /// <summary>DPR: años desde la notificación a partir de los cuales aplica la prescripción. V1 no descarta actos que interrumpan.</summary>
    public int AniosPrescripcion { get; init; } = 3;

    /// <summary>DLL: si solo aplica a comparendos detectados por cámara. V1 no lo exigía.</summary>
    public bool DllSoloFotomulta { get; init; }

    /// <summary>Infracciones de la sentencia C-321 de 2022: DRP solo aplica a ellas y DBN y DLL las excluyen.</summary>
    public IReadOnlySet<string> CodigosC321 { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C35", "D02" };

    /// <summary>DRP: primer día de imposición que cubre (inclusive).</summary>
    public DateOnly InicioRevocatoria { get; init; } = new(2020, 1, 6);

    /// <summary>DRP: último día de imposición que cubre (inclusive).</summary>
    public DateOnly FinRevocatoria { get; init; } = new(2022, 9, 14);

    /// <summary>Orden en que se elige cuando aplican varias: prescripción y caducidad, DRP, DBN y DLL (por fuerza jurídica, sin validar).</summary>
    public IReadOnlyList<string> Prioridad { get; init; } =
    [
        CodigosTipologiaDp.Prescripcion,
        CodigosTipologiaDp.Caducidad,
        CodigosTipologiaDp.RevocatoriaC321,
        CodigosTipologiaDp.MalaNotificacion,
        CodigosTipologiaDp.PersonaJuridica,
    ];
}
