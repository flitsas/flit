namespace Flit.Impuestos.Domain;

/// <summary>
/// Una máquina de estado del dominio (plan §3) recibió un evento que su tabla no admite desde el estado actual. Las API solo
/// aplican transiciones permitidas: esta excepción se responde como 409 con <see cref="Codigo"/>.
/// </summary>
/// <example>
/// <code>
/// throw TransicionInvalida.FueraDeTabla("LISTO", "Alta201");
/// throw TransicionInvalida.Con(CierreLote.CodigoYaTerminada, "La liquidación ya terminó.");
/// </code>
/// </example>
public sealed class TransicionInvalida : InvalidOperationException
{
    /// <summary>Código de una transición fuera de la tabla.</summary>
    public const string CodigoPorDefecto = "TRANSICION_INVALIDA";

    public TransicionInvalida()
        : base("Transición de estado no permitida.")
    {
    }

    public TransicionInvalida(string message)
        : base(message)
    {
    }

    public TransicionInvalida(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Código estable para la API (<see cref="CodigoPorDefecto"/> u otro de negocio, como <c>YA_TERMINADA</c>).</summary>
    public string Codigo { get; private init; } = CodigoPorDefecto;

    /// <summary>Transición fuera de la tabla: desde <paramref name="desde"/> no se admite <paramref name="evento"/>.</summary>
    public static TransicionInvalida FueraDeTabla(string desde, string evento) => new($"Desde {desde} no se admite {evento}.");

    /// <summary>Regla de negocio con su propio código (no relanzable, ya terminada).</summary>
    public static TransicionInvalida Con(string codigo, string mensaje) => new(mensaje) { Codigo = codigo };
}
