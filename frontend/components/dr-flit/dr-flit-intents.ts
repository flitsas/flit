export type DrFlitSession = "gestion" | "ayuda";

/** Intents de búsqueda operativa (sesión Gestión). */
export type DrFlitIntentId = "placa" | "vin" | "tramite" | "cliente";

/** Opciones de la sesión Ayuda. */
export type DrFlitHelpOptionId = "necesito-ayuda" | "soporte";

/** Intent interno solo para flujo de documentación. */
export type DrFlitHelpIntentId = "ayuda";

export type DrFlitClientBranch = "tramites" | "validaciones";

export interface DrFlitIntent {
  id: DrFlitIntentId;
  label: string;
  valueLabel: string;
}

export interface DrFlitHelpOption {
  id: DrFlitHelpOptionId;
  label: string;
  description?: string;
}

export const DR_FLIT_GESTION_INTENTS: readonly DrFlitIntent[] = [
  { id: "placa", label: "Buscar por placa", valueLabel: "placa" },
  { id: "vin", label: "Buscar por VIN", valueLabel: "VIN" },
  {
    id: "tramite",
    label: "Búsqueda por Trámites",
    valueLabel: "ID del trámite",
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
    id: "soporte",
    label: "Soporte",
    description: "Canales de contacto y radicación de casos",
  },
] as const;

/** @deprecated Usar DR_FLIT_GESTION_INTENTS */
export const DR_FLIT_INTENTS = DR_FLIT_GESTION_INTENTS;

export const DR_FLIT_SUPPORT_EMAIL = "soporte@flitsas.com";
export const DR_FLIT_SUPPORT_PHONE = "300 000 0000";
export const DR_FLIT_SUPPORT_CASE_URL = "https://flitsas.com.co/SOPORTE/";

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
  return `Indícame el valor de ${intent.valueLabel} a consultar.`;
}

export function buildHelpValuePrompt(): string {
  return "Cuéntame qué necesitas. Por ejemplo: «cómo creo un trámite», «documentos de matrícula» o «preasignación de placas».";
}

export function buildSupportIntro(): string {
  return "Estos son nuestros **canales de comunicación**. Si necesitas reportar un incidente o solicitud formal, genera un caso de soporte.";
}

export function buildClientBranchPrompt(cliente: string): string {
  return `¿Qué deseas consultar para **${cliente.trim()}**? Validación de identidad o trámites.`;
}

export function buildTramitesIntro(
  queryLabel: string,
  queryValue: string,
  count: number,
): string {
  if (count === 0) {
    return `No encontré trámites asociados a **${queryLabel}** \`${queryValue.trim()}\`.`;
  }
  const n = count === 1 ? "1 trámite" : `${count} trámites`;
  return `Encontré **${n}** asociado(s) a **${queryLabel}** \`${queryValue.trim()}\`.`;
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
