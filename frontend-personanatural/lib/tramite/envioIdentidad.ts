import { useSyncExternalStore } from "react";

// Personas cuya validación de identidad no se pudo enviar sola al pasar de Actores. Vive solo en memoria de la pestaña
// (sin documentos ni nombres: solo la cantidad por solicitud) para que el Resumen pueda avisar.

const fallidos = new Map<string, Set<string>>();
const oyentes = new Set<() => void>();
const avisar = () => oyentes.forEach((o) => o());

/** Marca (o desmarca) a una persona de la solicitud con el envío automático fallido. */
export function marcarEnvioFallido(solicitudId: string, clavePersona: string, fallido: boolean): void {
  const conjunto = fallidos.get(solicitudId) ?? new Set<string>();
  const tenia = conjunto.has(clavePersona);
  if (fallido === tenia) return;
  if (fallido) conjunto.add(clavePersona);
  else conjunto.delete(clavePersona);
  fallidos.set(solicitudId, conjunto);
  cantidades.delete(solicitudId);
  avisar();
}

// Instantánea estable por solicitud (useSyncExternalStore exige el mismo valor mientras nada cambie).
const cantidades = new Map<string, number>();
const cantidadDe = (solicitudId: string): number => {
  let n = cantidades.get(solicitudId);
  if (n === undefined) {
    n = fallidos.get(solicitudId)?.size ?? 0;
    cantidades.set(solicitudId, n);
  }
  return n;
};

const suscribir = (oyente: () => void) => {
  oyentes.add(oyente);
  return () => void oyentes.delete(oyente);
};

/** Cuántas personas de la solicitud tienen su validación de identidad pendiente de envío. */
export function useEnviosFallidos(solicitudId: string): number {
  return useSyncExternalStore(
    suscribir,
    () => cantidadDe(solicitudId),
    () => 0,
  );
}

export function limpiarEnviosFallidos(): void {
  fallidos.clear();
  cantidades.clear();
  avisar();
}
