// Tipos del módulo de reportes (contrato-api.md §2–§5, diagnostico.v1.yaml 1.5.0). JSON snake_case.

export type TipoReporte = "vehiculos" | "consultas" | "saneamientos" | "derivaciones" | "consumo";
export type FormatoExportacion = "excel" | "pdf";
export type TipoColumna = "texto" | "numero" | "fecha" | "fecha_hora" | "moneda" | "porcentaje";

export interface FiltroCatalogo {
  clave: string;
  /** `enum`: valores cerrados de `valores`; `prefijo`: texto libre (solo `placa`). */
  tipo: "enum" | "prefijo";
  repetible: boolean;
  valores: string[];
}

export interface ReporteCatalogo {
  tipo: TipoReporte;
  nombre: string;
  descripcion: string;
  /** true = documentos de identidad completos (PII alta). false = sin documentos (la placa sigue siendo PII baja). */
  contiene_pii: boolean;
  fecha_referencia: string;
  filtros: FiltroCatalogo[];
  maximo_excel: number;
  maximo_pdf: number;
}

export interface CatalogoReportesDto {
  reportes: ReporteCatalogo[];
}

export interface ColumnaReporte {
  clave: string;
  titulo: string;
  tipo: TipoColumna;
  pii: boolean;
}

export type ValorCelda = string | number | null;

export interface PaginaReporteDto {
  tipo: TipoReporte;
  ambito: "COMPANIA" | "BACKOFFICE_EMPRESA" | "BACKOFFICE_GLOBAL";
  pagina: number;
  tamano: number;
  total: number;
  acceso_id: string;
  columnas: ColumnaReporte[];
  /** Cada fila tiene exactamente las claves de `columnas`. */
  filas: Array<Record<string, ValorCelda>>;
}

/** Filtros de la pantalla. La placa vive solo aquí (estado del componente), nunca en la URL (SP-18). */
export interface FiltrosReporteValor {
  rango: { desde: string; hasta: string };
  /** "" = todas las compañías (solo vista global). */
  empresa: string;
  /** Enums del catálogo: clave → valores elegidos. */
  filtros: Record<string, string[]>;
  placa: string;
}
