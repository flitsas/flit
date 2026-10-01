// HU #13140 (Feature F3 #13115, épica #13090) — desactivar, reactivar y eliminar un mandatario con
// advertencia previa de impacto. Un bloque por criterio de aceptación.
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

const fetchCompanyMandateSigners = vi.fn();
const fetchImpact = vi.fn();
const deleteSigner = vi.fn();
const inactivateSigner = vi.fn();
const reactivateSigner = vi.fn();

vi.mock("@/lib/api/admin-mandate-signers", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-mandate-signers")>(
    "@/lib/api/admin-mandate-signers",
  );
  return {
    ...actual,
    fetchCompanyMandateSigners: (...a: unknown[]) => fetchCompanyMandateSigners(...a),
    fetchCompanyTransitOffices: vi
      .fn()
      .mockResolvedValue([{ transitOfficeId: "ot-1", code: "05001000", name: "Tránsito de Medellín" }]),
    fetchRepresentedCompanies: vi.fn().mockResolvedValue([]),
    createCompanyMandateSigner: vi.fn(),
    updateCompanyMandateSigner: vi.fn(),
    fetchCompanyMandateSignerImpact: (...a: unknown[]) => fetchImpact(...a),
    deleteCompanyMandateSigner: (...a: unknown[]) => deleteSigner(...a),
    inactivateCompanyMandateSigner: (...a: unknown[]) => inactivateSigner(...a),
    reactivateCompanyMandateSigner: (...a: unknown[]) => reactivateSigner(...a),
  };
});
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

import { CompanyMandatariosPanel } from "../CompanyMandatariosPanel";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  return `${header}.${Buffer.from(JSON.stringify(payload)).toString("base64url")}.`;
}

function signer(overrides: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id: "ms-1",
    transitOfficeId: "ot-1",
    fullName: "Ana Restrepo",
    documentType: "CC",
    documentNumber: "1020304050",
    integrityHash: "a".repeat(64),
    email: null,
    userId: null,
    identityStatus: "valid",
    signatureVaultId: "v-1",
    registeredAt: "2026-08-01T10:00:00Z",
    isActive: true,
    companyTenantIds: ["t-1"],
    transitOfficeIds: ["ot-1"],
    signerModel: "natural",
    validityKind: "fixed",
    validityStatus: "vigente",
    origin: "compania",
    puedeEditar: true,
    puedeEliminar: true,
    ...overrides,
  };
}

const SIN_IMPACTO = { hasImpact: false, onlyActiveFor: [], defaults: [], pendingProcedures: 0 };
const CON_IMPACTO = {
  hasImpact: true,
  onlyActiveFor: [{ transitOfficeId: "ot-1", companyTenantId: "t-1" }],
  defaults: [{ kind: "company_rule", transitOfficeId: "ot-1", companyTenantId: "t-1" }],
  pendingProcedures: 3,
};

function renderPanel() {
  return render(
    <ToastProvider>
      <CompanyMandatariosPanel tenantId="t-1" />
    </ToastProvider>,
  );
}

async function abrir(user: ReturnType<typeof userEvent.setup>, nombre: RegExp) {
  await screen.findByRole("table", { name: "Mandatarios de la compañía" });
  await user.click(screen.getByRole("button", { name: nombre }));
  return screen.findByRole("dialog");
}

beforeEach(() => {
  for (const f of [fetchCompanyMandateSigners, fetchImpact, deleteSigner, inactivateSigner, reactivateSigner]) {
    f.mockReset();
  }
  fetchCompanyMandateSigners.mockResolvedValue([signer()]);
  window.localStorage.clear();
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({
      sub: "u-1",
      exp: Math.floor(Date.now() / 1000) + 3600,
      role: "AdminCompany",
      roles: [{ id: "r", code: "AdminCompany" }],
    }),
  );
});
afterEach(() => window.localStorage.clear());

describe("HU #13140 — desactivar, reactivar y eliminar", () => {
  it("AC1: eliminar un único activo lista la compañía y el organismo afectados y exige confirmar", async () => {
    fetchImpact.mockResolvedValue(CON_IMPACTO);
    deleteSigner.mockResolvedValue({ reassigned: 0, pendingOtDecision: 0 });
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /eliminar mandatario ana restrepo/i);
    const impacto = await within(dialogo).findByTestId("mandatario-baja-impacto");
    expect(impacto).toHaveTextContent("Esta compañía en Tránsito de Medellín");
    expect(impacto).toHaveTextContent(/default/i);
    expect(impacto).toHaveTextContent("3 trámites radicados sin aprobar");
    expect(impacto).toHaveTextContent(/se reasignan con la prelación/i);
    expect(impacto).toHaveTextContent(/si no queda nadie, el ot decide al aprobar/i);
    expect(impacto).toHaveTextContent(/se conserva el historial/i);
    // Aún no se envió nada.
    expect(deleteSigner).not.toHaveBeenCalled();
    await user.click(within(dialogo).getByRole("button", { name: /sí, eliminar/i }));
    await waitFor(() => expect(deleteSigner).toHaveBeenCalledTimes(1));
    // Con impacto, la confirmación viaja como confirmarImpacto=true.
    expect(deleteSigner).toHaveBeenCalledWith("t-1", "ms-1", true, undefined);
  });

  it("AC2: sin impacto, desactivar pide una confirmación corta y al aceptar desactiva", async () => {
    fetchImpact.mockResolvedValue(SIN_IMPACTO);
    inactivateSigner.mockResolvedValue({ reassigned: 0, pendingOtDecision: 0 });
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /desactivar mandatario ana restrepo/i);
    expect(await within(dialogo).findByText(/¿desactivar a/i)).toBeInTheDocument();
    expect(within(dialogo).queryByTestId("mandatario-baja-impacto")).not.toBeInTheDocument();
    await user.click(within(dialogo).getByRole("button", { name: "Desactivar" }));
    await waitFor(() => expect(inactivateSigner).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(/ana restrepo quedó desactivado/i)).toBeInTheDocument();
  });

  it("AC2b: eliminar sin impacto aclara que se conserva el historial y envía confirmarImpacto=false", async () => {
    fetchImpact.mockResolvedValue(SIN_IMPACTO);
    deleteSigner.mockResolvedValue({ reassigned: 0, pendingOtDecision: 0 });
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /eliminar mandatario ana restrepo/i);
    expect(await within(dialogo).findByText(/se conserva su historial/i)).toBeInTheDocument();
    await user.click(within(dialogo).getByRole("button", { name: "Eliminar" }));
    await waitFor(() => expect(deleteSigner).toHaveBeenCalledWith("t-1", "ms-1", false, undefined));
  });

  it("AC3: cancelar no envía ninguna baja y la lista no cambia", async () => {
    fetchImpact.mockResolvedValue(CON_IMPACTO);
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /eliminar mandatario ana restrepo/i);
    await within(dialogo).findByTestId("mandatario-baja-impacto");
    await user.click(within(dialogo).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(deleteSigner).not.toHaveBeenCalled();
    expect(inactivateSigner).not.toHaveBeenCalled();
    expect(fetchCompanyMandateSigners).toHaveBeenCalledTimes(1);
  });

  it("AC4: informa cuántos trámites se reasignaron y cuántos quedan para el OT", async () => {
    fetchImpact.mockResolvedValue(CON_IMPACTO);
    deleteSigner.mockResolvedValue({ reassigned: 2, pendingOtDecision: 1 });
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /eliminar mandatario ana restrepo/i);
    await user.click(await within(dialogo).findByRole("button", { name: /sí, eliminar/i }));
    expect(await screen.findByText(/se reasignaron 2 trámites/i)).toBeInTheDocument();
    expect(screen.getByText(/queda 1 trámite para que el ot decida al aprobar/i)).toBeInTheDocument();
  });

  it("AC5: reactivar vuelve a activo y, si ya había otro vigente, avisa que no lo desplazó", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({ isActive: false, validityStatus: "inactivo" }),
    ]);
    reactivateSigner.mockResolvedValue({
      restoredLinks: [],
      conflictLinks: [{ transitOfficeId: "ot-1", companyTenantId: "t-1" }],
      restoredDefaults: 0,
    });
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", { name: "Mandatarios de la compañía" });
    await user.click(screen.getByRole("button", { name: /reactivar mandatario ana restrepo/i }));
    expect(await screen.findByText(/no desplazó al mandatario vigente/i)).toBeInTheDocument();
    expect(reactivateSigner).toHaveBeenCalledWith("t-1", "ms-1", undefined);
  });

  it("AC5b: reactivar sin conflicto solo confirma que vuelve a estar activo", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([signer({ isActive: false, validityStatus: "inactivo" })]);
    reactivateSigner.mockResolvedValue({ restoredLinks: [], conflictLinks: [], restoredDefaults: 1 });
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", { name: "Mandatarios de la compañía" });
    await user.click(screen.getByRole("button", { name: /reactivar mandatario ana restrepo/i }));
    expect(await screen.findByText("Ana Restrepo vuelve a estar activo.")).toBeInTheDocument();
  });

  it("AC5c: 409 mandatario_activo_existente al reactivar se muestra con un mensaje claro", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([signer({ isActive: false, validityStatus: "inactivo" })]);
    reactivateSigner.mockRejectedValue(
      new ApiError(409, "conflicto", { code: "mandatario_activo_existente", error: "x" }),
    );
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", { name: "Mandatarios de la compañía" });
    await user.click(screen.getByRole("button", { name: /reactivar mandatario ana restrepo/i }));
    expect(await screen.findByText(/ya hay otro mandatario activo para esa compañía y organismo/i)).toBeInTheDocument();
  });

  it("AC6: un 403 al confirmar muestra la falta de permiso y la fila no cambia", async () => {
    fetchImpact.mockResolvedValue(SIN_IMPACTO);
    deleteSigner.mockRejectedValue(new ApiError(403, "prohibido", { code: "mandatario_sin_permiso" }));
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /eliminar mandatario ana restrepo/i);
    await user.click(await within(dialogo).findByRole("button", { name: "Eliminar" }));
    expect(await within(dialogo).findByTestId("mandatario-baja-error")).toHaveTextContent(
      "No tienes permiso para esta acción.",
    );
    // El diálogo sigue abierto y la lista no se recargó.
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(fetchCompanyMandateSigners).toHaveBeenCalledTimes(1);
  });

  it("AC6b: un 409 de confirmación requerida refresca el impacto y pide confirmar de nuevo", async () => {
    fetchImpact.mockResolvedValue(SIN_IMPACTO);
    deleteSigner.mockRejectedValueOnce(
      new ApiError(409, "confirmar", {
        code: "mandatario_baja_requiere_confirmacion",
        impact: CON_IMPACTO,
      }),
    );
    deleteSigner.mockResolvedValue({ reassigned: 0, pendingOtDecision: 0 });
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /eliminar mandatario ana restrepo/i);
    await user.click(await within(dialogo).findByRole("button", { name: "Eliminar" }));
    expect(await within(dialogo).findByTestId("mandatario-baja-impacto")).toBeInTheDocument();
    expect(within(dialogo).getByTestId("mandatario-baja-error")).toHaveTextContent(/el impacto cambió/i);
    await user.click(within(dialogo).getByRole("button", { name: /sí, eliminar/i }));
    await waitFor(() => expect(deleteSigner).toHaveBeenLastCalledWith("t-1", "ms-1", true, undefined));
  });

  it("AC7: el doble clic en confirmar envía una sola solicitud", async () => {
    fetchImpact.mockResolvedValue(SIN_IMPACTO);
    let resolver: (v: unknown) => void = () => undefined;
    deleteSigner.mockReturnValue(new Promise((r) => (resolver = r)));
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /eliminar mandatario ana restrepo/i);
    const boton = await within(dialogo).findByRole("button", { name: "Eliminar" });
    await user.dblClick(boton);
    expect(deleteSigner).toHaveBeenCalledTimes(1);
    resolver({ reassigned: 0, pendingOtDecision: 0 });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("borde: error al consultar el impacto ofrece reintentar y no permite confirmar", async () => {
    fetchImpact.mockRejectedValueOnce(new Error("boom")).mockResolvedValueOnce(SIN_IMPACTO);
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /eliminar mandatario ana restrepo/i);
    expect(await within(dialogo).findByText(/no se pudo revisar el impacto/i)).toBeInTheDocument();
    expect(within(dialogo).getByRole("button", { name: "Eliminar" })).toBeDisabled();
    await user.click(within(dialogo).getByRole("button", { name: /reintentar/i }));
    await waitFor(() => expect(within(dialogo).getByRole("button", { name: "Eliminar" })).toBeEnabled());
  });

  it("sin la bandera puedeEliminar no se ofrece eliminar, pero sí desactivar", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([signer({ puedeEliminar: false })]);
    renderPanel();
    await screen.findByRole("table", { name: "Mandatarios de la compañía" });
    expect(screen.queryByRole("button", { name: /eliminar mandatario/i })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /desactivar mandatario/i })).toBeInTheDocument();
  });
});
