// HU #12211 (Feature #12201) — CF-14: polling cada 4 segundos que se detiene al llegar a un
// estado terminal Y al perder el foco la pestaña.
// Uso de ejemplo: renderHook(() => useBatchPolling("id-del-lote")) con el cliente mockeado.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";

const mocks = vi.hoisted(() => ({ fetchStandaloneBatch: vi.fn() }));

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  fetchStandaloneBatch: mocks.fetchStandaloneBatch,
}));

import { BATCH_POLL_INTERVAL_MS, useBatchPolling } from "../useBatchPolling";

const BATCH_ID = "0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee";

function lote(overrides: Record<string, unknown> = {}) {
  return {
    batchId: BATCH_ID,
    status: "processing",
    total: 10,
    generated: 3,
    errors: 0,
    processed: 3,
    isTerminal: false,
    createdAt: "2026-09-09T10:00:00.000Z",
    completedAt: null,
    ...overrides,
  };
}

/** Fuerza el estado de visibilidad de la pestaña y dispara el evento del navegador. */
function cambiarVisibilidad(estado: "visible" | "hidden") {
  Object.defineProperty(document, "visibilityState", {
    configurable: true,
    get: () => estado,
  });
  document.dispatchEvent(new Event("visibilitychange"));
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.clearAllMocks();
  cambiarVisibilidad("visible");
  mocks.fetchStandaloneBatch.mockResolvedValue(lote());
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe("useBatchPolling — cadencia (CF-14)", () => {
  it("consulta al montar y vuelve a consultar cada 4 segundos", async () => {
    renderHook(() => useBatchPolling(BATCH_ID));

    await waitFor(() => expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS);
    });
    expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(2);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS);
    });
    expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(3);
  });

  it("el intervalo es de 4 segundos exactos: a los 3.9 s todavía no ha vuelto a consultar", async () => {
    renderHook(() => useBatchPolling(BATCH_ID));

    await waitFor(() => expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS - 100);
    });

    expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1);
  });
});

describe("useBatchPolling — se detiene en estado terminal (CF-14)", () => {
  it.each(["completed", "partial_failure", "failed"])(
    "deja de sondear cuando el lote llega a %s",
    async (estadoTerminal) => {
      mocks.fetchStandaloneBatch.mockResolvedValueOnce(lote());
      mocks.fetchStandaloneBatch.mockResolvedValue(
        lote({ status: estadoTerminal, isTerminal: true, processed: 10, generated: 9, errors: 1 }),
      );

      const { result } = renderHook(() => useBatchPolling(BATCH_ID));

      await waitFor(() => expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1));

      await act(async () => {
        await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS);
      });

      await waitFor(() => expect(result.current.batch?.isTerminal).toBe(true));
      expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(2);

      // Tres ciclos más de reloj: si el temporizador siguiera vivo, habría tres llamadas más.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS * 3);
      });

      expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(2);
      expect(result.current.polling).toBe(false);
    },
  );

  it("un lote que YA llega terminal no programa ni un solo sondeo", async () => {
    mocks.fetchStandaloneBatch.mockResolvedValue(
      lote({ status: "completed", isTerminal: true, processed: 10, generated: 10 }),
    );

    const { result } = renderHook(() => useBatchPolling(BATCH_ID));

    await waitFor(() => expect(result.current.status).toBe("ready"));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS * 5);
    });

    expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1);
  });
});

describe("useBatchPolling — se detiene al perder el foco la pestaña (CF-14)", () => {
  it("no sondea mientras la pestaña está en segundo plano", async () => {
    renderHook(() => useBatchPolling(BATCH_ID));

    await waitFor(() => expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1));

    await act(async () => {
      cambiarVisibilidad("hidden");
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS * 4);
    });

    expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(
      1,
      // Cuatro ciclos con la pestaña oculta y ni una llamada: el temporizador se cancelo.
    );
  });

  it("al volver el foco consulta de inmediato y reanuda el ciclo", async () => {
    const { result } = renderHook(() => useBatchPolling(BATCH_ID));

    await waitFor(() => expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1));

    await act(async () => {
      cambiarVisibilidad("hidden");
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS * 2);
    });
    expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1);

    await act(async () => {
      cambiarVisibilidad("visible");
    });

    // Consulta YA, sin esperar los 4 segundos: si no, la vista pareceria congelada.
    await waitFor(() => expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(2));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS);
    });
    expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(3);
    expect(result.current.polling).toBe(true);
  });

  it("una pestaña oculta con lote terminal tampoco reanuda al volver", async () => {
    mocks.fetchStandaloneBatch.mockResolvedValue(
      lote({ status: "partial_failure", isTerminal: true, processed: 10, generated: 9, errors: 1 }),
    );

    renderHook(() => useBatchPolling(BATCH_ID));

    await waitFor(() => expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1));

    await act(async () => {
      cambiarVisibilidad("hidden");
    });
    await act(async () => {
      cambiarVisibilidad("visible");
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS * 3);
    });

    expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1);
  });
});

describe("useBatchPolling — errores", () => {
  it("un 404 detiene el sondeo y no se reintenta en bucle", async () => {
    mocks.fetchStandaloneBatch.mockRejectedValue(
      Object.assign(new Error("not found"), { status: 404 }),
    );

    const { result } = renderHook(() => useBatchPolling(BATCH_ID));

    await waitFor(() => expect(result.current.status).toBe("notFound"));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS * 3);
    });

    expect(mocks.fetchStandaloneBatch).toHaveBeenCalledTimes(1);
  });

  it("un fallo transitorio deja el estado en error pero sigue reintentando", async () => {
    mocks.fetchStandaloneBatch.mockRejectedValueOnce(
      Object.assign(new Error("boom"), { status: 502 }),
    );
    mocks.fetchStandaloneBatch.mockResolvedValue(lote());

    const { result } = renderHook(() => useBatchPolling(BATCH_ID));

    await waitFor(() => expect(result.current.status).toBe("error"));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(BATCH_POLL_INTERVAL_MS);
    });

    await waitFor(() => expect(result.current.status).toBe("ready"));
  });
});
