import type { EstadoRadicado } from "@/lib/api/cliente";

export type Tono = "success" | "warning" | "alert";

export interface Titular {
  texto: string;
  tono: Tono;
}

/**
 * Titular del resultado de la consulta. Se decide con el macro-estado y el paso final que informa el back: «finalizado»
 * con el último paso completo es aprobado; «finalizado» con ese paso rechazado (anulado o revocado) es cerrado.
 */
export function titularDe(radicado: EstadoRadicado): Titular {
  const codigo = radicado.macroEstado.codigo;
  const ultimo = radicado.pasos.at(-1);
  if (codigo === "subsanacion") return { texto: "Tu trámite ha sido rechazado", tono: "alert" };
  if (codigo === "finalizado") {
    return ultimo?.estado === "done" ? { texto: "Tu trámite ha sido aprobado", tono: "success" } : { texto: "Tu trámite ha sido cerrado", tono: "alert" };
  }
  return { texto: "Tu trámite está en proceso", tono: "warning" };
}

export const esAprobado = (radicado: EstadoRadicado): boolean => titularDe(radicado).texto === "Tu trámite ha sido aprobado";

export const CLASE_TITULAR: Record<Tono, string> = { success: "text-success", warning: "text-warning", alert: "text-alert" };
export const CLASE_MENSAJE: Record<Tono, string> = { success: "bg-success/10", warning: "bg-warning/10", alert: "bg-destructive/10" };
