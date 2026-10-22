// Datos ficticios del listado de diagnósticos (HU #13852): lotes y creadores inventados con la forma de
// GET /diagnosticos (HU #13845, schemas ListaDiagnosticos y DiagnosticoListado). Nada sale de datos reales.
import type { DiagnosticoListado, FiltrosListado, ListaDiagnosticos } from "@/lib/diagnosticos/listado";

export function diagnostico(n: number, cambios: Partial<DiagnosticoListado> = {}): DiagnosticoListado {
  const sufijo = String(n).padStart(3, "0");
  return {
    id: `0192f0a${n}-0000-7000-8000-000000000${sufijo}`,
    nombre: `Flota de prueba ${sufijo}`,
    creado_en: "2026-10-22T14:00:00+00:00",
    modo: "CON_ANALISIS",
    tipo_tramite: { codigo: "TRASPASO_STANDARD", nombre: "TRASPASO" },
    estado: "EN_PROCESO",
    usuario_creador: { id: "00000000-0000-7000-8000-0000000000c1", nombre: "Ana Prueba" },
    ...cambios,
  };
}

export function lista(diagnosticos: DiagnosticoListado[], cambios: Partial<ListaDiagnosticos> = {}): ListaDiagnosticos {
  return { pagina: 1, tamano: 25, total: diagnosticos.length, diagnosticos, ...cambios };
}

export function filtros(cambios: Partial<FiltrosListado> = {}): FiltrosListado {
  return { desde: "", hasta: "", modo: null, estado: null, nombre: "", pagina: 1, tamano: 25, ...cambios };
}
