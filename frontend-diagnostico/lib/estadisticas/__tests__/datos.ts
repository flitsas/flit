import type { EstadisticasDto } from "@/lib/estadisticas/tipos";

// Respuesta de GET /estadisticas (contrato-api §1) con placeholders: nunca datos reales (Ley 1581).
export const HOY = "2026-11-15";
/** 08:00 en Colombia del día HOY. */
export const AHORA = new Date("2026-11-15T13:00:00Z");

export function estadisticas(cambios: Partial<EstadisticasDto> = {}): EstadisticasDto {
  return {
    ambito: { tipo: "EMPRESA", empresa_id: "01920000-0000-7000-8000-000000000001", empresa_nombre: "Compañía Demo" },
    periodo: { desde: "2026-10-17", hasta: HOY, granularidad: "DIA" },
    generado_en: "2026-11-15T08:01:02-05:00",
    resumen: {
      diagnosticos: 42,
      diagnosticos_express: 30,
      diagnosticos_masivos: 12,
      vehiculos: 1830,
      vehiculos_unicos: 1702,
      consultas: 10980,
      consultas_exitosas: 10500,
      consultas_con_error: 300,
      consultas_no_aplica: 150,
      consultas_pendientes: 30,
      tasa_exito: 0.9722,
      tiempo_promedio_consulta_ms: 2310,
      saneamientos_solicitados: 14,
      tramites_creados: 9,
    },
    serie: [
      { inicio: "2026-11-14", diagnosticos: 2, express: 1, masivos: 1, vehiculos: 80 },
      { inicio: "2026-11-15", diagnosticos: 0, express: 0, masivos: 0, vehiculos: 0 },
    ],
    veredictos: { viables: 1500, bloqueados: 250, sin_dictamen: 80 },
    tipos_analisis: { simple: 20, con_analisis: 22 },
    fuentes: [
      { fuente: "RUNT", consultas: 1830, exitosas: 1800, con_error: 20, no_aplica: 0, pendientes: 10, tasa_exito: 0.989, tiempo_promedio_ms: 1800 },
      { fuente: "SIMIT_VEHICULO", consultas: 1830, exitosas: 1700, con_error: 100, no_aplica: 0, pendientes: 30, tasa_exito: null, tiempo_promedio_ms: null },
    ],
    saneamientos: [
      { estado: "SOLICITADO", cantidad: 3 },
      { estado: "EN_GESTION", cantidad: 5 },
      { estado: "SANEADO", cantidad: 4 },
      { estado: "NO_PROCEDE", cantidad: 1 },
      { estado: "ANULADO", cantidad: 1 },
    ],
    derivaciones: { creadas: 9, omitidas: 2, fallidas: 1, en_curso: 0 },
    top_empresas: [],
    ...cambios,
  };
}
