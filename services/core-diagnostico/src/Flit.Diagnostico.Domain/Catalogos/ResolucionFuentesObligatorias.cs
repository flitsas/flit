using System.Text.RegularExpressions;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Catalogos;

/// <summary>
/// Una definición de fuentes obligatorias todavía en memoria (ADR-0078). <see cref="Familia"/> y
/// <see cref="TipoTramiteCodigo"/> son excluyentes. <see cref="Fuentes"/> vacía con la definición presente es la lista
/// vacía explícita; no es lo mismo que no tener definición.
/// </summary>
/// <param name="Familia">Familia del trámite, o <see langword="null"/> si la definición es de un tipo.</param>
/// <param name="TipoTramiteCodigo">Código del tipo, o <see langword="null"/> si la definición es de una familia.</param>
/// <param name="Fuentes">Fuentes de la definición. Vacía es un vacío explícito.</param>
public sealed record DefinicionDeFuentes(string? Familia, string? TipoTramiteCodigo, IReadOnlyList<Fuente> Fuentes);

/// <summary>
/// Rechazo de un guardado de definición (ADR-0078). No escribe nada: el PUT decide el HTTP a partir de
/// <see cref="Codigo"/>. La forma (<see cref="DefinicionAmbigua"/>) se evalúa antes que el negocio.
/// </summary>
/// <param name="Codigo">Código que lee el test y la pantalla.</param>
/// <param name="Campo"><c>familia</c> o <c>fuentes</c>.</param>
public sealed record RechazoGuardadoFuentes(string Codigo, string Campo)
{
    /// <summary>Familia y tipo a la vez, ninguno, o un texto fuera de <c>^[A-Z][A-Z0-9_]{0,63}$</c>.</summary>
    public const string DefinicionAmbigua = "DEFINICION_AMBIGUA";

    /// <summary>El arreglo incluye <see cref="Fuente.Runt"/>.</summary>
    public const string FuenteRuntNoConfigurable = "FUENTE_RUNT_NO_CONFIGURABLE";

    /// <summary><c>TRASPASO_UNILATERAL</c> incluye cualquier <c>SIMIT_*</c>.</summary>
    public const string SimitProhibidoEnUnilateral = "SIMIT_PROHIBIDO_EN_UNILATERAL";

    /// <summary>Falta <see cref="Fuente.SimitPropietario"/> en un alcance del piso.</summary>
    public const string PisoNoDispensable = "PISO_NO_DISPENSABLE";

    /// <summary>Campo de <see cref="DefinicionAmbigua"/>.</summary>
    public const string CampoFamilia = "familia";

    /// <summary>Campo de los rechazos de fuente.</summary>
    public const string CampoFuentes = "fuentes";
}

/// <summary>
/// Resolución y piso de las fuentes obligatorias del trámite (ADR-0078). La semilla, la herencia y los rechazos del
/// guardado viven aquí: el proyector y el PUT leen esta clase y no copian los códigos. No persiste.
/// </summary>
public static partial class ResolucionFuentesObligatorias
{
    /// <summary>Familia que cubre un tipo de traspaso sin cabecera propia.</summary>
    public const string FamiliaTraspaso = "TRASPASO";

    /// <summary>Traspaso ordinario. El piso no se puede quitar.</summary>
    public const string TraspasoStandard = "TRASPASO_STANDARD";

    /// <summary>Se trata como el ordinario hasta que producto diga otra cosa. El piso no se puede quitar.</summary>
    public const string TraspasoTransferenciaDeDominio = "TRASPASO_TRANSFERENCIA_DE_DOMINIO";

    /// <summary>Vacío explícito. No hereda un <c>SIMIT_*</c> de la familia.</summary>
    public const string TraspasoUnilateral = "TRASPASO_UNILATERAL";

    private static readonly IReadOnlyList<Fuente> Piso = [Fuente.SimitPropietario];
    private static readonly IReadOnlyList<Fuente> Vacia = [];

    /// <summary>
    /// Cuatro cabeceras de la semilla. La familia y los dos ordinarios llevan <see cref="Fuente.SimitPropietario"/>.
    /// <see cref="TraspasoUnilateral"/> lleva la lista vacía: la cabecera existe y no hereda.
    /// </summary>
    public static IReadOnlyList<DefinicionDeFuentes> Semilla { get; } =
    [
        new(FamiliaTraspaso, null, Piso),
        new(null, TraspasoStandard, Piso),
        new(null, TraspasoTransferenciaDeDominio, Piso),
        new(null, TraspasoUnilateral, Vacia),
    ];

    /// <summary>
    /// Fuentes que publica un tipo. Si hay cabecera de ese tipo, su lista gana aunque esté vacía. Si no, la de la
    /// familia. Si tampoco, <see cref="Vacia"/>.
    /// </summary>
    /// <param name="tipoTramiteCodigo">Código del tipo publicado.</param>
    /// <param name="familia">Familia del tipo, o <see langword="null"/> si no tiene.</param>
    /// <param name="definiciones">Cabeceras guardadas, cada una con sus líneas (cero líneas es vacío explícito).</param>
    public static IReadOnlyList<Fuente> Resolver(
        string tipoTramiteCodigo,
        string? familia,
        IEnumerable<DefinicionDeFuentes> definiciones)
    {
        ArgumentNullException.ThrowIfNull(tipoTramiteCodigo);
        ArgumentNullException.ThrowIfNull(definiciones);

        DefinicionDeFuentes? porTipo = null;
        DefinicionDeFuentes? porFamilia = null;
        foreach (var definicion in definiciones)
        {
            if (definicion.TipoTramiteCodigo == tipoTramiteCodigo)
            {
                porTipo ??= definicion;
                continue;
            }

            if (familia is not null && definicion.Familia == familia && definicion.TipoTramiteCodigo is null)
                porFamilia ??= definicion;
        }

        if (porTipo is not null)
            return porTipo.Fuentes;
        if (porFamilia is not null)
            return porFamilia.Fuentes;
        return Vacia;
    }

    /// <summary>
    /// Rechazo del PUT, o <see langword="null"/> si el guardado puede seguir. No escribe. Una fuente repetida cuenta
    /// como presente. El orden es el del ADR: alcance, después <see cref="Fuente.Runt"/>, después un <c>SIMIT_*</c> en
    /// el unilateral, después quitar el piso.
    /// </summary>
    /// <param name="familia">Familia pedida, o <see langword="null"/>.</param>
    /// <param name="tipoTramiteCodigo">Tipo pedido, o <see langword="null"/>.</param>
    /// <param name="fuentes">Lista completa que se quiere guardar.</param>
    public static RechazoGuardadoFuentes? ValidarGuardado(
        string? familia,
        string? tipoTramiteCodigo,
        IReadOnlyList<Fuente> fuentes)
    {
        ArgumentNullException.ThrowIfNull(fuentes);

        var tieneFamilia = familia is not null;
        var tieneTipo = tipoTramiteCodigo is not null;
        if (tieneFamilia == tieneTipo
            || (tieneFamilia && !PatronAlcance().IsMatch(familia!))
            || (tieneTipo && !PatronAlcance().IsMatch(tipoTramiteCodigo!)))
        {
            return new RechazoGuardadoFuentes(RechazoGuardadoFuentes.DefinicionAmbigua, RechazoGuardadoFuentes.CampoFamilia);
        }

        if (fuentes.Contains(Fuente.Runt))
            return new RechazoGuardadoFuentes(RechazoGuardadoFuentes.FuenteRuntNoConfigurable, RechazoGuardadoFuentes.CampoFuentes);

        if (tipoTramiteCodigo == TraspasoUnilateral && fuentes.Any(EsSimit))
            return new RechazoGuardadoFuentes(RechazoGuardadoFuentes.SimitProhibidoEnUnilateral, RechazoGuardadoFuentes.CampoFuentes);

        if (ExigePiso(familia, tipoTramiteCodigo) && !fuentes.Contains(Fuente.SimitPropietario))
            return new RechazoGuardadoFuentes(RechazoGuardadoFuentes.PisoNoDispensable, RechazoGuardadoFuentes.CampoFuentes);

        return null;
    }

    /// <summary><c>^[A-Z][A-Z0-9_]{0,63}$</c>, el mismo texto del CHECK de la cabecera.</summary>
    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronAlcance();

    private static bool ExigePiso(string? familia, string? tipo) =>
        familia == FamiliaTraspaso || tipo == TraspasoStandard || tipo == TraspasoTransferenciaDeDominio;

    /// <summary>Cualquier <c>SIMIT_*</c>: el nombre del enum empieza por <c>Simit</c> y sigue (<c>SimitVehiculo</c>).</summary>
    private static bool EsSimit(Fuente fuente)
    {
        const string prefijo = "Simit";
        var nombre = fuente.ToString();
        return nombre.StartsWith(prefijo, StringComparison.Ordinal) && nombre.Length > prefijo.Length;
    }
}
