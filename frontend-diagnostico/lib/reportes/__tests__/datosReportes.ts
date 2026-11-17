import type { CatalogoReportesDto, PaginaReporteDto, ReporteCatalogo } from "@/lib/reportes/tipos";

// Catálogo y páginas de reporte (contrato-api.md §2–§4) con placeholders: nunca datos reales (Ley 1581).

export const VEHICULOS: ReporteCatalogo = {
  tipo: "vehiculos",
  nombre: "Vehículos diagnosticados",
  descripcion: "Un renglón por vehículo de los diagnósticos creados en el periodo.",
  contiene_pii: true,
  fecha_referencia: "Fecha de creación del diagnóstico",
  filtros: [
    { clave: "modalidad", tipo: "enum", repetible: false, valores: ["EXPRESS", "MASIVO"] },
    { clave: "tipo_analisis", tipo: "enum", repetible: false, valores: ["SIMPLE", "CON_ANALISIS"] },
    { clave: "veredicto", tipo: "enum", repetible: false, valores: ["VIABLE", "BLOQUEADO", "SIN_DICTAMEN"] },
    { clave: "placa", tipo: "prefijo", repetible: false, valores: [] },
  ],
  maximo_excel: 5000,
  maximo_pdf: 1000,
};

export const CONSULTAS: ReporteCatalogo = {
  tipo: "consultas",
  nombre: "Consultas a fuentes",
  descripcion: "Una fila por consulta.",
  contiene_pii: false,
  fecha_referencia: "Fecha de creación del diagnóstico",
  filtros: [
    { clave: "fuente", tipo: "enum", repetible: true, valores: ["RUNT", "SIMIT_VEHICULO"] },
    { clave: "estado", tipo: "enum", repetible: true, valores: ["PENDIENTE", "EN_CURSO", "COMPLETADO", "ERROR", "NA"] },
    { clave: "placa", tipo: "prefijo", repetible: false, valores: [] },
  ],
  maximo_excel: 10000,
  maximo_pdf: 2000,
};

const simple = (tipo: ReporteCatalogo["tipo"], nombre: string): ReporteCatalogo => ({
  tipo,
  nombre,
  descripcion: nombre,
  contiene_pii: false,
  fecha_referencia: "Fecha",
  filtros: tipo === "consumo" ? [] : [{ clave: "placa", tipo: "prefijo", repetible: false, valores: [] }],
  maximo_excel: 10000,
  maximo_pdf: 2000,
});

export const SANEAMIENTOS = simple("saneamientos", "Saneamientos");
export const DERIVACIONES = simple("derivaciones", "Derivaciones a trámite");
export const CONSUMO = simple("consumo", "Consumo por compañía");

/** R0 (compañía): 4 tipos, en un orden distinto del de la pantalla a propósito. */
export const catalogoCompania = (): CatalogoReportesDto => ({ reportes: [CONSULTAS, VEHICULOS, DERIVACIONES, SANEAMIENTOS] });
/** B0 (backoffice): los 5. */
export const catalogoBackoffice = (): CatalogoReportesDto => ({ reportes: [VEHICULOS, CONSULTAS, SANEAMIENTOS, DERIVACIONES, CONSUMO] });

export function paginaVehiculos(cambios: Partial<PaginaReporteDto> = {}): PaginaReporteDto {
  return {
    tipo: "vehiculos",
    ambito: "COMPANIA",
    pagina: 1,
    tamano: 10,
    total: 1830,
    acceso_id: "01920000-0000-7000-8000-00000000acc1",
    columnas: [
      { clave: "fecha_diagnostico", titulo: "Fecha del diagnóstico", tipo: "fecha_hora", pii: false },
      { clave: "diagnostico_nombre", titulo: "Diagnóstico", tipo: "texto", pii: false },
      { clave: "modalidad", titulo: "Modalidad", tipo: "texto", pii: false },
      { clave: "placa", titulo: "Placa", tipo: "texto", pii: true },
      { clave: "propietarios_documentos", titulo: "Propietarios (documento)", tipo: "texto", pii: true },
      { clave: "veredicto", titulo: "Veredicto", tipo: "texto", pii: false },
    ],
    filas: [
      {
        fecha_diagnostico: "2026-10-22T09:00:00-05:00",
        diagnostico_nombre: "Flota de prueba 001",
        modalidad: "EXPRESS",
        placa: "ABC123",
        propietarios_documentos: "CC 0000000",
        veredicto: null,
      },
    ],
    ...cambios,
  };
}
