// Ningún código interno visible al usuario. Helper único para pintar un código (estado, tipo, error…):
//   1. la etiqueta que manda la API, si la trae;
//   2. si falta, el diccionario local de respaldo;
//   3. si tampoco hay, «Sin clasificar» —nunca el código— y un console.warn técnico (contexto y código, sin datos
//      personales).
// Los códigos siguen viajando igual como valores; esto es solo presentación. Este módulo no importa nada de lib/ para que
// cualquier módulo de dominio pueda usarlo sin ciclos.

/** Texto neutro cuando un código no tiene rótulo. */
export const ROTULO_SIN_CLASIFICAR = "Sin clasificar";

export type Respaldo = Readonly<Record<string, string>> | ((codigo: string) => string | null | undefined);

export interface Rotular {
  /** Dónde se pinta (para el aviso técnico): «estado del lote», «tipo de aviso»… */
  contexto: string;
  codigo: string | null | undefined;
  /** Etiqueta de la API. Vacía o igual al código cuenta como ausente. */
  etiqueta?: string | null;
  respaldo?: Respaldo;
}

const valida = (texto: string | null | undefined, codigo: string): texto is string => Boolean(texto && texto.trim() && texto.trim() !== codigo);

export function rotular({ contexto, codigo, etiqueta, respaldo }: Rotular): string {
  const c = codigo ?? "";
  if (valida(etiqueta, c)) return etiqueta.trim();
  if (respaldo && c) {
    const local = typeof respaldo === "function" ? respaldo(c) : Object.prototype.hasOwnProperty.call(respaldo, c) ? respaldo[c] : undefined;
    if (local && local.trim()) return local;
  }
  console.warn(`[rotulos] Código sin rótulo en «${contexto}»: ${c || "(vacío)"}`);
  return ROTULO_SIN_CLASIFICAR;
}

/** Código interno con forma de enum (MAYÚSCULAS con guion bajo): nunca se muestra como mensaje. */
export function pareceCodigo(texto: string | null | undefined): boolean {
  return Boolean(texto && /^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$/.test(texto.trim()));
}

/** Estados de aprovisionamiento de la compañía (plan §3.1, F1a-CF10). */
export const ESTADOS_APROVISIONAMIENTO: Readonly<Record<string, string>> = {
  PENDIENTE: "Pendiente",
  LISTO: "Listo",
  LISTO_SIN_WEBHOOK: "Listo sin webhook",
  ERROR: "Error",
  INCIERTO: "Incierto",
  REQUIERE_CREDENCIALES: "Requiere credenciales",
  REQUIERE_REVISION_NIT: "Requiere revisión del NIT",
  SUSPENDIDO: "Suspendido",
};

/** Nombre del estado de aprovisionamiento; desconocido, «Sin clasificar». */
export function nombreEstadoAprovisionamiento(estado: string | null | undefined): string {
  return rotular({ contexto: "estado de aprovisionamiento", codigo: estado, respaldo: ESTADOS_APROVISIONAMIENTO });
}
