using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;

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
/// <summary>
/// HU #11641 — transformaciones que el trámite declara como subtrámite simultáneo, para marcar sus
/// casillas en la rejilla "3. TRÁMITE SOLICITADO" del FUR.
///
/// <para>Es información distinta de la que va a OBSERVACIONES. El texto de observaciones se DERIVA
/// del diff snapshot RUNT vs efectivo (ADR-0029) y declara a qué se transformó; la casilla declara
/// QUÉ TRÁMITE se está solicitando. Por eso aquí basta con que el gestor lo haya declarado: si el
/// RUNT no devolvió el valor original no hay diff que calcular, pero el trámite de cambio de color
/// se solicitó igual y el formulario debe decirlo. Antes de esta HU el FUR quedaba mudo en ese caso,
/// mientras el wizard mostraba el subtrámite como activo.</para>
/// </summary>
public readonly record struct FurTransformacionesDeclaradas(
    bool Color = false,
    bool Carroceria = false,
    bool Combustible = false,
    bool Blindaje = false);

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
    // HU #10601 (Feature #10585), ampliado por HU #11257 (Feature #11254) — marcación de prenda/gravamen
    // en el FUR: PrendaMarking resuelve, YA en el dominio (PrendaDecision.ToFurMarking), qué casilla
    // marca el mapper: Constitucion → requested_process_11 (solicitar/registrar), Levantamiento →
    // requested_process_12 (levantar), Ninguna → ninguna de las dos. Antes de esta HU el modelo
    // transportaba solo `bool TienePrenda`: `levantar` colapsaba al mismo `false` que "sin prenda", así
    // que el generador no podía distinguirlos. Por defecto sin marcación.
    FurPrendaMarking PrendaMarking = FurPrendaMarking.Ninguna,
    // Beneficiario del gravamen. Se imprime en el numeral 20 «A FAVOR DE» (alert_data_code_5) y, si hay
    // nombre, también en el párrafo 23 vía FurPrendaObservation. Sin nombre: sí casilla de alerta, campo vacío.
    string? AcreedorPrenda = null,
    // HU #10920 (Feature #10918) — plantilla de FUR a generar según la clasificación del vehículo
    // (resuelta por IFurTemplateResolver). Por defecto AUTOMOTOR (comportamiento previo intacto).
    FurTemplateFormat TemplateFormat = FurTemplateFormat.Automotor,
    // Casilla 19 "EMPRESA VINCULADORA" del FUR: solo aplica a servicio público (transporte vinculado a
    // una empresa habilitada); en particular/matrícula sin vinculación queda null y la casilla sale en
    // blanco (comportamiento por defecto, sin romper trámites existentes que no traen este dato).
    string? EmpresaVinculadoraRazonSocial = null,
    string? EmpresaVinculadoraNit = null,
    // HU #11641 — subtrámites simultáneos declarados (color / carrocería / combustible / blindaje),
    // que marcan sus casillas propias. Blindaje usa SI/NO de vehículo blindado; las otras tres van
    // a la rejilla de trámite solicitado. Por defecto ninguno.
    FurTransformacionesDeclaradas Transformaciones = default,
    /// <summary>Código de <c>tramites.procedure_types.code</c> (p. ej. <c>MATRICULA_NUEVA</c>).</summary>
    string? ProcedureTypeCode = null,
    /// <summary>Nombre de <c>tramites.procedure_types.name</c>. El mandato lo usa como objeto del contrato.</summary>
    string? ProcedureTypeName = null,
    /// <summary>Familia de <c>tramites.procedure_types.family</c> (<c>MATRICULAS</c> | <c>TRASPASO</c> | <c>OTROS</c>).</summary>
    string? ProcedureFamily = null,
    /// <summary>
    /// ADR-0050 — el tipo declara si hay parte VENDEDORA (<c>gate_profile.requiresSeller</c>).
    /// <para>Convive con <see cref="ProcedureFamily"/> y no lo duplica: la familia dice QUÉ ES el
    /// trámite y esta bandera QUÉ EXIGE. Es la que decide si el FUR estampa sección de comprador y
    /// si el mandato se redacta con parte otorgante, preguntas que son de capacidad. Sustituye a
    /// deducirlo por substring sobre la tipología o la modalidad, que daba por traspaso cualquier
    /// código que contuviera esa palabra.</para>
    /// </summary>
    bool RequiereVendedor = false)
{
    public string? Vin => Vehiculo.Vin;
    public string? Placa => Vehiculo.Placa;

    // HU #11257 — aquí vivía `TienePrenda`, conservada como propiedad calculada para que los
    // call-sites que la pasaban como parámetro con nombre rompieran la compilación. Cumplida esa
    // función y migrados todos, se elimina: no le quedaba ningún consumidor y su nombre engañaba
    // —un trámite en `levantar` SÍ tiene prenda y la propiedad devolvía `false`—. La única fuente
    // de verdad es `PrendaMarking`.

    /// <summary>La parte radicadora (comprador en matrícula; comprador en traspaso es el adquiriente).</summary>
    public DocumentParte? Radicador => Partes.FirstOrDefault(p =>
        string.Equals(p.Rol, "comprador", StringComparison.OrdinalIgnoreCase))
        ?? (Partes.Count > 0 ? Partes[0] : null);

    /// <summary>
    /// Tenant contra el que se resuelven las firmas del baúl (HU #11030). El baúl es por tenant; el
    /// ensamblador lo fija desde la instancia del trámite.
    /// </summary>
    public Guid TenantIdParaFirmas { get; init; }

    /// <summary>
    /// Parte que OTORGA: en un traspaso es quien VENDE el vehículo —el propietario actual, que apodera
    /// para el trámite y a cuyo nombre se declara— y en matrícula inicial, donde no hay vendedor, es la
    /// parte radicadora. Antes el mandato y la solicitud de trámite virtual se emitían siempre a nombre
    /// del radicador, de modo que en un traspaso hablaban del COMPRADOR: apoderaban y declaraban por la
    /// parte equivocada (HU #11030/#11032).
    /// </summary>
    public DocumentParte? Otorgante =>
        Partes.FirstOrDefault(p => string.Equals(p.Rol, "vendedor", StringComparison.OrdinalIgnoreCase))
        ?? Radicador;

    /// <summary>Quien otorga el mandato (HU #11030). Ver <see cref="Otorgante"/>.</summary>
    public DocumentParte? Mandante => Otorgante;

    /// <summary>
    /// Propietario que solicita el trámite virtual (HU #11032): el vendedor en traspaso —es su vehículo
    /// el que se transfiere— y el radicador en matrícula inicial. Ver <see cref="Otorgante"/>.
    /// </summary>
    public DocumentParte? Propietario => Otorgante;
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
public sealed record MandatarioFirmante(
    string? Nombre,
    string? Documento,
    // HU #11030 — firma del MANDATARIO con la misma precedencia que el resto de documentos: imagen del
    // baúl si la tiene, y si no el sello de su validación de identidad. Sin ninguna, el PDF deja la
    // línea en blanco (comportamiento previo).
    byte[]? FirmaImagen = null,
    string? SelloIdentidad = null,
    // HU #11170 — trazabilidad de la firma del baúl del mandatario (vigencia y hash), que se estampa
    // bajo la imagen. No se resuelve por rol del trámite como la de las partes: el mandatario no es
    // parte, así que sus metadatos viajan aquí.
    FirmaBaulMetadata? FirmaBaulMetadatos = null);

/// <summary>
/// Datos para el <b>Contrato Privado de Mandato</b> (ADR-0036, HU #10915). El MANDANTE es la parte que
/// otorga el poder (<see cref="FurDocumentData.Mandante"/>): el VENDEDOR en un traspaso —su empresa es
/// la que apodera— y el radicador en matrícula inicial. Persona natural a nombre propio, o el
/// representante legal en nombre de la empresa. El MANDATARIO depende del OT: institucional fijo (Sabaneta UT-SETSA /
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
    MandatarioFirmante? Mandatario,
    // HU #11204 — familia del mandatario y datos propios del OT que antes estaban incrustados en el
    // generador. La familia dice QUIÉN firma como mandatario (una persona o el propio organismo); la
    // redacción la sigue eligiendo <see cref="TemplateCode"/>, porque dos OT de la misma familia pueden
    // tener textos legales distintos (Bello y Sabaneta lo son).
    MandatoFamilia Familia = MandatoFamilia.Individuo,
    string? ChamberCity = null,
    string? MandatarySigla = null,
    // HU #11206 — transformaciones declaradas en el trámite (claves de field_values). Se componen DENTRO
    // del objeto del contrato, sin cláusula nueva: ninguna plantilla del PO las menciona.
    IReadOnlyList<string>? Transformaciones = null,
    /// <summary>
    /// Qué hacer con el bloque de firma del MANDATARIO. Se resuelve fuera del generador porque depende
    /// del convenio comercial compañía↔organismo y de la marca de firma física del mandatario, datos que
    /// viven en Admin.
    /// </summary>
    MandatarioFirmaModo ModoFirmaMandatario = MandatarioFirmaModo.Estampada,
    /// <summary>none | pdf | editor — plantilla propia del OT.</summary>
    string CustomTemplateKind = "none",
    /// <summary>Cuerpo del editor (placeholders {{placa}}, {{mandante_nombre}}, …).</summary>
    string? CustomTemplateBody = null,
    /// <summary>Bytes del PDF blank propio (cargados por el caller antes de generar).</summary>
    byte[]? CustomTemplatePdf = null);

/// <summary>
/// Cómo aparece el MANDATARIO en el recuadro de firmas del contrato de mandato.
///
/// <para>Sus datos siguen nombrados en el CUERPO del contrato en los tres casos: lo que cambia es solo
/// el recuadro de firmas.</para>
/// </summary>
public enum MandatarioFirmaModo
{
    /// <summary>
    /// Bloque con estampa: firma del baúl si la tiene, si no el sello de su validación de identidad, y
    /// si no la línea vacía. Es el caso por defecto — el mandatario es un actor obligatorio.
    /// </summary>
    Estampada,

    /// <summary>
    /// Bloque con línea de guiones bajos y sus datos debajo, sin estampa: firma a mano. Lo activan la
    /// marca de firma física del mandatario en ese organismo y los organismos cuyo mandatario es el
    /// propio organismo (familia <c>organismo_transito</c>).
    /// </summary>
    Manual,

    /// <summary>
    /// Sin bloque de mandatario: el recuadro de firmas solo lleva al MANDANTE. Lo activa el convenio
    /// comercial entre la compañía y el organismo.
    /// </summary>
    SinBloque,
}

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
    IReadOnlyList<RuesActividad>? Actividades = null,
    // HU #11132 — jurisdicción de la cámara de comercio: la respuesta del RUES la trae y se descartaba.
    string? CamaraCiudad = null,
    string? CamaraDepartamento = null);

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
