import { ENLACE_ACCION_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import type { RenglonSaneamiento } from "@/lib/saneamiento/api";

// Piezas comunes de PS-5 (gestión de un saneamiento en el backoffice, CR-5): tipos de las transiciones y los modales,
// textos fijos de error y la regla para «Cerrar como saneado». Sin estado ni efectos.

export type Transicion = "anular" | "reabrir" | "no_procede" | "saneado";

export type ModalAbierto =
  | { tipo: Transicion }
  | { tipo: "agregar" }
  | { tipo: "evidencia"; renglon: RenglonSaneamiento }
  | { tipo: "quitar-evidencia"; renglon: RenglonSaneamiento }
  | { tipo: "quitar-renglon"; renglon: RenglonSaneamiento };

export const RED = "Revisa tu conexión e intenta de nuevo.";
export const GENERICO = "No pudimos guardar el cambio. Intenta de nuevo.";
export const ERROR_DESCARGA = "No pudimos generar el archivo. Intenta de nuevo.";
export const CIERRE_INCOMPLETO = "Para cerrar como saneado ningún concepto puede quedar pendiente y al menos uno debe quedar saneado.";
export const DEMASIADOS_REEMPLAZOS = "Este concepto ya llegó al máximo de reemplazos de la evidencia.";

export const ENLACE_PELIGRO_CLS = `${ENLACE_ACCION_CLS} text-[color:var(--di-danger)]`;

export const codigo = (e: unknown) => (e instanceof ApiError ? e.code : undefined);
export const es403 = (e: unknown) => e instanceof ApiError && e.status === 403;

/** Copia del registro sin la clave. */
export function sinClave<T>(registro: Record<string, T>, clave: string): Record<string, T> {
  const copia = { ...registro };
  delete copia[clave];
  return copia;
}

/** Motivo por el que aún no se puede «Cerrar como saneado» (regla del plan §3.3), o null si se puede. */
export function motivoNoCerrarSaneado(renglones: readonly RenglonSaneamiento[], sinGuardar: boolean): string | null {
  if (sinGuardar) return "Espera a que se guarden los cambios.";
  if (renglones.length === 0) return "Para cerrar como saneado agrega al menos un concepto.";
  const pendientes = renglones.filter((r) => r.resultado === "PENDIENTE").length;
  if (pendientes > 0) return `Para cerrar como saneado ningún concepto puede quedar pendiente (faltan ${pendientes}).`;
  if (!renglones.some((r) => r.resultado === "SANEADO")) return "Para cerrar como saneado al menos un concepto debe quedar saneado.";
  return null;
}
