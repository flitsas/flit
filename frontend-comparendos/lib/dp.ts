// Lógica pura del derecho de petición (HU #13491, §7.3) y del envío masivo (§6.17). La API vuelve a validar todo.
import type { Etiqueta } from "@/lib/etiquetas";

export interface TipologiaComparendo {
  id: string;
  codigo: string;
  nombre: string;
  descripcionJuridica: string;
  estadosLegalesPermitidos: string[];
  permiteMasivo: boolean;
  procede: boolean;
}

export interface Bloqueo {
  codigo: string;
  mensaje: string;
}

export interface VistaPreviaDp {
  puedeEnviar: boolean;
  bloqueos: Bloqueo[];
  advertencias: string[];
  radicado: string;
  organismo: string | null;
  destinatarios: string[];
  /** Copias que la empresa configuró para los DP (§7.3.1). */
  cc: string[];
  bcc: string[];
  representante: { nombres: string; cargo: string | null; numeroDocumento: string } | null;
  anexos: string[];
  estadoLegal: string;
}

/** Las que proceden primero (por el estado legal de hoy), luego por código. */
export function ordenarTipologias<T extends { codigo: string; procede: boolean }>(lista: T[]): T[] {
  return [...lista].sort((a, b) => Number(b.procede) - Number(a.procede) || a.codigo.localeCompare(b.codigo));
}

export interface FormRespuestaDp {
  radicadoOt: string;
  fecha: string;
  sentido: "Favorable" | "Desfavorable" | "";
  archivo: File | null;
}

/** `radicacion` y `hoy` en aaaa-mm-dd. */
export function erroresRespuestaDp(f: FormRespuestaDp, radicacion: string | null, hoy: string): string[] {
  const errores: string[] = [];
  const radicado = f.radicadoOt.trim();
  if (!radicado || radicado.length > 60) errores.push("Escribe el radicado de la respuesta del organismo (hasta 60 caracteres).");
  if (!f.sentido) errores.push("Elige el sentido de la respuesta.");
  if (!f.fecha) errores.push("Escribe la fecha de la respuesta.");
  else if (f.fecha > hoy || (radicacion && f.fecha < radicacion.slice(0, 10))) errores.push("La fecha debe estar entre la radicación y hoy.");
  if (!f.archivo) errores.push("Adjunta el PDF de la respuesta del organismo.");
  else if (f.archivo.type !== "application/pdf") errores.push("La respuesta debe ser un PDF.");
  return errores;
}

/** Cuerpo multipart de POST /comparendos/{id}/dp/respuesta. */
export function armarRespuestaDp(f: FormRespuestaDp): FormData {
  const form = new FormData();
  form.append("radicadoOt", f.radicadoOt.trim());
  form.append("fecha", f.fecha);
  form.append("sentido", f.sentido);
  if (f.archivo) form.append("archivo", f.archivo);
  return form;
}

export const MAXIMO_LOTE = 500;

/** Lo mínimo de una fila de la bandeja para armar un lote. */
export interface FilaLote {
  id: string;
  numero: string;
  organismoTransitoId: string | null;
  organismo: string | null;
  activo: boolean;
  pagado: boolean;
  tieneDp: boolean;
}

export interface RevisionLote {
  /** Bloqueantes: la API rechazaría el lote completo. */
  errores: string[];
  /** Comparendos que la API omitirá con su motivo (no detienen el lote). */
  omitiran: { numero: string; motivo: string }[];
  organismo: string | null;
}

/** Misma regla que la API: de 1 a 500 comparendos, todos del mismo organismo de tránsito (S-62). */
export function revisarLote(filas: FilaLote[]): RevisionLote {
  const errores: string[] = [];
  if (filas.length === 0) errores.push("Selecciona al menos un comparendo.");
  if (filas.length > MAXIMO_LOTE) errores.push(`Selecciona como máximo ${MAXIMO_LOTE} comparendos.`);
  if (filas.some((f) => !f.organismoTransitoId)) errores.push("Hay comparendos sin organismo de tránsito identificado.");
  if (new Set(filas.map((f) => f.organismoTransitoId).filter(Boolean)).size > 1)
    errores.push("Todos los comparendos del lote deben ser del mismo organismo de tránsito.");
  const omitiran = filas.flatMap((f) => {
    const motivo = !f.activo ? "Está inactivo" : f.pagado ? "Está pagado" : f.tieneDp ? "Ya tiene derecho de petición" : null;
    return motivo ? [{ numero: f.numero, motivo }] : [];
  });
  return { errores, omitiran, organismo: filas.find((f) => f.organismo)?.organismo ?? null };
}

export const ESTADO_LOTE: Record<string, Etiqueta> = {
  EnCurso: { texto: "En curso", tono: "info" },
  Completado: { texto: "Completado", tono: "success" },
  CompletadoConErrores: { texto: "Con errores", tono: "warning" },
};

export const ESTADO_ITEM_LOTE: Record<string, Etiqueta> = {
  Pendiente: { texto: "Pendiente", tono: "neutral" },
  Enviado: { texto: "Enviado", tono: "success" },
  Omitido: { texto: "Omitido", tono: "warning" },
  Fallido: { texto: "Falló", tono: "danger" },
};

/** Avance del lote en porcentaje: lo ya resuelto (enviado, omitido o fallido) sobre el total. */
export function avanceLote(l: { total: number; enviados: number; omitidos: number; fallidos: number }): number {
  if (l.total <= 0) return 100;
  return Math.min(100, Math.round(((l.enviados + l.omitidos + l.fallidos) / l.total) * 100));
}
