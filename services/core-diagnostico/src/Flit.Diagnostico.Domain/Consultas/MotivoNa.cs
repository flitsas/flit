namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Por qué una celda no aplica (<c>diagnostico.consultas.motivo_na</c>, plan §5, R-9 y R-3, HU #13723). La base exige que la
/// celda tenga motivo si y solo si está en <see cref="EstadoCelda.Na"/> (<c>ck_motivo_na</c>).
/// </summary>
public enum MotivoNa
{
    /// <summary>Impuesto departamental de un vehículo de servicio público.</summary>
    ServicioPublico,

    /// <summary>core-consultas no soporta el impuesto del departamento (R-9).</summary>
    DepartamentoNoSoportado,

    /// <summary>RUNT o SIMIT del locatario sin documento declarado ni hidratado (R-3).</summary>
    SinDocumentoLocatario,
}
