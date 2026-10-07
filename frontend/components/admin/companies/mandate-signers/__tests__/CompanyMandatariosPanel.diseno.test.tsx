// Mejora de diseño — Mandatarios: búsqueda, resumen, acciones como iconos (RowActions) y «Filas por página».
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

const fetchCompanyMandateSigners = vi.fn();

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

function signer(i: number, overrides: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id: `ms-${i}`,
    transitOfficeId: "ot-1",
    fullName: `Mandatario ${i}`,
    documentType: "CC",
    documentNumber: `20000000${String(i).padStart(2, "0")}`,
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

function renderPanel() {
  return render(
    <ToastProvider>
      <CompanyMandatariosPanel tenantId="t-1" />
    </ToastProvider>,
  );
}

beforeEach(() => {
  fetchCompanyMandateSigners.mockReset();
  fetchCompanyMandateSigners.mockResolvedValue(
    Array.from({ length: 12 }, (_, i) =>
      signer(
        i + 1,
        i === 0 ? { signatureMethod: "biometria", signatureVaultId: null, identityStatus: "none" } : {},
      ),
    ),
  );
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

describe("CompanyMandatariosPanel — diseño", () => {
  it("resume el total y las validaciones sin aprobar", async () => {
    renderPanel();
    await screen.findByRole("table", { name: "Mandatarios de la compañía" });
    expect(screen.getByTestId("mandatarios-resumen")).toHaveTextContent(
      "12 mandatarios · 1 sin validación aprobada",
    );
  });

  it("pagina con «Filas por página»", async () => {
    renderPanel();
    await screen.findByRole("table", { name: "Mandatarios de la compañía" });
    expect(screen.getByText("Mandatario 10")).toBeInTheDocument();
    expect(screen.queryByText("Mandatario 11")).not.toBeInTheDocument();
    expect(screen.getByText(/filas por página/i)).toBeInTheDocument();
  });

  it("la búsqueda filtra por nombre o documento", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", { name: "Mandatarios de la compañía" });
    const caja = screen.getByRole("textbox", { name: "Buscar mandatarios" });
    await user.type(caja, "mandatario 12");
    expect(screen.getByText("Mandatario 12")).toBeInTheDocument();
    expect(screen.queryByText("Mandatario 1")).not.toBeInTheDocument();
    await user.clear(caja);
    await user.type(caja, "2000000005");
    expect(screen.getByText("Mandatario 5")).toBeInTheDocument();
    expect(screen.queryByText("Mandatario 6")).not.toBeInTheDocument();
    await user.clear(caja);
    await user.type(caja, "zzz");
    expect(screen.getByText(/ningún mandatario coincide/i)).toBeInTheDocument();
  });

  it("las acciones son iconos con nombre accesible y sin menú «Acciones»", async () => {
    renderPanel();
    await screen.findByRole("table", { name: "Mandatarios de la compañía" });
    const fila = screen.getByText("Mandatario 2").closest("tr") as HTMLElement;
    expect(within(fila).queryByRole("button", { name: /^acciones de/i })).not.toBeInTheDocument();
    for (const nombre of [
      "Editar mandatario Mandatario 2",
      "Desactivar mandatario Mandatario 2",
      "Eliminar mandatario Mandatario 2",
    ]) {
      expect(within(fila).getByRole("button", { name: nombre })).toHaveAttribute("title", nombre);
    }
    const fila1 = screen.getByText("Mandatario 1").closest("tr") as HTMLElement;
    expect(
      within(fila1).getByRole("button", { name: "Reenviar validación a Mandatario 1" }),
    ).toBeInTheDocument();
  });
});
