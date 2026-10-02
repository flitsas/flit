// HU #13140 — cliente de baja, impacto y reactivación de mandatarios.
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  deleteCompanyMandateSigner,
  fetchCompanyMandateSignerImpact,
  impactFromConfirmationError,
  inactivateMandateSigner,
  reactivateCompanyMandateSigner,
} from "../admin-mandate-signers";
import { ApiError } from "../types";

function respuesta(status: number, body: unknown, headers: Record<string, string> = {}) {
  return new Response(body === null ? null : JSON.stringify(body), { status, headers });
}

afterEach(() => vi.restoreAllMocks());

describe("HU #13140 — cliente de mandatarios", () => {
  it("DELETE envía confirmarImpacto y lee los conteos de reasignación de las cabeceras del 204", async () => {
    const f = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(null, {
        status: 204,
        headers: { "X-Mandatario-Reasignados": "4", "X-Mandatario-Pendientes-Decision-OT": "2" },
      }),
    );
    const outcome = await deleteCompanyMandateSigner("t-1", "ms-1", true);
    expect(outcome).toEqual({ reassigned: 4, pendingOtDecision: 2 });
    const [url, init] = f.mock.calls[0];
    expect(String(url)).toContain("/api/v1/admin/companies/t-1/mandate-signers/ms-1");
    expect(String(url)).toContain("confirmarImpacto=true");
    expect((init as RequestInit).method).toBe("DELETE");
  });

  it("sin cabeceras los conteos son cero", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
    expect(await inactivateMandateSigner("ot-1", "ms-1")).toEqual({ reassigned: 0, pendingOtDecision: 0 });
  });

  it("el impacto se normaliza y la ruta de red usa la cabeza", async () => {
    const f = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(respuesta(200, { data: { hasImpact: true, pendingProcedures: 2 } }));
    const impact = await fetchCompanyMandateSignerImpact("hija", "ms-1", undefined, "cabeza");
    expect(impact).toEqual({ hasImpact: true, onlyActiveFor: [], defaults: [], pendingProcedures: 2 });
    expect(String(f.mock.calls[0][0])).toContain("/companies/cabeza/children/hija/mandate-signers/ms-1/impact");
  });

  it("reactivar devuelve el detalle del 200", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      respuesta(200, { restoredLinks: [], conflictLinks: [{ transitOfficeId: "o", companyTenantId: "c" }], restoredDefaults: 0 }),
    );
    const r = await reactivateCompanyMandateSigner("t-1", "ms-1");
    expect(r.conflictLinks).toHaveLength(1);
  });

  it("el 409 de confirmación requerida trae el impacto; otro 409 no", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      respuesta(409, {
        code: "mandatario_baja_requiere_confirmacion",
        error: "confirme",
        impact: { hasImpact: true, pendingProcedures: 1 },
      }),
    );
    const err = await deleteCompanyMandateSigner("t-1", "ms-1", false).catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(impactFromConfirmationError(err)?.pendingProcedures).toBe(1);
    expect(impactFromConfirmationError(new ApiError(409, "x", { code: "mandatario_activo_existente" }))).toBeNull();
  });
});
