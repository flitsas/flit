export type DrFlitIntentId = "placa" | "vin" | "tramite" | "cliente";

export type DrFlitClientBranch = "tramites" | "validaciones";

export interface DrFlitIntent {
  id: DrFlitIntentId;
  label: string;
  valueLabel: string;
}

export const DR_FLIT_INTENTS: readonly DrFlitIntent[] = [
  { id: "placa", label: "Buscar por placa", valueLabel: "placa" },
  { id: "vin", label: "Buscar por VIN", valueLabel: "VIN" },
  {
    id: "tramite",
    label: "Búsqueda por Trámites",
    valueLabel: "ID del trámite",
  },
  { id: "cliente", label: "Buscar por cliente", valueLabel: "cliente (documento o nombre)" },
] as const;

export function getIntentById(id: DrFlitIntentId): DrFlitIntent | undefined {
  return DR_FLIT_INTENTS.find((i) => i.id === id);
}

export function buildGreeting(displayName?: string | null): string {
  const name = displayName?.trim();
  const hello = name ? `Hola ${name}` : "Hola";
  return `${hello} 👋, soy DR. FLIT. Puedo ayudarte a localizar registros por placa, VIN, trámite o cliente.`;
}

export function buildValuePrompt(intent: DrFlitIntent): string {
  return `Indícame el valor de ${intent.valueLabel} a consultar.`;
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

export function buildSearchError(message: string): string {
  return `No pude completar la búsqueda: ${message}`;
}

export const DR_FLIT_FREE_TEXT_HINT = "Elige una sugerencia para empezar";

export const DR_FLIT_BACK_LABEL = "Buscar de otra forma";

export const DR_FLIT_CLIENT_BRANCHES: readonly {
  id: DrFlitClientBranch;
  label: string;
}[] = [
  { id: "tramites", label: "Ver trámites" },
  { id: "validaciones", label: "Ver validación de identidad" },
] as const;
