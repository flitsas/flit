// Feature #14453 — cliente de /api/v1/ot/caja: multipart con Idempotency-Key, Retry-After del 429 y total que cambió.
import { afterEach, describe, expect, it, vi } from "vitest";
import { CajaError, otCajaApi } from "../ot-caja";

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json", ...headers } });

afterEach(() => vi.unstubAllGlobals());

describe("otCajaApi", () => {
  it("confirmar envía multipart (medioCaja, detalle, totalVisto, prefactura) con Idempotency-Key", async () => {
    const fetchMock = vi.fn().mockResolvedValue(json(200, { codigo: "PN-2026-000123", valorCop: 1000, medio: "otro", pagadoEn: "x", mensaje: "ok" }));
    vi.stubGlobal("fetch", fetchMock);
    const pdf = new File(["%PDF"], "p.pdf", { type: "application/pdf" });

    await otCajaApi.confirmar({ codigo: "PN-2026-000123", medio: "otro", medioDetalle: "Bono", totalVistoCop: 1000, prefactura: pdf, llave: "llave-1" });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toContain("/api/v1/ot/caja/tramites/PN-2026-000123/confirmar");
    expect(init.method).toBe("POST");
    expect((init.headers as Record<string, string>)["Idempotency-Key"]).toBe("llave-1");
    const form = init.body as FormData;
    expect(form.get("medioCaja")).toBe("otro");
    expect(form.get("detalle")).toBe("Bono");
    expect(form.get("totalVisto")).toBe("1000");
    expect((form.get("prefactura") as File).name).toBe("p.pdf");
  });

  it("confirmar con 409 total_cambio trae el nuevo total", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(409, { code: "total_cambio", totalActualCop: 95000 })));
    const error = await otCajaApi
      .confirmar({ codigo: "PN-2026-000123", medio: "efectivo", totalVistoCop: 90000, llave: "l" })
      .catch((e: unknown) => e);
    expect(error).toBeInstanceOf(CajaError);
    expect((error as CajaError).code).toBe("total_cambio");
    expect((error as CajaError).totalActualCop).toBe(95000);
  });

  it("tramite con 429 conserva el Retry-After y el code", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(429, { code: "rate_limited" }, { "retry-after": "120" })));
    const error = (await otCajaApi.tramite("PN-2026-000123").catch((e: unknown) => e)) as CajaError;
    expect(error).toBeInstanceOf(CajaError);
    expect(error.status).toBe(429);
    expect(error.reintentarEnSegundos).toBe(120);
  });

  it("cobros pide el día y la página; anular manda motivo y rowVersion", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(json(200, { items: [], total: 0, totalDiaCop: 0 }))
      .mockResolvedValueOnce(json(200, {}));
    vi.stubGlobal("fetch", fetchMock);

    await otCajaApi.cobros({ date: "2026-11-17", page: 2, pageSize: 20 });
    const url = new URL((fetchMock.mock.calls[0] as [string])[0]);
    expect(url.pathname).toBe("/api/v1/ot/caja/cobros");
    expect(url.searchParams.get("date")).toBe("2026-11-17");
    expect(url.searchParams.get("page")).toBe("2");
    expect(url.searchParams.get("pageSize")).toBe("20");

    await otCajaApi.anular({ cobroId: "c-1", rowVersion: 4 }, "  Se cobró dos veces  ");
    const [anularUrl, init] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(new URL(anularUrl).pathname).toBe("/api/v1/ot/caja/cobros/c-1/anular");
    expect(JSON.parse(init.body as string)).toEqual({ motivo: "Se cobró dos veces", rowVersion: 4 });
  });

  it("sin red: error sin respuesta definitiva", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fallo")));
    const error = (await otCajaApi.confirmar({ codigo: "PN-2026-000123", medio: "efectivo", totalVistoCop: 1, llave: "l" }).catch((e: unknown) => e)) as CajaError;
    expect(error.sinRespuestaDefinitiva).toBe(true);
  });

  it("cobros pasa el `usuario` de core-api a `pagadoPorNombre` para la columna Usuario", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        json(200, {
          fecha: "2026-11-17",
          totalDiaCop: 150,
          total: 1,
          items: [{ cobroId: "c1", codigo: "PN-2026-000052", valorCop: 150, medio: "efectivo", usuario: "Cajero Prueba Uno", estado: "pagado", anulable: true, rowVersion: 1 }],
        }),
      ),
    );
    const lista = await otCajaApi.cobros({ page: 1, pageSize: 10 });
    expect(lista.items[0].pagadoPorNombre).toBe("Cajero Prueba Uno");
  });
});
