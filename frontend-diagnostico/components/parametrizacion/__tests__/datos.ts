// Datos ficticios de las pruebas del formulario de parametrización (HU #13752): configuración, catálogo de tipos y
// respuestas inventadas, con el fetch del navegador simulado por URL.
import { vi } from "vitest";
import type { Diagnostico, TipoTramite } from "@/lib/diagnosticos/api";

export { DISPONIBLES_CON_IMPUESTO, DISPONIBLES_SIN_IMPUESTO, configuracion, sesion, cuerpo } from "@/__tests__/configuracion/datos";

export const ID = "00000000-0000-7000-8000-00000000d001";

export const TIPOS: TipoTramite[] = [
  { codigo: "TRASPASO_STANDARD", nombre: "TRASPASO", familia: "TRASPASO", fuentesObligatorias: [] },
  { codigo: "TRASPASO_UNILATERAL", nombre: "TRASPASO UNILATERAL", familia: "TRASPASO", fuentesObligatorias: ["SIMIT_VEHICULO"] },
];

export function diagnostico(cambios: Partial<Diagnostico> = {}): Diagnostico {
  return {
    id: ID,
    nombre: "Flota norte",
    modo: "SIMPLE",
    tipo_tramite_codigo: null,
    es_express: false,
    estado: "CREADO",
    alcance: { por_vehiculo: ["RUNT"], por_propietario: [], por_locatario: [] },
    configuracion: {
      fuentes_habilitadas: ["RUNT", "SIMIT_VEHICULO", "AVALUO"],
      limite_vehiculos_lote: 500,
      visibilidad_usuario: "EMPRESA",
      fuentes_por_defecto: ["RUNT"],
      estimado_consultas: "NO_MOSTRAR",
    },
    usuario_creador_id: "00000000-0000-7000-8000-000000000001",
    creado_en: "2026-10-21T15:00:00Z",
    ...cambios,
  };
}

type Respuesta = (init?: RequestInit) => Response | Promise<Response>;

/** Simula el BFF: cada clave es «MÉTODO ruta» bajo /api/v1/diagnostico. Lo no previsto responde 404. */
export function simularApi(rutas: Record<string, Respuesta>) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const metodo = init?.method ?? "GET";
    const clave = `${metodo} ${url.replace("/api/v1/diagnostico", "")}`;
    const respuesta = rutas[clave];
    return respuesta ? respuesta(init) : Response.json({ code: "NO_ENCONTRADO", error: "No existe" }, { status: 404 });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

/** Llamadas al fetch simulado con un método y una ruta. */
export function llamadas(fetchMock: { mock: { calls: unknown[][] } }, metodo: string, ruta: string): unknown[][] {
  return fetchMock.mock.calls.filter(([url, init]) => url === `/api/v1/diagnostico${ruta}` && ((init as RequestInit | undefined)?.method ?? "GET") === metodo);
}

export function cuerpoDe(llamada: unknown[]): unknown {
  const init = llamada[1] as RequestInit | undefined;
  return init?.body ? JSON.parse(String(init.body)) : undefined;
}

export const tiposOk: Respuesta = () => Response.json({ tipos: TIPOS });
