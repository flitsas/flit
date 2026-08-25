// Columnas del resultado de una consulta.
//
// Comparte con los otros informes toda la maquinaria (CSV, Excel, selector, presets): lo único
// propio es QUÉ columnas hay. Aquí aparecen las que ningún informe fijo tiene —prenda, acreedor,
// licencia de tránsito, transformaciones, comprador y vendedor—, que son justo las que el usuario
// viene a buscar cuando arma su propia consulta.

import { bogotaClock, bogotaDay } from "@/lib/xlsx";
import type { OtQueryRow } from "@/lib/api/ot-queries";
import {
  buildCsv,
  buildWorkbook,
  defaultVisible,
  groupsOf,
  rangedFileName,
  type ColumnPreset,
  type DataColumn,
} from "@/components/consultas/columns";
import { estadoMeta, formatDate, formatDateTime, formatHours, formatInt } from "./report-columns";
import { familiaLabel } from '@/lib/api/types/familia-labels';

const TRANSFORMACION_LABEL: Record<string, string> = {
  cambio_color: "Color",
  cambio_carroceria: "Carrocería",
  cambio_combustible: "Combustible",
};

export type QueryColumn = DataColumn<OtQueryRow>;

/** El texto de las celdas vacías. Nunca llega al Excel: allí la celda va vacía de verdad. */
const VACIO = "—";

function siNo(value: boolean): string {
  return value ? "Sí" : "No";
}

export const QUERY_COLUMNS: QueryColumn[] = [
  {
    id: "referencia",
    label: "Radicado",
    group: "Identificación",
    value: (r) => r.referenceNumber,
    width: 18,
    sort: "referencia",
    defaultVisible: true,
  },
  {
    id: "placa",
    label: "Placa",
    group: "Identificación",
    value: (r) => r.placa ?? VACIO,
    raw: (r) => r.placa,
    width: 12,
    sort: "placa",
    defaultVisible: true,
  },
  {
    id: "vin",
    label: "VIN",
    group: "Identificación",
    value: (r) => r.vin ?? VACIO,
    raw: (r) => r.vin,
    width: 20,
  },
  {
    id: "empresa",
    label: "Empresa",
    group: "Identificación",
    value: (r) => r.clientTenantName,
    width: 28,
    sort: "empresa",
    defaultVisible: true,
  },
  {
    id: "tipo_tramite",
    label: "Tipo de trámite",
    group: "Identificación",
    value: (r) => familiaLabel(r.modalidad),
    width: 16,
    defaultVisible: true,
  },

  {
    id: "estado",
    label: "Estado",
    group: "Estado",
    value: (r) => estadoMeta(r.estadoOt).label,
    width: 18,
    sort: "estado",
    defaultVisible: true,
  },
  {
    id: "prioritario",
    label: "Prioritario",
    group: "Estado",
    value: (r) => siNo(r.prioritario),
    width: 11,
  },
  {
    id: "decidido_por",
    label: "Decidido por",
    group: "Estado",
    value: (r) => r.decididoPor ?? VACIO,
    raw: (r) => r.decididoPor,
    width: 24,
  },
  {
    id: "causales",
    label: "Causales del último rechazo",
    group: "Estado",
    value: (r) => (r.causalesUltimoRechazo.length > 0 ? r.causalesUltimoRechazo.join(" · ") : VACIO),
    raw: (r) =>
      r.causalesUltimoRechazo.length > 0 ? r.causalesUltimoRechazo.join(" · ") : null,
    width: 40,
  },
  {
    id: "devoluciones",
    label: "Devoluciones",
    group: "Estado",
    value: (r) => formatInt(r.devoluciones),
    raw: (r) => r.devoluciones,
    width: 13,
    numeric: true,
  },

  {
    id: "comprador",
    label: "Comprador",
    group: "Personas",
    value: (r) => r.comprador ?? VACIO,
    raw: (r) => r.comprador,
    width: 28,
  },
  {
    id: "vendedor",
    label: "Vendedor",
    group: "Personas",
    value: (r) => r.vendedor ?? VACIO,
    raw: (r) => r.vendedor,
    width: 28,
  },

  {
    id: "prenda",
    label: "Tiene prenda",
    group: "Características",
    value: (r) => siNo(r.tienePrenda),
    width: 13,
  },
  {
    id: "acreedor_prenda",
    label: "Acreedor de la prenda",
    group: "Características",
    value: (r) => r.acreedorPrenda ?? VACIO,
    raw: (r) => r.acreedorPrenda,
    width: 28,
  },
  {
    id: "licencia_transito",
    label: "LT cargada",
    group: "Características",
    value: (r) => siNo(r.tieneLicenciaTransito),
    width: 12,
  },
  {
    id: "transformaciones",
    label: "Transformaciones",
    group: "Características",
    value: (r) =>
      r.transformaciones.length > 0
        ? r.transformaciones.map((t) => TRANSFORMACION_LABEL[t] ?? t).join(" · ")
        : VACIO,
    raw: (r) =>
      r.transformaciones.length > 0
        ? r.transformaciones.map((t) => TRANSFORMACION_LABEL[t] ?? t).join(" · ")
        : null,
    width: 26,
  },

  {
    id: "radicado_en",
    label: "Radicado",
    group: "Fechas",
    xlsxHeader: "Fecha de radicación",
    value: (r) => formatDate(r.radicadoEn),
    raw: (r) => bogotaDay(r.radicadoEn),
    width: 14,
    sort: "radicado",
    defaultVisible: true,
  },
  {
    id: "decidido_en",
    label: "Decidido",
    group: "Fechas",
    xlsxHeader: "Fecha de decisión",
    value: (r) => formatDate(r.decididoEn),
    raw: (r) => bogotaDay(r.decididoEn),
    width: 14,
    sort: "decidido",
  },
  {
    id: "aprobado_en",
    label: "Aprobado",
    group: "Fechas",
    xlsxHeader: "Fecha de aprobación",
    value: (r) => formatDate(r.aprobadoEn),
    raw: (r) => bogotaDay(r.aprobadoEn),
    width: 14,
  },
  {
    id: "actualizado_en",
    label: "Última actualización",
    group: "Fechas",
    value: (r) => formatDateTime(r.actualizadoEn),
    raw: (r) => bogotaClock(r.actualizadoEn),
    width: 18,
    sort: "actualizado",
  },
  {
    id: "horas_decision",
    label: "Tiempo de decisión",
    group: "Fechas",
    xlsxHeader: "Tiempo de decisión (h)",
    value: (r) => formatHours(r.horasHastaDecision),
    raw: (r) => r.horasHastaDecision,
    width: 18,
    numeric: true,
  },
  {
    id: "dias_en_organismo",
    label: "Días en el organismo",
    group: "Fechas",
    value: (r) => (r.diasEnOrganismo === null ? VACIO : formatInt(Math.round(r.diasEnOrganismo))),
    raw: (r) => r.diasEnOrganismo,
    width: 18,
    numeric: true,
    sort: "dias",
  },
];

export const QUERY_COLUMN_GROUPS = groupsOf(QUERY_COLUMNS);

export function defaultQueryColumns(): string[] {
  return defaultVisible(QUERY_COLUMNS);
}

/**
 * Vistas rápidas de columnas. No pretenden ser exhaustivas: son puntos de partida, porque nadie
 * marca dieciocho casillas para ver un resultado.
 */
export const QUERY_PRESETS: ColumnPreset[] = [
  {
    id: "basico",
    label: "Lo esencial",
    hint: "Identificación, estado y fecha de radicación.",
    columns: defaultQueryColumns(),
  },
  {
    id: "vehiculo",
    label: "Ficha del vehículo",
    hint: "Placa, VIN, prenda, LT y transformaciones.",
    columns: [
      "referencia",
      "placa",
      "vin",
      "empresa",
      "prenda",
      "acreedor_prenda",
      "licencia_transito",
      "transformaciones",
    ],
  },
  {
    id: "personas",
    label: "Quién es quién",
    hint: "Comprador, vendedor y quién decidió.",
    columns: ["referencia", "placa", "empresa", "comprador", "vendedor", "decidido_por", "estado"],
  },
  {
    id: "gestion",
    label: "Seguimiento",
    hint: "Estado, tiempos, devoluciones y causales.",
    columns: [
      "referencia",
      "placa",
      "empresa",
      "estado",
      "radicado_en",
      "dias_en_organismo",
      "devoluciones",
      "causales",
    ],
  },
];

/**
 * El aviso de cobertura viaja DENTRO del archivo, no solo en pantalla. El .xlsx es lo que se
 * reenvía a quien no ejecutó la consulta, y quien lo recibe cuenta las filas.
 */
export function buildQueryCsv(
  rows: OtQueryRow[],
  visibleColumnIds: string[],
  notas: string[] = [],
): string {
  const csv = buildCsv(QUERY_COLUMNS, rows, visibleColumnIds);
  return notas.length > 0 ? `${csv}\r\n\r\n${notas.map((n) => `"${n.replace(/"/g, '""')}"`).join("\r\n")}` : csv;
}

export function buildQueryXlsx(
  rows: OtQueryRow[],
  visibleColumnIds: string[],
  notas: string[] = [],
): Uint8Array<ArrayBuffer> {
  return buildWorkbook("Consulta OT", QUERY_COLUMNS, rows, visibleColumnIds, notas);
}

/**
 * El nombre del archivo lleva el nombre de la consulta cuando lo tiene. Media docena de
 * `consulta-2026-07-01-a-2026-07-31.xlsx` en Descargas son indistinguibles entre sí.
 */
export function queryFileName(
  nombre: string | null,
  from: string,
  to: string,
  ext: "csv" | "xlsx",
): string {
  const base = nombre
    ? nombre
        .toLowerCase()
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "")
        .slice(0, 40)
    : "";

  return rangedFileName(base || "consulta-ot", from, to, ext);
}
