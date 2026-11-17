import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { INTERVALO_SONDEO_MS, useGrillaIncremental } from "@/lib/tablero/useGrillaIncremental";
import { ID, V1, celda, desdesPedidos, grilla, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";
import type { Grilla } from "@/lib/tablero/api";

// Uso de ejemplo: const { grilla, cargando, error, reanudar } = useGrillaIncremental(id) → primero pide la grilla completa
// (?desde=0) y, mientras quede alguna celda PENDIENTE o EN_CURSO, cada 3 s pide solo los cambios desde la última versión y
// los fusiona; sin pendientes se detiene y `reanudar()` (tras un reproceso) vuelve a sondear.

const RUTA = `GET /diagnosticos/${ID}/grilla`;

/** Avanza los timers falsos y deja resolver las promesas del fetch simulado. */
const avanzar = (ms: number) => act(async () => {
  await vi.advanceTimersByTimeAsync(ms);
});

/** Responde en orden; la última se repite. */
function respuestas(...grillas: Grilla[]) {
  let n = 0;
  return simularApi({ [RUTA]: () => Response.json(grillas[Math.min(n++, grillas.length - 1)]) });
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("useGrillaIncremental — HU #13748 AC2", () => {
  it("expone el intervalo de 3 s del sondeo", () => {
    expect(INTERVALO_SONDEO_MS).toBe(3000);
  });

  it("pide la grilla completa con desde=0 y luego, cada 3 s, solo los cambios desde la última versión", async () => {
    const v3Runt = vehiculo(3, [celda(3, "RUNT", "COMPLETADO", { version: 13 })]);
    const api = respuestas(grilla({ version: 12 }), grilla({ version: 13, desde: 12, vehiculos: [v3Runt] }), grilla({ version: 13, desde: 13, vehiculos: [] }));
    const { result } = renderHook(() => useGrillaIncremental(ID));

    await avanzar(0);
    expect(desdesPedidos(api)).toEqual([0]);
    expect(result.current.cargando).toBe(false);
    expect(result.current.grilla?.version).toBe(12);

    await avanzar(INTERVALO_SONDEO_MS - 1);
    expect(desdesPedidos(api)).toEqual([0]);
    await avanzar(1);
    expect(desdesPedidos(api)).toEqual([0, 12]);
    expect(result.current.grilla?.vehiculos[2].celdas.find((c) => c.fuente === "RUNT")?.estado).toBe("COMPLETADO");
    expect(result.current.grilla?.vehiculos[2].celdas).toHaveLength(4);

    await avanzar(INTERVALO_SONDEO_MS);
    expect(desdesPedidos(api)).toEqual([0, 12, 13]);
    expect(result.current.sondeando).toBe(true);
  });

  it("deja de consultar cuando no quedan celdas PENDIENTE ni EN_CURSO", async () => {
    const final = vehiculo(3, [celda(3, "RUNT", "COMPLETADO", { version: 13 })], { terminado: true });
    const api = respuestas(grilla({ vehiculos: [V1, vehiculo(3, [celda(3, "RUNT", "PENDIENTE")])] }), grilla({ version: 13, vehiculos: [final] }));
    const { result } = renderHook(() => useGrillaIncremental(ID));

    await avanzar(0);
    await avanzar(INTERVALO_SONDEO_MS);
    expect(desdesPedidos(api)).toEqual([0, 12]);

    await avanzar(INTERVALO_SONDEO_MS * 5);
    expect(desdesPedidos(api)).toEqual([0, 12]);
    expect(result.current.sondeando).toBe(false);
  });

  it("un lote sin pendientes desde el principio no vuelve a consultar", async () => {
    const api = respuestas(grilla({ vehiculos: [V1] }));
    renderHook(() => useGrillaIncremental(ID));
    await avanzar(0);
    await avanzar(INTERVALO_SONDEO_MS * 3);
    expect(desdesPedidos(api)).toEqual([0]);
  });

  it("reanudar() tras un reproceso vuelve a sondear desde la última versión", async () => {
    const api = respuestas(grilla({ vehiculos: [V1], version: 20 }), grilla({ version: 21, vehiculos: [] }));
    const { result } = renderHook(() => useGrillaIncremental(ID));
    await avanzar(0);
    await avanzar(INTERVALO_SONDEO_MS * 2);
    expect(desdesPedidos(api)).toEqual([0]);

    act(() => result.current.reanudar());
    expect(result.current.sondeando).toBe(true);
    await avanzar(INTERVALO_SONDEO_MS);
    expect(desdesPedidos(api)).toEqual([0, 20]);
  });

  it("actualizar() cambia la grilla en memoria (reproceso optimista) sin pedirla de nuevo", async () => {
    const api = respuestas(grilla({ vehiculos: [V1] }));
    const { result } = renderHook(() => useGrillaIncremental(ID));
    await avanzar(0);
    act(() => result.current.actualizar((g) => ({ ...g, estado: "INCOMPLETO" })));
    expect(result.current.grilla?.estado).toBe("INCOMPLETO");
    expect(desdesPedidos(api)).toEqual([0]);
  });

  it("refrescar() pide una vez, de inmediato, los cambios desde la última versión", async () => {
    const api = respuestas(grilla({ vehiculos: [V1], version: 20 }), grilla({ estado: "INCOMPLETO", version: 20, vehiculos: [] }));
    const { result } = renderHook(() => useGrillaIncremental(ID));
    await avanzar(0);
    act(() => result.current.refrescar());
    await avanzar(0);
    expect(desdesPedidos(api)).toEqual([0, 20]);
    expect(result.current.grilla?.estado).toBe("INCOMPLETO");
  });

  it("error de la primera carga: expone el error y reintentar() vuelve a pedir desde 0", async () => {
    let n = 0;
    const api = simularApi({ [RUTA]: () => (n++ === 0 ? Response.json({ error: "x" }, { status: 500 }) : Response.json(grilla({ vehiculos: [V1] }))) });
    const { result } = renderHook(() => useGrillaIncremental(ID));
    await avanzar(0);
    expect(result.current.error).toBeTruthy();
    expect(result.current.grilla).toBeNull();

    act(() => result.current.reintentar());
    expect(result.current.cargando).toBe(true);
    await avanzar(0);
    expect(desdesPedidos(api)).toEqual([0, 0]);
    expect(result.current.error).toBeNull();
    expect(result.current.grilla?.vehiculos).toHaveLength(1);
  });

  it("error en un sondeo: conserva la grilla, expone errorSondeo y se detiene", async () => {
    let n = 0;
    const api = simularApi({ [RUTA]: () => (n++ === 0 ? Response.json(grilla()) : Response.json({ error: "x" }, { status: 503 })) });
    const { result } = renderHook(() => useGrillaIncremental(ID));
    await avanzar(0);
    await avanzar(INTERVALO_SONDEO_MS);
    expect(result.current.grilla?.vehiculos).toHaveLength(3);
    expect(result.current.errorSondeo).toBeTruthy();
    await avanzar(INTERVALO_SONDEO_MS * 3);
    expect(desdesPedidos(api)).toEqual([0, 12]);
  });

  it("deshabilitado (sin permiso de ver) no consulta", async () => {
    const api = respuestas(grilla());
    renderHook(() => useGrillaIncremental(ID, { habilitado: false }));
    await avanzar(INTERVALO_SONDEO_MS * 2);
    expect(api).not.toHaveBeenCalled();
  });

  it("al desmontar deja de sondear", async () => {
    const api = respuestas(grilla());
    const { unmount } = renderHook(() => useGrillaIncremental(ID));
    await avanzar(0);
    unmount();
    await avanzar(INTERVALO_SONDEO_MS * 3);
    expect(desdesPedidos(api)).toEqual([0]);
  });
});
