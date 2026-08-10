// Columnas del resultado de una consulta de la empresa gestora.
//
// Comparte con el organismo toda la maquinaria (CSV, Excel, selector, presets): lo único propio es
// QUÉ columnas hay, y son otras. Aquí no está «empresa cliente» —la empresa es quien pregunta— y sí
// están el organismo al que se radicó, quién lo radicó y lo comercial: leasing, método de pago y
// tipo de traspaso, que es lo que una gestora factura y concilia.

import {
  buildCsv,
  buildWorkbook,
  defaultVisible,
  type ColumnPreset,
  type DataColumn,
} from "@/components/consultas/columns";
import { formatDate, formatDateTime, formatDays, formatInt } from "@/components/consultas/format";
import type { CompanyQueryRow } from "@/lib/api/company-queries";
import { bogotaClock, bogotaDay } from "@/lib/xlsx";

const ESTADO_LABEL: Record<string, string> = {
  borrador: "Borrador",
  preparado: "Preparado",
  entregado: "Entregado",
  aprobado: "Aprobado",
  rechazado: "Rechazado",
  anulado: "Anulado",
};

/** Color por estado. El mismo criterio que en la bandeja: verde cierra bien, rojo cierra mal. */
const ESTADO_COLOR: Record<string, string> = {
  borrador: "#9AA5B4",
  preparado: "#557EFF",
  entregado: "#00A8B5",
  aprobado: "#1D6F42",
  rechazado: "#C0392B",
  anulado: "#6B7280",
};

export function estadoEmpresa(estado: string): { label: string; color: string } {
  return { label: ESTADO_LABEL[estado] ?? estado, color: ESTADO_COLOR[estado] ?? "#CBD5E1" };
}

const MODALIDAD_LABEL: Record<string, string> = {
  matricula_inicial: "Matrícula inicial",
  traspaso: "Traspaso",
};

const TRANSFORMACION_LABEL: Record<string, string> = {
  cambio_color: "Color",
  cambio_carroceria: "Carrocería",
  cambio_combustible: "Combustible",
};

const TRASPASO_LABEL: Record<string, string> = {
  transferencia_dominio: "Transferencia de dominio",
  unilateral: "Unilateral",
  bilateral: "Bilateral",
};

/** Sí/no legible. Un `true` crudo en una celda obliga a traducir mentalmente en cada fila. */
function siNo(value: boolean): string {
  return value ? "Sí" : "No";
}

const GRUPO_IDENT = "Identificación";
const GRUPO_TRAMITE = "Trámite";
const GRUPO_PERSONAS = "Personas";
const GRUPO_CARACTERISTICAS = "Características";
const GRUPO_COMERCIAL = "Comercial";
const GRUPO_TIEMPOS = "Tiempos";

export const COMPANY_QUERY_COLUMNS: DataColumn<CompanyQueryRow>[] = [
  {
    id: "referencia",
    label: "Radicado",
    group: GRUPO_IDENT,
    value: (r) => r.referenceNumber,
    width: 18,
    defaultVisible: true,
  },
  {
    id: "placa",
    label: "Placa",
    group: GRUPO_IDENT,
    value: (r) => r.placa ?? "—",
    raw: (r) => r.placa,
    width: 10,
    defaultVisible: true,
  },
  {
    id: "vin",
    label: "VIN",
    group: GRUPO_IDENT,
    value: (r) => r.vin ?? "—",
    raw: (r) => r.vin,
    width: 20,
  },
  {
    id: "organismo",
    label: "Organismo",
    group: GRUPO_TRAMITE,
    value: (r) => r.transitOfficeName ?? "—",
    raw: (r) => r.transitOfficeName,
    width: 28,
    defaultVisible: true,
  },
  {
    id: "tipo",
    label: "Tipo de trámite",
    group: GRUPO_TRAMITE,
    value: (r) => r.procedureTypeName,
    width: 24,
    defaultVisible: true,
  },
  {
    id: "modalidad",
    label: "Modalidad",
    group: GRUPO_TRAMITE,
    value: (r) => MODALIDAD_LABEL[r.modalidad] ?? r.modalidad,
    width: 16,
  },
  {
    id: "estado",
    label: "Estado",
    group: GRUPO_TRAMITE,
    value: (r) => estadoEmpresa(r.status).label,
    width: 14,
    defaultVisible: true,
  },
  {
    id: "prioritario",
    label: "Prioritario",
    group: GRUPO_TRAMITE,
    value: (r) => siNo(r.prioritario),
    width: 11,
  },
  {
    id: "radicado_por",
    label: "Radicado por",
    group: GRUPO_TRAMITE,
    value: (r) => r.radicadoPor,
    width: 22,
  },
  {
    id: "comprador",
    label: "Comprador",
    group: GRUPO_PERSONAS,
    value: (r) => r.comprador ?? "—",
    raw: (r) => r.comprador,
    width: 26,
  },
  {
    id: "vendedor",
    label: "Vendedor",
    group: GRUPO_PERSONAS,
    value: (r) => r.vendedor ?? "—",
    raw: (r) => r.vendedor,
    width: 26,
  },
  {
    id: "prenda",
    label: "Prenda",
    group: GRUPO_CARACTERISTICAS,
    value: (r) => siNo(r.tienePrenda),
    width: 9,
  },
  {
    id: "acreedor_prenda",
    label: "Acreedor",
    group: GRUPO_CARACTERISTICAS,
    value: (r) => r.acreedorPrenda ?? "—",
    raw: (r) => r.acreedorPrenda,
    width: 24,
  },
  {
    id: "licencia_transito",
    label: "LT cargada",
    group: GRUPO_CARACTERISTICAS,
    value: (r) => siNo(r.tieneLicenciaTransito),
    width: 12,
  },
  {
    id: "transformaciones",
    label: "Transformaciones",
    group: GRUPO_CARACTERISTICAS,
    value: (r) =>
      r.transformaciones.length === 0
        ? "—"
        : r.transformaciones.map((t) => TRANSFORMACION_LABEL[t] ?? t).join(", "),
    raw: (r) =>
      r.transformaciones.length === 0
        ? null
        : r.transformaciones.map((t) => TRANSFORMACION_LABEL[t] ?? t).join(", "),
    width: 24,
  },
  {
    id: "subsanaciones",
    label: "Subsanaciones",
    group: GRUPO_CARACTERISTICAS,
    value: (r) => formatInt(r.subsanacionCount),
    raw: (r) => r.subsanacionCount,
    numeric: true,
    width: 13,
  },
  {
    id: "leasing",
    label: "Leasing",
    group: GRUPO_COMERCIAL,
    value: (r) => siNo(r.esLeasing),
    width: 9,
  },
  {
    id: "metodo_pago",
    label: "Método de pago",
    group: GRUPO_COMERCIAL,
    value: (r) => r.metodoPago ?? "—",
    raw: (r) => r.metodoPago,
    width: 18,
  },
  {
    id: "tipo_traspaso",
    label: "Tipo de traspaso",
    group: GRUPO_COMERCIAL,
    // En matrícula inicial no hay traspaso que clasificar; la celda va vacía y no «Bilateral»,
    // que se leería como un dato y no como un «no aplica».
    value: (r) => (r.tipoTraspaso ? (TRASPASO_LABEL[r.tipoTraspaso] ?? r.tipoTraspaso) : "—"),
    raw: (r) => (r.tipoTraspaso ? (TRASPASO_LABEL[r.tipoTraspaso] ?? r.tipoTraspaso) : null),
    width: 22,
  },
  {
    id: "creado_en",
    label: "Creado",
    group: GRUPO_TIEMPOS,
    value: (r) => formatDate(r.creadoEn),
    raw: (r) => bogotaDay(r.creadoEn),
    width: 12,
    defaultVisible: true,
  },
  {
    id: "enviado_en",
    label: "Enviado al organismo",
    group: GRUPO_TIEMPOS,
    value: (r) => formatDateTime(r.enviadoEn),
    raw: (r) => bogotaClock(r.enviadoEn),
    width: 18,
  },
  {
    id: "cerrado_en",
    label: "Cerrado",
    group: GRUPO_TIEMPOS,
    value: (r) => formatDateTime(r.cerradoEn),
    raw: (r) => bogotaClock(r.cerradoEn),
    width: 18,
  },
  {
    id: "actualizado_en",
    label: "Última actualización",
    group: GRUPO_TIEMPOS,
    value: (r) => formatDateTime(r.actualizadoEn),
    raw: (r) => bogotaClock(r.actualizadoEn),
    width: 18,
  },
  {
    id: "dias_hasta_envio",
    label: "Días hasta el envío",
    group: GRUPO_TIEMPOS,
    value: (r) => formatDays(r.diasHastaEnvio),
    raw: (r) => r.diasHastaEnvio,
    xlsxHeader: "Días hasta el envío",
    numeric: true,
    width: 16,
  },
  {
    id: "dias_en_organismo",
    label: "Días en el organismo",
    group: GRUPO_TIEMPOS,
    value: (r) => formatDays(r.diasEnOrganismo),
    raw: (r) => r.diasEnOrganismo,
    xlsxHeader: "Días en el organismo",
    numeric: true,
    width: 17,
  },
  {
    id: "devoluciones",
    label: "Devoluciones",
    group: GRUPO_TIEMPOS,
    value: (r) => formatInt(r.devoluciones),
    raw: (r) => r.devoluciones,
    numeric: true,
    width: 12,
  },
];

export function defaultCompanyQueryColumns(): string[] {
  return defaultVisible(COMPANY_QUERY_COLUMNS);
}

/**
 * Vistas de un clic.
 *
 * No son un atajo estético: quien abre el selector de columnas por primera vez ve veinticinco
 * casillas y lo cierra. Un preset enseña de golpe para qué sirven, y a partir de ahí se editan.
 *
 * <p><b>«Tipo de trámite» va en TODOS.</b> Es lo que dice qué se está mirando: sin esa columna, una
 * matrícula y un traspaso son dos filas indistinguibles, y varias de las demás columnas —el tipo de
 * traspaso, sin ir más lejos— solo significan algo en una de las dos.</p>
 *
 * <p><b>«Tipo de traspaso» no va en ninguno.</b> Queda a un clic en el selector para quien la
 * quiera, como cualquier otra: viene vacía en todo lo que no sea un traspaso, así que de salida
 * ocupa una columna que en media tabla no dice nada.</p>
 */
export const COMPANY_QUERY_PRESETS: ColumnPreset[] = [
  {
    id: "basico",
    label: "Lo esencial",
    hint: "Identificación, organismo, estado y fecha de creación.",
    columns: defaultCompanyQueryColumns(),
  },
  {
    id: "seguimiento",
    label: "Seguimiento",
    hint: "Dónde está cada trámite y cuánto lleva ahí.",
    columns: [
      "referencia",
      "placa",
      "organismo",
      "tipo",
      "estado",
      "enviado_en",
      "dias_en_organismo",
      "devoluciones",
      "subsanaciones",
    ],
  },
  {
    id: "vehiculo",
    label: "Ficha del vehículo",
    hint: "Placa, VIN, prenda, LT y transformaciones.",
    columns: [
      "referencia",
      "placa",
      "tipo",
      "vin",
      "prenda",
      "acreedor_prenda",
      "licencia_transito",
      "transformaciones",
    ],
  },
  {
    id: "comercial",
    label: "Comercial",
    hint: "Leasing, método de pago y las personas de la operación.",
    columns: ["referencia", "placa", "tipo", "comprador", "vendedor", "leasing", "metodo_pago"],
  },
];

export function buildCompanyQueryCsv(
  rows: CompanyQueryRow[],
  visibleColumnIds: string[],
  notas: string[] = [],
): string {
  const csv = buildCsv(COMPANY_QUERY_COLUMNS, rows, visibleColumnIds);
  return notas.length > 0
    ? `${csv}\r\n\r\n${notas.map((n) => `"${n.replace(/"/g, '""')}"`).join("\r\n")}`
    : csv;
}

export function buildCompanyQueryXlsx(
  rows: CompanyQueryRow[],
  visibleColumnIds: string[],
  notas: string[] = [],
): Uint8Array<ArrayBuffer> {
  return buildWorkbook("Consulta", COMPANY_QUERY_COLUMNS, rows, visibleColumnIds, notas);
}
