using Flit.Diagnostico.Api.Modulos.Comun;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>Tipo de archivo de una evidencia (HU #13846): PDF por vehículo, Excel del lote o ZIP del lote.</summary>
public enum TipoEvidencia
{
    Pdf,
    Xlsx,
    Zip,
}

/// <summary>
/// Estado de una evidencia (HU #13846). PDF y Excel nacen <see cref="Lista"/>; el ZIP pasa por la cola de F07-H5
/// (<see cref="Pendiente"/> → <see cref="Generando"/> → <see cref="Lista"/> o <see cref="Error"/>).
/// </summary>
public enum EstadoEvidencia
{
    Pendiente,
    Generando,
    Lista,
    Error,
}

/// <summary>
/// Qué evidencia es (HU #13846): el lote, el vehículo (solo en PDF) y el tipo. Una sola vigente por clave
/// (<c>ux_evidencias_vigente</c>, con <c>NULLS NOT DISTINCT</c> para el Excel y el ZIP, que no tienen vehículo).
/// </summary>
public sealed record ClaveEvidencia
{
    private ClaveEvidencia(Guid diagnosticoId, Guid? vehiculoId, TipoEvidencia tipo)
    {
        if (diagnosticoId == Guid.Empty)
            throw new ArgumentException("La evidencia exige un diagnóstico.", nameof(diagnosticoId));
        if (vehiculoId == Guid.Empty)
            throw new ArgumentException("El PDF exige un vehículo.", nameof(vehiculoId));
        DiagnosticoId = diagnosticoId;
        VehiculoId = vehiculoId;
        Tipo = tipo;
    }

    public Guid DiagnosticoId { get; }

    /// <summary>Vehículo del PDF; null en el Excel y el ZIP del lote.</summary>
    public Guid? VehiculoId { get; }

    public TipoEvidencia Tipo { get; }

    /// <summary>El PDF de un vehículo del lote.</summary>
    public static ClaveEvidencia Pdf(Guid diagnosticoId, Guid vehiculoId) => new(diagnosticoId, vehiculoId, TipoEvidencia.Pdf);

    /// <summary>El Excel del lote.</summary>
    public static ClaveEvidencia Xlsx(Guid diagnosticoId) => new(diagnosticoId, null, TipoEvidencia.Xlsx);

    /// <summary>El ZIP del lote (lo genera la cola de F07-H5).</summary>
    public static ClaveEvidencia Zip(Guid diagnosticoId) => new(diagnosticoId, null, TipoEvidencia.Zip);
}

/// <summary>
/// Un archivo generado para el cliente (<c>diagnostico.evidencias</c>, HU #13846, F07-C8, F07-C11, F07-C12, R-18): PDF, Excel
/// o ZIP con la huella de sus entradas (<see cref="HuellaEvidencia"/>) y la referencia del archivo en el file-manager. Mientras
/// la huella no cambie se entrega el mismo archivo; si cambia, la anterior pasa a <see cref="Vigente"/>=false y se conserva
/// (nunca se borra). Clase (a) de ADR-0072: FK compuestas por empresa, <c>xmin</c>, sin borrado lógico ni físico.
/// Nunca lleva origen de las celdas, usuario manual, proveedor ni transacción (R-20).
/// </summary>
public sealed class Evidencia : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa dueña del lote.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Lote de la evidencia.</summary>
    public Guid DiagnosticoId { get; init; }

    /// <summary>Vehículo del PDF; null en el Excel y el ZIP.</summary>
    public Guid? VehiculoId { get; init; }

    public TipoEvidencia Tipo { get; init; }

    /// <summary>SHA-256 en hexadecimal (minúsculas) de las entradas del archivo (<see cref="HuellaEvidencia"/>).</summary>
    public required string Huella { get; init; }

    public EstadoEvidencia Estado { get; set; }

    /// <summary>Referencia del archivo en el almacén (id del file-manager); presente si y solo si está <see cref="EstadoEvidencia.Lista"/>.</summary>
    public string? ArchivoRef { get; set; }

    /// <summary>Tamaño del archivo en bytes; presente si está lista.</summary>
    public long? TamanoBytes { get; set; }

    /// <summary>Usuario que pidió el archivo (claim <c>sub</c>).</summary>
    public Guid SolicitadoPor { get; init; }

    /// <summary>Nombre de la empresa del claim <c>company_name</c>; lo usa el ZIP, que se genera sin sesión (F07-H5).</summary>
    public string? EmpresaNombre { get; init; }

    /// <summary>
    /// La evidencia que se entrega hoy para su clave; false = reemplazada, se conserva (R-18). Un ZIP nace no vigente y pasa a
    /// vigente al quedar LISTA (HU #13850): mientras el nuevo está en la cola, el anterior LISTA se sigue entregando.
    /// </summary>
    public bool Vigente { get; set; } = true;

    /// <summary>Intentos de generación (la cola del ZIP los suma; PDF y Excel se generan en el primero).</summary>
    public int Intentos { get; set; }

    /// <summary>
    /// Cola del ZIP: en GENERANDO, hasta cuándo lo tiene tomado el trabajador; en PENDIENTE tras un fallo, desde cuándo se puede
    /// reintentar (backoff, HU #13850); null si nadie lo tiene ni espera.
    /// </summary>
    public DateTimeOffset? LeaseHasta { get; set; }

    /// <summary>Causa del fallo de generación (solo con estado ERROR); nunca la causa técnica de una fuente.</summary>
    public string? TipoError { get; set; }

    public DateTimeOffset CreadoEn { get; init; }

    /// <summary>ZIP (HU #13850): vehículos con su PDF en el archivo (al pedirlo, los que se esperan; al quedar LISTA, los que trae). 0 en PDF y Excel.</summary>
    public int VehiculosIncluidos { get; set; }

    /// <summary>ZIP (HU #13850): vehículos del lote que quedan fuera por no haber terminado (van en el índice). 0 en PDF y Excel.</summary>
    public int VehiculosPendientes { get; set; }

    /// <summary>Momento en que el archivo quedó listo; null mientras no lo está.</summary>
    public DateTimeOffset? GeneradoEn { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>): dos pedidos no invalidan dos veces la misma vigente.</summary>
    public uint Version { get; set; }
}
