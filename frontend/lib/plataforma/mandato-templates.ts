/**
 * Catálogo de plantillas de Contrato de Mandato aplicadas en FLIT.
 * Espejo de dominio (`MandatoTemplateResolver` + seed HU #10912 / familias HU #11204 /
 * assignment_mode Plataforma tres tipos).
 */

export type MandatoTemplateCode = "generico" | "sabaneta" | "bello";

export type MandatoFamiliaCode = "individuo" | "organismo_transito";

/** Modo de asignación (negocio): independiente de la redacción (`template_code`). */
export type MandateAssignmentMode = "signer" | "institutional" | "open";

/** Tipo de negocio mostrado en Plataforma (capa UX sobre assignment_mode). */
export type MandatoTipoNegocio = "persona_rl" | "institucional" | "abierto";

export const MANDATO_TIPOS: readonly {
  value: MandatoTipoNegocio;
  label: string;
  summary: string;
}[] = [
  {
    value: "persona_rl",
    label: "Persona o RL",
    summary:
      "Una persona natural (mandatario registrado o representante legal) firma como mandatario. Default de los OT sin configuración propia.",
  },
  {
    value: "institucional",
    label: "Institucional (OT / UT)",
    summary:
      "El organismo o unión temporal actúa como mandatario. Suele firmar solo el mandante (p. ej. Sabaneta UT-SETSA).",
  },
  {
    value: "abierto",
    label: "Abierto (sin asumir)",
    summary:
      "El contrato se genera sin mandatario asignado (campos en blanco). No se exige firmante persona al aprobar.",
  },
] as const;

export function resolveTipoNegocio(
  assignmentMode: MandateAssignmentMode | string | null | undefined,
): MandatoTipoNegocio {
  const mode = (assignmentMode ?? "signer").trim().toLowerCase();
  if (mode === "open") return "abierto";
  if (mode === "institutional") return "institucional";
  return "persona_rl";
}

export function resolveAssignmentMode(tipo: MandatoTipoNegocio): MandateAssignmentMode {
  switch (tipo) {
    case "institucional":
      return "institutional";
    case "abierto":
      return "open";
    default:
      return "signer";
  }
}

/** Familia sugerida al cambiar tipo; no fuerza plantilla. */
export function suggestedFamilyForTipo(
  tipo: MandatoTipoNegocio,
  templateCode: string,
): MandatoFamiliaCode {
  if (tipo === "institucional") return "organismo_transito";
  if (tipo === "abierto") return "individuo";
  // Bello: RL de UT — familia organismo con firmante persona.
  return templateCode === "bello" ? "organismo_transito" : "individuo";
}

export function tipoNegocioLabel(tipo: MandatoTipoNegocio): string {
  return MANDATO_TIPOS.find((t) => t.value === tipo)?.label ?? tipo;
}

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
  /** Tipo de negocio típico de esta redacción. */
  tipoTipico: MandatoTipoNegocio;
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
      "Redacción por defecto. Suele usarse con tipo Persona/RL o Abierto. Firman mandante y mandatario (o placeholders si está abierto).",
    familia: "individuo",
    familiaLabel: "Individuo",
    tipoTipico: "persona_rl",
    requiresForNaturalPerson: true,
    mandatarioFirma: "Mandante y mandatario (o abierto)",
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
      "Redacción institucional: mandatario = unión temporal. Solo firma el mandante. Tipo típico: Institucional.",
    familia: "organismo_transito",
    familiaLabel: "Organismo de tránsito",
    tipoTipico: "institucional",
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
      "Redacción con RL de la unión temporal. Tipo típico: Persona/RL. Firman ambas partes.",
    familia: "organismo_transito",
    familiaLabel: "Organismo de tránsito",
    tipoTipico: "persona_rl",
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

/** Etiqueta legible de la redacción del sistema (fallback sin plantilla propia). */
export function systemTemplateLabel(code: string | null | undefined): string {
  const normalized = (code ?? "generico").trim().toLowerCase();
  return MANDATO_TEMPLATES.find((t) => t.code === normalized)?.label ?? "Genérico";
}

/** Filas planas OT → plantilla para la tabla de aplicación. */
export interface MandatoOtApplicationRow {
  officeCode: string;
  officeName: string;
  templateCode: MandatoTemplateCode;
  hasExplicitConfig: boolean;
}
