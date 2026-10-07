/**
 * Utilidades de los formatos de Contrato de Mandato.
 *
 * HU #13174: la lista de formatos (códigos, nombres, tipo por defecto) ya NO vive aquí; la publica el
 * backend en GET /mandatos/formatos (`MandatoFormatView`). Este módulo conserva solo lo que el catálogo
 * no trae: tipos de negocio, familias y qué organismo es el dueño de cada redacción de sistema.
 */
import type { MandatoFormatView } from "@/lib/api/admin-plataforma-mandatos";

/**
 * Código de la opción «automática»: delega en la redacción del organismo. Es el único código que el
 * frontend conoce por nombre, porque no es un formato del catálogo editable sino una elección.
 */
export const MANDATO_TEMPLATE_AUTO_CODE = "auto";

/**
 * Tipos de mandato que el Super Admin ve en la fila del organismo (HU #13150).
 * «Mandatario de la compañía» es el tipo de un organismo nuevo y de toda compañía sin regla propia.
 */
export function resumenTiposPorCompania(explicitJuridica: number, explicitAbierto: number): string {
  const partes = [tipoNegocioLabel("persona_rl")];
  if (explicitJuridica > 0) partes.push(`${tipoNegocioLabel("institucional")} (${explicitJuridica})`);
  if (explicitAbierto > 0) partes.push(`${tipoNegocioLabel("abierto")} (${explicitAbierto})`);
  return partes.join(" · ");
}

/** Nombre vigente del formato; si el código guardado ya no está en el catálogo, muestra el código. */
export function mandatoFormatName(
  formatos: readonly MandatoFormatView[],
  code: string | null | undefined,
): string {
  const normalized = (code ?? "").trim();
  if (normalized === "") return "";
  return formatos.find((f) => f.code.toLowerCase() === normalized.toLowerCase())?.name ?? normalized;
}

/** Formato del catálogo por código, o undefined si no existe (código desconocido guardado). */
export function findMandatoFormat(
  formatos: readonly MandatoFormatView[],
  code: string | null | undefined,
): MandatoFormatView | undefined {
  const normalized = (code ?? "").trim().toLowerCase();
  return formatos.find((f) => f.code.toLowerCase() === normalized);
}

export type MandatoFamiliaCode = "individuo" | "organismo_transito";

/** Modo de asignación (negocio): independiente de la redacción (`template_code`). */
export type MandateAssignmentMode = "signer" | "institutional" | "open";

/** Tipo de negocio mostrado en Plataforma (capa UX sobre assignment_mode). */
export type MandatoTipoNegocio = "persona_rl" | "institucional" | "abierto";

/**
 * Nombres de los tipos de mandato. No se llaman «Persona natural» / «Persona jurídica»: esos son los modelos del
 * MANDATARIO (quién es la persona que se registra) y el tipo de mandato dice otra cosa, QUIÉN ocupa el bloque del
 * mandatario en el contrato. Con los mismos nombres se leía que «Persona natural» excluía a un mandatario persona
 * jurídica y que «Persona jurídica» era cualquier empresa, cuando es el propio organismo.
 */
export const MANDATO_TIPOS: readonly {
  value: MandatoTipoNegocio;
  label: string;
  summary: string;
}[] = [
  {
    value: "persona_rl",
    label: "Mandatario de la compañía",
    summary:
      "Firma el mandatario que la compañía tiene registrado, sea persona natural o persona jurídica. Es el tipo por defecto de todo organismo y compañía.",
  },
  {
    value: "institucional",
    label: "Institucional (organismo)",
    summary:
      "El propio organismo de tránsito o su unión temporal actúa como mandatario; no firma una persona de la compañía. Suele firmar solo el mandante (p. ej. Sabaneta UT-SETSA).",
  },
  {
    value: "abierto",
    label: "Abierto (sin mandatario)",
    summary:
      "El contrato sale sin mandatario asignado: nombre, documento, firma y huella quedan en líneas en blanco (___) para llenarse a mano.",
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

/**
 * Modo persistido según el formato: el tipo por defecto que trae el catálogo. Con «automática» o un
 * código desconocido, Persona natural.
 */
export function assignmentModeFromFormat(
  formatos: readonly MandatoFormatView[],
  templateCode: string | null | undefined,
): MandateAssignmentMode {
  const code = (templateCode ?? "").trim().toLowerCase();
  if (code === MANDATO_TEMPLATE_AUTO_CODE) return "signer";
  return resolveAssignmentMode(resolveTipoNegocio(findMandatoFormat(formatos, code)?.assignmentMode));
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

/**
 * Organismos que son dueños de una redacción de sistema (los datos de su municipio o mandatario
 * institucional van en el texto). No viene del catálogo del backend; solo sirve para advertir cuando
 * se asigna la redacción de un organismo a otro. Una redacción sin entrada (p. ej. la genérica) no
 * nombra a nadie y nunca advierte.
 */
export const MANDATO_FORMATO_VINCULOS: Readonly<Record<string, readonly MandatoTemplateOtBinding[]>> = {
  sabaneta: [
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
  bello: [
    {
      officeCode: "5088000",
      officeName: "Bello",
      hasExplicitConfig: true,
      institutionalMandataryName: "UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB",
      institutionalMandataryNit: "901783814-6",
      chamberCity: "Medellín",
    },
  ],
  municipio: [
    { officeCode: "5266000", officeName: "Envigado", hasExplicitConfig: true, chamberCity: "Envigado" },
    { officeCode: "25286000", officeName: "Funza", hasExplicitConfig: true, chamberCity: "Funza" },
    { officeCode: "5001000", officeName: "Medellín", hasExplicitConfig: true, chamberCity: "Medellín" },
  ],
};

/**
 * true si la redacción del sistema nombra a un mandatario institucional (UT) cuyos datos salen de la
 * config del OT. Se deduce de los vínculos, no de comparar el código; cuando el catálogo del backend
 * exponga la familia del formato, esta función se reemplaza por ese dato.
 */
export function formatoNombraMandatarioInstitucional(code: string | null | undefined): boolean {
  const bindings = MANDATO_FORMATO_VINCULOS[(code ?? "").trim().toLowerCase()] ?? [];
  return bindings.some((b) => Boolean(b.institutionalMandataryName));
}

/**
 * HU #11718 — qué tercero ajeno quedaría citado en el contrato si se aplica `templateCode` al
 * organismo `officeCode`, o `null` si la combinación es coherente.
 *
 * <p>Al emitir el trámite, el generador usa la plantilla resuelta del OT (o la genérica de
 * mandato cliente). Esta alerta sigue sirviendo en
 * Plataforma si alguien asigna a mano una redacción de otro organismo.</p>
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
  if (code === "" || code === MANDATO_TEMPLATE_AUTO_CODE) return null;

  // Sin dueños conocidos (genérica o código desconocido) no nombra a ningún organismo concreto.
  const bindings = MANDATO_FORMATO_VINCULOS[code];
  if (!bindings || bindings.length === 0) return null;

  const ot = (officeCode ?? "").trim();
  if (ot !== "" && bindings.some((b) => b.officeCode === ot)) return null;

  // Es de otro. Se nombra a quién, que es lo que el gestor necesita para juzgar.
  const nombres = bindings.map(
    (b) => b.institutionalMandataryName ?? b.chamberCity ?? b.officeName,
  );
  return [...new Set(nombres)].join(", ");
}
