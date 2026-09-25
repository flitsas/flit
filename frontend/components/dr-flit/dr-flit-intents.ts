import { COPY } from "@/lib/copy/copy-catalog";

export type DrFlitSession = "gestion" | "ayuda";

/** Intents de búsqueda operativa (sesión Gestión). */
export type DrFlitIntentId = "placa" | "vin" | "tramite" | "cliente";

/** Opciones de la sesión Ayuda. */
export type DrFlitHelpOptionId = "necesito-ayuda" | "normativa" | "soporte";

/** Intent interno solo para flujo de documentación. */
export type DrFlitHelpIntentId = "ayuda";

export type DrFlitClientBranch = "tramites" | "validaciones";

export interface DrFlitIntent {
  id: DrFlitIntentId;
  label: string;
  valueLabel: string;
  /** Pregunta específica del intent; si falta, se usa la genérica de `buildValuePrompt`. */
  prompt?: string;
}

export interface DrFlitHelpOption {
  id: DrFlitHelpOptionId;
  label: string;
  description?: string;
}

// HU-E — las palabras del menú salen del catálogo canónico OT↔Gestor (HU #12693): «Placa», «VIN»,
// «Trámite», «Radicado». «Cliente» no está homologado en el catálogo y se escribe aquí.
export const DR_FLIT_GESTION_INTENTS: readonly DrFlitIntent[] = [
  {
    id: "placa",
    label: `Buscar por ${COPY.A02Placa.toLowerCase()}`,
    valueLabel: COPY.A02Placa.toLowerCase(),
  },
  { id: "vin", label: `Buscar por ${COPY.A02Vin}`, valueLabel: COPY.A02Vin },
  {
    id: "tramite",
    label: `Buscar por ${COPY.A03.toLowerCase()}`,
    valueLabel: COPY.B15.toLowerCase(),
    prompt:
      "Indícame el número de radicado del trámite (por ejemplo FT1-0000012 o solo 12). También acepto el ID del trámite.",
  },
  {
    id: "cliente",
    label: "Buscar por cliente",
    valueLabel: "cliente (documento o nombre)",
  },
] as const;

export const DR_FLIT_HELP_OPTIONS: readonly DrFlitHelpOption[] = [
  {
    id: "necesito-ayuda",
    label: "Necesito ayuda",
    description: "Consulta la documentación del sistema",
  },
  {
    id: "normativa",
    label: "Normativa",
    description: "La resolución que avala los trámites virtuales",
  },
  {
    id: "soporte",
    label: "Soporte",
    description: "Canales de contacto y radicación de casos",
  },
] as const;

/** @deprecated Usar DR_FLIT_GESTION_INTENTS */
export const DR_FLIT_INTENTS = DR_FLIT_GESTION_INTENTS;

/**
 * HU-F — canales de soporte configurables por ambiente (`NEXT_PUBLIC_DR_FLIT_SUPPORT_*`, se hornean
 * en build). El teléfono NO tiene respaldo: sin configurar no se muestra, antes que mostrar un número
 * inventado. Correo y URL conservan el respaldo institucional.
 */
function envOrNull(value: string | undefined): string | null {
  const v = value?.trim();
  return v ? v : null;
}

export const DR_FLIT_SUPPORT_EMAIL =
  envOrNull(process.env.NEXT_PUBLIC_DR_FLIT_SUPPORT_EMAIL) ?? "soporte@flitsas.com";
export const DR_FLIT_SUPPORT_PHONE: string | null = envOrNull(
  process.env.NEXT_PUBLIC_DR_FLIT_SUPPORT_PHONE,
);
export const DR_FLIT_SUPPORT_CASE_URL =
  envOrNull(process.env.NEXT_PUBLIC_DR_FLIT_SUPPORT_CASE_URL) ?? "https://flitsas.com.co/SOPORTE/";

export function getIntentById(id: DrFlitIntentId): DrFlitIntent | undefined {
  return DR_FLIT_GESTION_INTENTS.find((i) => i.id === id);
}

export function getHelpOptionById(
  id: DrFlitHelpOptionId,
): DrFlitHelpOption | undefined {
  return DR_FLIT_HELP_OPTIONS.find((o) => o.id === id);
}

export function buildGreeting(displayName?: string | null): string {
  const name = displayName?.trim();
  const hello = name ? `Hola ${name}` : "Hola";
  return `${hello} 👋, soy DR. FLIT. En **Gestión** localizo registros; en **Ayuda** te guío con documentación y soporte.`;
}

export function buildValuePrompt(intent: DrFlitIntent): string {
  return intent.prompt ?? `Indícame el valor de ${intent.valueLabel} a consultar.`;
}

export function buildHelpValuePrompt(): string {
  return "Cuéntame qué necesitas. Por ejemplo: «cómo creo un trámite», «documentos de matrícula» o «preasignación de placas».";
}

/** Normativa — la fuente principal: la norma que habilita y regula lo que FLIT hace. */
export function buildNormativaIntro(): string {
  return "La **Resolución 20233040017145 de 2023** del Ministerio de Transporte es la norma que habilita los trámites virtuales ante los organismos de tránsito y fija sus requisitos: es la fuente principal que respalda a FLIT. Abre el resumen por temas o el texto completo en PDF.";
}

/** HU-G — hay artículo para el módulo actual: se ofrece primero, sin cerrar la pregunta libre. */
export function buildContextHelpPrompt(articleTitle: string): string {
  return `Estás en un módulo con documentación: **${articleTitle}**. Ábrelo abajo o cuéntame qué necesitas.`;
}

export function buildSupportIntro(): string {
  return "Estos son nuestros **canales de comunicación**. Si necesitas reportar un incidente o solicitud formal, genera un caso de soporte.";
}

export function buildClientBranchPrompt(cliente: string): string {
  return `¿Qué deseas consultar para **${cliente.trim()}**?`;
}

export function buildTramitesIntro(
  queryLabel: string,
  queryValue: string,
  count: number,
  /** Universo que cumple el criterio; si supera `count`, se dice cuántos se muestran. */
  total: number = count,
): string {
  if (count === 0) {
    return `No encontré trámites asociados a **${queryLabel}** \`${queryValue.trim()}\`.`;
  }
  const universo = Math.max(total, count);
  const n = universo === 1 ? "1 trámite" : `${universo} trámites`;
  const base = `Encontré **${n}** asociado(s) a **${queryLabel}** \`${queryValue.trim()}\`.`;
  if (universo > count) {
    return `${base} Te muestro los ${count} más recientes.`;
  }
  return base;
}

export function buildValidacionesIntro(
  cliente: string,
  count: number,
): string {
  if (count === 0) {
    return `No encontré validaciones de identidad para **${cliente.trim()}**. Puedes abrir el módulo Validaciones para revisar.`;
  }
  const n = count === 1 ? "1 validación" : `${count} validaciones`;
  return `Encontré **${n}** de identidad para **${cliente.trim()}**.`;
}

export function buildHelpIntro(query: string, count: number): string {
  if (count === 0) {
    return `No encontré un artículo del manual para «${query.trim()}». Prueba con otras palabras o abre el Centro de Ayuda.`;
  }
  const n = count === 1 ? "1 artículo" : `${count} artículos`;
  return `Encontré **${n}** en la documentación relacionados con tu consulta. Elige uno para abrirlo:`;
}

export function buildSearchError(message: string): string {
  return `No pude completar la búsqueda: ${message}`;
}

export const DR_FLIT_FREE_TEXT_HINT =
  "Elige una opción de Gestión o Ayuda.";

export const DR_FLIT_BACK_LABEL = "Volver al menú";

export const DR_FLIT_MANUAL_HOME_HREF = "/manual";

/**
 * HU-C — atajo al módulo «Historial por placa» (HU #12194/#12196) con la placa ya consultada.
 * Misma forma canónica que el módulo (sin espacios, mayúscula); el servidor la repite.
 */
export function buildHistorialPlacaHref(placa: string): string {
  const canon = placa.replace(/\s+/g, "").toUpperCase();
  return `/?m=historial-placa&placa=${encodeURIComponent(canon)}`;
}

export const DR_FLIT_CLIENT_BRANCHES: readonly {
  id: DrFlitClientBranch;
  label: string;
}[] = [
  { id: "tramites", label: "Ver trámites" },
  { id: "validaciones", label: "Ver validación de identidad" },
] as const;

export const DR_FLIT_SESSION_LABEL: Record<DrFlitSession, string> = {
  gestion: "Gestión",
  ayuda: "Ayuda",
};
