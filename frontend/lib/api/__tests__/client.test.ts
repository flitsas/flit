import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { ApiError, ApiValidationError } from "../types";
import { SESSION_EXPIRED_EVENT } from "@/lib/auth/session";

// Uso de ejemplo:
// const data = await apiFetch<T>("/api/v1/foo", { method: "POST", body: {...} });
// Ante 4xx/5xx no-ok lanza ApiError cuyo `message` sale del ProblemDetails del backend
// (detail → title → genérico), nunca de la ruta/status técnicos (Bug #11626).
import { apiFetch, friendlyErrorMessage } from "../client";

const originalFetch = global.fetch;

beforeEach(() => {
  vi.clearAllMocks();
  document.cookie = "flit_token=; path=/; Max-Age=0";
  window.localStorage.clear();
});

afterEach(() => {
  global.fetch = originalFetch;
});

function mockFetchOnce(status: number, body: unknown, headers: Record<string, string> = {}): void {
  const text = body === null ? "" : JSON.stringify(body);
  global.fetch = vi.fn().mockResolvedValue(
    new Response(text, { status, headers: { "Content-Type": "application/json", ...headers } }),
  ) as never;
}

describe("apiFetch — errores no-ok (Bug #11626)", () => {
  it("403 con detail: el mensaje es el detail del backend, sin ruta ni status", async () => {
    mockFetchOnce(403, {
      title: "Forbidden",
      detail: "La preasignación no está habilitada entre la compañía y el OT.",
    });

    await expect(apiFetch("/api/v1/admin/plate-ranges/assign")).rejects.toMatchObject({
      status: 403,
      message: "La preasignación no está habilitada entre la compañía y el OT.",
    });
  });

  it("403 sin cuerpo: cae al mensaje genérico en español, nunca a 'Error 403 al llamar ...'", async () => {
    mockFetchOnce(403, null);

    let caught: unknown;
    try {
      await apiFetch("/api/v1/admin/plate-ranges/assign");
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    const err = caught as ApiError;
    expect(err.status).toBe(403);
    expect(err.message).toBe("No se pudo completar la solicitud. Inténtalo de nuevo.");
    expect(err.message).not.toMatch(/al llamar/);
    expect(err.message).not.toContain("/api/v1/admin/plate-ranges/assign");
  });

  it("403 solo con title (sin detail): usa el title como mensaje", async () => {
    mockFetchOnce(403, { title: "No autorizado para esta operación." });

    await expect(apiFetch("/api/v1/admin/companies/1")).rejects.toMatchObject({
      status: 403,
      message: "No autorizado para esta operación.",
    });
  });

  it("409 con data útil: el ApiError conserva el body para que el caller lea procedureTypes", async () => {
    const body = { detail: "No se puede desactivar: hay tipos de trámite en uso.", procedureTypes: ["MATRICULA"] };
    mockFetchOnce(409, body);

    let caught: unknown;
    try {
      await apiFetch("/api/v1/admin/procedure-types/1");
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    const err = caught as ApiError;
    expect(err.status).toBe(409);
    expect(err.message).toBe("No se puede desactivar: hay tipos de trámite en uso.");
    expect(err.body).toEqual(body);
  });

  it("422 con detail (ProblemDetails): lanza ApiError con ese detail, no ApiValidationError", async () => {
    mockFetchOnce(422, { detail: "La placa ya está asignada a otro trámite." });

    let caught: unknown;
    try {
      await apiFetch("/api/v1/admin/plate-ranges/assign");
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    expect((caught as ApiError).status).toBe(422);
    expect((caught as ApiError).message).toBe("La placa ya está asignada a otro trámite.");
  });

  it("422 con errors[] (diccionario de validación): lanza ApiValidationError", async () => {
    mockFetchOnce(422, { errors: [{ field: "nit", message: "Formato inválido" }] });

    let caught: unknown;
    try {
      await apiFetch("/api/v1/admin/companies");
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiValidationError);
    expect((caught as ApiValidationError).errors).toEqual([{ field: "nit", message: "Formato inválido" }]);
  });

  it("401 SESSION_EXPIRED: limpia el token, emite el evento global y preserva el mensaje 'SESSION_EXPIRED'", async () => {
    document.cookie = "flit_token=jwt-viejo; path=/";
    mockFetchOnce(401, { code: "SESSION_EXPIRED" });

    const listener = vi.fn();
    window.addEventListener(SESSION_EXPIRED_EVENT, listener);

    let caught: unknown;
    try {
      await apiFetch("/api/v1/admin/companies");
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    expect((caught as ApiError).status).toBe(401);
    expect((caught as ApiError).message).toBe("SESSION_EXPIRED");
    expect(listener).toHaveBeenCalledOnce();
    expect(document.cookie).not.toContain("flit_token=jwt-viejo");

    window.removeEventListener(SESSION_EXPIRED_EVENT, listener);
  });
});

// Uso de ejemplo: friendlyErrorMessage({ detail: "..." }) → "..."
// Helper compartido por apiFetch Y por los clientes ad-hoc que no pueden usarlo
// (multipart/descargas binarias: download.ts, admin-ot.ts, admin-plataforma-mandatos.ts).
describe("friendlyErrorMessage — precedencia error > detail > title > genérico (Bug #11626)", () => {
  it("prioriza { error } sobre { detail } y { title } cuando los tres vienen", () => {
    expect(
      friendlyErrorMessage({ error: "motivo de negocio", detail: "detalle técnico", title: "Conflict" }),
    ).toBe("motivo de negocio");
  });

  it("usa { detail } cuando no hay { error }", () => {
    expect(friendlyErrorMessage({ detail: "La placa ya está asignada.", title: "Unprocessable" })).toBe(
      "La placa ya está asignada.",
    );
  });

  it("cae a { title } cuando faltan { error } y { detail }", () => {
    expect(friendlyErrorMessage({ title: "No autorizado para esta operación." })).toBe(
      "No autorizado para esta operación.",
    );
  });

  it("cae al mensaje genérico en español cuando no hay body ni campos útiles", () => {
    expect(friendlyErrorMessage(null)).toBe("No se pudo completar la solicitud. Inténtalo de nuevo.");
    expect(friendlyErrorMessage(undefined)).toBe("No se pudo completar la solicitud. Inténtalo de nuevo.");
    expect(friendlyErrorMessage({})).toBe("No se pudo completar la solicitud. Inténtalo de nuevo.");
  });

  it("ignora candidatos vacíos/blancos y no numéricos, y sigue la cadena de fallback", () => {
    expect(friendlyErrorMessage({ error: "  ", detail: "", title: "Motivo real" })).toBe("Motivo real");
    expect(friendlyErrorMessage({ error: 500 as unknown as string, detail: "Motivo del backend" })).toBe(
      "Motivo del backend",
    );
  });

  it("nunca produce un mensaje que contenga una ruta interna del API", () => {
    const message = friendlyErrorMessage({ path: "/api/v1/admin/plate-ranges/assign" } as never);
    expect(message).not.toContain("/api/v1/");
    expect(message).toBe("No se pudo completar la solicitud. Inténtalo de nuevo.");
  });
});
