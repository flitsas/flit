// Lógica pura del cambio de contraventor (HU #13479/#13481, §7.4). La API vuelve a validar todo.

export interface CondicionViabilidad {
  codigo: string;
  descripcion: string;
  cumple: boolean;
}

export interface Viabilidad {
  viable: boolean;
  condiciones: CondicionViabilidad[];
  representante: string | null;
  organismo: string | null;
  correos: string[] | null;
}

export interface FormRespuestaCambio {
  resultado: "Aceptado" | "Rechazado" | "";
  causal: string;
  fecha: string;
  soporte: File | null;
}

/** `diaSolicitud` y `hoy` en aaaa-mm-dd (hora de Colombia). */
export function erroresRespuestaCambio(f: FormRespuestaCambio, diaSolicitud: string, hoy: string): string[] {
  const errores: string[] = [];
  if (!f.resultado) errores.push("Elige si el organismo aceptó o rechazó.");
  if (f.resultado === "Rechazado" && f.causal.trim().length < 10) errores.push("Escribe la causal del rechazo (al menos 10 caracteres).");
  if (!f.fecha) errores.push("Escribe la fecha de la respuesta del organismo.");
  else if (f.fecha > hoy || f.fecha < diaSolicitud) errores.push("La fecha de respuesta debe estar entre la solicitud y hoy.");
  return errores;
}

/** Cuerpo multipart de POST /comparendos/{id}/cambio-contraventor/{solicitudId}/respuesta. */
export function armarRespuestaCambio(f: FormRespuestaCambio): FormData {
  const form = new FormData();
  form.append("resultado", f.resultado);
  form.append("fecha", f.fecha);
  if (f.resultado === "Rechazado") form.append("causal", f.causal.trim());
  if (f.soporte) form.append("soporte", f.soporte);
  return form;
}

/** La solicitud que espera respuesta del organismo: la más reciente en estado Solicitado. */
export function solicitudPendiente<T extends { estado: string; numeroIntento: number }>(solicitudes: T[]): T | null {
  return [...solicitudes].filter((s) => s.estado === "Solicitado").sort((a, b) => b.numeroIntento - a.numeroIntento)[0] ?? null;
}
