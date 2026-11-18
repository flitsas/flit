// Seguimiento del trámite del punto con el mismo contenido que el `RadicadoTracking` del diseño de referencia (Lovable):
// 5 pasos fijos. Aquí se arma el seguimiento de lo que solo conocemos por el cierre del pago (confirmación final y consulta por
// código); cuando el trámite ya está en FLIT 2.0 los pasos y las fechas reales los entrega `radicados.consultar`.
//
// Mapeo de estados reales → título / seguimiento (consulta por código, `CierrePuntoRespuesta.estadoCierre`):
//   en_registro (pagada, aún sin radicado FLIT 2.0)  → «Tu trámite está en proceso»   · Enviado al OT = actual
//   registrado  (radicado FLIT 2.0 creado)           → «Tu trámite está en proceso»   · Enviado al OT = actual
//                                                      (si FLIT 2.0 ya informa revisión/aprobación se usa su seguimiento real)
//   rechazado   (FLIT 2.0 no pudo crearlo)           → «Tu trámite ha sido rechazado» · Enviado al OT = rechazado
// Mapeo de estados de `radicados.consultar` (ver lib/radicados/presentacion.ts):
//   pendiente_validacion (borrador / pendiente de pago / pagada) y en_revision_ot → «en proceso»
//   subsanacion → «rechazado» (con causal y ruta de corrección) · finalizado + último paso hecho → «aprobado»
import type { PasoVisible } from "@/components/modales/RadicadoTracking";
import type { EstadoCierrePunto } from "@/lib/api/cliente";
import type { Tono } from "@/lib/radicados/presentacion";

export const TITULOS_SEGUIMIENTO = ["Borrador del trámite", "Validación de identidad", "Enviado al OT", "En revisión", "Aprobado / Rechazado"] as const;

/** Seguimiento de un trámite pagado en el punto según el cierre: la identidad ya se validó antes de poder pagar. */
export function pasosDeCierre(estado: EstadoCierrePunto): PasoVisible[] {
  return TITULOS_SEGUIMIENTO.map((titulo, i) => ({
    titulo,
    fecha: null,
    estado: i < 2 ? "done" : i === 2 ? (estado === "rechazado" ? "rejected" : "current") : "pending",
  }));
}

/** Titular de la consulta por código (3 títulos del diseño de referencia). */
export function titularDeCierre(estado: EstadoCierrePunto): { texto: string; tono: Tono } {
  return estado === "rechazado" ? { texto: "Tu trámite ha sido rechazado", tono: "alert" } : { texto: "Tu trámite está en proceso", tono: "warning" };
}

export const MENSAJE_EN_PROCESO =
  "Tu trámite está en curso y aún no presenta respuesta por parte del Organismo de Tránsito. Te notificaremos a tu correo tan pronto sea procesado.";
