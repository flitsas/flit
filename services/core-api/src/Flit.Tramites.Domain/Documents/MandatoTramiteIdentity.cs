using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Domain.Documents;

/// <summary>
/// Identidad del trámite para el Contrato de Mandato.
///
/// <para>Hay dos vocabularios en FLIT: el catálogo <c>tramites.procedure_types</c>
/// (<c>MATRICULA_NUEVA</c> / <c>TRASPASO_STANDARD</c>) y la tipología del wizard
/// (<c>matricula_inicial</c> / <c>traspaso_standard</c>). El PDF debe clasificar y nombrar
/// el objeto con el catálogo; esta clase es el único punto que entiende ambos.</para>
/// </summary>
public static class MandatoTramiteIdentity
{
    public const string CodigoMatriculaNueva = "MATRICULA_NUEVA";
    public const string CodigoTraspasoStandard = "TRASPASO_STANDARD";

    /// <summary>Redacción legal de respaldo si el catálogo no trajo nombre.</summary>
    public const string NombreMatriculaFallback = "MATRÍCULA INICIAL";

    /// <summary>Redacción legal de respaldo si el catálogo no trajo nombre.</summary>
    public const string NombreTraspasoFallback = "TRASPASO DE PROPIEDAD";

    /// <summary>
    /// ¿El trámite es un traspaso? Acepta código de catálogo, familia, tipología wizard y modalidad.
    /// </summary>
    public static bool EsTraspaso(
        string? procedureTypeCode,
        string? family,
        string? tipologiaCodigo,
        string? modalidad)
    {
        if (EqualsFamily(family, ProcedureFamily.Traspaso))
            return true;

        if (EsCodigoTraspaso(procedureTypeCode)
            || EsCodigoTraspaso(tipologiaCodigo)
            || EsCodigoTraspaso(modalidad))
            return true;

        return string.Equals(
            modalidad?.Trim(),
            TramiteModalidadEntradaCodes.Traspaso,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Nombre que entra al objeto del contrato (mayúsculas). Si hay nombre de
    /// <c>procedure_types.name</c>, manda ese; si no, la redacción legal de matrícula o traspaso.
    /// </summary>
    public static string NombreObjeto(
        string? procedureTypeName,
        string? procedureTypeCode,
        string? family,
        string? tipologiaCodigo,
        string? modalidad)
    {
        if (!string.IsNullOrWhiteSpace(procedureTypeName))
            return procedureTypeName.Trim().ToUpperInvariant();

        return EsTraspaso(procedureTypeCode, family, tipologiaCodigo, modalidad)
            ? NombreTraspasoFallback
            : NombreMatriculaFallback;
    }

    /// <summary>
    /// Código canónico de catálogo a partir de lo que mande el simulador o el wizard.
    /// Desconocido o vacío ⇒ traspaso (el default histórico del simulador).
    /// </summary>
    public static string CanonicalCode(string? procedureTypeCode, string? tipologiaCodigo)
    {
        var raw = FirstNonEmpty(procedureTypeCode, tipologiaCodigo);
        if (raw is null)
            return CodigoTraspasoStandard;

        if (EsCodigoTraspaso(raw))
            return CodigoTraspasoStandard;

        if (EsCodigoMatricula(raw))
            return CodigoMatriculaNueva;

        return raw.ToUpperInvariant();
    }

    public static bool EsCodigoTraspaso(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var v = value.Trim();
        if (v.Equals(CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase)
            || v.Equals(TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase)
            || v.Equals(TramiteModalidadEntradaCodes.Traspaso, StringComparison.OrdinalIgnoreCase))
            return true;

        return v.Contains("TRASPASO", StringComparison.OrdinalIgnoreCase);
    }

    public static bool EsCodigoMatricula(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var v = value.Trim();
        return v.Equals(CodigoMatriculaNueva, StringComparison.OrdinalIgnoreCase)
            || v.Equals(TramiteTipologiaCatalog.CodigoMatriculaInicial, StringComparison.OrdinalIgnoreCase)
            || v.Equals("matricula", StringComparison.OrdinalIgnoreCase)
            || v.Equals(TramiteModalidadEntradaCodes.MatriculaInicial, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EqualsFamily(string? family, string expected) =>
        string.Equals(family?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(string? a, string? b)
    {
        if (!string.IsNullOrWhiteSpace(a))
            return a.Trim();
        return string.IsNullOrWhiteSpace(b) ? null : b.Trim();
    }
}
