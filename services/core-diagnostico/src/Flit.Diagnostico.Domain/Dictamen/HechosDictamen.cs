namespace Flit.Diagnostico.Domain.Dictamen;

/// <summary>
/// Estado de una fuente obligatoria ya leído por quien arma el dictamen. <see cref="Disponible"/> trae lectura.
/// <see cref="Error"/>, <see cref="NoAplica"/> y <see cref="SinResultado"/> disparan <c>FUENTE_PENDIENTE</c>.
/// </summary>
public enum EstadoFuenteDictamen
{
    /// <summary>La celda llegó con resultado y la lectura correspondiente viene en los hechos.</summary>
    Disponible,

    /// <summary>La consulta falló.</summary>
    Error,

    /// <summary>La celda no aplica.</summary>
    NoAplica,

    /// <summary>La consulta cerró sin resultado utilizable.</summary>
    SinResultado,
}

/// <summary>Un acuerdo de pago ya leído. <see cref="SaldoPendientePesos"/> en cero está al día y no dispara.</summary>
public sealed record AcuerdoPago(long SaldoPendientePesos);

/// <summary>
/// SIMIT ya leído, sin documento ni nombre. El saldo del acuerdo va en pesos enteros; el evaluador no lo calcula.
/// </summary>
public sealed record HechosSimit(
    int CantidadComparendos,
    bool? PazYSalvo,
    bool Suspendida,
    bool Cancelada,
    IReadOnlyList<AcuerdoPago> Acuerdos);

/// <summary>
/// Licencia ya resumida de una persona. <see cref="CertificadoAptitudVigente"/> en false dispara aunque el estado
/// de la licencia no sea suspendida, vencida ni cancelada. Null en los hechos significa que esa lectura no se pasó.
/// </summary>
public sealed record LicenciaConductor(string? Estado, bool CertificadoAptitudVigente);

/// <summary>
/// RUNT del vehículo ya leído para el dictamen. Las listas de SOAT y RTM son los estados de cada póliza o revisión.
/// La prenda, el gravamen y los conteos salen de esa lectura; este tipo no guarda documentos ni nombres.
/// </summary>
public sealed record HechosRuntVehiculo(
    string? EstadoVehiculo,
    DateOnly? FechaMatricula,
    bool EsLeasing,
    bool HayDocumentoLocatario,
    IReadOnlyList<string?> EstadosSoat,
    IReadOnlyList<string?> EstadosRtm,
    int Limitaciones,
    bool TieneGravamenes,
    int GarantiasMobiliarias,
    bool TienePrenda,
    int Infracciones);

/// <summary>
/// Entrada del evaluador. <see cref="Hoy"/> fija el corte de los 5 años de matrícula. El impuesto en null o en cero
/// no dispara. Las cuatro fuentes obligatorias son el RUNT y el SIMIT del vehículo y del propietario.
/// </summary>
public sealed record HechosDictamen(
    string TipoTramiteCodigo,
    DateOnly Hoy,
    EstadoFuenteDictamen EstadoRunt,
    EstadoFuenteDictamen EstadoSimitVehiculo,
    EstadoFuenteDictamen EstadoRuntPropietario,
    EstadoFuenteDictamen EstadoSimitPropietario,
    HechosRuntVehiculo? Runt,
    HechosSimit? SimitVehiculo,
    HechosSimit? SimitPropietario,
    HechosSimit? SimitLocatario,
    LicenciaConductor? LicenciaPropietario,
    LicenciaConductor? LicenciaLocatario,
    long? ImpuestoMontoPesos);
