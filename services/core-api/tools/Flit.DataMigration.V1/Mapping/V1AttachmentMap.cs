namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Mapea cada columna <c>id_attach*</c> de un master de V1 al <c>tipo</c> de adjunto de V2.
///
/// <para>
/// El <c>tipo</c> destino NO es inventado: sale del catálogo real <c>tramites.document_types</c>
/// ∪ la whitelist <c>AttachmentRules.ValidTipos</c>. Si un tipo no existe en el catálogo, el
/// adjunto migrado no se puede volver a validar ni re-subir desde el front.
/// </para>
///
/// <para>
/// Está separado por tipo de trámite por la misma razón que <see cref="IV1StateMap"/>: traspaso y
/// matrícula comparten <b>15</b> columnas de adjunto pero cada una tiene las suyas, y un mapa único
/// convertiría cualquier columna ajena en un aviso de "columna sin tipo mapeado" — es decir, en un
/// adjunto silenciosamente no migrado. Con dos mapas explícitos, cada columna de cada tabla está
/// declarada o excluida a propósito.
/// </para>
/// </summary>
public abstract class V1AttachmentMap
{
    /// <summary>Resultado de resolver una columna de adjunto.</summary>
    public enum Resolution
    {
        /// <summary>Tiene <c>tipo</c> destino: se migra.</summary>
        Mapped,

        /// <summary>Decidimos explícitamente no migrarla (PDF generado por V1 ya superado).</summary>
        Excluded,

        /// <summary>Es <c>id_attach*</c> pero no está mapeada ni excluida: columna nueva, avisar.</summary>
        Unknown,
    }

    /// <summary>Columna de V1 → <c>tipo</c> de V2.</summary>
    protected abstract IReadOnlyDictionary<string, string> Tipos { get; }

    /// <summary>Columnas que decidimos NO migrar (distinto de "columna nueva sin mapear").</summary>
    protected abstract IReadOnlySet<string> Excluded { get; }

    /// <summary>Resuelve el <c>tipo</c> de V2 para una columna de adjunto de V1.</summary>
    public Resolution Resolve(string column, out string tipo)
    {
        if (Tipos.TryGetValue(column, out var mapped))
        {
            tipo = mapped;
            return Resolution.Mapped;
        }

        tipo = string.Empty;
        return Excluded.Contains(column) ? Resolution.Excluded : Resolution.Unknown;
    }

    /// <summary>Columnas declaradas (mapeadas o excluidas), para el auto-diagnóstico del arranque.</summary>
    public IReadOnlyCollection<string> DeclaredColumns() => [.. Tipos.Keys, .. Excluded];
}

/// <summary>
/// Adjuntos de TRASPASO (<c>vehicle_transfer_master</c>): 37 columnas, 36 migrables.
/// <para>
/// El catálogo de V2 fue diseñado pensando en traspaso, así que 34 de las 37 mapean 1:1.
/// Los 3 casos de criterio se resolvieron así (ver bitácora ADR-012):
/// <list type="bullet">
///   <item>Huellas biométricas (<c>*_fingerprint</c>): V2 no tiene un <c>tipo</c> para huella;
///   se envían como <c>otro</c> sin inventar una categoría inexistente.</item>
///   <item><c>id_attached_payment_receipt</c> → <c>comprobante_derechos</c>.</item>
///   <item>El consolidado final que V1 generaba (<c>id_attachment_pdf_prepared</c>) →
///   <c>consolidado</c>: se migra para que la foto histórica muestre el expediente tal cual lo
///   produjo V1. Su borrador superado (<c>id_attachment_pdf_draft</c>) NO se migra.</item>
/// </list>
/// Los documentos de identidad (cédula, anverso/reverso, selfie, id generado) se mapean a
/// <c>cedulas</c> — el <c>tipo</c> del checklist requerido de <c>TRASPASO_STANDARD</c> — para que
/// el trámite migrado muestre el documento requerido como presente. La columna original de V1 se
/// conserva en la metadata del adjunto para no perder la distinción fina.
/// </para>
/// </summary>
public sealed class TransferAttachmentMap : V1AttachmentMap
{
    public static readonly TransferAttachmentMap Instance = new();

    private TransferAttachmentMap() { }

    protected override IReadOnlyDictionary<string, string> Tipos { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Identidad (comprador / vendedor) → 'cedulas' (slot requerido del checklist)
            ["id_attached_buyer_id"] = "cedulas",
            ["id_attached_seller_id"] = "cedulas",
            ["id_attach_document_front_buyer"] = "cedulas",
            ["id_attach_document_back_buyer"] = "cedulas",
            ["id_attach_document_front_seller"] = "cedulas",
            ["id_attach_document_back_seller"] = "cedulas",
            ["id_attached_buyer_generated_id"] = "cedulas",
            ["id_attached_seller_generated_id"] = "cedulas",
            ["id_attach_image_face_buyer"] = "cedulas",
            ["id_attach_image_face_seller"] = "cedulas",
            // Huella biométrica: V2 no tiene 'tipo' propio → 'otro' (no se inventa categoría).
            ["id_attached_buyer_fingerprint"] = "otro",
            ["id_attached_seller_fingerprint"] = "otro",

            // ── Vehículo / checklist
            ["id_attached_soat"] = "soat",
            ["id_attached_rtm"] = "rtm",
            ["id_attached_traffic_license"] = "licencia_transito",
            ["id_attached_imprints"] = "impronta",
            ["id_attached_certificate_tax_clearance"] = "paz_salvo",
            ["id_attached_buy_sell"] = "compraventa",
            ["id_attached_bodywork_invoice"] = "factura_carroceria",
            ["id_attached_validity_certificate_soat_rtm"] = "certificado_vigencia_soat_rtm",

            // ── Prenda / leasing
            ["id_attached_certificate_of_peace_and_security_pledge"] = "paz_salvo_prenda",
            ["id_attached_registered_pledge"] = "inscripcion_prenda",
            ["id_attached_leasing_contract"] = "contrato_leasing",
            ["id_attached_leasing_company_declaration"] = "declaracion_arrendadora",

            // ── Actores / representación
            ["id_attached_buyer_power_processor"] = "poder_tramitador",
            ["id_attached_seller_power_processor"] = "poder_tramitador",
            ["id_attached_buyer_signature"] = "firma",
            ["id_attached_seller_signature"] = "firma",
            ["id_attached_chamber_commerce_buyer"] = "camara_comercio",
            ["id_attached_chamber_commerce_seller"] = "camara_comercio",
            ["id_attached_rues_certificate"] = "rues",

            // ── Documento consolidado generado por V1 (expediente completo del trámite). V1 lo
            //    persiste en id_attachment_pdf_prepared (versión "preparada"/final); se migra al tipo
            //    'consolidado' de V2 para que la foto histórica muestre el expediente tal cual lo
            //    produjo V1. La versión 'draft' (borrador superado) permanece excluida.
            ["id_attachment_pdf_prepared"] = "consolidado",

            // ── Otros
            ["id_attached_transfer_domain"] = "transferencia_dominio",
            ["id_attached_virtual_procedures"] = "tramite_virtual",
            ["id_attached_general_others_annex"] = "anexos_generales",
            ["id_attached_payment_receipt"] = "comprobante_derechos",
        };

    /// <summary>
    /// Borrador del consolidado que V1 auto-generaba (<c>id_attachment_pdf_draft</c>): NO se migra
    /// porque queda superado por la versión "preparada"/final (<c>id_attachment_pdf_prepared</c>, que
    /// SÍ se migra a <c>consolidado</c>). Se lista explícitamente para distinguir "decidimos no
    /// migrarla" de "columna nueva sin mapear".
    /// </summary>
    protected override IReadOnlySet<string> Excluded { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id_attachment_pdf_draft",
        };
}

/// <summary>
/// Adjuntos de MATRÍCULA INICIAL (<c>vehicle_registration_master</c>): 30 columnas en producción
/// (31 en develop, que añade <c>id_attached_soat_manual</c>), 29/30 migrables.
///
/// <para>
/// Comparte 15 columnas con traspaso, con el mismo significado. Las 15 propias son la razón de que
/// este mapa exista aparte. Los <c>tipo</c> destino se eligieron contra el <b>nombre que la propia
/// V1 le da a cada archivo</b> en <c>vehicleRegistrationConsolidatePdfService</c>, no por parecido
/// del nombre de la columna:
/// </para>
///
/// <list type="table">
///   <item><term>id_attached_gas</term><description>V1 lo rotula "Certificado de Cepd" →
///     <c>certificado_ambiental</c> ("Certificado CEPD"). NO es un certificado de conversión a gas,
///     aunque la columna lo sugiera. 1.667 trámites en pdn.</description></item>
///   <item><term>id_attached_import_or_customs</term><description>"Certificado Aduana /
///     Declaración Importación" → <c>aduana</c>, cuyo nombre en el catálogo de V2 es literalmente
///     el mismo. 6.550 trámites.</description></item>
///   <item><term>id_attached_invoice</term><description>"Factura de Venta" → <c>factura</c>.
///     8.615 trámites. Es la factura del concesionario, el documento que origina la matrícula.
///     No confundir con <c>id_attached_bodywork_invoice</c> (carrocería, 4 trámites).</description></item>
///   <item><term>id_attached_validity_certificate_imprints</term><description>"Certificado
///     validación de improntas" → <c>impronta_validada</c>. 12.566 trámites: es casi universal en
///     matrícula y no existe en traspaso.</description></item>
///   <item><term>departmental_tax_settlement / _payment</term><description>"Liquidación impuestos"
///     e "Impuesto Departamental" → ambos a <c>liquidacion_impuesto</c>, cuyo nombre en V2 es
///     "Liquidación / pago impuesto departamental" y cubre las dos caras. Dos columnas al mismo
///     <c>tipo</c> no colisionan: el id determinístico del adjunto se deriva de la COLUMNA.</description></item>
/// </list>
///
/// <para>
/// Una sola parte (<c>owner</c>) en vez de comprador + vendedor, así que las columnas de identidad
/// son tres y no seis. Al igual que en traspaso van a <c>cedulas</c>, y la huella a <c>otro</c>.
/// </para>
/// </summary>
public sealed class RegistrationAttachmentMap : V1AttachmentMap
{
    public static readonly RegistrationAttachmentMap Instance = new();

    private RegistrationAttachmentMap() { }

    protected override IReadOnlyDictionary<string, string> Tipos { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Identidad del titular → 'cedulas' (una sola parte, no dos)
            ["id_attached_owner_id"] = "cedulas",
            ["id_attached_owner_generated_id"] = "cedulas",
            ["id_attach_document_front_owner"] = "cedulas",
            ["id_attach_document_back_owner"] = "cedulas",
            ["id_attach_image_face_owner"] = "cedulas",
            ["id_attached_owner_fingerprint"] = "otro",

            // ── Vehículo / checklist
            ["id_attached_soat"] = "soat",
            // Solo en develop (HU de SOAT manual); en pdn la columna todavía no existe y el
            // resolvedor simplemente nunca la ve.
            ["id_attached_soat_manual"] = "soat_manual",
            ["id_attached_rtm"] = "rtm",
            ["id_attached_traffic_license"] = "licencia_transito",
            ["id_attached_imprints"] = "impronta",
            ["id_attached_validity_certificate_imprints"] = "impronta_validada",
            ["id_attached_validity_certificate_soat_rtm"] = "certificado_vigencia_soat_rtm",
            ["id_attached_certificate_tax_clearance"] = "paz_salvo",
            ["id_attached_bodywork_invoice"] = "factura_carroceria",

            // ── Origen del vehículo (propio de matrícula: el carro se registra por primera vez)
            ["id_attached_invoice"] = "factura",
            ["id_attached_import_or_customs"] = "aduana",
            ["id_attached_gas"] = "certificado_ambiental",

            // ── Impuestos departamentales (dos columnas, un solo tipo en V2)
            ["id_attached_departmental_tax_settlement"] = "liquidacion_impuesto",
            ["id_attached_departmental_tax_payment"] = "liquidacion_impuesto",

            // ── Prenda / leasing
            ["id_attached_registered_pledge"] = "inscripcion_prenda",
            ["id_attached_leasing_contract"] = "contrato_leasing",

            // ── Actores / representación
            ["id_attached_owner_power_processor"] = "poder_tramitador",
            ["id_attached_owner_signature"] = "firma",
            ["id_attached_chamber_commerce_owner"] = "camara_comercio",
            ["id_attached_rues_certificate"] = "rues",

            // ── Consolidado final producido por V1 (mismo criterio que traspaso)
            ["id_attachment_pdf_prepared"] = "consolidado",

            // ── Otros
            ["id_attached_virtual_procedures"] = "tramite_virtual",
            ["id_attached_general_others_annex"] = "anexos_generales",
            ["id_attached_payment_receipt"] = "comprobante_derechos",
        };

    /// <summary>Mismo criterio que traspaso: el borrador del consolidado queda superado.</summary>
    protected override IReadOnlySet<string> Excluded { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id_attachment_pdf_draft",
        };
}
