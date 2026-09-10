// Columnas del informe de revisores.
//
// Regla que gobierna esta lista: el volumen NUNCA va solo. Un informe de personas que solo contara
// decisiones premiaría a quien decide rápido y mal, así que cada indicador de cantidad tiene al lado
// uno de calidad — decididos con reincidencia, mediana con p90. Por eso ninguna vista rápida trae
// «decididos» sin acompañamiento.

import type { OtReviewerRow, OtReviewerSort } from "@/lib/api/ot-metrics";
import { OT_REVIEWER_SORT } from "@/lib/api/ot-metrics";
import { bogotaClock } from "@/lib/xlsx";
import {
  activePreset,
  buildCsv,
  buildWorkbook,
  defaultVisible,
  rangedFileName,
  type ColumnPreset,
  type DataColumn,
} from "@/components/consultas/columns";
import { formatDateTime, formatHours, formatInt } from "./report-columns";

export type ReviewerColumn = DataColumn<OtReviewerRow, OtReviewerSort> & {
  group: "Persona" | "Volumen" | "Tiempos" | "Calidad" | "Actividad";
};

const pctFmt = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 1 });

function formatPct(value: number): string {
  return `${pctFmt.format(value)} %`;
}

/** Decimal legible en español. `2.5` en pantalla se lee como un error de formato, no como 2,5. */
function formatDecimal(value: number): string {
  return pctFmt.format(value);
}

export const REVIEWER_COLUMNS: ReviewerColumn[] = [
  {
    id: "revisor",
    label: "Revisor",
    group: "Persona",
    value: (r) => r.displayName,
    sort: OT_REVIEWER_SORT.nombre,
    width: 26,
    defaultVisible: true,
  },
  {
    id: "decididos",
    label: "Trámites gestionados",
    group: "Volumen",
    value: (r) => formatInt(r.decididos),
    raw: (r) => r.decididos,
    sort: OT_REVIEWER_SORT.decididos,
    width: 20,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "aprobados",
    label: "Aprobados",
    group: "Volumen",
    value: (r) => formatInt(r.aprobados),
    raw: (r) => r.aprobados,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "aprobacion_pct",
    label: "% aprobación",
    group: "Volumen",
    value: (r) => formatPct(r.aprobacionPct),
    // Al Excel va el número (89), no la cadena «89 %»: así se puede promediar y ordenar. La unidad
    // la lleva el encabezado, que es donde no estorba.
    raw: (r) => r.aprobacionPct,
    sort: OT_REVIEWER_SORT.aprobacion,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "rechazados",
    label: "Rechazados",
    group: "Volumen",
    value: (r) => formatInt(r.rechazados),
    raw: (r) => r.rechazados,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "rechazo_pct",
    label: "% rechazo",
    group: "Volumen",
    value: (r) => formatPct(r.rechazoPct),
    raw: (r) => r.rechazoPct,
    sort: OT_REVIEWER_SORT.rechazo,
    numeric: true,
  },
  {
    id: "tiempo_mediano",
    label: "Tiempo mediano",
    group: "Tiempos",
    value: (r) => formatHours(r.tiempoMedianoHoras),
    raw: (r) => r.tiempoMedianoHoras,
    xlsxHeader: "Tiempo mediano (h)",
    sort: OT_REVIEWER_SORT.tiempo,
    width: 18,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "tiempo_p90",
    label: "p90",
    group: "Tiempos",
    value: (r) => formatHours(r.tiempoP90Horas),
    raw: (r) => r.tiempoP90Horas,
    xlsxHeader: "p90 (h)",
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "tiempo_promedio",
    label: "Promedio",
    group: "Tiempos",
    value: (r) => formatHours(r.tiempoPromedioHoras),
    raw: (r) => r.tiempoPromedioHoras,
    xlsxHeader: "Promedio (h)",
    numeric: true,
  },
  {
    id: "tiempo_maximo",
    label: "El más lento",
    group: "Tiempos",
    value: (r) => formatHours(r.tiempoMaximoHoras),
    raw: (r) => r.tiempoMaximoHoras,
    xlsxHeader: "El más lento (h)",
    numeric: true,
  },
  {
    id: "menos_24h",
    label: "Resueltos en < 24 h",
    group: "Tiempos",
    value: (r) => formatPct(r.enMenosDe24hPct),
    raw: (r) => r.enMenosDe24hPct,
    width: 20,
    numeric: true,
  },
  {
    id: "reincidencia",
    label: "Vuelven a rechazarse",
    group: "Calidad",
    value: (r) => (r.rechazados === 0 ? "—" : formatPct(r.vuelvenARechazarsePct)),
    // Sin rechazos no hay base: un 0 % ahí se leería como calidad impecable cuando lo que pasa es
    // que no hay nada que medir.
    raw: (r) => (r.rechazados === 0 ? null : r.vuelvenARechazarsePct),
    sort: OT_REVIEWER_SORT.reincidencia,
    width: 22,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "causales_por_rechazo",
    label: "Causales por rechazo",
    group: "Calidad",
    value: (r) => (r.rechazados === 0 ? "—" : formatDecimal(r.causalesPorRechazo)),
    raw: (r) => (r.rechazados === 0 ? null : r.causalesPorRechazo),
    width: 22,
    numeric: true,
  },
  {
    id: "dias_activos",
    label: "Días activos",
    group: "Actividad",
    value: (r) => formatInt(r.diasActivos),
    raw: (r) => r.diasActivos,
    numeric: true,
  },
  {
    id: "por_dia_activo",
    label: "Decisiones por día activo",
    group: "Actividad",
    value: (r) => formatDecimal(r.decisionesPorDiaActivo),
    raw: (r) => r.decisionesPorDiaActivo,
    sort: OT_REVIEWER_SORT.actividad,
    width: 24,
    numeric: true,
    defaultVisible: true,
  },
  {
    id: "empresas",
    label: "Empresas atendidas",
    group: "Actividad",
    value: (r) => formatInt(r.empresasAtendidas),
    raw: (r) => r.empresasAtendidas,
    width: 20,
    numeric: true,
  },
  {
    id: "prioritarios",
    label: "Prioritarios",
    group: "Actividad",
    value: (r) => formatInt(r.prioritariosDecididos),
    raw: (r) => r.prioritariosDecididos,
    numeric: true,
  },
  {
    id: "primera_decision",
    label: "Primera decisión",
    group: "Actividad",
    value: (r) => formatDateTime(r.primeraDecision),
    raw: (r) => bogotaClock(r.primeraDecision),
    width: 18,
  },
  {
    id: "ultima_decision",
    label: "Última decisión",
    group: "Actividad",
    value: (r) => formatDateTime(r.ultimaDecision),
    raw: (r) => bogotaClock(r.ultimaDecision),
    width: 18,
  },
];

export function defaultVisibleReviewerColumns(): string[] {
  return defaultVisible(REVIEWER_COLUMNS);
}

/**
 * Vistas de arranque. Son tres preguntas distintas sobre las mismas personas: cuánto hicieron,
 * cuánto tardaron y con qué calidad. Ninguna trae el volumen a solas, a propósito.
 */
export const REVIEWER_PRESETS: ColumnPreset[] = [
  {
    id: "carga",
    label: "Carga de trabajo",
    hint: "Cuánto gestionó cada quien y a qué ritmo.",
    columns: ["revisor", "decididos", "aprobados", "rechazados", "dias_activos", "por_dia_activo"],
  },
  {
    id: "tiempos",
    label: "Tiempos de respuesta",
    hint: "Cuánto tarda cada quien en decidir.",
    columns: [
      "revisor",
      "decididos",
      "tiempo_mediano",
      "tiempo_p90",
      "tiempo_promedio",
      "tiempo_maximo",
      "menos_24h",
    ],
  },
  {
    id: "calidad",
    label: "Calidad de la decisión",
    hint: "Si lo decidido se sostiene: reincidencia y uso de causales.",
    columns: [
      "revisor",
      "decididos",
      "rechazados",
      "rechazo_pct",
      "reincidencia",
      "causales_por_rechazo",
    ],
  },
];

export function activeReviewerPresetId(visibleColumnIds: string[]): string | null {
  return activePreset(REVIEWER_PRESETS, visibleColumnIds);
}

export function buildReviewersCsv(rows: OtReviewerRow[], visibleColumnIds: string[]): string {
  return buildCsv(REVIEWER_COLUMNS, rows, visibleColumnIds);
}

export function buildReviewersXlsx(
  rows: OtReviewerRow[],
  visibleColumnIds: string[],
): Uint8Array<ArrayBuffer> {
  return buildWorkbook("Revisores OT", REVIEWER_COLUMNS, rows, visibleColumnIds);
}

export function reviewersFileName(from: string, to: string, ext: "csv" | "xlsx"): string {
  return rangedFileName("revisores-ot", from, to, ext);
}
