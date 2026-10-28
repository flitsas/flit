import { useSyncExternalStore } from "react";

// Contexto que la landing le deja al wizard al abrir (o reanudar) un trámite. Vive SOLO en memoria de esta pestaña: nunca en
// la URL ni en el almacenamiento del navegador (Habeas Data), así que se pierde al recargar y el wizard debe pedir
// «Consultar mi trámite» de nuevo. El token de la sesión no está aquí: va en una cookie HttpOnly que solo ve el servidor.

export interface ContextoTramite {
  /** «iniciar» = trámite recién abierto; «subsanar» = vuelve a corregir uno devuelto (flujo de la HU F7). */
  intencion: "iniciar" | "subsanar";
  tipoCodigo: string;
  solicitudId?: string;
  /** Código público (radicado) del trámite. */
  radicado?: string;
  organismoId?: string;
  cedula?: string;
  placa?: string;
  vin?: string;
}

let actual: ContextoTramite | null = null;
const oyentes = new Set<() => void>();

export function fijarContexto(contexto: ContextoTramite): void {
  actual = contexto;
  oyentes.forEach((o) => o());
}

export function limpiarContexto(): void {
  actual = null;
  oyentes.forEach((o) => o());
}

export const obtenerContexto = (): ContextoTramite | null => actual;

const suscribir = (oyente: () => void) => {
  oyentes.add(oyente);
  return () => void oyentes.delete(oyente);
};

export function useContextoTramite(): ContextoTramite | null {
  return useSyncExternalStore(suscribir, obtenerContexto, () => null);
}
