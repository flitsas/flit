// Columnas del resultado de una consulta de ICT.
//
// Comparte con las otras dos consolas de consultas toda la maquinaria (CSV, Excel, selector,
// presets): lo único propio es QUÉ columnas hay. Aquí aparecen las que ningún otro informe tiene —
// secretaría, cliente de integración, novedades y borrador ya creado—, que son justo las que
// distinguen un pre-trámite de ICT de un trámite ya radicado.

import type { IctQueryRow } from "@/lib/api/ict-queries";
import {
  buildCsv,
  buildWorkbook,
  defaultVisible,
  groupsOf,
  rangedFileName,
  type ColumnPreset,
  type DataColumn,
} from "@/components/consultas/columns";
import { formatDateTime, formatInt } from "@/components/consultas/format";

export type IctQueryColumn = DataColumn<IctQueryRow>;

/** El texto de las celdas vacías. Nunca llega al Excel: allí la celda va vacía de verdad. */
const VACIO = "—";

function siNo(value: boolean): string {
  return value ? "Sí" : "No";
}

// ── Estado ─────────────────────────────────────────────────────────────────────
//
// Espejo de `IctQueryRepository.ResolveEstado` (backend): tener procedure_instance_id gana
// siempre, sin importar el process_status_id — un pre-trámite puede seguir marcado como "en
// validación" en ICT mientras el borrador ya existe en FLIT.

export interface IctEstadoMeta {
  label: string;
  color: string;
}

export const ICT_ESTADO_META: Record<string, IctEstadoMeta> = {
  recibido: { label: "Recibido", color: "#94A3B8" },
  en_validacion_negocio: { label: "En validación de negocio", color: "#F9AC00" },
  en_validacion_externa: { label: "En validación externa", color: "#557EFF" },
  con_novedades: { label: "Con novedades", color: "#FF4E00" },
  borrador_creado: { label: "Borrador creado", color: "#8CC63F" },
  anulado: { label: "Anulado", color: "#64748B" },
};

export function ictEstadoMeta(estado: string): IctEstadoMeta {
  return ICT_ESTADO_META[estado] ?? { label: estado, color: "#CBD5E1" };
}

export const ICT_QUERY_COLUMNS: IctQueryColumn[] = [
  {
    id: "radicado",
    label: "Radicado",
    group: "Identificación",
    value: (r) => r.radicado ?? VACIO,
    raw: (r) => r.radicado,
    width: 18,
    sort: "radicado",
    defaultVisible: true,
  },
  {
    id: "transaccion",
    label: "N.º de transacción",
    group: "Identificación",
    value: (r) => formatInt(r.transactionNumber),
    raw: (r) => r.transactionNumber,
    width: 16,
    numeric: true,
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
    value: (r) => r.tenantNombre,
    width: 28,
    defaultVisible: true,
  },
  {
    id: "tipo_tramite",
    label: "Tipo de trámite",
    group: "Identificación",
    value: (r) => r.tipoTramite ?? VACIO,
    raw: (r) => r.tipoTramite,
    width: 18,
    defaultVisible: true,
  },

  {
    id: "estado",
    label: "Estado",
    group: "Estado",
    value: (r) => ictEstadoMeta(r.estado).label,
    width: 20,
    sort: "estado",
    defaultVisible: true,
  },
  {
    id: "tiene_novedades",
    label: "Tiene novedades",
    group: "Estado",
    value: (r) => siNo(r.tieneNovedades),
    width: 14,
  },
  {
    id: "tiene_borrador",
    label: "Borrador creado",
    group: "Estado",
    value: (r) => siNo(r.tieneBorrador),
    width: 14,
  },
  {
    id: "prioritario",
    label: "Prioritario",
    group: "Estado",
    value: (r) => siNo(r.prioritario),
    width: 11,
  },
  {
    id: "comentarios",
    label: "Comentarios",
    group: "Estado",
    value: (r) => r.comentarios ?? VACIO,
    raw: (r) => r.comentarios,
    width: 40,
  },

  {
    id: "secretaria",
    label: "Secretaría",
    group: "Origen",
    value: (r) => r.secretaria ?? VACIO,
    raw: (r) => r.secretaria,
    width: 24,
  },
  {
    id: "cliente_integracion",
    label: "Cliente de integración",
    group: "Origen",
    value: (r) => r.clienteIntegracion ?? VACIO,
    raw: (r) => r.clienteIntegracion,
    width: 24,
  },

  {
    id: "registrado_en",
    label: "Registrado",
    group: "Fechas",
    xlsxHeader: "Fecha de registro",
    value: (r) => formatDateTime(r.registradoEn),
    raw: (r) => r.registradoEn,
    width: 18,
    sort: "registrado",
    defaultVisible: true,
  },
  {
    id: "validacion_negocio_en",
    label: "Validación de negocio",
    group: "Fechas",
    xlsxHeader: "Fecha de validación de negocio",
    value: (r) => formatDateTime(r.validacionNegocioEn),
    raw: (r) => r.validacionNegocioEn,
    width: 18,
  },
  {
    id: "validacion_externa_en",
    label: "Validación externa",
    group: "Fechas",
    xlsxHeader: "Fecha de validación externa",
    value: (r) => formatDateTime(r.validacionExternaEn),
    raw: (r) => r.validacionExternaEn,
    width: 18,
  },
];

export const ICT_QUERY_COLUMN_GROUPS = groupsOf(ICT_QUERY_COLUMNS);

export function defaultIctQueryColumns(): string[] {
  return defaultVisible(ICT_QUERY_COLUMNS);
}

/**
 * Vistas rápidas de columnas. Puntos de partida, no listas exhaustivas: nadie marca una docena de
 * casillas para ver un resultado.
 */
export const ICT_QUERY_PRESETS: ColumnPreset[] = [
  {
    id: "basico",
    label: "Lo esencial",
    hint: "Identificación, estado y fecha de registro.",
    columns: defaultIctQueryColumns(),
  },
  {
    id: "pipeline",
    label: "Seguimiento del pipeline",
    hint: "Estado, novedades, borrador y ambas validaciones.",
    columns: [
      "radicado",
      "placa",
      "empresa",
      "estado",
      "tiene_novedades",
      "tiene_borrador",
      "registrado_en",
      "validacion_negocio_en",
      "validacion_externa_en",
    ],
  },
  {
    id: "origen",
    label: "De dónde vino",
    hint: "Secretaría, cliente de integración y comentarios.",
    columns: ["radicado", "placa", "empresa", "secretaria", "cliente_integracion", "comentarios"],
  },
];

/**
 * El aviso de cobertura viaja DENTRO del archivo, no solo en pantalla. El .xlsx es lo que se
 * reenvía a quien no ejecutó la consulta, y quien lo recibe cuenta las filas.
 */
export function buildIctQueryCsv(
  rows: IctQueryRow[],
  visibleColumnIds: string[],
  notas: string[] = [],
): string {
  const csv = buildCsv(ICT_QUERY_COLUMNS, rows, visibleColumnIds);
  return notas.length > 0 ? `${csv}\r\n\r\n${notas.map((n) => `"${n.replace(/"/g, '""')}"`).join("\r\n")}` : csv;
}

export function buildIctQueryXlsx(
  rows: IctQueryRow[],
  visibleColumnIds: string[],
  notas: string[] = [],
): Uint8Array<ArrayBuffer> {
  return buildWorkbook("Consulta ICT", ICT_QUERY_COLUMNS, rows, visibleColumnIds, notas);
}

/**
 * El nombre del archivo lleva el nombre de la consulta cuando lo tiene. Media docena de
 * `consulta-ict-2026-07-01-a-2026-07-31.xlsx` en Descargas son indistinguibles entre sí.
 */
export function ictQueryFileName(
  nombre: string | null,
  from: string,
  to: string,
  ext: "csv" | "xlsx",
): string {
  const base = nombre
    ? nombre
        .toLowerCase()
        .normalize("NFD")
        .replace(/[̀-ͯ]/g, "")
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "")
        .slice(0, 40)
    : "";

  return rangedFileName(base || "consulta-ict", from, to, ext);
}
