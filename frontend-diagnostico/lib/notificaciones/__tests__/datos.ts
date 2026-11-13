import { vi } from "vitest";
import type { NotificacionDiagnostico, RespuestaNotificaciones } from "@/lib/notificaciones/api";

/** Datos sintéticos de la campana (B8). Sin PII: solo ids inventados y el nombre de un lote de prueba. */
export const LOTE = "00000000-0000-7000-8000-0000000000d1";
export const EVIDENCIA = "00000000-0000-7000-8000-0000000000e9";

export function aviso(n: number, cambios: Partial<NotificacionDiagnostico> = {}): NotificacionDiagnostico {
  return {
    id: `00000000-0000-7000-8000-00000000a${String(n).padStart(3, "0")}`,
    tipo: "LOTE_TERMINADO",
    creada_en: "2026-11-12T15:00:00+00:00",
    leida_en: null,
    diagnostico_id: LOTE,
    diagnostico_nombre: "Lote flota norte",
    referencia_id: `00000000-0000-7000-8000-00000000c${String(n).padStart(3, "0")}`,
    datos: {},
    ...cambios,
  };
}

export const bandeja = (items: NotificacionDiagnostico[], noLeidas = items.filter((i) => !i.leida_en).length): RespuestaNotificaciones => ({
  no_leidas: noLeidas,
  items,
  siguiente: null,
});

type Manejador = (req: Request) => Response | Promise<Response>;

const sinPrefijo = (url: URL) => url.pathname.replace(/^\/api\/v1\/diagnostico/, "");

/** fetch simulado por «MÉTODO /ruta» (sin el prefijo /api/v1/diagnostico y sin la query). Lo demás responde 404. */
export function simularApi(rutas: Record<string, Manejador>) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const req = new Request(new URL(String(input), "http://localhost"), init);
    const manejador = rutas[`${req.method} ${sinPrefijo(new URL(req.url))}`];
    if (!manejador) return new Response(null, { status: 404 });
    return manejador(req);
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

/** Peticiones hechas a una ruta, con su método, su If-None-Match y su query. */
export function pedidos(fetchMock: ReturnType<typeof simularApi>, ruta: string) {
  return fetchMock.mock.calls
    .map(([u, init]) => ({ url: new URL(String(u), "http://localhost"), init: init as RequestInit | undefined }))
    .filter((p) => sinPrefijo(p.url) === ruta)
    .map((p) => ({
      metodo: p.init?.method ?? "GET",
      etag: new Headers(p.init?.headers).get("If-None-Match"),
      query: Object.fromEntries(p.url.searchParams),
    }));
}

/**
 * GET /notificaciones con ETag: entrega las bandejas en orden con ETag `W/"i"`; cuando ya entregó la última y llega
 * su mismo If-None-Match, responde 304 sin cuerpo.
 */
export function bandejas(...lista: RespuestaNotificaciones[]) {
  let siguiente = 0;
  return (req: Request) => {
    const ultima = `W/"${siguiente - 1}"`;
    if (siguiente >= lista.length && req.headers.get("If-None-Match") === ultima) return new Response(null, { status: 304 });
    const i = Math.min(siguiente, lista.length - 1);
    siguiente = Math.min(siguiente + 1, lista.length);
    return Response.json(lista[i], { headers: { ETag: `W/"${i}"` } });
  };
}
