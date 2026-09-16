// HU #12427 — cliente del dominio propio de la red. Cubre: (1) 404 => null (sin dominio, no error),
// (2) base de endpoints admin vs company, (3) códigos de error tipados (`DOMAIN_*`,
// `CONCURRENCY_CONFLICT`), (4) 429 de `/verify` con mensaje de espera, (5) validación de formato
// de host en cliente.
//
// Uso de ejemplo:
//   const domain = await getAdminDomain("tenant-1"); // null si la red no tiene dominio
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../types";
import {
  cooldownSecondsFromError,
  domainErrorCode,
  domainErrorMessage,
  getAdminDomain,
  getCompanyDomain,
  isDomainNotFound,
  isValidHostFormat,
  registerAdminDomain,
  removeAdminDomain,
  verifyAdminDomain,
  verifyCompanyDomain,
} from "../domain-client";
import type { TenantDomainResponse } from "../types";

const originalFetch = global.fetch;

function domain(overrides: Partial<TenantDomainResponse> = {}): TenantDomainResponse {
  return {
    host: "app.movilidadandina.com",
    status: "pending",
    statusReason: null,
    statusChangedAt: "2026-09-15T10:00:00Z",
    verification: {
      txtName: "_flit-verify.app.movilidadandina.com",
      txtValue: "flit-verify=abc123",
      cnameName: "app.movilidadandina.com",
      cnameTarget: "edge.flitsas.online",
    },
    verifiedAt: null,
    activatedAt: null,
    certificate: { issuedAt: null, expiresAt: null },
    lastCheckedAt: null,
    nextCheckAt: "2026-09-15T10:05:00Z",
    graceUntil: null,
    rowVersion: 1,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("getAdminDomain / getCompanyDomain — 404 => null (AC1)", () => {
  it("getAdminDomain: 404 DOMAIN_NOT_FOUND devuelve null, no lanza", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "DOMAIN_NOT_FOUND" }), { status: 404 }),
    ) as never;

    const result = await getAdminDomain("tenant-1");
    expect(result).toBeNull();
  });

  it("getCompanyDomain: 404 devuelve null", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 404 })) as never;
    const result = await getCompanyDomain();
    expect(result).toBeNull();
  });

  it("getAdminDomain usa /api/v1/admin/companies/{tenantId}/domain", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(domain()), { status: 200 });
    }) as never;

    await getAdminDomain("tenant-42");
    expect(capturedUrl).toContain("/api/v1/admin/companies/tenant-42/domain");
  });

  it("getCompanyDomain usa /api/v1/company/domain (sin tenantId, solo lectura)", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(domain()), { status: 200 });
    }) as never;

    await getCompanyDomain();
    expect(capturedUrl).toContain("/api/v1/company/domain");
    expect(capturedUrl).not.toContain("/admin/");
  });

  it("estado lleno: devuelve el TenantDomainResponse tal cual", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(domain({ status: "active" })), { status: 200 })) as never;
    const result = await getAdminDomain("tenant-1");
    expect(result?.status).toBe("active");
  });
});

describe("isDomainNotFound", () => {
  it("true para 404", () => {
    expect(isDomainNotFound(new ApiError(404, "boom"))).toBe(true);
  });
  it("false para otros status o errores ajenos", () => {
    expect(isDomainNotFound(new ApiError(500, "boom"))).toBe(false);
    expect(isDomainNotFound(new Error("otro"))).toBe(false);
  });
});

describe("registerAdminDomain — PUT .../domain (HU #12416 AC1/AC2/AC3)", () => {
  it("hace PUT con host y rowVersion contra la base admin", async () => {
    let capturedUrl = "";
    let capturedMethod = "";
    let capturedBody: unknown = null;
    global.fetch = vi.fn(async (url: string | URL, init?: RequestInit) => {
      capturedUrl = url.toString();
      capturedMethod = init?.method ?? "";
      capturedBody = init?.body ? JSON.parse(init.body as string) : null;
      return new Response(JSON.stringify(domain()), { status: 200 });
    }) as never;

    await registerAdminDomain("tenant-42", { host: "app.movilidadandina.com", rowVersion: 1 });

    expect(capturedUrl).toContain("/api/v1/admin/companies/tenant-42/domain");
    expect(capturedMethod).toBe("PUT");
    expect(capturedBody).toEqual({ host: "app.movilidadandina.com", rowVersion: 1 });
  });

  it("400 DOMAIN_HOST_INVALID se traduce a ApiError con mensaje y código legibles", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "DOMAIN_HOST_INVALID" }), { status: 400 }),
    ) as never;

    const err = await registerAdminDomain("tenant-1", { host: "not a host" }).catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(domainErrorCode(err)).toBe("DOMAIN_HOST_INVALID");
    expect((err as ApiError).message).toMatch(/formato válido/i);
  });

  it("409 CONCURRENCY_CONFLICT se traduce con mensaje de recarga", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "CONCURRENCY_CONFLICT" }), { status: 409 }),
    ) as never;

    const err = await registerAdminDomain("tenant-1", { host: "app.x.com", rowVersion: 1 }).catch((e) => e);
    expect(domainErrorCode(err)).toBe("CONCURRENCY_CONFLICT");
    expect((err as ApiError).message).toMatch(/alguien más modificó/i);
  });

  it("409 DOMAIN_HOST_ALREADY_REGISTERED — mensaje de otra red", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "DOMAIN_HOST_ALREADY_REGISTERED" }), { status: 409 }),
    ) as never;

    const err = await registerAdminDomain("tenant-1", { host: "app.x.com" }).catch((e) => e);
    expect(domainErrorMessage(domainErrorCode(err))).toMatch(/otra red/i);
  });
});

describe("removeAdminDomain — DELETE .../domain (204)", () => {
  it("hace DELETE contra la base admin", async () => {
    let capturedUrl = "";
    let capturedMethod = "";
    global.fetch = vi.fn(async (url: string | URL, init?: RequestInit) => {
      capturedUrl = url.toString();
      capturedMethod = init?.method ?? "";
      return new Response(null, { status: 204 });
    }) as never;

    await expect(removeAdminDomain("tenant-42")).resolves.toBeUndefined();
    expect(capturedUrl).toContain("/api/v1/admin/companies/tenant-42/domain");
    expect(capturedMethod).toBe("DELETE");
  });
});

describe("verifyAdminDomain / verifyCompanyDomain — POST .../domain/verify (HU #12425 AC2)", () => {
  it("verifyAdminDomain hace POST y devuelve el estado resultante", async () => {
    let capturedUrl = "";
    let capturedMethod = "";
    global.fetch = vi.fn(async (url: string | URL, init?: RequestInit) => {
      capturedUrl = url.toString();
      capturedMethod = init?.method ?? "";
      return new Response(JSON.stringify(domain({ status: "verified" })), { status: 200 });
    }) as never;

    const result = await verifyAdminDomain("tenant-42");
    expect(capturedUrl).toContain("/api/v1/admin/companies/tenant-42/domain/verify");
    expect(capturedMethod).toBe("POST");
    expect(result.status).toBe("verified");
  });

  it("verifyCompanyDomain usa /api/v1/company/domain/verify", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(domain({ status: "failed", statusReason: "TXT_MISMATCH" })), { status: 200 });
    }) as never;

    const result = await verifyCompanyDomain();
    expect(capturedUrl).toContain("/api/v1/company/domain/verify");
    expect(result.status).toBe("failed");
    expect(result.statusReason).toBe("TXT_MISMATCH");
  });

  it("429 DOMAIN_VERIFICATION_COOLDOWN con Retry-After: mensaje incluye los segundos", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "DOMAIN_VERIFICATION_COOLDOWN" }), {
        status: 429,
        headers: { "Retry-After": "45" },
      }),
    ) as never;

    const err = await verifyAdminDomain("tenant-1").catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(429);
    expect((err as ApiError).message).toMatch(/45 s/);
    expect(cooldownSecondsFromError(err)).toBeNull(); // el body no trae retryAfterSeconds, solo la cabecera
  });

  it("429 con retryAfterSeconds en el cuerpo: cooldownSecondsFromError lo expone", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "DOMAIN_VERIFICATION_COOLDOWN", retryAfterSeconds: 30 }), { status: 429 }),
    ) as never;

    const err = await verifyCompanyDomain().catch((e) => e);
    expect(cooldownSecondsFromError(err)).toBe(30);
  });

  it("429 sin Retry-After ni retryAfterSeconds: mensaje genérico", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "DOMAIN_VERIFICATION_COOLDOWN" }), { status: 429 }),
    ) as never;

    const err = await verifyAdminDomain("tenant-1").catch((e) => e);
    expect((err as ApiError).message).toMatch(/pidió una comprobación hace poco/i);
  });
});

describe("isValidHostFormat — validación básica RFC 1123 en cliente", () => {
  it("acepta hosts válidos en minúsculas", () => {
    expect(isValidHostFormat("app.movilidadandina.com")).toBe(true);
    expect(isValidHostFormat("sub.dominio.co")).toBe(true);
  });

  it("rechaza mayúsculas, esquema, puerto o ruta", () => {
    expect(isValidHostFormat("App.Movilidad.com")).toBe(false);
    expect(isValidHostFormat("https://app.movilidad.com")).toBe(false);
    expect(isValidHostFormat("app.movilidad.com:8080")).toBe(false);
    expect(isValidHostFormat("app.movilidad.com/ruta")).toBe(false);
  });

  it("rechaza vacío, sin punto o con guiones al borde de una etiqueta", () => {
    expect(isValidHostFormat("")).toBe(false);
    expect(isValidHostFormat("localhost")).toBe(false);
    expect(isValidHostFormat("-app.movilidad.com")).toBe(false);
    expect(isValidHostFormat("app-.movilidad.com")).toBe(false);
  });
});
