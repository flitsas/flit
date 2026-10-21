namespace Flit.Diagnostico.Domain.Vehiculos;

/// <summary>
/// Resultado de <see cref="ValidadorIdentificador.Validar"/> (HU #13740, F02-H2). <see cref="Normalizado"/> es el valor que
/// alimenta <c>diagnostico.vehiculos.identificador_norm</c>; <see cref="Tipo"/> es la clase del identificador o
/// <see langword="null"/> cuando no es placa ni VIN (código <see cref="CodigoInvalido"/> en la tabla de casos compartida).
/// </summary>
/// <param name="Normalizado">Identificador en mayúsculas y sin espacios en blanco.</param>
/// <param name="Tipo">Clase detectada; <see langword="null"/> si el identificador es inválido.</param>
public sealed record ResultadoValidacion(string Normalizado, TipoIdentificador? Tipo)
{
    /// <summary>
    /// Código del identificador inválido en <c>contracts/fixtures/validacion/identificadores-vehiculo.json</c>. No es un valor
    /// de <see cref="TipoIdentificador"/>: un identificador inválido nunca se guarda ni llega a una fuente externa.
    /// </summary>
    public const string CodigoInvalido = "INVALIDO";

    /// <summary>Verdadero si el identificador es una placa o un VIN.</summary>
    public bool EsValido => Tipo is not null;
}
