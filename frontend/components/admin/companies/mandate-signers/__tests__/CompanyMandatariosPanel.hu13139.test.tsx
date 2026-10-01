// HU #13139 (Feature F3 #13115, épica #13090) — lista de mandatarios con estados visuales, candado del
// organismo y acciones por rol. Un bloque por criterio de aceptación.
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

const fetchCompanyMandateSigners = vi.fn();

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchCompanyMandateSigners: (...a: unknown[]) => fetchCompanyMandateSigners(...a),
  fetchCompanyTransitOffices: vi
    .fn()
    .mockResolvedValue([{ transitOfficeId: "ot-1", code: "05001000", name: "Tránsito de Medellín" }]),
  fetchRepresentedCompanies: vi.fn().mockResolvedValue([]),
  createCompanyMandateSigner: vi.fn(),
  updateCompanyMandateSigner: vi.fn(),
  inactivateCompanyMandateSigner: vi.fn(),
  reactivateCompanyMandateSigner: vi.fn(),
}));
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

import { CompanyMandatariosPanel } from "../CompanyMandatariosPanel";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  return `${header}.${Buffer.from(JSON.stringify(payload)).toString("base64url")}.`;
}
function loginAs(roleCode: string) {
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({
      sub: "u-1",
      exp: Math.floor(Date.now() / 1000) + 3600,
      role: roleCode,
      roles: [{ id: "r", code: roleCode }],
    }),
  );
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

function renderPanel() {
  return render(
    <ToastProvider>
      <CompanyMandatariosPanel tenantId="t-1" />
    </ToastProvider>,
  );
}
const TABLA = { name: "Mandatarios de la compañía" };
const fila = (nombre: string) => screen.getByText(nombre).closest("tr") as HTMLElement;

beforeEach(() => {
  fetchCompanyMandateSigners.mockReset();
  window.localStorage.clear();
  loginAs("AdminCompany");
});
afterEach(() => window.localStorage.clear());

const DELEGADO_ORGANISMO = { origin: "organismo", puedeEditar: false, puedeEliminar: false } as const;

describe("HU #13139 — lista con estados, candado y acciones por rol", () => {
  it("AC1: el Admin de Compañía ve el de origen organismo en gris, con candado, leyenda y sin acciones", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({ id: "1", fullName: "Del Organismo", ...DELEGADO_ORGANISMO }),
      signer({ id: "2", fullName: "Propio" }),
    ]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    const f = fila("Del Organismo");
    expect(f).toHaveAttribute("data-candado", "true");
    expect(within(f).getByTestId("mandatario-candado")).toHaveTextContent(
      "Configurado por el organismo de tránsito",
    );
    expect(within(f).queryByRole("button")).not.toBeInTheDocument();
    // El propio conserva sus acciones y no lleva candado.
    expect(within(fila("Propio")).queryByTestId("mandatario-candado")).not.toBeInTheDocument();
    expect(within(fila("Propio")).getByRole("button", { name: /editar mandatario propio/i })).toBeInTheDocument();
  });

  it("AC2: con las banderas del Admin OT (puede editar) la misma fila conserva sus acciones y no se bloquea", async () => {
    loginAs("ot_admin");
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({ fullName: "Del Organismo", origin: "organismo", puedeEditar: true, puedeEliminar: true }),
    ]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    const f = fila("Del Organismo");
    expect(f).not.toHaveAttribute("data-candado");
    expect(within(f).queryByTestId("mandatario-candado")).not.toBeInTheDocument();
    expect(within(f).getByRole("button", { name: /editar mandatario/i })).toBeInTheDocument();
  });

  it("AC3: verde, naranja, rojo y rojo con los textos Vigente, Por vencer, Vencido e Inactivo", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({ id: "1", fullName: "Ana Vigente" }),
      signer({ id: "2", fullName: "Beto Porvencer", validityStatus: "por_vencer" }),
      signer({ id: "3", fullName: "Carla Vencida", validityStatus: "vencido" }),
      signer({ id: "4", fullName: "Dario Inactivo", validityStatus: "inactivo", isActive: false }),
    ]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    const est = (n: string) => within(fila(n)).getByRole("status") as HTMLElement;
    expect(
      [est("Ana Vigente"), est("Beto Porvencer"), est("Carla Vencida"), est("Dario Inactivo")].map(
        (e) => e.style.color,
      ),
    ).toEqual([
      "var(--badge-success-fg)",
      "var(--badge-warning-fg)",
      "var(--badge-danger-fg)",
      "var(--badge-danger-fg)",
    ]);
    expect(est("Ana Vigente")).toHaveTextContent("Vigente");
    expect(est("Beto Porvencer")).toHaveTextContent("Por vencer");
    expect(est("Carla Vencida")).toHaveTextContent("Vencido");
    expect(est("Dario Inactivo")).toHaveTextContent("Inactivo");
  });

  it("AC5: el Gestor/Radicador no ve crear, editar ni eliminar", async () => {
    loginAs("gestor_tramites");
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({ fullName: "Ana Restrepo", puedeEditar: false, puedeEliminar: false }),
    ]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    expect(screen.queryByRole("button", { name: /nuevo mandatario/i })).not.toBeInTheDocument();
    expect(within(fila("Ana Restrepo")).queryByRole("button")).not.toBeInTheDocument();
  });

  it("AC6: con más de 10 mandatarios pagina y muestra el loader mientras carga", async () => {
    fetchCompanyMandateSigners.mockResolvedValue(
      Array.from({ length: 12 }, (_, i) =>
        signer({ id: `m${i}`, fullName: `Mandatario ${String(i).padStart(2, "0")}` }),
      ),
    );
    const user = userEvent.setup();
    renderPanel();
    expect(screen.getByText(/cargando mandatarios/i)).toBeInTheDocument();
    await screen.findByRole("table", TABLA);
    expect(screen.getByText("Mandatario 09")).toBeInTheDocument();
    expect(screen.queryByText("Mandatario 10")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /página 2|^2$/i }));
    expect(await screen.findByText("Mandatario 10")).toBeInTheDocument();
    expect(screen.queryByText("Mandatario 00")).not.toBeInTheDocument();
  });

  it("AC7: sin mandatarios muestra el estado vacío; ante un error ofrece reintentar", async () => {
    fetchCompanyMandateSigners.mockResolvedValueOnce([]);
    const { unmount } = renderPanel();
    expect(await screen.findByText(/no tiene mandatarios registrados/i)).toBeInTheDocument();
    unmount();

    fetchCompanyMandateSigners.mockRejectedValueOnce(new Error("boom")).mockResolvedValueOnce([signer()]);
    const user = userEvent.setup();
    renderPanel();
    expect(await screen.findByText(/no se pudieron cargar los mandatarios/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /reintentar/i }));
    await waitFor(() => expect(screen.getByRole("table", TABLA)).toBeInTheDocument());
  });
});
