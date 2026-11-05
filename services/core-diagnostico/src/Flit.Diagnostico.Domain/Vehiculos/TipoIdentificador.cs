namespace Flit.Diagnostico.Domain.Vehiculos;

/// <summary>
/// Clase del identificador de un vehículo del lote (plan §5, R-19), según el <c>PLATE_PATTERN</c> y la regla de VIN de
/// Trámites. En la base: <c>PLACA_CARRO</c>, <c>PLACA_MOTO</c>, <c>PLACA_REMOLQUE</c>, <c>PLACA_MAQUINARIA</c> o <c>VIN</c>.
/// </summary>
public enum TipoIdentificador
{
    /// <summary>Placa de carro: tres letras y tres dígitos.</summary>
    PlacaCarro,

    /// <summary>Placa de moto: tres letras, dos dígitos y una letra opcional.</summary>
    PlacaMoto,

    /// <summary>Placa de remolque o semirremolque: R o S y cinco o seis dígitos.</summary>
    PlacaRemolque,

    /// <summary>Placa de maquinaria: dos letras y seis dígitos (HU #13735).</summary>
    PlacaMaquinaria,

    /// <summary>Número de identificación vehicular de 17 caracteres.</summary>
    Vin,
}
