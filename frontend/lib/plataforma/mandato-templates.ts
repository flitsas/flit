/**
 * Catálogo de plantillas de Contrato de Mandato aplicadas en FLIT.
 * Espejo de dominio (`MandatoTemplateResolver` + seed HU #10912 / familias HU #11204 /
 * assignment_mode Plataforma tres tipos).
 */

export type MandatoTemplateCode = "generico" | "sabaneta" | "bello" | "municipio";

/**
 * Redacción ELEGIDA para un OT. `auto` no es una redacción: delega en la plantilla de sistema del
 * organismo (o en la genérica si no tiene). Es lo que se guarda; la redacción EFECTIVA la resuelve
 * el backend.
 */
export type MandatoConfiguredTemplateCode = MandatoTemplateCode | "auto";

/** Opción "automática" del selector: encabeza la lista porque es el default sensato. */
export const MANDATO_TEMPLATE_AUTO = {
  code: "auto" as const,
  label: "Automática (según el organismo)",
  summary:
    "El organismo usa la plantilla que el sistema tiene asignada a su código. Si no tiene ninguna, usa la genérica.",
};

/** Opciones del selector de plantilla por OT: la automática más las redacciones del sistema. */
export function mandatoTemplateOptions(): readonly {
  code: MandatoConfiguredTemplateCode;
  label: string;
  summary: string;
}[] {
  return [
    MANDATO_TEMPLATE_AUTO,
    ...MANDATO_TEMPLATES.map((t) => ({
      code: t.code as MandatoConfiguredTemplateCode,
      label: t.label,
      summary: t.summary,
    })),
  ];
}

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
      "Una persona natural (mandatario de la empresa que radica) firma como mandatario. Si elige este tipo, el PDF usa la plantilla genérica.",
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
      "El contrato se genera sin mandatario asignado: nombre, cédula, firma y hash en líneas abiertas (___) dentro del recuadro. Es el default al nacer un OT.",
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
      "Plantilla por defecto del sistema. Aplica a cualquier organismo que no tenga una plantilla propia. Firman mandante y mandatario (o líneas en blanco si el tipo es Abierto).",
    familia: "individuo",
    familiaLabel: "Individuo",
    tipoTipico: "persona_rl",
    requiresForNaturalPerson: true,
    mandatarioFirma: "Mandante y mandatario (o abierto)",
    bindings: [
      {
        officeCode: "*",
        officeName: "Cualquier OT sin plantilla propia del sistema",
        hasExplicitConfig: false,
      },
    ],
  },
  {
    code: "sabaneta",
    label: "Sabaneta",
    summary:
      "Plantilla del sistema para el organismo de Sabaneta. Mandatario institucional UT-SETSA; solo firma el mandante.",
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
    label: "Bello",
    summary:
      "Plantilla del sistema para el organismo de Bello. El mandatario es el representante legal de la UT-MAB; firman ambas partes.",
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
  {
    code: "municipio",
    label: "Envigado, Funza y Medellín",
    summary:
      "Misma plantilla del sistema para estos tres organismos. Redacción corta; firman mandante y mandatario. La ciudad del cierre cambia según el OT del trámite.",
    familia: "individuo",
    familiaLabel: "Individuo",
    tipoTipico: "persona_rl",
    requiresForNaturalPerson: true,
    mandatarioFirma: "Mandante y mandatario",
    bindings: [
      {
        officeCode: "5266000",
        officeName: "Envigado",
        hasExplicitConfig: true,
        chamberCity: "Envigado",
      },
      {
        officeCode: "25286000",
        officeName: "Funza",
        hasExplicitConfig: true,
        chamberCity: "Funza",
      },
      {
        officeCode: "5001000",
        officeName: "Medellín",
        hasExplicitConfig: true,
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

/**
 * HU #11718 — qué tercero ajeno quedaría citado en el contrato si se aplica `templateCode` al
 * organismo `officeCode`, o `null` si la combinación es coherente.
 *
 * <p>Las redacciones del sistema llevan datos quemados en `MandatoPdfGenerator`: la de Bello cierra
 * con su municipio y nombra a la UT-MAB; la de Sabaneta nombra a UT-SETSA como mandatario y la
 * Cámara de Comercio de Medellín. Desde que la plantilla se elige por OT (Feature #11702) se puede
 * aplicar cualquiera a cualquier organismo, y el documento sale nombrando a alguien que no tiene
 * nada que ver — Bello aplicado a Bogotá cierra «en el municipio de Bello, Antioquia».</p>
 *
 * <p><b>Advierte, no bloquea</b> (decisión de producto del 2026-08-21): restringir contradiría la
 * libertad de parametrización que introdujo el Feature #11702.</p>
 */
export function terceroAjenoEnPlantilla(
  templateCode: string | null | undefined,
  officeCode: string | null | undefined,
): string | null {
  const code = (templateCode ?? "").trim().toLowerCase();

  // La automática nunca advierte: por definición aplica la redacción propia del organismo.
  if (code === "" || code === MANDATO_TEMPLATE_AUTO.code) return null;

  const template = MANDATO_TEMPLATES.find((t) => t.code === code);
  if (!template) return null;

  // La genérica no nombra a ningún organismo concreto: es el respaldo de todos.
  if (template.bindings.some((b) => b.officeCode === "*")) return null;

  const ot = (officeCode ?? "").trim();
  if (ot !== "" && template.bindings.some((b) => b.officeCode === ot)) return null;

  // Es de otro. Se nombra a quién, que es lo que el gestor necesita para juzgar.
  const nombres = template.bindings.map(
    (b) => b.institutionalMandataryName ?? b.chamberCity ?? b.officeName,
  );
  return [...new Set(nombres)].join(", ");
}
