// Columnas del informe del organismo y su formateo.
//
// La lista vive en un solo sitio a propósito: la tabla, el selector de columnas y el CSV consumen la
// MISMA definición, de modo que lo exportado es literalmente lo que se está viendo. Mantener dos
// listas paralelas terminaría con un CSV que no coincide con la pantalla, que es la peor forma de
// perder la confianza en un informe.

import type { OtReportRow, OtReportSort } from "@/lib/api/ot-metrics";
import { OT_REPORT_ESTADOS, OT_REPORT_SORT } from "@/lib/api/ot-metrics";

// ── Estados ────────────────────────────────────────────────────────────────────

export interface EstadoMeta {
  label: string;
  color: string;
  /** Qué significa, en la voz del organismo. Alimenta el tooltip y la leyenda. */
  hint: string;
}

export const ESTADO_META: Record<string, EstadoMeta> = {
  [OT_REPORT_ESTADOS.enRevision]: {
    label: "En revisión",
    color: "#557EFF",
    hint: "Radicado y esperando decisión del organismo. Es el trabajo que está sobre la mesa.",
  },
  [OT_REPORT_ESTADOS.esperandoPlaca]: {
    label: "Esperando placa",
    color: "#00DBD5",
    hint: "Aprobado el expediente, falta asignar la placa. Sigue siendo trabajo del organismo.",
  },
  [OT_REPORT_ESTADOS.esperandoCliente]: {
    label: "Esperando al cliente",
    color: "#94A3B8",
    hint: "La pelota está en la empresa: SOAT, impuestos o trámite pausado. No cuenta como demora del organismo.",
  },
  [OT_REPORT_ESTADOS.aprobado]: {
    label: "Aprobado",
    color: "#8CC63F",
    hint: "Cerrado a favor. Es el desenlace que el informe mide contra el tiempo.",
  },
  [OT_REPORT_ESTADOS.enSubsanacion]: {
    label: "En subsanación",
    color: "#F9AC00",
    hint: "Rechazado y con subsanación abierta: va a volver. Es trabajo futuro ya comprometido.",
  },
  [OT_REPORT_ESTADOS.rechazado]: {
    label: "Rechazado",
    color: "#FF4E00",
    hint: "Rechazado y sin subsanación abierta: por ahora no vuelve.",
  },
  [OT_REPORT_ESTADOS.anulado]: {
    label: "Anulado",
    color: "#64748B",
    hint: "La empresa lo dio de baja después de radicarlo.",
  },
  [OT_REPORT_ESTADOS.otro]: {
    label: "Otro",
    color: "#CBD5E1",
    hint: "Volvió a un estado anterior a la radicación. No debería ocurrir; si aparece, hay que mirarlo.",
  },
};

/** Orden en el que se leen los estados: primero lo abierto, luego lo cerrado. */
export const ESTADO_ORDER: string[] = [
  OT_REPORT_ESTADOS.enRevision,
  OT_REPORT_ESTADOS.esperandoPlaca,
  OT_REPORT_ESTADOS.esperandoCliente,
  OT_REPORT_ESTADOS.enSubsanacion,
  OT_REPORT_ESTADOS.aprobado,
  OT_REPORT_ESTADOS.rechazado,
  OT_REPORT_ESTADOS.anulado,
  OT_REPORT_ESTADOS.otro,
];

export function estadoMeta(estado: string): EstadoMeta {
  return ESTADO_META[estado] ?? { label: estado, color: "#CBD5E1", hint: "" };
}

const MODALIDAD_LABEL: Record<string, string> = {
  matricula_inicial: "Matrícula inicial",
  traspaso: "Traspaso",
};

// ── Formateo ───────────────────────────────────────────────────────────────────

const intFmt = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 });
const numFmt = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 1 });

export function formatInt(value: number | null | undefined): string {
  return value === null || value === undefined || Number.isNaN(value) ? "—" : intFmt.format(value);
}

/**
 * Duración legible. Por debajo de 48 h se dice en horas y por encima en días: «73,5 h» obliga a
 * dividir mentalmente, y el informe se lee de un vistazo o no se lee.
 */
export function formatHours(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  if (value < 1) return `${intFmt.format(Math.round(value * 60))} min`;
  if (value < 48) return `${numFmt.format(value)} h`;
  return `${numFmt.format(value / 24)} días`;
}

export function formatDays(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return `${numFmt.format(value)} ${value === 1 ? "día" : "días"}`;
}

/** Fecha corta en huso de Bogotá — el mismo con el que el backend agrupa el informe. */
export function formatDate(iso: string | null): string {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("es-CO", {
    timeZone: "America/Bogota",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(date);
}

export function formatDateTime(iso: string | null): string {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("es-CO", {
    timeZone: "America/Bogota",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

// ── Columnas ───────────────────────────────────────────────────────────────────

export interface ReportColumn {
  id: string;
  label: string;
  /** Grupo del selector: agrupa por la pregunta que responde la columna, no por tipo de dato. */
  group: "Identificación" | "Estado" | "Tiempos" | "Calidad";
  /** Texto plano de la celda. Es también lo que va al CSV: una sola verdad por columna. */
  value: (row: OtReportRow) => string;
  /** Campo de orden del backend, si la columna es ordenable. */
  sort?: OtReportSort;
  numeric?: boolean;
  /** Visible al abrir el informe por primera vez. */
  defaultVisible?: boolean;
}

export const REPORT_COLUMNS: ReportColumn[] = [
  {
    id: "referencia",
    label: "Radicado",
    group: "Identificación",
    value: (r) => r.referenceNumber,
    sort: OT_REPORT_SORT.referencia,
    defaultVisible: true,
  },
  {
    id: "empresa",
    label: "Empresa",
    group: "Identificación",
    value: (r) => r.clientTenantName,
    sort: OT_REPORT_SORT.empresa,
    defaultVisible: true,
  },
  {
    id: "modalidad",
    label: "Modalidad",
    group: "Identificación",
    value: (r) => MODALIDAD_LABEL[r.modalidad] ?? r.modalidad,
    defaultVisible: true,
  },
  {
    id: "placa",
    label: "Placa",
    group: "Identificación",
    value: (r) => r.placa ?? "—",
    defaultVisible: true,
  },
  {
    id: "vin",
    label: "VIN",
    group: "Identificación",
    value: (r) => r.vin ?? "—",
  },
  {
    id: "estado",
    label: "Estado",
    group: "Estado",
    value: (r) => estadoMeta(r.estadoOt).label,
    sort: OT_REPORT_SORT.estado,
    defaultVisible: true,
  },
  {
    id: "prioritario",
    label: "Prioritario",
    group: "Estado",
    value: (r) => (r.prioritario ? "Sí" : "No"),
  },
  {
    id: "radicado_en",
    label: "Radicado el",
    group: "Tiempos",
    value: (r) => formatDate(r.radicadoEn),
    sort: OT_REPORT_SORT.radicado,
    defaultVisible: true,
  },
  {
    id: "ultima_radicacion",
    label: "Última radicación",
    group: "Tiempos",
    value: (r) => formatDateTime(r.ultimaRadicacionEn),
  },
  {
    id: "decidido_en",
    label: "Decidido el",
    group: "Tiempos",
    value: (r) => formatDate(r.decididoEn),
    sort: OT_REPORT_SORT.decidido,
    defaultVisible: true,
  },
  {
    id: "horas_decision",
    label: "Tiempo de decisión",
    group: "Tiempos",
    value: (r) => formatHours(r.horasHastaDecision),
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "dias_organismo",
    label: "Días en el organismo",
    group: "Tiempos",
    value: (r) => formatDays(r.diasEnOrganismo),
    sort: OT_REPORT_SORT.dias,
    numeric: true,
  },
  {
    id: "decidido_por",
    label: "Decidido por",
    group: "Calidad",
    value: (r) => r.decididoPor ?? "—",
  },
  {
    id: "devoluciones",
    label: "Devoluciones",
    group: "Calidad",
    value: (r) => formatInt(r.devoluciones),
    sort: OT_REPORT_SORT.devoluciones,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "causales",
    label: "Causales del último rechazo",
    group: "Calidad",
    value: (r) => (r.causalesUltimoRechazo.length === 0 ? "—" : r.causalesUltimoRechazo.join(" · ")),
  },
];

export const COLUMN_GROUPS: ReportColumn["group"][] = [
  "Identificación",
  "Estado",
  "Tiempos",
  "Calidad",
];

export function defaultVisibleColumns(): string[] {
  return REPORT_COLUMNS.filter((c) => c.defaultVisible).map((c) => c.id);
}

/**
 * Vistas de arranque del informe. Son dos preguntas distintas, no dos estilos: «cómo va la gestión»
 * mira el desenlace de cada trámite, «cuánto tardo» mira el reloj. Con una sola vista, quien viene a
 * la segunda pregunta tiene que armarla a mano cada vez.
 */
export const REPORT_PRESETS: { id: string; label: string; hint: string; columns: string[] }[] = [
  {
    id: "gestion",
    label: "Gestión",
    hint: "Qué recibí de cada empresa y en qué acabó.",
    columns: [
      "referencia",
      "empresa",
      "modalidad",
      "placa",
      "estado",
      "radicado_en",
      "decidido_en",
      "devoluciones",
    ],
  },
  {
    id: "tiempos",
    label: "Tiempos de respuesta",
    hint: "Cuánto tardo en decidir y quién decidió.",
    columns: [
      "referencia",
      "empresa",
      "estado",
      "radicado_en",
      "decidido_en",
      "horas_decision",
      "dias_organismo",
      "decidido_por",
    ],
  },
  {
    id: "calidad",
    label: "Calidad de lo que llega",
    hint: "Qué se devuelve y por qué.",
    columns: [
      "referencia",
      "empresa",
      "modalidad",
      "estado",
      "devoluciones",
      "causales",
      "decidido_por",
    ],
  },
];

// ── Exportación ────────────────────────────────────────────────────────────────

/**
 * Neutraliza el prefijo de fórmula. Un valor que empieza por `=`, `+`, `-` o `@` lo ejecuta Excel al
 * abrir el archivo: es una vía de inyección real, y aquí los valores vienen de campos que la empresa
 * cliente escribe.
 */
function neutralize(value: string): string {
  return /^[=+\-@\t\r]/.test(value) ? `'${value}` : value;
}

function csvCell(value: string): string {
  const safe = neutralize(value);
  return `"${safe.replace(/"/g, '""')}"`;
}

/**
 * CSV de lo que se está viendo: mismas columnas, mismo orden, mismo formateo.
 *
 * Separador `;` y BOM porque el destino real es Excel en español, que con `,` mete la fila entera en
 * una sola celda y sin BOM rompe las tildes.
 */
export function buildReportCsv(rows: OtReportRow[], visibleColumnIds: string[]): string {
  const columns = REPORT_COLUMNS.filter((c) => visibleColumnIds.includes(c.id));
  const header = columns.map((c) => csvCell(c.label)).join(";");
  const body = rows.map((row) => columns.map((c) => csvCell(c.value(row))).join(";"));
  return `﻿${[header, ...body].join("\r\n")}`;
}

/** Nombre del archivo con el rango dentro: un `informe.csv` suelto en Descargas no dice de cuándo es. */
export function reportFileName(from: string, to: string): string {
  return `informe-ot-${from}-a-${to}.csv`;
}
