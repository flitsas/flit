// HU #14455 / #14462 — mensajes de los errores de la caja del portal. El servidor manda `{ code, message }`; aquí solo se
// sobreescriben los que merecen un texto fijo y el resto usa el `message` del servidor.
import { pnError } from "@/components/admin/plataforma/persona-natural/pnErrors";

export const CAJA_NO_HABILITADA = "La caja no está habilitada.";
export const CAJA_COBRO_NO_ANULABLE = "El trámite ya está en FLIT 2.0 y su cobro no se puede anular.";
export const CAJA_PRODUCTO_NO_HABILITADO = "La caja no está habilitada para este organismo.";

const POR_CODIGO: Record<string, string> = {
  COBRO_NO_ANULABLE: CAJA_COBRO_NO_ANULABLE,
  PRODUCT_NOT_ENABLED_FOR_HEAD: CAJA_PRODUCTO_NO_HABILITADO,
};

export function cajaErrorMessage(err: unknown): string {
  const info = pnError(err);
  // Sin cuerpo, un 404 es el interruptor `PortalCaja:Habilitado` apagado.
  if (info.status === 404 && !info.code) return CAJA_NO_HABILITADA;
  if (info.code && POR_CODIGO[info.code]) return POR_CODIGO[info.code];
  return info.message;
}

export function cajaNoHabilitada(err: unknown): boolean {
  const info = pnError(err);
  return info.status === 404 && !info.code;
}
