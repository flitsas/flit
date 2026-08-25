// Columnas del informe del periodo y formateo compartido por los reportes del organismo.
//
// La lista vive en un solo sitio a propósito: la tabla, el selector de columnas y los exports
// consumen la MISMA definición, de modo que lo exportado es literalmente lo que se está viendo.
// La maquinaria genérica (CSV, Excel, presets) está en `columns.ts`, compartida con el informe de
// revisores; aquí queda solo lo que es propio de este informe.

import type { OtReportRow, OtReportSort } from "@/lib/api/ot-metrics";
import { OT_REPORT_ESTADOS, OT_REPORT_SORT } from "@/lib/api/ot-metrics";
import { bogotaClock, bogotaDay } from "@/lib/xlsx";
import {
  activePreset,
  buildCsv,
  buildWorkbook,
  defaultVisible,
  groupsOf,
  rangedFileName,
  type ColumnPreset,
  type DataColumn,
} from "@/components/consultas/columns";
import { familiaLabel } from '@/lib/api/types/familia-labels';

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

// ── Formateo ───────────────────────────────────────────────────────────────────

// `formatInt` y `plural` viven en el kit compartido: los usan las dos consolas de consultas, y dos
// formateadores de número acaban enseñando «1.284» en un informe y «1284» en el otro.
import {
  formatDate,
  formatDateTime,
  formatDays,
  formatInt,
  plural,
} from "@/components/consultas/format";

export { formatDate, formatDateTime, formatDays, formatInt, plural };

const intFmt = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 });
const numFmt = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 1 });

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

// ── Columnas ───────────────────────────────────────────────────────────────────

/** Columna de este informe. El grupo se restringe a los cuatro que tiene la pantalla. */
export type ReportColumn = DataColumn<OtReportRow, OtReportSort> & {
  group: "Identificación" | "Estado" | "Tiempos" | "Calidad";
};

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
    width: 28,
    sort: OT_REPORT_SORT.empresa,
    defaultVisible: true,
  },
  {
    id: "familia",
    label: "Tipo de trámite",
    group: "Identificación",
    value: (r) => familiaLabel(r.familia),
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
    raw: (r) => bogotaDay(r.radicadoEn),
    width: 13,
    sort: OT_REPORT_SORT.radicado,
    defaultVisible: true,
  },
  {
    id: "ultima_radicacion",
    label: "Última radicación",
    group: "Tiempos",
    value: (r) => formatDateTime(r.ultimaRadicacionEn),
    raw: (r) => bogotaClock(r.ultimaRadicacionEn),
    width: 18,
  },
  {
    id: "decidido_en",
    label: "Decidido el",
    group: "Tiempos",
    value: (r) => formatDate(r.decididoEn),
    raw: (r) => bogotaDay(r.decididoEn),
    width: 13,
    sort: OT_REPORT_SORT.decidido,
    defaultVisible: true,
  },
  {
    id: "horas_decision",
    // La cabecera dice la unidad porque el valor crudo va en horas: en pantalla «2 días» se lee
    // solo, pero una celda con «48» sin unidad no significa nada.
    label: "Tiempo de decisión",
    group: "Tiempos",
    value: (r) => formatHours(r.horasHastaDecision),
    raw: (r) => r.horasHastaDecision,
    xlsxHeader: "Tiempo de decisión (h)",
    width: 20,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "dias_organismo",
    label: "Días en el organismo",
    group: "Tiempos",
    value: (r) => formatDays(r.diasEnOrganismo),
    raw: (r) => r.diasEnOrganismo,
    xlsxHeader: "Días en el organismo (d)",
    width: 22,
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
    raw: (r) => r.devoluciones,
    sort: OT_REPORT_SORT.devoluciones,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "causales",
    label: "Causales del último rechazo",
    group: "Calidad",
    value: (r) => (r.causalesUltimoRechazo.length === 0 ? "—" : r.causalesUltimoRechazo.join(" · ")),
    width: 40,
  },
];

export const COLUMN_GROUPS = groupsOf(REPORT_COLUMNS);

export function defaultVisibleColumns(): string[] {
  return defaultVisible(REPORT_COLUMNS);
}

/**
 * Vistas de arranque del informe. Son dos preguntas distintas, no dos estilos: «cómo va la gestión»
 * mira el desenlace de cada trámite, «cuánto tardo» mira el reloj. Con una sola vista, quien viene a
 * la segunda pregunta tiene que armarla a mano cada vez.
 */
export const REPORT_PRESETS: ColumnPreset[] = [
  {
    id: "gestion",
    label: "Gestión",
    hint: "Qué recibí de cada empresa y en qué acabó.",
    columns: [
      "referencia",
      "empresa",
      "familia",
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
      "familia",
      "estado",
      "devoluciones",
      "causales",
      "decidido_por",
    ],
  },
];

/**
 * Cuál de las vistas rápidas describe la selección actual, o `null` si es una combinación propia.
 *
 * Se compara como CONJUNTO y no como lista: el selector de columnas devuelve los ids en el orden
 * canónico de `REPORT_COLUMNS`, que no tiene por qué coincidir con el orden en que se escribió el
 * preset. Comparando listas, elegir «Gestión» dejaría de marcarse a sí mismo un segundo después.
 */
export function activePresetId(visibleColumnIds: string[]): string | null {
  return activePreset(REPORT_PRESETS, visibleColumnIds);
}

// ── Exportación ────────────────────────────────────────────────────────────────

export function buildReportCsv(rows: OtReportRow[], visibleColumnIds: string[]): string {
  return buildCsv(REPORT_COLUMNS, rows, visibleColumnIds);
}

export function buildReportXlsx(
  rows: OtReportRow[],
  visibleColumnIds: string[],
): Uint8Array<ArrayBuffer> {
  return buildWorkbook("Informe OT", REPORT_COLUMNS, rows, visibleColumnIds);
}

export function reportFileName(from: string, to: string, ext: "csv" | "xlsx"): string {
  return rangedFileName("informe-ot", from, to, ext);
}
