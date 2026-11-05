namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Qué provocó un intento de consulta (<c>diagnostico.intentos_consulta.disparador</c>, plan §5, R-1, HU #13723).
/// </summary>
public enum DisparadorIntento
{
    /// <summary>El procesador de la cola, sin usuario.</summary>
    Auto,

    /// <summary>Reproceso de un admin o del backoffice (lleva usuario).</summary>
    Reproceso,

    /// <summary>Llenado manual del backoffice (lleva usuario, sin transacción del proveedor).</summary>
    Manual,
}
