using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Catalog;

/// <summary>
/// Catálogo de tipologías de trámite + checklist (paridad <c>TRAMITE_TIPOLOGIAS</c> de Johan).
/// <para>
/// Fuente única de verdad en código (versionada con el repo). MVP: solo
/// <c>matricula_inicial</c> y <c>traspaso_standard</c>. El catálogo es extensible:
/// añadir una tipología (sucesión, remate, importación, flota) es agregar un
/// <see cref="TramiteTipologia"/> aquí, sin reescribir el motor.
/// </para>
/// </summary>
public static class TramiteTipologiaCatalog
{
    // ADR-0050 — la tipología ES el código del tipo (tramites.procedure_types.code). Antes eran un
    // vocabulario aparte ('matricula_inicial' / 'traspaso_standard') que TipologiaResolver traducía
    // desde la familia, colapsando OTROS en matrícula. Al unificarlos, instance.TypeCode entra
    // directo en este catálogo y en ConditionalDocumentRules.
    public const string CodigoMatriculaInicial = "MATRICULA_NUEVA";
    public const string CodigoTraspasoStandard = "TRASPASO_STANDARD";

    private static readonly IReadOnlyList<TramiteTipologia> Tipologias =
    [
        new TramiteTipologia(
            CodigoMatriculaInicial,
            "Matrícula inicial",
            "Primera matrícula del vehículo en Colombia (VIN-first, adquirente único).",
            [
                new ChecklistItem("factura", "Factura de venta", Obligatorio: true, DocTipo: "factura"),
                new ChecklistItem(
                    "aduana",
                    "Manifiesto de importación / Aduana",
                    Obligatorio: true,
                    DocTipo: "aduana"),
                new ChecklistItem("impronta", "Impronta de motor y chasis", Obligatorio: false, DocTipo: "impronta"),
                new ChecklistItem("soat", "SOAT vigente", Obligatorio: false, DocTipo: "soat"),
                new ChecklistItem(
                    "certificado_ambiental",
                    "Certificado ambiental",
                    Obligatorio: false,
                    DocTipo: "certificado_ambiental"),
                new ChecklistItem(
                    "declaracion_aduana",
                    "Declaración de importación (DIAN)",
                    Obligatorio: false,
                    DocTipo: "declaracion_aduana"),
                new ChecklistItem("acta_remate", "Acta de remate", Obligatorio: false, DocTipo: "acta_remate"),
                new ChecklistItem("oficio_judicial", "Oficio judicial", Obligatorio: false, DocTipo: "oficio_judicial"),
                new ChecklistItem("otro", "Otro documento", Obligatorio: false, DocTipo: "otro"),
            ]),
        new TramiteTipologia(
            CodigoTraspasoStandard,
            "Traspaso estándar",
            "Traspaso de propiedad entre particulares (compraventa directa).",
            [
                new ChecklistItem(
                    "contrato_compraventa",
                    "Contrato de compraventa autenticado",
                    Obligatorio: true,
                    DocTipo: "compraventa",
                    Ayuda: "Firmas autenticadas ante notaría de comprador y vendedor."),
                new ChecklistItem("impronta", "Impronta de motor y chasis", Obligatorio: false, DocTipo: "impronta"),
                new ChecklistItem("soat", "SOAT vigente", Obligatorio: true, DocTipo: "soat"),
                new ChecklistItem(
                    "rtm",
                    "Revisión técnico-mecánica vigente",
                    Obligatorio: true,
                    DocTipo: "rtm",
                    Ayuda: "Aplica según antigüedad del vehículo (Ley 769 Art. 52)."),
                new ChecklistItem(
                    "paz_salvo",
                    "Paz y salvo de impuestos y comparendos",
                    Obligatorio: true,
                    DocTipo: "paz_salvo",
                    Ayuda: "Impuesto vehicular al día + SIMIT sin comparendos en mora."),
                new ChecklistItem("cedulas", "Cédulas de comprador y vendedor", Obligatorio: true, DocTipo: "cedulas"),
                new ChecklistItem(
                    "cert_tradicion",
                    "Certificado de tradición y libertad",
                    Obligatorio: false,
                    DocTipo: "cert_tradicion",
                    Ayuda: "Recomendado para verificar prendas/embargos antes de radicar."),
            ]),
    ];

    // TODO(ICT-LEASING-CHECKLIST): promover a ChecklistItem los DocTipos "loose" que la integración ICT
    // ya clasifica (ict.external_integration_attachment_association) pero que NO existen aquí, por lo que
    // hoy el adjunto ICT se muestra sin etiqueta amigable, no aparece como ítem del checklist y no puede
    // satisfacer uno. Obligatoriedad según ict.external_integration_configuration_documents:
    //   matrícula (matricula_inicial): certificado_cepd "Certificado CEPD" (opcional),
    //       poder_comprador "Poder Comprador-Apoderado" (opcional)
    //   traspaso (traspaso_standard): poder_comprador (opcional), poder_vendedor "Poder Vendedor-
    //       Apoderado" (opcional), contrato_leasing "Contrato LEASING" (obligatorio en traspaso unilat.)
    //   leasing / otros (tipologías aún no modeladas): contrato_leasing y declaracion_arrendadora
    //       "Declaración cía arrendadora" (matrícula leasing), blindaje "Blindaje" (otros trámites)
    // Al agregarlos como ChecklistItem, el adjunto ICT quedará etiquetado en el frontend (DocumentChecklist
    // usa item.label del backend). Contraparte del TODO en core-ict 15-ICT-attachment-association.sql.

    private static readonly Dictionary<string, TramiteTipologia> ByCode =
        Tipologias.ToDictionary(t => t.Codigo);

    /// <summary>Todas las tipologías configuradas.</summary>
    public static IReadOnlyList<TramiteTipologia> All => Tipologias;

    /// <summary>Devuelve la tipología por código, o <c>null</c> si no existe.</summary>
    public static TramiteTipologia? Get(string? codigo) =>
        !string.IsNullOrEmpty(codigo) && ByCode.TryGetValue(codigo, out var t) ? t : null;

    /// <summary>¿El código es una tipología válida del catálogo?</summary>
    public static bool IsValid(string? codigo) =>
        !string.IsNullOrEmpty(codigo) && ByCode.ContainsKey(codigo);
}
