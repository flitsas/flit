namespace Flit.Tramites.Application.Documents;

/// <summary>Datos de una parte para el documento (FUR / compraventa).</summary>
public sealed record DocumentParte(
    string Rol,
    string? Nombre,
    string? Documento,
    string? Email,
    string? DocumentType = null,
    string? Phone = null,
    string? Address = null,
    string? City = null);

/// <summary>
/// Atributos del vehículo embebidos en el FUR (de field_values, Slice 5/M5).
/// Los campos HU #10256 (motor, chasis, serie, carrocería, servicio, capacidad, peso, ejes)
/// son opcionales para compatibilidad con call-sites existentes; el PDF los muestra como "-".
/// </summary>
public sealed record VehiculoDatos(
    string? Marca,
    string? Linea,
    string? Modelo,
    string? Color,
    string? Clase,
    string? Combustible,
    string? Cilindraje,
    string? Vin,
    string? Placa,
    // HU #10256 — datos ampliados desde field_values RUNT/Verifik
    string? NumeroMotor = null,
    string? NumeroChasis = null,
    string? NumeroSerie = null,
    string? TipoCarroceria = null,
    string? TipoServicio = null,
    string? Capacidad = null,
    string? PesoBruto = null,
    string? NumeroEjes = null);

/// <summary>Organismo de tránsito seleccionado (de field_values transit_office_*).</summary>
public sealed record OrganismoTransito(string? Codigo, string? Nombre, string? Ciudad);

/// <summary>
/// Datos del trámite ensamblados para generar los documentos. Vehículo (atributos completos),
/// partes (comprador/vendedor), organismo de tránsito, valor, causal y referencias del sello de firma.
/// </summary>
public sealed record FurDocumentData(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string Modalidad,
    string? TipologiaCodigo,
    VehiculoDatos Vehiculo,
    OrganismoTransito Organismo,
    IReadOnlyList<DocumentParte> Partes,
    decimal? ValorVenta,
    string? Causal,
    IReadOnlyList<string> SellosFirma,
    DateTime? FechaTramite = null,
    string? Observaciones = null,
    IReadOnlyDictionary<string, byte[]>? FirmaImagenes = null,
    // HU #10463 — false cuando NO hay validación de identidad aprobada+vigente: el FUR se pinta con
    // el sello "NO FIRMADO" en el espacio de firma. Por defecto true (comportamiento previo intacto).
    bool IdentidadValidada = true,
    // HU #10488 — sello de la validación biométrica por parte ("comprador"/"vendedor"): texto con
    // documento, uuid, serie/hash del certificado (firmaSerie de Kyverum) y fechas de aprobación/vencimiento.
    // Se pinta en el espacio de firma del FUR. Vacío/null ⇒ se cae al sello previo (SellosFirma).
    IReadOnlyDictionary<string, string>? SellosIdentidad = null)
{
    public string? Vin => Vehiculo.Vin;
    public string? Placa => Vehiculo.Placa;
}

/// <summary>Un documento generado, listo para persistir vía IAttachmentStorage.</summary>
public sealed record GeneratedDocument(string Tipo, string Filename, string Mimetype, byte[] Content);

/// <summary>
/// Contrato del generador de documentos del trámite (FUR + contrato de compraventa). La
/// implementación productiva es <c>FurOverlayDocumentGenerator</c> (Infrastructure): FUR por overlay
/// PdfSharpCore sobre las plantillas blank (HU #10256) y compraventa vía QuestPDF.
/// <see cref="MockFurDocumentGenerator"/> se conserva SOLO para tests (sin registro en DI). Patrón
/// contract-first: los handlers no cambian al swapear la implementación.
/// </summary>
public interface IFurDocumentGenerator
{
    /// <summary>Genera el FUR (Formulario Único de Registro) con los datos del trámite.</summary>
    GeneratedDocument GenerateFur(FurDocumentData data);

    /// <summary>Genera el contrato de compraventa (solo traspaso) con los datos del trámite.</summary>
    GeneratedDocument GenerateCompraventa(FurDocumentData data);
}

/// <summary>
/// Datos para el Certificado de validación de identidad: comprador (nombre/doc) + resultado de la
/// biométrica (score + estado APROBADO/RECHAZADO).
/// </summary>
public sealed record IdentityCertificateData(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string CompradorNombre,
    string CompradorDocumento,
    int Score,
    string Resultado);

/// <summary>
/// Contrato del generador del Certificado de validación de identidad. La implementación productiva es
/// <c>IdentityCertificatePdfGenerator</c> (Infrastructure): PDF real vía QuestPDF (HU #10458) que pasa
/// IsMergeableMime y se fusiona en el Expediente Consolidado. <see cref="MockIdentityCertificateGenerator"/>
/// se conserva SOLO para tests (sin registro en DI).
/// </summary>
public interface IIdentityCertificateGenerator
{
    /// <summary>Genera el certificado de validación de identidad (tipo 'certificado_identidad').</summary>
    GeneratedDocument GenerateIdentityCertificate(IdentityCertificateData data);
}
