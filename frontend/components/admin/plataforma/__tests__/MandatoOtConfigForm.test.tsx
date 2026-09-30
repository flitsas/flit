import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MandatoOtConfigForm } from "@/components/admin/plataforma/MandatoOtConfigForm";
import type { MandateOtConfigView } from "@/lib/api/admin-plataforma-mandatos";

const listCompanyOtMandateRules = vi.fn();
const fetchMandateSigners = vi.fn();
const upsertMandateOtConfig = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-plataforma-mandatos")>();
  return {
    ...actual,
    listCompanyOtMandateRules: (...a: unknown[]) => listCompanyOtMandateRules(...a),
    upsertMandateOtConfig: (...a: unknown[]) => upsertMandateOtConfig(...a),
    fetchMandateOtPreview: vi.fn(),
    fetchMandatoTemplatePreview: vi.fn(),
    uploadMandateOtPdfTemplate: vi.fn(),
    saveMandateOtEditorBody: vi.fn(),
    deleteMandateOtCustomTemplate: vi.fn(),
    upsertCompanyOtMandateRule: vi.fn(),
    deleteCompanyOtMandateRule: vi.fn(),
    setOtDefaultSigner: vi.fn(),
    setCompanyDefaultSigner: vi.fn(),
  };
});

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: (...a: unknown[]) => fetchMandateSigners(...a),
}));

const funza = {
  officeId: "eeacc872-a522-56bb-9150-70776b094009",
  code: "25286000",
  name: "STRIA TTOyTTE MCPAL FUNZA",
  templateCode: "municipio",
  configuredTemplateCode: "municipio",
  mandataryFamily: "individuo",
  requiresForNaturalPerson: true,
  hasExplicitConfig: true,
  assignmentMode: "open",
  customTemplateKind: "none",
  hasCustomTemplate: false,
  defaultMandateSignerId: null,
  defaultMandateSignerName: null,
  defaultMandateSignerDocumentType: null,
  defaultMandateSignerDocumentNumber: null,
  defaultMandateSignerIntegrityHash: null,
  rowVersion: 1,
} as MandateOtConfigView;

describe("MandatoOtConfigForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    listCompanyOtMandateRules.mockResolvedValue([]);
    fetchMandateSigners.mockResolvedValue([]);
  });

  it("abre Configurar mandato de Funza sin ReferenceError (error state)", async () => {
    render(
      <MandatoOtConfigForm
        office={funza}
        mode="mandato"
        onClose={() => undefined}
        onSaved={() => undefined}
      />,
    );

    await waitFor(() => {
      expect(screen.getByTestId("mandato-ot-config-form")).toBeInTheDocument();
    });
    expect(screen.getByTestId("mandato-template-select")).toBeInTheDocument();
    expect(listCompanyOtMandateRules).toHaveBeenCalledWith(funza.officeId);
    expect(screen.queryByTestId("mandato-ot-register-signer")).not.toBeInTheDocument();
    expect(screen.queryByTestId("mandato-ot-default-signer")).not.toBeInTheDocument();
  });

  it("permite registrar el mandatario default del OT desde el panel", async () => {
    const onRegisterSigner = vi.fn();
    listCompanyOtMandateRules.mockResolvedValue([
      {
        companyTenantId: "cia-1",
        companyName: "Gestora Funza S.A.S.",
        assignmentMode: "signer",
        hasExplicitRule: true,
        defaultMandateSignerId: null,
      },
    ]);

    render(
      <MandatoOtConfigForm
        office={funza}
        mode="mandatario"
        onRegisterSigner={onRegisterSigner}
        onClose={() => undefined}
        onSaved={() => undefined}
      />,
    );

    const cta = await screen.findByTestId("mandato-ot-register-signer");
    cta.click();
    expect(onRegisterSigner).toHaveBeenCalledWith("cia-1");
  });
  describe("HU #13152 campos institucionales según la redacción seleccionada", () => {
    const generico = {
      ...funza,
      templateCode: "generico",
      configuredTemplateCode: "generico",
      assignmentMode: "signer",
    } as MandateOtConfigView;

    const renderMandato = (office: MandateOtConfigView) =>
      render(
        <MandatoOtConfigForm
          office={office}
          mode="mandato"
          onClose={() => undefined}
          onSaved={() => undefined}
        />,
      );

    it("de Genérico a Sabaneta muestra los campos y los envía al guardar", async () => {
      const user = userEvent.setup();
      upsertMandateOtConfig.mockResolvedValue(generico);
      renderMandato(generico);
      expect(screen.queryByLabelText(/^mandatario institucional \/ UT$/i)).not.toBeInTheDocument();
      await user.selectOptions(screen.getByTestId("mandato-template-select"), "sabaneta");
      await user.type(screen.getByLabelText(/^mandatario institucional \/ UT$/i), "UT-SETSA");
      await user.type(screen.getByLabelText(/^nit$/i), "900273813-7");
      await user.type(screen.getByLabelText(/ciudad cámara/i), "Medellín");
      await user.type(screen.getByLabelText(/sigla/i), "SETSA");
      await user.click(screen.getByRole("button", { name: /guardar plantilla/i }));
      await waitFor(() => expect(upsertMandateOtConfig).toHaveBeenCalled());
      expect(upsertMandateOtConfig.mock.calls[0][1]).toMatchObject({
        templateCode: "sabaneta",
        institutionalMandataryName: "UT-SETSA",
        institutionalMandataryNit: "900273813-7",
        chamberCity: "Medellín",
        mandatarySigla: "SETSA",
      });
    });

    it("de Sabaneta a Genérico oculta los campos y los envía en null", async () => {
      const user = userEvent.setup();
      const sabaneta = {
        ...generico,
        templateCode: "sabaneta",
        configuredTemplateCode: "sabaneta",
        institutionalMandataryName: "UT-SETSA",
        institutionalMandataryNit: "900273813-7",
        chamberCity: "Medellín",
        mandatarySigla: "SETSA",
      } as MandateOtConfigView;
      upsertMandateOtConfig.mockResolvedValue(generico);
      renderMandato(sabaneta);
      expect(screen.getByLabelText(/^mandatario institucional \/ UT$/i)).toBeInTheDocument();
      await user.selectOptions(screen.getByTestId("mandato-template-select"), "generico");
      expect(screen.queryByLabelText(/^mandatario institucional \/ UT$/i)).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: /guardar plantilla/i }));
      await waitFor(() => expect(upsertMandateOtConfig).toHaveBeenCalled());
      expect(upsertMandateOtConfig.mock.calls[0][1]).toMatchObject({
        institutionalMandataryName: null,
        institutionalMandataryNit: null,
        chamberCity: null,
        mandatarySigla: null,
      });
    });

    it("con Automática que resuelve a Bello muestra los campos", () => {
      renderMandato({
        ...generico,
        templateCode: "bello",
        configuredTemplateCode: "auto",
      } as MandateOtConfigView);
      expect(screen.getByLabelText(/^mandatario institucional \/ UT$/i)).toBeInTheDocument();
    });

    it("no envía y avisa si el nombre está vacío", async () => {
      const user = userEvent.setup();
      renderMandato(generico);
      await user.selectOptions(screen.getByTestId("mandato-template-select"), "sabaneta");
      await user.click(screen.getByRole("button", { name: /guardar plantilla/i }));
      expect(await screen.findByText(/nombre del mandatario institucional es obligatorio/i)).toBeInTheDocument();
      expect(upsertMandateOtConfig).not.toHaveBeenCalled();
    });

    it("familia organismo_transito mantiene los campos visibles", () => {
      renderMandato({ ...generico, mandataryFamily: "organismo_transito" } as MandateOtConfigView);
      expect(screen.getByLabelText(/^mandatario institucional \/ UT$/i)).toBeInTheDocument();
    });
  });
  describe("HU #13150 columna Tipo de mandato por compañía", () => {
    const regla = (over: Record<string, unknown>) => ({
      companyTenantId: "cia-x",
      companyName: "Compañía X",
      assignmentMode: "signer",
      hasExplicitRule: true,
      defaultMandateSignerId: null,
      rowVersion: 1,
      ...over,
    });

    const abrir = () =>
      render(
        <MandatoOtConfigForm
          office={funza}
          mode="mandatario"
          onClose={() => undefined}
          onSaved={() => undefined}
        />,
      );

    it("muestra Persona natural, Persona jurídica y Mandato abierto por compañía", async () => {
      listCompanyOtMandateRules.mockResolvedValue([
        regla({ companyTenantId: "a", companyName: "Alfa", assignmentMode: "signer" }),
        regla({ companyTenantId: "b", companyName: "Beta", assignmentMode: "institutional" }),
        regla({ companyTenantId: "c", companyName: "Gamma", assignmentMode: "open" }),
      ]);
      abrir();
      expect(await screen.findByRole("columnheader", { name: /tipo de mandato/i })).toBeInTheDocument();
      expect(screen.getByTestId("mandato-company-tipo-a")).toHaveTextContent("Persona natural");
      expect(screen.getByTestId("mandato-company-tipo-b")).toHaveTextContent("Persona jurídica");
      expect(screen.getByTestId("mandato-company-tipo-c")).toHaveTextContent("Mandato abierto");
    });

    it("compañía sin regla propia: Persona natural con marca Default y ayuda", async () => {
      listCompanyOtMandateRules.mockResolvedValue([
        regla({ companyTenantId: "a", companyName: "Alfa", hasExplicitRule: false, rowVersion: null }),
      ]);
      abrir();
      expect(await screen.findByTestId("mandato-company-tipo-a")).toHaveTextContent("Persona natural");
      expect(screen.getByText("Default")).toBeInTheDocument();
      expect(screen.getByText(/sin regla propia: usa el tipo por defecto/i)).toBeInTheDocument();
    });

    it("no muestra etiquetas antiguas y un modo desconocido cae en Persona natural", async () => {
      listCompanyOtMandateRules.mockResolvedValue([
        regla({ companyTenantId: "z", companyName: "Zeta", assignmentMode: "otra_cosa" }),
      ]);
      const { container } = abrir();
      expect(await screen.findByTestId("mandato-company-tipo-z")).toHaveTextContent("Persona natural");
      expect(container.ownerDocument.body.textContent).not.toMatch(
        /Persona o RL|Persona\/RL|Institucional OT|Abierto \(sin asumir\)/,
      );
    });

    it("pagina con Filas por página cuando hay más compañías que la página", async () => {
      listCompanyOtMandateRules.mockResolvedValue(
        Array.from({ length: 12 }, (_, n) =>
          regla({ companyTenantId: `c${n}`, companyName: `Compañía ${n + 1}` }),
        ),
      );
      abrir();
      expect(await screen.findByText("Compañía 1")).toBeInTheDocument();
      expect(screen.queryByText("Compañía 12")).not.toBeInTheDocument();
      expect(screen.getByText(/filas por página/i)).toBeInTheDocument();
    });

    it("muestra Reintentar si la carga falla", async () => {
      listCompanyOtMandateRules.mockRejectedValue(new Error("boom"));
      abrir();
      expect(await screen.findByRole("button", { name: /reintentar/i })).toBeInTheDocument();
    });
  });
});
