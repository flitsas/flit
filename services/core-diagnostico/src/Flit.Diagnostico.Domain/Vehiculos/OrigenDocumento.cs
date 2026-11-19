namespace Flit.Diagnostico.Domain.Vehiculos;

/// <summary>
/// De dónde salió el documento del locatario de un vehículo (plan §5, R-3). En la base: <c>DECLARADO_CLIENTE</c> o
/// <c>PROVEEDOR</c>. El autollenado desde Intempo guarda <see cref="Proveedor"/>; el declarado en la captura queda de respaldo.
/// </summary>
public enum OrigenDocumento
{
    /// <summary>Lo declaró el cliente en la captura (masiva o express); no es verificable.</summary>
    DeclaradoCliente,

    /// <summary>Lo trajo el proveedor del RUNT (Intempo) con el vehículo.</summary>
    Proveedor,
}
