// HU #12431 AC2 — `getNotificationSample` gana `tenantId` opcional (aditivo). Cubre: (1) sin
// `tenantId` la URL es IDÉNTICA a como era antes de esta HU (paridad), (2) con `tenantId` se
// agrega al query string, (3) `theme` aditivo llega tal cual en la respuesta.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getNotificationSample } from "../admin-plataforma-notificaciones";

const originalFetch = global.fetch;

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  global.fetch = originalFetch;
});

function sampleResponse(overrides: Record<string, unknown> = {}) {
  return {
    templateId: "tramites.aprobado",
    subject: "Tu trámite fue aprobado",
    html: "<p>Hola</p>",
    ...overrides,
  };
}

describe("getNotificationSample — paridad sin tenantId (AC2)", () => {
  it("sin opciones no agrega ningún query param", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(sampleResponse()), { status: 200 });
    }) as never;

    await getNotificationSample("tramites.aprobado");

    expect(capturedUrl).toBe(
      new URL(
        "/api/v1/admin/plataforma/notificaciones/plantillas/tramites.aprobado/muestra",
        capturedUrl,
      ).toString(),
    );
    expect(capturedUrl).not.toContain("tenantId=");
    expect(capturedUrl).not.toContain("?");
  });

  it("con channel/procedureTypeId pero sin tenantId, no agrega tenantId", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(sampleResponse()), { status: 200 });
    }) as never;

    await getNotificationSample("tramites.aprobado", { channel: "FLIT_SMTP" });

    expect(capturedUrl).toContain("channel=FLIT_SMTP");
    expect(capturedUrl).not.toContain("tenantId=");
  });
});

describe("getNotificationSample — con tenantId (AC2)", () => {
  it("agrega tenantId al query string", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(sampleResponse()), { status: 200 });
    }) as never;

    await getNotificationSample("tramites.aprobado", { tenantId: "tenant-mb-1" });

    expect(capturedUrl).toContain("tenantId=tenant-mb-1");
  });

  it("devuelve el theme aditivo cuando el backend lo incluye", async () => {
    global.fetch = vi.fn(
      async () =>
        new Response(
          JSON.stringify(
            sampleResponse({
              theme: { kind: "brand", platformName: "Movilidad Andina", version: 3, senderName: "Movilidad Andina" },
            }),
          ),
          { status: 200 },
        ),
    ) as never;

    const result = await getNotificationSample("tramites.aprobado", { tenantId: "tenant-mb-1" });

    expect(result.theme).toEqual({
      kind: "brand",
      platformName: "Movilidad Andina",
      version: 3,
      senderName: "Movilidad Andina",
    });
  });
});
