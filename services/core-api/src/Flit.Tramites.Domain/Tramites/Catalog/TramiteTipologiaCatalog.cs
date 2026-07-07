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
    public const string CodigoMatriculaInicial = "matricula_inicial";
    public const string CodigoTraspasoStandard = "traspaso_standard";
    public const string CodigoTraspasoUnilateral = "traspaso_unilateral";

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
        new TramiteTipologia(
            CodigoTraspasoUnilateral,
            "Traspaso unilateral",
            "Traspaso de leasing/arrendamiento donde la compañía arrendadora transfiere la propiedad "
                + "amparada en el contrato, sin comparecencia del locatario (placa-first).",
            [
                new ChecklistItem(
                    "paz_salvo_locatario",
                    "Paz y salvo del locatario",
                    Obligatorio: true,
                    DocTipo: "paz_salvo_locatario",
                    Ayuda: "Paz y salvo del locatario frente a la compañía de leasing/arrendamiento."),
                new ChecklistItem(
                    "doc_locatario",
                    "Documento del locatario",
                    Obligatorio: true,
                    DocTipo: "doc_locatario",
                    Ayuda: "Si el locatario es NIT, adjunta cámara de comercio y cédula del representante "
                        + "legal en un solo archivo."),
                new ChecklistItem(
                    "contrato_leasing",
                    "Contrato de leasing",
                    Obligatorio: true,
                    DocTipo: "contrato_leasing",
                    Ayuda: "Contrato de leasing/arrendamiento financiero que ampara la transferencia unilateral."),
                new ChecklistItem(
                    "declaracion_arrendadora",
                    "Declaración de la arrendadora",
                    Obligatorio: true,
                    DocTipo: "declaracion_arrendadora",
                    Ayuda: "Declaración de la compañía arrendadora para el traspaso unilateral."),
            ]),
    ];

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
