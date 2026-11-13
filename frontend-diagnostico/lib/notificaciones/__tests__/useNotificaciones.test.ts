import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { EVENTO_SONDEAR_NOTIFICACIONES, INTERVALO_NOTIFICACIONES_MS, pedirSondeoNotificaciones, useNotificaciones } from "@/lib/notificaciones/useNotificaciones";
import { aviso, bandeja, bandejas, pedidos, simularApi } from "./datos";

// Uso de ejemplo: const { items, noLeidas, estado, marcarLeida, leerTodas, reintentar } = useNotificaciones() → pide GET
// /notificaciones?limite=20 y cada 30 s con la pestaña visible lo repite con If-None-Match (un 304 no cambia nada); se
// pausa con la pestaña oculta y `pedirSondeoNotificaciones()` (evento de ventana) fuerza un sondeo inmediato.

const RUTA = "/notificaciones";
const avanzar = (ms: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

let oculta = false;
beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval"] });
  oculta = false;
  Object.defineProperty(document, "visibilityState", { configurable: true, get: () => (oculta ? "hidden" : "visible") });
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const cambiarVisibilidad = async (ocultar: boolean) => {
  oculta = ocultar;
  await act(async () => {
    document.dispatchEvent(new Event("visibilitychange"));
  });
};

describe("HU14562 AC51 sondeo de la campana", () => {
  it("expone 30 s y el nombre constante del evento de sondeo inmediato", () => {
    expect(INTERVALO_NOTIFICACIONES_MS).toBe(30_000);
    expect(EVENTO_SONDEAR_NOTIFICACIONES).toBe("dx:sondear-notificaciones");
  });

  it("primer GET sin If-None-Match; a los 30 s repite con el ETag y un 304 no cambia nada", async () => {
    const api = simularApi({ [`GET ${RUTA}`]: bandejas(bandeja([aviso(1)])) });
    const { result } = renderHook(() => useNotificaciones());
    await avanzar(0);
    expect(pedidos(api, RUTA)).toEqual([{ metodo: "GET", etag: null, query: { limite: "20" } }]);
    expect(result.current.noLeidas).toBe(1);
    const antes = result.current.items;

    await avanzar(INTERVALO_NOTIFICACIONES_MS - 1);
    expect(pedidos(api, RUTA)).toHaveLength(1);
    await avanzar(1);
    expect(pedidos(api, RUTA)[1]).toMatchObject({ etag: 'W/"0"' });
    expect(result.current.items).toBe(antes);
    expect(result.current.estado).toBe("listo");
  });

  it("con la pestaña oculta se pausa y al volver sondea de inmediato", async () => {
    const api = simularApi({ [`GET ${RUTA}`]: bandejas(bandeja([])) });
    renderHook(() => useNotificaciones());
    await avanzar(0);
    await cambiarVisibilidad(true);
    await avanzar(INTERVALO_NOTIFICACIONES_MS * 3);
    expect(pedidos(api, RUTA)).toHaveLength(1);
    await cambiarVisibilidad(false);
    await avanzar(0);
    expect(pedidos(api, RUTA)).toHaveLength(2);
  });

  it("pedirSondeoNotificaciones() (tras pedir el ZIP o crear trámites masivo) sondea de inmediato", async () => {
    const api = simularApi({ [`GET ${RUTA}`]: bandejas(bandeja([])) });
    renderHook(() => useNotificaciones());
    await avanzar(0);
    act(() => pedirSondeoNotificaciones());
    await avanzar(0);
    expect(pedidos(api, RUTA)).toHaveLength(2);
  });
});

describe("HU14562 AC50 leída y leer todas", () => {
  it("marcarLeida llama POST …/{id}/leida y baja el contador; leerTodas llama POST …/leer-todas y lo deja en 0", async () => {
    const a1 = aviso(1);
    const a2 = aviso(2);
    const api = simularApi({
      [`GET ${RUTA}`]: bandejas(bandeja([a1, a2])),
      [`POST ${RUTA}/${a1.id}/leida`]: () => new Response(null, { status: 204 }),
      [`POST ${RUTA}/leer-todas`]: () => new Response(null, { status: 204 }),
    });
    const { result } = renderHook(() => useNotificaciones());
    await avanzar(0);
    expect(result.current.noLeidas).toBe(2);
    await act(() => result.current.marcarLeida(a1.id));
    expect(pedidos(api, `${RUTA}/${a1.id}/leida`)).toEqual([{ metodo: "POST", etag: null, query: {} }]);
    expect(result.current.noLeidas).toBe(1);
    await act(() => result.current.leerTodas());
    expect(pedidos(api, `${RUTA}/leer-todas`)).toHaveLength(1);
    expect(result.current.noLeidas).toBe(0);
    expect(result.current.items.every((i) => i.leida_en)).toBe(true);
  });
});

describe("HU14562 AC54 error del sondeo", () => {
  it("un 5xx deja estado error y reintentar vuelve a pedir", async () => {
    let falla = true;
    const api = simularApi({ [`GET ${RUTA}`]: () => (falla ? new Response(null, { status: 503 }) : Response.json(bandeja([]))) });
    const { result } = renderHook(() => useNotificaciones());
    await avanzar(0);
    expect(result.current.estado).toBe("error");
    falla = false;
    act(() => result.current.reintentar());
    await avanzar(0);
    expect(result.current.estado).toBe("listo");
    expect(pedidos(api, RUTA)).toHaveLength(2);
  });
});
