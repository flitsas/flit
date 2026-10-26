// HU #14002 (Feature #13996, Épica #13514) — filtros por columna en la pestaña Mandatarios.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";

const fetchCompanyMandateSigners = vi.fn();

vi.mock("@/lib/api/admin-mandate-signers", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-mandate-signers")>(
    "@/lib/api/admin-mandate-signers",
  );
  return {
    ...actual,
    fetchCompanyMandateSigners: (...a: unknown[]) => fetchCompanyMandateSigners(...a),
    fetchCompanyTransitOffices: vi.fn().mockResolvedValue([
      { transitOfficeId: "ot-1", code: "05001000", name: "Tránsito de Medellín" },
      { transitOfficeId: "ot-2", code: "11001000", name: "Tránsito de Bogotá" },
      { transitOfficeId: "ot-3", code: "76001000", name: "Tránsito de Cali" },
    ]),
    fetchRepresentedCompanies: vi.fn().mockResolvedValue([]),
  };
});
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

import { CompanyMandatariosPanel } from "../CompanyMandatariosPanel";

function signer(id: string, overrides: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id,
    transitOfficeId: "ot-1",
    fullName: `Mandatario ${id}`,
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

const LISTA = [
  signer("Ana", { documentNumber: "111", transitOfficeIds: ["ot-1"] }),
  signer("Beto", {
    documentNumber: "222",
    signerModel: "juridica",
    signatureMethod: undefined,
    transitOfficeIds: ["ot-2"],
    validityStatus: undefined,
  }),
  signer("Carla", {
    documentNumber: "333",
    identityStatus: "none",
    signatureVaultId: null,
    validityStatus: "vencido",
    transitOfficeIds: ["ot-1", "ot-2"],
  }),
];

function renderPanel() {
  return render(
    <ToastProvider>
      <CompanyMandatariosPanel tenantId="t-1" />
    </ToastProvider>,
  );
}

const TABLA = { name: "Mandatarios de la compañía" };
const filas = () =>
  screen.queryAllByText(/^Mandatario (Ana|Beto|Carla)$/).map((e) => e.textContent);

async function filtrarSeleccion(
  user: ReturnType<typeof userEvent.setup>,
  columna: string,
  opcion: string,
) {
  await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
  await user.click(screen.getByRole("button", { name: columna }));
  await user.click(screen.getByRole("checkbox", { name: opcion }));
  await user.click(screen.getByRole("button", { name: "Aplicar" }));
}

beforeEach(() => {
  fetchCompanyMandateSigners.mockReset();
  fetchCompanyMandateSigners.mockResolvedValue(LISTA);
});

describe("HU #14002 — filtros por columna en Mandatarios", () => {
  it("AC1: el buscador único se reemplaza por la barra estándar y ofrece las 6 columnas", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", TABLA);
    expect(screen.queryByRole("textbox", { name: "Buscar mandatarios" })).not.toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Buscar mandatarios" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    for (const c of ["Mandatario", "Documento", "Modelo", "Vigencia", "Validación", "Organismos"]) {
      expect(screen.getByRole("button", { name: c })).toBeInTheDocument();
    }
    expect(screen.queryByRole("button", { name: "Acciones" })).not.toBeInTheDocument();
  });

  it("AC1: filtra por texto en Documento", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", TABLA);
    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Documento" }));
    await user.type(screen.getByLabelText(/documento contiene/i), "222");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas()).toEqual(["Mandatario Beto"]);
  });

  it("AC1: filtra por Modelo, Vigencia y Validación con lo que muestra la tabla", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", TABLA);
    await filtrarSeleccion(user, "Modelo", "Persona jurídica");
    expect(filas()).toEqual(["Mandatario Beto"]);
    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Empezar de cero" }));

    await filtrarSeleccion(user, "Vigencia", "Vencido");
    expect(filas()).toEqual(["Mandatario Carla"]);
    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Empezar de cero" }));

    await filtrarSeleccion(user, "Validación", "Pendiente de validación");
    expect(filas()).toEqual(["Mandatario Carla"]);
  });

  it("AC1: combina condiciones (todas deben cumplirse) y filtra por Organismos", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", TABLA);
    await filtrarSeleccion(user, "Organismos", "Tránsito de Bogotá");
    expect(filas()).toEqual(["Mandatario Beto", "Mandatario Carla"]);
    await filtrarSeleccion(user, "Vigencia", "Vencido");
    expect(filas()).toEqual(["Mandatario Carla"]);
  });

  it("AC2: un organismo sin mandatarios muestra el estado vacío con «Limpiar filtros»", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", TABLA);
    await filtrarSeleccion(user, "Organismos", "Tránsito de Cali");
    expect(screen.getByText("Ningún mandatario coincide con los filtros")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(filas()).toHaveLength(3);
  });

  it("AC3: el aviso de sin validar cuenta sobre todos los mandatarios, no sobre los filtrados", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByRole("table", TABLA);
    const resumen = screen.getByTestId("mandatarios-resumen");
    expect(resumen).toHaveTextContent("3 mandatarios · 1 sin validación aprobada");
    await filtrarSeleccion(user, "Modelo", "Persona jurídica");
    expect(filas()).toEqual(["Mandatario Beto"]);
    expect(screen.getByTestId("mandatarios-resumen")).toHaveTextContent(
      "3 mandatarios · 1 sin validación aprobada",
    );
  });
});
