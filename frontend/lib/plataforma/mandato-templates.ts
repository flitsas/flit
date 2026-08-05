/**
 * Catálogo de plantillas de Contrato de Mandato aplicadas en FLIT.
 * Espejo de dominio (`MandatoTemplateResolver` + seed HU #10912 / familias HU #11204).
 * Fuente de verdad de producto hasta exista un GET admin de `transit_office_mandate_config`.
 */

export type MandatoTemplateCode = "generico" | "sabaneta" | "bello";

export type MandatoFamiliaCode = "individuo" | "organismo_transito";

export interface MandatoTemplateOtBinding {
  /** Código RUNT del OT (catálogo). */
  officeCode: string;
  officeName: string;
  /** true = fila en `admin.transit_office_mandate_config`; false = default implícito. */
  hasExplicitConfig: boolean;
  institutionalMandataryName?: string;
  institutionalMandataryNit?: string;
  mandatarySigla?: string;
  chamberCity?: string;
}

export interface MandatoTemplateDefinition {
  code: MandatoTemplateCode;
  label: string;
  summary: string;
  familia: MandatoFamiliaCode;
  familiaLabel: string;
  requiresForNaturalPerson: boolean;
  /** Quién firma el bloque del mandatario en el PDF. */
  mandatarioFirma: string;
  /** OT con esta plantilla sembrada o comportamiento default. */
  bindings: MandatoTemplateOtBinding[];
}

/** Plantillas que el generador conoce hoy (código cerrado en el PDF generator). */
export const MANDATO_TEMPLATES: readonly MandatoTemplateDefinition[] = [
  {
    code: "generico",
    label: "Genérico",
    summary:
      "Plantilla por defecto cuando el OT no tiene configuración propia. El mandatario es una persona (firmante registrado); firman mandante y mandatario. Aplica a persona natural y jurídica.",
    familia: "individuo",
    familiaLabel: "Individuo",
    requiresForNaturalPerson: true,
    mandatarioFirma: "Mandante y mandatario",
    bindings: [
      {
        officeCode: "*",
        officeName: "Cualquier OT sin fila en transit_office_mandate_config",
        hasExplicitConfig: false,
      },
    ],
  },
  {
    code: "sabaneta",
    label: "Sabaneta (UT-SETSA)",
    summary:
      "Mandatario institucional (unión temporal). Solo firma el mandante. Aplica a persona natural y jurídica.",
    familia: "organismo_transito",
    familiaLabel: "Organismo de tránsito",
    requiresForNaturalPerson: true,
    mandatarioFirma: "Solo mandante (sin bloque de firma del mandatario)",
    bindings: [
      {
        officeCode: "5631000",
        officeName: "Sabaneta",
        hasExplicitConfig: true,
        institutionalMandataryName:
          "UNION TEMPORAL SERVICIOS ESPECIALIZADOS DE TRANSITO Y TRANSPORTE DE SABANETA SETSA",
        institutionalMandataryNit: "900273813-7",
        mandatarySigla: "UT-SETSA",
        chamberCity: "Medellín",
      },
    ],
  },
  {
    code: "bello",
    label: "Bello (UT-MAB)",
    summary:
      "Mandatario = representante legal de la unión temporal. Firman ambas partes. Aplica a persona natural y jurídica.",
    familia: "organismo_transito",
    familiaLabel: "Organismo de tránsito",
    requiresForNaturalPerson: true,
    mandatarioFirma: "Mandante y mandatario (RL de la UT)",
    bindings: [
      {
        officeCode: "5088000",
        officeName: "Bello",
        hasExplicitConfig: true,
        institutionalMandataryName: "UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB",
        institutionalMandataryNit: "901783814-6",
        chamberCity: "Medellín",
      },
    ],
  },
] as const;

/** Filas planas OT → plantilla para la tabla de aplicación. */
export interface MandatoOtApplicationRow {
  id: string;
  officeCode: string;
  officeName: string;
  templateCode: MandatoTemplateCode;
  templateLabel: string;
  familiaLabel: string;
  requiresForNaturalPerson: boolean;
  hasExplicitConfig: boolean;
  institutionalMandataryName: string | null;
  institutionalMandataryNit: string | null;
}

export function listMandatoOtApplications(): MandatoOtApplicationRow[] {
  const rows: MandatoOtApplicationRow[] = [];
  for (const template of MANDATO_TEMPLATES) {
    for (const binding of template.bindings) {
      rows.push({
        id: `${template.code}:${binding.officeCode}`,
        officeCode: binding.officeCode,
        officeName: binding.officeName,
        templateCode: template.code,
        templateLabel: template.label,
        familiaLabel: template.familiaLabel,
        requiresForNaturalPerson: template.requiresForNaturalPerson,
        hasExplicitConfig: binding.hasExplicitConfig,
        institutionalMandataryName: binding.institutionalMandataryName ?? null,
        institutionalMandataryNit: binding.institutionalMandataryNit ?? null,
      });
    }
  }
  return rows;
}
