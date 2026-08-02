import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/**
 * HU #11202 — los mandatarios los registra la COMPAÑÍA desde su configurador.
 *
 * Antes los daba de alta cada organismo de tránsito y elegía a qué compañías aplicaban. Se invierte:
 * el mandatario es de la empresa, y la empresa marca en cuáles de SUS organismos aplica.
 */
const mocks = vi.hoisted(() => ({
  fetchCompanyMandateSigners: vi.fn(),
  fetchCompanyTransitOffices: vi.fn(),
  createCompanyMandateSigner: vi.fn(),
  updateCompanyMandateSigner: vi.fn(),
  inactivateCompanyMandateSigner: vi.fn(),
  reactivateCompanyMandateSigner: vi.fn(),
}));

vi.mock("@/lib/api/admin-mandate-signers", () => mocks);

vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show: vi.fn() }),
}));

import { CompanyMandatariosPanel } from "@/components/admin/companies/mandate-signers/CompanyMandatariosPanel";
import { OT_HUB_TABS } from "@/components/admin/transit-offices/ot-nav";

const OFICINAS = [
  { transitOfficeId: "ot-medellin", code: "05001000", name: "Secretaría de Movilidad de Medellín" },
  { transitOfficeId: "ot-envigado", code: "05266000", name: "Tránsito de Envigado" },
];

const MANDATARIO = {
  id: "ms-1",
  transitOfficeId: "ot-medellin",
  fullName: "Ana Restrepo",
  documentType: "CC",
  documentNumber: "1020304050",
  integrityHash: "a".repeat(64),
  email: "ana@ejemplo.com",
  userId: null,
  identityValidationRef: null,
  identityStatus: "none" as const,
  signatureVaultId: null,
  registeredAt: "2026-08-01T10:00:00Z",
  isActive: true,
  companyTenantIds: ["tenant-1"],
  transitOfficeIds: ["ot-medellin", "ot-envigado"],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchCompanyMandateSigners.mockResolvedValue({ signers: [MANDATARIO], mockIdentityEnabled: false });
  mocks.fetchCompanyTransitOffices.mockResolvedValue(OFICINAS);
  mocks.createCompanyMandateSigner.mockResolvedValue({ id: "ms-2", integrityHash: "b".repeat(64) });
  mocks.updateCompanyMandateSigner.mockResolvedValue({ id: "ms-1", integrityHash: "c".repeat(64) });
});

function renderPanel() {
  return render(<CompanyMandatariosPanel tenantId="tenant-1" />);
}

describe("HU #11202 — mandatarios desde el configurador de la compañía", () => {
  it("AC1: se registra un mandatario con sus datos desde la compañía", async () => {
    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole("button", { name: "Nuevo mandatario" }));

    await user.type(screen.getByLabelText("Nombre completo"), "Carlos Pérez");
    await user.type(screen.getByLabelText("Número de documento"), "9080706050");
    await user.type(screen.getByLabelText("Correo"), "carlos@ejemplo.com");
    await user.click(screen.getByRole("checkbox", { name: "Secretaría de Movilidad de Medellín" }));
    await user.click(screen.getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(mocks.createCompanyMandateSigner).toHaveBeenCalledWith(
        "tenant-1",
        expect.objectContaining({
          fullName: "Carlos Pérez",
          documentNumber: "9080706050",
          email: "carlos@ejemplo.com",
          transitOfficeIds: ["ot-medellin"],
        }),
      ),
    );
  });

  it("AC2: solo se ofrecen los organismos de la compañía, con selección múltiple", async () => {
    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole("button", { name: "Nuevo mandatario" }));

    // Exactamente los que la compañía tiene habilitados: ni uno más.
    const casillas = screen.getAllByRole("checkbox");
    expect(casillas).toHaveLength(OFICINAS.length);
    expect(screen.getByRole("checkbox", { name: "Secretaría de Movilidad de Medellín" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Tránsito de Envigado" })).toBeInTheDocument();

    // Selección múltiple: los dos a la vez.
    await user.type(screen.getByLabelText("Nombre completo"), "Carlos Pérez");
    await user.type(screen.getByLabelText("Número de documento"), "9080706050");
    await user.click(screen.getByRole("checkbox", { name: "Secretaría de Movilidad de Medellín" }));
    await user.click(screen.getByRole("checkbox", { name: "Tránsito de Envigado" }));
    await user.click(screen.getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(mocks.createCompanyMandateSigner).toHaveBeenCalledWith(
        "tenant-1",
        expect.objectContaining({ transitOfficeIds: ["ot-medellin", "ot-envigado"] }),
      ),
    );
  });

  it("AC2: sin ningún organismo elegido no se guarda", async () => {
    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole("button", { name: "Nuevo mandatario" }));
    await user.type(screen.getByLabelText("Nombre completo"), "Carlos Pérez");
    await user.type(screen.getByLabelText("Número de documento"), "9080706050");
    await user.click(screen.getByRole("button", { name: "Guardar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /al menos un organismo de tránsito/,
    );
    expect(mocks.createCompanyMandateSigner).not.toHaveBeenCalled();
  });

  it("AC3: se consultan sus datos y organismos, y se pueden editar", async () => {
    const user = userEvent.setup();
    renderPanel();

    // Consulta: la fila muestra los organismos donde aplica, no solo uno.
    const fila = (await screen.findByText("Ana Restrepo")).closest("tr")!;
    expect(within(fila).getByText(/Secretaría de Movilidad de Medellín/)).toBeInTheDocument();
    expect(within(fila).getByText(/Tránsito de Envigado/)).toBeInTheDocument();

    // Edición: el formulario llega precargado y quitar un organismo lo retira.
    await user.click(within(fila).getByRole("button", { name: "Editar" }));
    expect(screen.getByLabelText("Nombre completo")).toHaveValue("Ana Restrepo");
    expect(screen.getByRole("checkbox", { name: "Secretaría de Movilidad de Medellín" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Tránsito de Envigado" })).toBeChecked();

    await user.click(screen.getByRole("checkbox", { name: "Tránsito de Envigado" }));
    await user.click(screen.getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(mocks.updateCompanyMandateSigner).toHaveBeenCalledWith(
        "tenant-1",
        "ms-1",
        expect.objectContaining({ transitOfficeIds: ["ot-medellin"] }),
      ),
    );
  });

  it("AC4: el perfil del organismo ya no ofrece la gestión de mandatarios", () => {
    // La navegación del hub del organismo es la fuente de verdad de sus secciones.
    expect(OT_HUB_TABS.map((t) => t.id)).not.toContain("mandatarios");
  });

  it("sin organismos habilitados se explica qué falta en vez de dejar registrar en el vacío", async () => {
    mocks.fetchCompanyTransitOffices.mockResolvedValue([]);
    mocks.fetchCompanyMandateSigners.mockResolvedValue({ signers: [], mockIdentityEnabled: false });
    renderPanel();

    expect(
      await screen.findByText(/todavía no tiene organismos de tránsito habilitados/),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Nuevo mandatario" })).toBeDisabled();
  });
});
