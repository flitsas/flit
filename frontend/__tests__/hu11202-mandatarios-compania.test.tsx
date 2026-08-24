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
  // El panel las carga para acotar para qué empresas firma el mandatario en cada organismo. Sin este
  // doble, llamar a una función inexistente rompe la carga entera y el panel cae en estado de error.
  fetchRepresentedCompanies: vi.fn().mockResolvedValue([]),
  createCompanyMandateSigner: vi.fn(),
  updateCompanyMandateSigner: vi.fn(),
  inactivateCompanyMandateSigner: vi.fn(),
  reactivateCompanyMandateSigner: vi.fn(),
}));

vi.mock("@/lib/api/admin-mandate-signers", () => mocks);

vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show: vi.fn() }),
}));

// El formulario monta el selector de firma del baúl (mismo componente que el representante legal).
// Sin este doble, el selector no puede consultar y pinta su propia alerta, que compite con la del
// formulario al buscar por role="alert".
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
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
  mocks.fetchCompanyMandateSigners.mockResolvedValue([MANDATARIO]);
  mocks.fetchCompanyTransitOffices.mockResolvedValue(OFICINAS);
  mocks.createCompanyMandateSigner.mockResolvedValue({ id: "ms-2", integrityHash: "b".repeat(64) });
  mocks.updateCompanyMandateSigner.mockResolvedValue({ id: "ms-1", integrityHash: "c".repeat(64) });
});

function renderPanel() {
  return render(<CompanyMandatariosPanel tenantId="tenant-1" />);
}

const EMPRESAS = [
  { id: "emp-acme", documentNumber: "900111111", name: "ACME SAS" },
  { id: "emp-beta", documentNumber: "900222222", name: "BETA SAS" },
];

describe("HU #11202 — mandatarios desde el configurador de la compañía", () => {
  // ── Acotación por empresa representada ────────────────────────────────────

  it("se puede acotar para qué empresas firma en cada organismo", async () => {
    // El mandatario se llaveaba solo contra la gestora, así que no había forma de decir "firma para
    // ESTA empresa". Las empresas se listan con su NIT, que es lo que distingue dos homónimas.
    mocks.fetchRepresentedCompanies.mockResolvedValue(EMPRESAS);
    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole("button", { name: "Nuevo mandatario" }));
    await user.type(screen.getByLabelText("Nombre completo"), "Ana Restrepo");
    await user.type(screen.getByLabelText("Número de documento"), "1020304050");
    // Desde la HU #11715 no se habilita en un organismo a quien no puede firmar ante él.
    await user.type(screen.getByLabelText("Correo"), "ana@ejemplo.com");
    await user.click(screen.getByRole("checkbox", { name: "Secretaría de Movilidad de Medellín" }));

    // La empresa se identifica por NIT, no solo por razón social.
    await user.click(
      screen.getByRole("checkbox", { name: /ACME SAS \(900111111\) en Secretaría de Movilidad de Medellín/ }),
    );
    await user.click(screen.getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(mocks.createCompanyMandateSigner).toHaveBeenCalledWith(
        "tenant-1",
        expect.objectContaining({
          officeCompanies: [
            { transitOfficeId: "ot-medellin", representedCompanyIds: ["emp-acme"] },
          ],
        }),
      ),
    );
  });

  it("sin empresas marcadas NO se manda acotación: aplica a todas", async () => {
    // La ausencia es lo que significa "todas". Mandar un arreglo vacío diría "ninguna" y dejaría al
    // mandatario sin poder firmar nada.
    mocks.fetchRepresentedCompanies.mockResolvedValue(EMPRESAS);
    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole("button", { name: "Nuevo mandatario" }));
    await user.type(screen.getByLabelText("Nombre completo"), "Ana Restrepo");
    await user.type(screen.getByLabelText("Número de documento"), "1020304050");
    // Desde la HU #11715 no se habilita en un organismo a quien no puede firmar ante él.
    await user.type(screen.getByLabelText("Correo"), "ana@ejemplo.com");
    await user.click(screen.getByRole("checkbox", { name: "Secretaría de Movilidad de Medellín" }));
    await user.click(screen.getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(mocks.createCompanyMandateSigner).toHaveBeenCalledWith(
        "tenant-1",
        expect.objectContaining({ officeCompanies: [] }),
      ),
    );
  });

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
    await user.type(screen.getByLabelText("Correo"), "carlos@ejemplo.com");
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

  it("HU #11715: sin medio de firma no se puede guardar, y se explica por qué", async () => {
    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole("button", { name: "Nuevo mandatario" }));
    await user.type(screen.getByLabelText("Nombre completo"), "Ana Restrepo");
    await user.type(screen.getByLabelText("Número de documento"), "1020304050");
    await user.click(screen.getByRole("checkbox", { name: "Secretaría de Movilidad de Medellín" }));

    expect(
      screen.getByText(/no está en condiciones de firmar/i),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Guardar" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(mocks.createCompanyMandateSigner).not.toHaveBeenCalled();
  });

  it("HU #11715: con firma de forma física sí se guarda, porque la línea en blanco es lo correcto", async () => {
    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole("button", { name: "Nuevo mandatario" }));
    await user.type(screen.getByLabelText("Nombre completo"), "Ana Restrepo");
    await user.type(screen.getByLabelText("Número de documento"), "1020304050");
    await user.click(screen.getByRole("checkbox", { name: "Secretaría de Movilidad de Medellín" }));
    await user.click(
      screen.getByRole("checkbox", {
        name: "Firma de forma física · Secretaría de Movilidad de Medellín",
      }),
    );

    expect(screen.queryByText(/no está en condiciones de firmar/i)).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Guardar" }));

    await waitFor(() => expect(mocks.createCompanyMandateSigner).toHaveBeenCalled());
  });

  it("AC4: el perfil del organismo ya no ofrece la gestión de mandatarios", () => {
    // La navegación del hub del organismo es la fuente de verdad de sus secciones.
    expect(OT_HUB_TABS.map((t) => t.id)).not.toContain("mandatarios");
  });

  it("sin organismos habilitados se explica qué falta en vez de dejar registrar en el vacío", async () => {
    mocks.fetchCompanyTransitOffices.mockResolvedValue([]);
    mocks.fetchCompanyMandateSigners.mockResolvedValue([]);
    renderPanel();

    expect(
      await screen.findByText(/todavía no tiene organismos de tránsito habilitados/),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Nuevo mandatario" })).toBeDisabled();
  });
});
