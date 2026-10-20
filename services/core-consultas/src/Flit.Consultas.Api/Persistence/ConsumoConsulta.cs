namespace Flit.Consultas.Api.Persistence;

/// <summary>
/// Una consulta atendida (HU #13345): para que el SuperAdmin controle el costo de los proveedores por empresa, producto y
/// fuente. Sin datos personales: ni placa ni documento, solo qué se consultó, a quién y cómo terminó.
/// </summary>
public sealed class ConsumoConsulta
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Producto que consultó: el código del cliente de servicio (<c>svc-tramites</c> → <c>tramites</c>).</summary>
    public string Producto { get; set; } = string.Empty;

    /// <summary>vehiculo, conductor, multas, rnmc, rues, avaluos.</summary>
    public string Fuente { get; set; } = string.Empty;

    /// <summary>Proveedor que dio la respuesta (después del respaldo); vacío si ninguno respondió.</summary>
    public string Proveedor { get; set; } = string.Empty;

    /// <summary>verde, amarillo, rojo (semáforo) o error.</summary>
    public string Resultado { get; set; } = string.Empty;

    public bool DesdeCache { get; set; }

    public int LatenciaMs { get; set; }

    public DateTimeOffset OcurridoEn { get; set; }
}
