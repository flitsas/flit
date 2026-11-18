import { comoCajaError } from "@/lib/api/ot-caja";

// Mensajes de la caja: una frase fija por `code` del servidor. Lo desconocido cae en una frase genérica.
export type TonoMensaje = "error" | "aviso";

export interface MensajeDeCaja {
  texto: string;
  tono: TonoMensaje;
}

/** Texto de la espera de un 429: «2 minutos», «1 minuto» o «30 segundos». */
export function esperaLegible(segundos: number | undefined): string {
  if (!segundos || segundos <= 0) return "unos minutos";
  if (segundos < 60) return `${Math.ceil(segundos)} segundos`;
  const minutos = Math.ceil(segundos / 60);
  return minutos === 1 ? "1 minuto" : `${minutos} minutos`;
}

export const CAJA_NO_HABILITADA = "La caja no está habilitada para tu organismo";
export const CAJA_COBRO_NO_ANULABLE = "El trámite ya está en FLIT 2.0 y su cobro no se puede anular.";

const POR_CODIGO: Record<string, MensajeDeCaja> = {
  no_encontrado: { texto: "No encontramos ese trámite.", tono: "error" },
  otro_organismo: { texto: "Este trámite no es de este organismo.", tono: "error" },
  ya_pagado: { texto: "Este trámite ya fue pagado.", tono: "aviso" },
  cobro_vencido: { texto: "El pago en caja de este trámite venció.", tono: "error" },
  no_habilitado: { texto: "Este trámite no tiene el pago en caja habilitado.", tono: "error" },
  medio_caja_requerido: { texto: "Elija cómo se cobró.", tono: "error" },
  // Código que devuelve core-api (/api/v1/ot/caja) cuando falta el medio o no es válido.
  medio_invalido: { texto: "Elija cómo se cobró.", tono: "error" },
  detalle_requerido: { texto: "Escriba el detalle del medio.", tono: "error" },
  prefactura_invalida: { texto: "La prefactura debe ser PDF, JPG o PNG de hasta 5 MB.", tono: "error" },
  total_cambio: { texto: "El total cambió. Revise el nuevo valor y confirme de nuevo.", tono: "aviso" },
  cobro_no_anulable: { texto: CAJA_COBRO_NO_ANULABLE, tono: "error" },
};

export const MENSAJE_GENERICO_CAJA: MensajeDeCaja = { texto: "No pudimos completar la acción. Intente de nuevo.", tono: "error" };

/** La ruta de la caja no existe para el organismo: el 404 sin `code` (con `code` es «no encontrado»). */
export function cajaNoHabilitada(error: unknown): boolean {
  const e = comoCajaError(error);
  return e.status === 404 && e.code === "";
}

export function mensajeDeCaja(origen: unknown): MensajeDeCaja {
  const error = comoCajaError(origen);
  if (cajaNoHabilitada(error)) return { texto: CAJA_NO_HABILITADA, tono: "error" };
  if (error.code === "rate_limited" || error.status === 429) {
    return { texto: `Demasiadas búsquedas sin resultado. Espere ${esperaLegible(error.reintentarEnSegundos)}.`, tono: "error" };
  }
  if (error.status === 503) return { texto: "El servicio no está disponible. Intente de nuevo en unos minutos.", tono: "error" };
  return POR_CODIGO[error.code] ?? MENSAJE_GENERICO_CAJA;
}
