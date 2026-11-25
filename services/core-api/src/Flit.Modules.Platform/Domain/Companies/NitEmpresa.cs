namespace Flit.Modules.Platform.Domain.Companies;

/// <summary>Estado del NIT de una compañía para el alta en un proveedor (D-TAXID, DA-8; plan §1.3).</summary>
public enum EstadoNit
{
    /// <summary>Se pudo separar la base sin dígito de verificación.</summary>
    Valido = 1,

    /// <summary>No se puede separar el dígito de verificación sin ambigüedad.</summary>
    Ambiguo = 2,

    /// <summary>La compañía no tiene NIT.</summary>
    Ausente = 3,

    /// <summary>La base es la misma de una compañía ancestro (cabeza).</summary>
    IgualACabeza = 4,
}

/// <summary>
/// HU #14694 — NIT base de una compañía, sin dígito de verificación, tal como se envía al alta del proveedor (D-TAXID).
/// Todo lo que no es <see cref="EstadoNit.Valido"/> es «Requiere revisión del NIT» (DA-8). La regla vive solo aquí: la
/// usan el diálogo de activación (<c>GET …/alta-previa</c>) y el rpc <c>EmpresasService.ObtenerDatosAltaProveedor</c>.
/// </summary>
/// <param name="Estado">Resultado de la normalización.</param>
/// <param name="Base">La base solo cuando <see cref="Estado"/> es <see cref="EstadoNit.Valido"/>.</param>
public sealed record NitEmpresa(EstadoNit Estado, string? Base)
{
    /// <summary>Largo mínimo de la base, con o sin guion.</summary>
    public const int BaseMinima = 3;

    /// <summary>Largo máximo de la base cuando el NIT trae guion.</summary>
    public const int BaseMaximaConGuion = 20;

    /// <summary>Largo máximo sin guion: con 10 dígitos o más no se sabe si el último es el DV.</summary>
    public const int BaseMaximaSinGuion = 9;

    private static readonly NitEmpresa AmbiguoSinBase = new(EstadoNit.Ambiguo, null);

    /// <summary>
    /// Normaliza el NIT de la compañía (plan §1.3):
    /// 1) quita puntos y espacios y pasa a mayúsculas;
    /// 2) con guion, la base es lo que va antes (3 a 20 dígitos) y tras el guion debe haber exactamente un dígito;
    /// 3) sin guion, de 3 a 9 dígitos es la base tal cual; con 10 o más, con letras o con menos de 3, es ambiguo;
    /// 4) vacío es <see cref="EstadoNit.Ausente"/>; base igual a la de un ancestro es <see cref="EstadoNit.IgualACabeza"/>.
    /// </summary>
    /// <param name="taxId"><c>identity.tenants.tax_id</c> de la compañía.</param>
    /// <param name="nitsAncestros">NIT de sus ancestros; los que no dan una base válida no se comparan.</param>
    public static NitEmpresa NormalizarBase(string? taxId, IEnumerable<string?>? nitsAncestros = null)
    {
        var propio = SepararBase(taxId);
        if (propio.Estado != EstadoNit.Valido || nitsAncestros is null)
            return propio;

        foreach (var ancestro in nitsAncestros)
        {
            if (SepararBase(ancestro) is { Estado: EstadoNit.Valido, Base: { } suya } && string.Equals(suya, propio.Base, StringComparison.Ordinal))
                return new NitEmpresa(EstadoNit.IgualACabeza, null);
        }

        return propio;
    }

    /// <summary>Código del contrato (<c>VALIDO</c>, <c>AMBIGUO</c>, <c>AUSENTE</c>, <c>IGUAL_A_CABEZA</c>).</summary>
    public static string Codigo(EstadoNit estado) => estado switch
    {
        EstadoNit.Valido => "VALIDO",
        EstadoNit.Ambiguo => "AMBIGUO",
        EstadoNit.Ausente => "AUSENTE",
        EstadoNit.IgualACabeza => "IGUAL_A_CABEZA",
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado de NIT sin código en el contrato."),
    };

    private static NitEmpresa SepararBase(string? taxId)
    {
        var limpio = string.Concat((taxId ?? string.Empty).Where(c => c != '.' && !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (limpio.Length == 0)
            return new NitEmpresa(EstadoNit.Ausente, null);

        var guion = limpio.IndexOf('-', StringComparison.Ordinal);
        if (guion < 0)
        {
            return SoloDigitos(limpio) && limpio.Length is >= BaseMinima and <= BaseMaximaSinGuion
                ? new NitEmpresa(EstadoNit.Valido, limpio)
                : AmbiguoSinBase;
        }

        var antes = limpio[..guion];
        var despues = limpio[(guion + 1)..];
        return SoloDigitos(antes) && antes.Length is >= BaseMinima and <= BaseMaximaConGuion
               && despues.Length == 1 && char.IsAsciiDigit(despues[0])
            ? new NitEmpresa(EstadoNit.Valido, antes)
            : AmbiguoSinBase;
    }

    private static bool SoloDigitos(string valor) => valor.Length > 0 && valor.All(char.IsAsciiDigit);
}
