namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Vocabulario CERRADO de vigencia de una certificación externa (SOAT, RTM, registro mercantil).
/// Es el único vocabulario que cruza la frontera del proveedor: cada mapper traduce su jerga a estos
/// cuatro valores y el resto del sistema no vuelve a interpretar texto.
/// </summary>
/// <remarks>
/// Los literales coinciden con los de <see cref="Tramites.Services.SoatGate"/> a propósito
/// (<c>vigente</c>/<c>vencido</c>/<c>unknown</c>): la proyección a <c>field_values.soat_estado</c> es
/// gate del organismo de tránsito y el frontend compara ESTRICTO en minúscula.
///
/// <para><b><see cref="Unknown"/> no es un error</b>: es la respuesta honesta cuando el proveedor dijo
/// algo que no significa vigencia. El caso canónico es <c>APROBADA</c> en una revisión técnico-mecánica:
/// describe el resultado del trámite de la revisión, NO su vigencia (placa YNK04A: cuatro revisiones
/// <c>APROBADA</c>, las cuatro con <c>vigente:"NO"</c>). Mapearlo a <see cref="Vigente"/> afirmaría una
/// cobertura que el RUNT nunca declaró.</para>
/// </remarks>
public enum VigencyStatus
{
    /// <summary>El proveedor no dijo nada interpretable como vigencia. No decide nada.</summary>
    Unknown = 0,

    /// <summary>Cobertura o registro activo a la fecha de la consulta.</summary>
    Vigente = 1,

    /// <summary>Cobertura o registro expirado / no vigente / cancelado.</summary>
    Vencido = 2,

    /// <summary>La certificación no aplica al vehículo (p. ej. RTM en vehículo nuevo).</summary>
    NoAplica = 3,
}

/// <summary>Literales persistidos de <see cref="VigencyStatus"/>. El CHECK del DDL usa estos mismos.</summary>
public static class VigencyStatusCodes
{
    public const string Unknown = "unknown";
    public const string Vigente = "vigente";
    public const string Vencido = "vencido";
    public const string NoAplica = "no_aplica";

    public static string ToCode(VigencyStatus status) => status switch
    {
        VigencyStatus.Vigente => Vigente,
        VigencyStatus.Vencido => Vencido,
        VigencyStatus.NoAplica => NoAplica,
        _ => Unknown,
    };

    public static VigencyStatus FromCode(string? code) => code switch
    {
        Vigente => VigencyStatus.Vigente,
        Vencido => VigencyStatus.Vencido,
        NoAplica => VigencyStatus.NoAplica,
        _ => VigencyStatus.Unknown,
    };
}
