namespace Flit.Tramites.Domain.ExternalSync;

/// <summary>
/// HU #13079 (Feature #13066, Épica #12737) — ítem del feed de sincronización, con los nombres y la
/// forma del contrato v3.1 §4. Todas las claves van siempre presentes: el dato ausente es <c>null</c>,
/// nunca una clave omitida ni una cadena vacía; <see cref="Compradores"/> nunca es <c>null</c>.
/// Sin enmascarar: el enmascarado de datos personales lo aplica el endpoint (HU #13081).
/// </summary>
public sealed record ProcedureSyncItem(
    Guid Id,
    string Radicado,
    long Consecutivo,
    long SyncVersion,
    DateTimeOffset FechaUltimoCambio,
    bool Eliminado,
    string Estado,
    ProcedureSyncTramite Tramite,
    DateTimeOffset FechaCreacion,
    DateTimeOffset? FechaRadicacion,
    DateTimeOffset? FechaAprobacion,
    ProcedureSyncVehiculo? Vehiculo,
    ProcedureSyncOrganismo? Organismo,
    IReadOnlyList<ProcedureSyncComprador> Compradores,
    ProcedureSyncFactura? Factura,
    ProcedureSyncCompania CompaniaGestora);

public sealed record ProcedureSyncTramite(string Codigo, string Nombre, string? Familia);

public sealed record ProcedureSyncVehiculo(
    string? Vin,
    string? Placa,
    string? Clase,
    string? Marca,
    string? Linea,
    int? ModeloAno,
    string? Carroceria,
    int? Cilindraje,
    string? CilindrajeTexto,
    int? Capacidad,
    string? NumeroMotor,
    string? NumeroSerie,
    ProcedureSyncTipoServicio? TipoServicio);

public sealed record ProcedureSyncTipoServicio(string Codigo, string? Nombre);

public sealed record ProcedureSyncOrganismo(
    string? CodigoTransito,
    string? Nombre,
    string? CodigoSecretaria,
    string? Ciudad,
    string? Departamento);

public sealed record ProcedureSyncComprador(
    int Ordinal,
    decimal? PorcentajeParticipacion,
    string RolActor,
    string? TipoPersona,
    string? TipoDocumento,
    string? NumeroDocumento,
    string? NombreCompleto,
    string? Direccion,
    string? Ciudad,
    string? Celular,
    string? Correo);

public sealed record ProcedureSyncFactura(Guid AdjuntoId, string? NombreArchivo, DateTimeOffset CargadaEn);

public sealed record ProcedureSyncCompania(Guid TenantId, string? Nit, string? Nombre);

/// <summary>Ítem armado y su posición en el feed, para que el endpoint avance el cursor (HU #13081).</summary>
public sealed record ProcedureSyncEntry(ProcedureSyncPosition Position, ProcedureSyncItem Item);

/// <summary>
/// Fila cruda de la lectura del feed, tal como sale de la base: textos sin normalizar, campos del
/// vehículo como texto y los actores del rol elegido (comprador, o propietario si no hay comprador).
/// </summary>
public sealed record ProcedureSyncRow(
    Guid Id,
    ProcedureSyncPosition Position,
    string ReferenceNumber,
    long Consecutivo,
    DateTimeOffset ChangedAt,
    bool IsDeleted,
    string Status,
    string ProcedureTypeCode,
    string ProcedureTypeName,
    string? ProcedureTypeFamily,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    string? Vin,
    string? Plate,
    string? VehicleClass,
    string? VehicleBrand,
    string? VehicleLine,
    string? VehicleYear,
    string? VehicleBodyType,
    string? VehicleEngineDisplacement,
    string? VehiclePassengers,
    string? VehicleEngineNumber,
    string? VehicleSeries,
    string? VehicleService,
    string? TransitOfficeCode,
    string? TransitOfficeName,
    string? TransitOfficeCityCode,
    string? TransitOfficeCityName,
    string? TransitOfficeDepartmentName,
    IReadOnlyList<ProcedureSyncActorRow> Actors,
    Guid? InvoiceAttachmentId,
    string? InvoiceFilename,
    DateTimeOffset? InvoiceUploadedAt,
    Guid TenantId,
    string? TenantTaxId,
    string? TenantLegalName);

public sealed record ProcedureSyncActorRow(
    int Ordinal,
    decimal? OwnershipPercentage,
    string ActorType,
    string? PersonType,
    string? DocumentType,
    string? DocumentNumber,
    string? FullName,
    string? Address,
    string? City,
    string? Phone,
    string? Email);
