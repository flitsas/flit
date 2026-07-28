namespace Flit.Tramites.Application.Documents;

/// <summary>Datos de una parte para el documento (FUR / compraventa).</summary>
/// <param name="EsJuridica">
/// HU #10688 — persona jurídica: el <see cref="Nombre"/> es la razón social y NO debe trocearse en
/// apellidos/nombres; el mapper la escribe completa en la casilla de nombre.
/// </param>
public sealed record DocumentParte(
    string Rol,
    string? Nombre,
    string? Documento,
    string? Email,
    string? DocumentType = null,
    string? Phone = null,
    string? Address = null,
    string? City = null,
    bool EsJuridica = false,
    // ADR-0036 (HU #10914/#10915) — representante legal del mandante (solo persona jurídica): quien
    // firma en representación de la empresa en la solicitud virtual y el mandato. Null si no aplica.
    string? RepresentanteLegalNombre = null,
    string? RepresentanteLegalTipoDoc = null,
    string? RepresentanteLegalDocumento = null);

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
/// Metadatos de la firma del baúl estampados junto a la imagen en el FUR (ADR-0025 §4): documento y
/// nombre del firmante, vigencia y UUID de la firma custodiada.
/// </summary>
public sealed record FirmaBaulMetadata(
    string DocumentNumber,
    string FullName,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    Guid SignatureVaultId,
    // HU #10930 (Feature #10929): código alfanumérico digitado por el usuario en el baúl. Es el valor
    // que se estampa como "Hash" en el FUR (NO el UUID de la fila SignatureVaultId). null si no lo trae.
    string? Hash = null);

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
    IReadOnlyDictionary<string, FirmaBaulMetadata>? FirmaBaulMetadatos = null,
    // HU #10463 — false cuando NO hay validación de identidad aprobada+vigente: el FUR se pinta con
    // el sello "NO FIRMADO" en el espacio de firma. Por defecto true (comportamiento previo intacto).
    bool IdentidadValidada = true,
    // HU #10488 — sello de la validación biométrica por parte ("comprador"/"vendedor"): texto con
    // documento, uuid, serie/hash del certificado (firmaSerie de Kyverum) y fechas de aprobación/vencimiento.
    // Se pinta en el espacio de firma del FUR. Vacío/null ⇒ se cae al sello previo (SellosFirma).
    IReadOnlyDictionary<string, string>? SellosIdentidad = null,
    // HU #10601 (Feature #10585) — marcación de prenda/gravamen en el FUR: TienePrenda marca el
    // checkbox requested_process_11 cuando la decisión de prenda vigente implica gravamen
    // (solicitar/registrar). AcreedorPrenda es el beneficiario del gravamen. Por defecto sin prenda.
    bool TienePrenda = false,
    string? AcreedorPrenda = null,
    // ADR-0036 (HU #10914/#10915) — las firmas (mandato / solicitud virtual) solo se muestran en
    // estado distinto de borrador. Por defecto true (no afecta FUR/compraventa).
    bool FirmasVisibles = true,
    // HU #10920 (Feature #10918) — plantilla de FUR a generar según la clasificación del vehículo
    // (resuelta por IFurTemplateResolver). Por defecto AUTOMOTOR (comportamiento previo intacto).
    FurTemplateFormat TemplateFormat = FurTemplateFormat.Automotor)
{
    public string? Vin => Vehiculo.Vin;
    public string? Placa => Vehiculo.Placa;

    /// <summary>La parte radicadora (comprador en matrícula; comprador en traspaso es el adquiriente).</summary>
    public DocumentParte? Radicador => Partes.FirstOrDefault(p =>
        string.Equals(p.Rol, "comprador", StringComparison.OrdinalIgnoreCase))
        ?? (Partes.Count > 0 ? Partes[0] : null);
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
/// Contrato del generador de la <b>Solicitud de trámite de forma virtual</b> (ADR-0036, HU #10914).
/// Aplica SIEMPRE (persona natural y jurídica); solo varía el firmante (persona natural a nombre
/// propio; persona jurídica su representante legal). Implementación productiva vía QuestPDF (tipo
/// <c>tramite_virtual</c>), fusionable al Expediente Consolidado (mismo patrón que compraventa/RUES).
/// </summary>
public interface ISolicitudVirtualGenerator
{
    /// <summary>Genera la solicitud de trámite virtual (tipo <c>tramite_virtual</c>).</summary>
    GeneratedDocument GenerateSolicitudVirtual(FurDocumentData data);
}

/// <summary>
/// Firmante del mandato (MANDATARIO) resuelto para el trámite (ADR-0036, HU #10915/#10916): el
/// representante que el OT registró en <c>admin.mandate_signers</c> y firma en nombre de la compañía
/// gestora. <c>null</c> mientras el trámite está en <i>preparado</i> y aún no se ha elegido/filtrado el
/// firmante (se regenera al aprobar, HU #10916): el PDF pinta placeholders y no muestra el bloque de firmas.
/// </summary>
public sealed record MandatarioFirmante(string? Nombre, string? Documento);

/// <summary>
/// Datos para el <b>Contrato Privado de Mandato</b> (ADR-0036, HU #10915). El MANDANTE es la parte que
/// radica (<see cref="FurDocumentData.Radicador"/>): persona natural a nombre propio, o el representante
/// legal en nombre de la empresa. El MANDATARIO depende del OT: institucional fijo (Sabaneta UT-SETSA /
/// Bello UT-MAB, tomado de <see cref="InstitutionalMandataryName"/>/<see cref="InstitutionalMandataryNit"/>)
/// o el firmante persona (<see cref="Mandatario"/>) de la plantilla genérica/Bello.
/// <para><b>Texto legal:</b> transcrito de las plantillas legacy de FLIT 1.0; queda marcado para
/// revisión del PO (ADR-0036 §10.2) antes de considerar cerrada la HU.</para>
/// </summary>
public sealed record MandatoData(
    FurDocumentData Tramite,
    string TemplateCode,
    string? InstitutionalMandataryName,
    string? InstitutionalMandataryNit,
    MandatarioFirmante? Mandatario);

/// <summary>
/// Contrato del generador del <b>Contrato Privado de Mandato</b> (ADR-0036, HU #10915). Solo aplica
/// cuando el OT/persona exige mandato (<c>TramiteDocumentContext.ExigeMandato</c>: persona jurídica
/// siempre; persona natural solo si el OT lo configura). Implementación productiva vía QuestPDF (tipo
/// <c>mandato</c>), fusionable al Expediente Consolidado (mismo patrón que compraventa/RUES/solicitud
/// virtual). La variante de plantilla la decide <c>MandatoTemplateResolver</c> (dominio) por el
/// <c>template_code</c> del OT.
/// </summary>
public interface IMandatoGenerator
{
    /// <summary>Genera el contrato de mandato (tipo <c>mandato</c>).</summary>
    GeneratedDocument GenerateMandato(MandatoData data);
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

/// <summary>Una actividad económica del RUES (HU #10589): código CIIU, nombre y descripción. Nulos → en blanco.</summary>
public sealed record RuesActividad(string? Codigo, string? Nombre, string? Descripcion);

/// <summary>
/// Datos para el Certificado RUES (HU #10589): razón social + NIT del actor persona jurídica y su
/// estado en RUES. En modo mock el estado es "ACTIVA"; con el proveedor real se enriquece con
/// matrícula mercantil / cámara de comercio. HU #10589 (Feature #10852) — se amplía a la tabla
/// certificadora completa según la muestra oficial: REGISTRO COMERCIAL, REPRESENTACIÓN LEGAL y
/// ACTIVIDADES ECONÓMICAS. Todo lo que no devuelve la consulta se deja en blanco.
/// </summary>
public sealed record RuesCertificateData(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string RazonSocial,
    string Nit,
    string Estado,
    // Tabla certificadora del RUES (existencia y representación legal). Nulos → en blanco.
    string? MatriculaMercantil = null,
    string? CamaraComercio = null,
    string? Sigla = null,
    string? FechaMatricula = null,
    string? UltimoAnoRenovado = null,
    string? FechaRenovacion = null,
    string? Direccion = null,
    string? Municipio = null,
    string? Categoria = null,
    string? ActividadEconomica = null,
    string? TipoOrganizacion = null,
    // HU #10589 (Feature #10852) — campos adicionales del REGISTRO COMERCIAL de la muestra oficial.
    string? TipoCompania = null,
    string? Email = null,
    string? IdRm = null,
    string? FechaActualizacion = null,
    string? RazonCancelacion = null,
    // Bloque de texto de la REPRESENTACIÓN LEGAL (facultades del representante) y lista de ACTIVIDADES.
    string? RepresentacionLegal = null,
    IReadOnlyList<RuesActividad>? Actividades = null);

/// <summary>
/// Contrato del generador del Certificado RUES. La implementación productiva es
/// <c>RuesCertificatePdfGenerator</c> (Infrastructure): PDF real vía QuestPDF (tipo 'certificado_rues')
/// que pasa IsMergeableMime y se fusiona en el Expediente Consolidado (mismo patrón que el certificado
/// de identidad).
/// </summary>
public interface IRuesCertificateGenerator
{
    /// <summary>Genera el certificado RUES de una persona jurídica (tipo 'certificado_rues').</summary>
    GeneratedDocument GenerateRuesCertificate(RuesCertificateData data);
}

/// <summary>
/// Una entrada del Certificado RNMC: el resultado de la consulta de medidas correctivas de UNA parte
/// (comprador/vendedor) del trámite.
/// </summary>
/// <param name="Estado">
/// Texto ya resuelto para el usuario ("SIN MEDIDAS CORRECTIVAS" / "CON MEDIDAS CORRECTIVAS" /
/// "SIN DATOS" / "NO VERIFICABLE"): el certificado NO traduce estados del preflight, solo los pinta.
/// </param>
public sealed record RnmcCertificateEntry(
    string Rol,
    string Nombre,
    string Documento,
    string Estado,
    string? Detalle);

/// <summary>
/// Datos para el Certificado RNMC (HU #10762): resultado por parte de la consulta al Registro Nacional
/// de Medidas Correctivas, tomado del snapshot de preflight.
/// <para><b>Restricción dura:</b> este record NUNCA transporta el identificador del proveedor
/// (p. ej. "verifik_rnmc"). La fuente que ve el usuario es la entidad oficial (Policía Nacional), no el
/// integrador; el generador la escribe como literal.</para>
/// </summary>
/// <param name="ConsultadoEn">Fecha de la corrida de preflight que produjo estos resultados.</param>
public sealed record RnmcCertificateData(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    DateTimeOffset ConsultadoEn,
    IReadOnlyList<RnmcCertificateEntry> Entradas);

/// <summary>
/// Contrato del generador del Certificado RNMC. La implementación productiva es
/// <c>RnmcCertificatePdfGenerator</c> (Infrastructure): PDF real vía QuestPDF (tipo 'certificado_rnmc')
/// que pasa IsMergeableMime y se fusiona en el Expediente Consolidado (mismo patrón que el RUES).
/// </summary>
public interface IRnmcCertificateGenerator
{
    /// <summary>Genera el certificado RNMC del trámite (tipo 'certificado_rnmc').</summary>
    GeneratedDocument GenerateRnmcCertificate(RnmcCertificateData data);
}
