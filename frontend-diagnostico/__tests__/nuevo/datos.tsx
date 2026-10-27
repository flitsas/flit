// Datos ficticios y montaje de la pantalla única de creación (HU #14085): /diagnosticos/nuevo con el selector
// Express | Masivo, dentro del shell. Las placas (TST123…) y documentos son de prueba; no corresponden a nadie.
import { render } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import NuevoDiagnostico from "@/app/(app)/diagnosticos/nuevo/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { ExpressCreado } from "@/lib/express/api";
import { configuracion, sesion, simularApi, tiposOk } from "@/components/parametrizacion/__tests__/datos";

export { ID, TIPOS, configuracion, cuerpoDe, diagnostico, llamadas, simularApi, tiposOk } from "@/components/parametrizacion/__tests__/datos";
export { archivo, correccionesDe, fila, formularioDe, resumen } from "@/components/carga/__tests__/datos";

export const CREADOR: Partial<CapacidadesDiagnostico> = { crearDiagnosticos: true, verDiagnosticos: true, verConfiguracion: true };
export const ID_EXPRESS = "00000000-0000-7000-8000-00000000e001";
export const FECHA = new Date("2026-10-21T15:30:00Z");

/** Navegación simulada: cada archivo de pruebas la crea con vi.hoisted y la pasa al vi.mock de next/navigation. */
export interface Navegacion {
  push: (...args: unknown[]) => void;
  replace: (...args: unknown[]) => void;
  refresh: () => void;
  params: URLSearchParams;
}

export function expressCreado(cambios: Partial<ExpressCreado> = {}): ExpressCreado {
  return {
    id: ID_EXPRESS,
    nombre: "Express TST123 21/10/2026",
    modo: "SIMPLE",
    es_express: true,
    estado: "EN_PROCESO",
    creado_en: "2026-10-21T15:30:00Z",
    vehiculo: {
      id: "00000000-0000-7000-8000-00000000f001",
      orden: 1,
      identificador: "TST123",
      identificador_norm: "TST123",
      tipo_identificador: "PLACA_CARRO",
      tipo_documento_locatario: null,
      documento_locatario: null,
    },
    ...cambios,
  };
}

export const creadoOk = () => Response.json(expressCreado(), { status: 201 });

type Rutas = Parameters<typeof simularApi>[0];

export interface OpcionesMontaje {
  rutas?: Rutas;
  capacidades?: Partial<CapacidadesDiagnostico>;
  estimado?: "NO_MOSTRAR" | "ANTES_DE_LANZAR";
  /** Configuración completa; si falta, la de prueba con `estimado`. */
  config?: ReturnType<typeof configuracion>;
  /** Query de la URL (`modo=masivo`, `modo=express&placa=TST123`). */
  query?: string;
}

/** Monta /diagnosticos/nuevo dentro del shell con la sesión, la configuración y el catálogo simulados. */
export function montarNuevo(navegacion: Navegacion, { rutas = {}, capacidades = CREADOR, estimado = "NO_MOSTRAR", query = "", config }: OpcionesMontaje = {}) {
  navegacion.params = new URLSearchParams(query);
  window.history.replaceState(null, "", `/diagnosticos/nuevo${query ? `?${query}` : ""}`);
  const api = simularApi({
    "GET /sesion": () => Response.json(sesion(capacidades)),
    "GET /configuracion": () => Response.json(config ?? configuracion({ operacion: { estimado_consultas: estimado } })),
    "GET /tipos-tramite": tiposOk,
    "POST /express": creadoOk,
    ...rutas,
  });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      <NuevoDiagnostico />
    </DiagnosticoShell>,
  );
  return api;
}

/** Índice de la primera llamada al fetch simulado con un método y una ruta (para verificar el orden). */
export function indiceDe(fetchMock: { mock: { calls: unknown[][] } }, metodo: string, ruta: string): number {
  return fetchMock.mock.calls.findIndex(
    ([url, init]) => url === `/api/v1/diagnostico${ruta}` && ((init as RequestInit | undefined)?.method ?? "GET") === metodo,
  );
}
