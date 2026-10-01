// HU #13133 (Feature F2 #13114, épica #13090) — lista de mandatarios de la compañía sin firma física
// y con modelo y estado de vigencia. Un bloque por criterio de aceptación.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { ToastProvider } from "@/components/admin/Toast";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";

const fetchCompanyMandateSigners = vi.fn();

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchCompanyMandateSigners: (...a: unknown[]) => fetchCompanyMandateSigners(...a),
  fetchCompanyTransitOffices: vi
    .fn()
    .mockResolvedValue([{ transitOfficeId: "ot-1", code: "05001000", name: "Tránsito de Medellín" }]),
  fetchCompanyAssociableCompanies: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100, aplicaSoloASuCompania: true }),
  fetchOtAssociableCompanies: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 10, aplicaSoloASuCompania: false }),
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

const fila = (nombre: string) => screen.getByText(nombre).closest("tr") as HTMLElement;
const TABLA = { name: "Mandatarios de la compañía" };

beforeEach(() => {
  fetchCompanyMandateSigners.mockReset();
});

describe("HU #13133 — lista de mandatarios de la compañía", () => {
  it("AC1: sin opción de firma física en la lista ni en sus avisos", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({ identityStatus: "none", signatureVaultId: null }),
    ]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    expect(screen.queryByText(/firma f[ií]sica|a mano|forma f[ií]sica/i)).not.toBeInTheDocument();
    // Sin medio de firma se avisa que no puede firmar: ya no hay organismo exento.
    expect(screen.getByText(/no puede firmar en tránsito de medellín/i)).toBeInTheDocument();
  });

  it("AC2: cada mandatario muestra su modelo y una etiqueta con su estado de vigencia", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({ id: "1", fullName: "Ana Vigente" }),
      signer({
        id: "2",
        fullName: "Beto Porvencer",
        validityStatus: "por_vencer",
        validityKind: "range",
        validTo: "2026-10-05",
      }),
      signer({ id: "3", fullName: "Carla Vencida", validityStatus: "vencido" }),
      signer({ id: "4", fullName: "Dario Inactivo", validityStatus: "inactivo", isActive: false }),
    ]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    expect(within(fila("Ana Vigente")).getByText("Persona natural")).toBeInTheDocument();
    expect(within(fila("Ana Vigente")).getByTestId("mandatario-vigencia")).toHaveTextContent("Vigente");
    const porVencer = within(fila("Beto Porvencer")).getByTestId("mandatario-vigencia");
    expect(porVencer).toHaveTextContent("Por vencer");
    expect(porVencer).toHaveTextContent("Hasta 05/10/2026");
    expect(within(fila("Carla Vencida")).getByTestId("mandatario-vigencia")).toHaveTextContent("Vencido");
    expect(within(fila("Dario Inactivo")).getByTestId("mandatario-vigencia")).toHaveTextContent("Inactivo");
  });

  it("AC2: los tonos son verde, naranja, rojo y rojo (inactivo, HU #13139) (tokens de la paleta de estados)", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({ id: "1", fullName: "Ana Vigente" }),
      signer({ id: "2", fullName: "Beto Porvencer", validityStatus: "por_vencer" }),
      signer({ id: "3", fullName: "Carla Vencida", validityStatus: "vencido" }),
      signer({ id: "4", fullName: "Dario Inactivo", validityStatus: "inactivo", isActive: false }),
    ]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    const tono = (nombre: string) => (within(fila(nombre)).getByRole("status") as HTMLElement).style.color;
    expect(tono("Ana Vigente")).toBe("var(--badge-success-fg)");
    expect(tono("Beto Porvencer")).toBe("var(--badge-warning-fg)");
    expect(tono("Carla Vencida")).toBe("var(--badge-danger-fg)");
    expect(tono("Dario Inactivo")).toBe("var(--badge-danger-fg)");
  });

  it("AC3: el estado se lee en el texto y en el nombre accesible, no solo en el color", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([signer({ validityStatus: "vencido" })]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    const etiqueta = within(fila("Ana Restrepo")).getByRole("status", { name: "Vigencia: Vencido" });
    expect(etiqueta).toHaveTextContent("Vencido");
  });

  it("AC4: persona jurídica y formato en blanco no muestran vigencia", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({
        id: "1",
        fullName: "Entidad SAS",
        signerModel: "juridica",
        validityStatus: undefined,
        identityStatus: "none",
        signatureVaultId: null,
      }),
      signer({
        id: "2",
        fullName: "Sin nombre propio",
        signerModel: "formato_blanco",
        documentNumber: null,
        identityStatus: "none",
        signatureVaultId: null,
      }),
    ]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    for (const n of ["Entidad SAS", "Sin nombre propio"]) {
      const v = within(fila(n)).getByTestId("mandatario-vigencia");
      expect(v).toHaveAttribute("data-estado", "sin_vigencia");
      expect(v).toHaveTextContent("Sin vigencia");
      expect(within(fila(n)).queryByRole("status")).not.toBeInTheDocument();
    }
    // No firman con medio propio: no se les avisa «no puede firmar».
    expect(screen.queryByText(/no puede firmar/i)).not.toBeInTheDocument();
  });

  it("AC5: lo que el servidor no devuelve (mandatario eliminado) no aparece", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([signer({ fullName: "Ana Restrepo" })]);
    renderPanel();
    await screen.findByRole("table", TABLA);
    expect(screen.queryByText("Luis Eliminado")).not.toBeInTheDocument();
    expect(screen.getAllByRole("row")).toHaveLength(2); // cabecera + 1
  });

  it("estados de UI: vacío y error", async () => {
    fetchCompanyMandateSigners.mockResolvedValueOnce([]);
    const { unmount } = renderPanel();
    expect(await screen.findByText(/no tiene mandatarios registrados/i)).toBeInTheDocument();
    unmount();
    fetchCompanyMandateSigners.mockRejectedValueOnce(new Error("x"));
    renderPanel();
    expect(await screen.findByText(/no se pudieron cargar los mandatarios/i)).toBeInTheDocument();
  });
});
