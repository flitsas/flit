// Catálogo de los 4 reportes de Impuestos (D-REPORTES; impuestos.v1.yaml 1.0.0, tag Reportes, enum TipoReporte). Los
// nombres de reporte y de cada valor de enum salen de `x-flit-etiquetas` del contrato; el front no redacta otros textos
// para esos valores. Topes de exportación: Excel 5000 y PDF 1000 filas (más ⇒ 422 TOPE_EXPORTACION, sin truncar).
import { rotular } from "@/lib/rotulos";

export type TipoReporte = "detalle" | "lotes" | "deuda" | "errores";
export type FormatoExportacion = "excel" | "pdf";
export type FiltroReporte = "tipo" | "estado" | "departamento" | "usuario" | "placa";

export interface ReporteCatalogo {
  tipo: TipoReporte;
  /** x-flit-etiquetas de TipoReporte. */
  nombre: string;
  /** Trae documento y nombre del propietario (DA-7=b): aviso de Ley 1581. */
  contienePii: boolean;
  /** Filtros que aplican a este reporte (spec-visual §3.7: solo se pintan esos). */
  filtros: readonly FiltroReporte[];
}

export const REPORTES: readonly ReporteCatalogo[] = [
  { tipo: "detalle", nombre: "Detalle de liquidaciones", contienePii: true, filtros: ["tipo", "estado", "departamento", "usuario", "placa"] },
  { tipo: "lotes", nombre: "Resumen por lote", contienePii: false, filtros: ["tipo", "usuario"] },
  { tipo: "deuda", nombre: "Placas con deuda", contienePii: true, filtros: ["tipo", "departamento", "placa"] },
  { tipo: "errores", nombre: "Errores de liquidación", contienePii: true, filtros: ["tipo", "estado", "departamento", "usuario", "placa"] },
];

export const REPORTE_POR_DEFECTO: TipoReporte = "detalle";

export function reporteDe(tipo: string | null | undefined): ReporteCatalogo {
  return REPORTES.find((r) => r.tipo === tipo) ?? (REPORTES[0] as ReporteCatalogo);
}

/** Máximo de filas por formato (impuestos.v1.yaml: /excel hasta 5000, /pdf hasta 1000). */
export const TOPE_EXPORTACION: Readonly<Record<FormatoExportacion, number>> = { excel: 5000, pdf: 1000 };
export const NOMBRE_FORMATO: Readonly<Record<FormatoExportacion, string>> = { excel: "Excel", pdf: "PDF" };

// ── x-flit-etiquetas de los enums que aparecen en las filas y en los filtros ─────────────────────────────────────────

export const ETIQUETA_TIPO_LIQUIDACION: Readonly<Record<string, string>> = { INDIVIDUAL: "Individual", MASIVA: "Masiva" };

export const ETIQUETA_ESTADO_LIQUIDACION: Readonly<Record<string, string>> = {
  CREADO: "Creado",
  EN_PROCESO: "En proceso",
  COMPLETADO: "Completado",
  COMPLETADO_CON_NOVEDADES: "Completado con novedades",
  CERRADO_MANUALMENTE: "Cerrado manualmente",
};

/** EstadoItem en el orden del contrato (también es el orden del filtro «Estado»). */
export const ETIQUETA_ESTADO_ITEM: Readonly<Record<string, string>> = {
  PENDIENTE: "Pendiente",
  CONSULTANDO_RUNT: "Consultando RUNT",
  REINTENTO_PROGRAMADO: "Reintento programado",
  ENVIANDO: "Enviando",
  ENVIADO: "Esperando resultado",
  INCIERTO: "Incierto",
  INCIERTO_REQUIERE_DECISION: "Incierto — requiere decisión",
  DATOS_INCOMPLETOS: "Datos incompletos",
  DEPARTAMENTO_NO_HABILITADO: "Departamento no habilitado",
  EXITOSO: "Exitosa",
  ERROR: "Con error",
  SIN_RESULTADO_LOTE_CERRADO: "Sin resultado (lote cerrado)",
};

export const ETIQUETA_RESULTADO: Readonly<Record<string, string>> = { con_deuda: "Con deuda", paz_y_salvo: "Paz y salvo", error: "Con error" };

export const ETIQUETA_CLASE_ERROR: Readonly<Record<string, string>> = {
  CORREGIR_DATOS: "Corregir datos",
  DEPENDE: "Depende del mensaje",
  REINTENTABLE: "Reintentable como nueva consulta",
};

export const ETIQUETA_CAMPO_FALTANTE: Readonly<Record<string, string>> = {
  documentType: "Tipo de documento del propietario",
  documentNumber: "Número de documento del propietario",
  OwnerName: "Nombre del propietario",
  vehicleModel: "Modelo del vehículo",
  secretaryDepartmentCode: "Secretaría de tránsito sin homologar",
  secretaryCity: "Municipio de la secretaría de tránsito",
  OwnerEmail: "Correo del propietario",
  OwnerPhone: "Celular o teléfono del propietario",
  OwnerAddress: "Dirección del propietario",
};

/** Opciones del filtro «Tipo» (SelectorSegmentado): vocabulario de pestañas de D-UI-PESTANAS. */
export const OPCIONES_TIPO: ReadonlyArray<{ valor: "" | "INDIVIDUAL" | "MASIVA"; label: string }> = [
  { valor: "", label: "Todas" },
  { valor: "INDIVIDUAL", label: "Por placa" },
  { valor: "MASIVA", label: "Masivas" },
];

/** Rótulo de un valor de enum de una columna del reporte; el desconocido, «Sin clasificar» (nunca el código). */
export function rotuloDeColumna(tipo: TipoReporte, clave: string, codigo: string): string | null {
  const respaldo =
    clave === "tipo"
      ? ETIQUETA_TIPO_LIQUIDACION
      : clave === "estado"
        ? tipo === "lotes"
          ? ETIQUETA_ESTADO_LIQUIDACION
          : ETIQUETA_ESTADO_ITEM
        : clave === "resultadoStatus"
          ? ETIQUETA_RESULTADO
          : clave === "errorClase"
            ? ETIQUETA_CLASE_ERROR
            : clave === "campoFaltante"
              ? ETIQUETA_CAMPO_FALTANTE
              : null;
  return respaldo ? rotular({ contexto: `columna ${clave} del reporte ${tipo}`, codigo, respaldo }) : null;
}

/** Columnas de fecha y de monto (formato §0.1). */
export const COLUMNAS_FECHA: ReadonlySet<string> = new Set(["creadaEn", "terminadaEn", "consultadaEn"]);
export const COLUMNAS_MONTO: ReadonlySet<string> = new Set(["totalAPagar"]);
