namespace Flit.Tramites.Domain.Enums;

/// <summary>
/// Familia del tipo de trámite (ADR-0050). Único eje de clasificación de un expediente: sustituye a
/// <c>modalidad_entrada</c>, que solo tenía dos valores y colapsaba <see cref="Otros"/> en
/// <see cref="Matriculas"/>. El valor persistido vive en <c>tramites.procedure_types.family</c>, con
/// CHECK de dominio, y se congela por expediente en <c>procedure_type_snapshots</c>.
/// </summary>
public enum ProcedureFamily
{
    /// <summary>Matrícula inicial, leasing, cancelación, rematrícula.</summary>
    Matriculas,

    /// <summary>Traspaso estándar, unilateral, transferencia de dominio.</summary>
    Traspaso,

    /// <summary>Prendas, duplicados, blindaje, cambios de color/carrocería y demás novedades.</summary>
    Otros,
}

/// <summary>
/// Códigos persistidos de <see cref="ProcedureFamily"/> y el <b>único</b> parser del vocabulario.
/// <para>Antes de ADR-0050 convivían cuatro criterios para los mismos strings:
/// <c>TramiteModalidadEntradaCodes.FromCode</c> (case-sensitive, sin trim),
/// <c>ProcedureInstanceEndpoints.EsMatriculaInicial</c> (trim + ignore-case),
/// <c>RejectionReasonModalidades.EsValida</c> (Ordinal) y <c>TipologiaResolver.FromFamily</c>
/// (OrdinalIgnoreCase). Todo el parseo de familia pasa ahora por aquí.</para>
/// </summary>
public static class ProcedureFamilyCodes
{
    public const string Matriculas = "MATRICULAS";
    public const string Traspaso = "TRASPASO";
    public const string Otros = "OTROS";

    /// <summary>Todos los códigos válidos, en el orden del enum.</summary>
    public static readonly IReadOnlyList<string> All = [Matriculas, Traspaso, Otros];

    /// <summary>Código persistido de la familia.</summary>
    public static string ToCode(ProcedureFamily family) => family switch
    {
        ProcedureFamily.Matriculas => Matriculas,
        ProcedureFamily.Traspaso => Traspaso,
        ProcedureFamily.Otros => Otros,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Familia no soportada."),
    };

    /// <summary>
    /// Parsea el código persistido. Tolerante a espacios y mayúsculas — la columna arrastra seeds
    /// históricos con distinta capitalización. Devuelve <c>null</c> si no pertenece al dominio.
    /// </summary>
    public static ProcedureFamily? FromCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToUpperInvariant() switch
        {
            Matriculas => ProcedureFamily.Matriculas,
            Traspaso => ProcedureFamily.Traspaso,
            Otros => ProcedureFamily.Otros,
            _ => null,
        };
    }

    /// <summary>
    /// Igual que <see cref="FromCode"/> pero degrada a <see cref="ProcedureFamily.Otros"/> en vez de
    /// <c>null</c>. Es el default seguro: un tipo mal clasificado cae en la familia sin privilegios
    /// operativos, nunca en matrículas — que era justo el colapso que ADR-0050 elimina.
    /// </summary>
    public static ProcedureFamily FromCodeOrOtros(string? value) =>
        FromCode(value) ?? ProcedureFamily.Otros;

    /// <summary><c>true</c> si el valor pertenece al dominio de familias.</summary>
    public static bool IsValid(string? value) => FromCode(value) is not null;

    /// <summary>
    /// PUENTE TEMPORAL — acepta además los dos valores de la difunta <c>modalidad_entrada</c>
    /// (<c>matricula_inicial</c> / <c>traspaso</c>) que el frontend todavía envía en los requests de
    /// creación y de pre-vuelo.
    /// <para>Se retira cuando el cliente pase a enviar <c>procedureTypeCode</c> (HU del selector
    /// familia → tipo). No usar en código nuevo: para eso está <see cref="FromCode"/>.</para>
    /// </summary>
    public static ProcedureFamily? FromCodeOrLegacyModalidad(string? value)
    {
        var familia = FromCode(value);
        if (familia is not null)
            return familia;

        return value?.Trim().ToLowerInvariant() switch
        {
            "matricula_inicial" => ProcedureFamily.Matriculas,
            "traspaso" => ProcedureFamily.Traspaso,
            _ => null,
        };
    }
}
