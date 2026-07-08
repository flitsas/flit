// Reportes2 HU-A — cliente de telemetría fire-and-forget (lib/telemetry.ts):
// encola con validación de taxonomía, flush por lote (20) y por intervalo (10 s),
// nunca lanza ante fallos de red y sin token no envía nada.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  __resetTelemetryForTests,
  __telemetryQueueSize,
  flushTelemetry,
  trackEvent,
  trackModuleView,
} from "@/lib/telemetry";

const TOKEN_STORAGE_KEY = "flit:jwt";

function stubFetch(impl?: () => Promise<Response>) {
  const mock = vi.fn(impl ?? (() => Promise.resolve(new Response(null, { status: 202 }))));
  vi.stubGlobal("fetch", mock);
  return mock;
}

describe("telemetry (Reportes 2.0 · HU-A)", () => {
  beforeEach(() => {
    __resetTelemetryForTests();
    window.localStorage.setItem(TOKEN_STORAGE_KEY, "jwt-de-prueba");
  });

  afterEach(() => {
    __resetTelemetryForTests();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("encola eventos válidos con occurredAt por defecto", () => {
    stubFetch();

    trackEvent({ eventType: "wizard_step_view", module: "tramites", stepKey: "comprador" });
    trackEvent({ eventType: "module_view", module: "reportes" });

    expect(__telemetryQueueSize()).toBe(2);
  });

  it("descarta eventos fuera de la taxonomía", () => {
    stubFetch();

    trackEvent({ eventType: "evento_inventado" });
    trackEvent({ eventType: "" });

    expect(__telemetryQueueSize()).toBe(0);
  });

  it("hace flush inmediato al llegar a 20 eventos (lote)", async () => {
    const fetchMock = stubFetch();

    for (let i = 0; i < 20; i++) {
      trackEvent({ eventType: "wizard_step_view", module: "tramites", stepKey: `paso_${i}` });
    }
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain("/api/v1/analytics/events");
    expect(init.method).toBe("POST");
    expect(init.keepalive).toBe(true);
    expect((init.headers as Record<string, string>).Authorization).toBe("Bearer jwt-de-prueba");
    const body = JSON.parse(init.body as string) as { events: unknown[] };
    expect(body.events).toHaveLength(20);
    expect(__telemetryQueueSize()).toBe(0);
  });

  it("hace flush por intervalo (10 s) cuando el lote no se llena", async () => {
    vi.useFakeTimers();
    const fetchMock = stubFetch();

    trackEvent({ eventType: "wizard_abandon", module: "tramites", stepKey: "fur" });
    expect(fetchMock).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(10_000);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(__telemetryQueueSize()).toBe(0);
  });

  it("no lanza ni propaga cuando la red falla (lote descartado)", async () => {
    stubFetch(() => Promise.reject(new Error("network down")));

    trackEvent({ eventType: "wizard_complete", module: "tramites", durationMs: 1200 });

    await expect(flushTelemetry()).resolves.toBeUndefined();
    expect(__telemetryQueueSize()).toBe(0);
  });

  it("sin token no envía nada (y no lanza)", async () => {
    const fetchMock = stubFetch();
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);

    trackEvent({ eventType: "module_view", module: "tramites" });
    await flushTelemetry();

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("normaliza durationMs negativo a ausente", async () => {
    const fetchMock = stubFetch();

    trackEvent({ eventType: "wizard_step_complete", module: "tramites", stepKey: "comercial", durationMs: -5 });
    await flushTelemetry();

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const body = JSON.parse(init.body as string) as { events: Array<{ durationMs?: number }> };
    expect(body.events[0].durationMs).toBeUndefined();
  });

  it("trackModuleView emite una sola vez por sesión y módulo", () => {
    stubFetch();

    trackModuleView("reportes");
    trackModuleView("reportes");
    trackModuleView("tramites");

    expect(__telemetryQueueSize()).toBe(2);
  });
});
