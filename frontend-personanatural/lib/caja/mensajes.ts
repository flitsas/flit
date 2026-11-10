import { CajaError } from "./cliente";

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

const POR_CODIGO: Record<string, MensajeDeCaja> = {
  no_encontrado: { texto: "No encontramos ese trámite.", tono: "error" },
  otro_organismo: { texto: "Este trámite no es de este organismo.", tono: "error" },
  ya_pagado: { texto: "Este trámite ya fue pagado.", tono: "aviso" },
  cobro_vencido: { texto: "El pago en caja de este trámite venció.", tono: "error" },
  no_habilitado: { texto: "Este trámite no tiene el pago en caja habilitado.", tono: "error" },
  sin_permiso: { texto: "Su usuario no puede cobrar en este organismo.", tono: "error" },
  medio_invalido: { texto: "Elija cómo se cobró.", tono: "error" },
  detalle_requerido: { texto: "Escriba el detalle del medio.", tono: "error" },
  prefactura_invalida: { texto: "La prefactura debe ser PDF, JPG o PNG de hasta 5 MB.", tono: "error" },
  total_cambio: { texto: "El total cambió. Revise el nuevo valor y confirme de nuevo.", tono: "aviso" },
};

export const MENSAJE_GENERICO_CAJA: MensajeDeCaja = { texto: "No pudimos completar la acción. Intente de nuevo.", tono: "error" };

/** Mensaje para la persona de caja. Los códigos conocidos usan su frase fija; el resto, una frase genérica. */
export function mensajeDeCaja(error: unknown): MensajeDeCaja {
  if (!(error instanceof CajaError)) return MENSAJE_GENERICO_CAJA;
  if (error.code === "demasiados_intentos" || error.status === 429) {
    return { texto: `Demasiadas búsquedas sin resultado. Espere ${esperaLegible(error.reintentarEnSegundos)}.`, tono: "error" };
  }
  if (error.status === 503) return { texto: "El servicio no está disponible. Intente de nuevo en unos minutos.", tono: "error" };
  return POR_CODIGO[error.code] ?? MENSAJE_GENERICO_CAJA;
}
