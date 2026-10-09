import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { accionDeshabilitada, hayAccion, pulsarAccion } from "@/lib/test-acciones";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MandatosCatalogPanel } from "@/components/admin/plataforma/MandatosCatalogPanel";
import { ToastProvider } from "@/components/admin/Toast";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

const listMandateOtConfigs = vi.fn();
const deleteMandateOtConfig = vi.fn();
const openPdfBlobInNewTab = vi.fn();
const fetchMandatoTemplatePreview = vi.fn();
const listMandatoFormats = vi.fn();
const getMandatoFormat = vi.fn();
const updateMandatoFormat = vi.fn();
const resetMandatoFormatTemplate = vi.fn();
const formato = (code: string, name: string, assignmentMode = "signer") => ({
  code,
  name,
  assignmentMode,
  baseRedaction: code === "auto" ? null : code,
  selectableAsRedaction: code !== "auto",
  delegatesToOfficeTemplate: code === "auto",
});
const CATALOGO = [
  formato("auto", "Automática (según el organismo)"),
  formato("generico", "Genérico"),
  formato("sabaneta", "Sabaneta", "institutional"),
  formato("bello", "Bello"),
  formato("municipio", "Envigado, Funza y Medellín"),
];

vi.mock("@/lib/api/admin-plataforma-mandatos", () => ({
  listMandateOtConfigs: (...a: unknown[]) => listMandateOtConfigs(...a),
  listMandatoFormats: (...a: unknown[]) => listMandatoFormats(...a),
  getMandatoFormat: (...a: unknown[]) => getMandatoFormat(...a),
  updateMandatoFormat: (...a: unknown[]) => updateMandatoFormat(...a),
  resetMandatoFormatTemplate: (...a: unknown[]) => resetMandatoFormatTemplate(...a),
  getMandatoFormatVersion: vi.fn(),
  previewMandatoFormatDraft: vi.fn(),
  readFormatError: () => ({ error: null, unknownVariables: [] }),
  deleteMandateOtConfig: (...a: unknown[]) => deleteMandateOtConfig(...a),
  fetchMandatoTemplatePreview: (...a: unknown[]) => fetchMandatoTemplatePreview(...a),
  fetchMandateOtPreview: vi.fn(),
  upsertMandateOtConfig: vi.fn(),
  uploadMandateOtPdfTemplate: vi.fn(),
  saveMandateOtEditorBody: vi.fn(),
  deleteMandateOtCustomTemplate: vi.fn(),
  listCompanyOtMandateRules: vi.fn().mockResolvedValue([]),
  upsertCompanyOtMandateRule: vi.fn(),
  deleteCompanyOtMandateRule: vi.fn(),
  // El panel monta el simulador (HU #11707); sin estas entradas el módulo mockeado las deja
  // indefinidas y el componente revienta al montar.
  listMandateSimulatorSigners: vi.fn().mockResolvedValue([]),
  fetchMandateSimulationPreview: vi.fn(),
  sendMandateSimulation: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([]),
  },
}));

vi.mock("@/lib/documents/open-document-tab", () => ({
  openPdfBlobInNewTab: (...a: unknown[]) => openPdfBlobInNewTab(...a),
}));

// Las filas y el catálogo llegan por separado: se espera a una acción de fila, no a un texto repetido.
const esperarFilas = () => screen.findByRole("button", { name: /acciones de mandato para sabaneta/i });

// La pantalla se organiza en pestañas: «Formatos de contrato» (la que abre por defecto), «Configuración por
// organismo» y «Simulador». Los tests abren la pestaña que necesitan.
function renderPanel(pestana: "organismos" | "formatos" = "organismos") {
  const r = render(
    <ToastProvider>
      <MandatosCatalogPanel />
    </ToastProvider>,
  );
  if (pestana === "organismos") {
    fireEvent.click(screen.getByRole("tab", { name: /configuración por organismo/i }));
  }
  return r;
}

const sampleRows = [
  {
    officeId: "o1",
    code: "5631000",
    name: "Sabaneta",
    templateCode: "sabaneta",
    requiresForNaturalPerson: true,
    mandataryFamily: "organismo_transito",
    assignmentMode: "institutional",
    institutionalMandataryName: "UT-SETSA",
    institutionalMandataryNit: "900273813-7",
    chamberCity: "Medellín",
    mandatarySigla: "UT-SETSA",
    hasExplicitConfig: true,
    rowVersion: 1,
    customTemplateKind: "none",
    customTemplateFileName: null,
    customTemplateBody: null,
    hasCustomTemplate: false,
    defaultMandateSignerId: null,
    defaultMandateSignerName: null,
    defaultMandateSignerDocumentType: null,
    defaultMandateSignerDocumentNumber: null,
    defaultMandateSignerIntegrityHash: null,
    explicitPersonaJuridica: 2,
    explicitMandatoAbierto: 1,
  },
  {
    officeId: "o2",
    code: "05001000",
    name: "Medellín",
    templateCode: "generico",
    requiresForNaturalPerson: false,
    mandataryFamily: "individuo",
    assignmentMode: "signer",
    institutionalMandataryName: null,
    institutionalMandataryNit: null,
    chamberCity: null,
    mandatarySigla: null,
    hasExplicitConfig: false,
    rowVersion: null,
    customTemplateKind: "none",
    customTemplateFileName: null,
    customTemplateBody: null,
    hasCustomTemplate: false,
    defaultMandateSignerId: null,
    defaultMandateSignerName: null,
    defaultMandateSignerDocumentType: null,
    defaultMandateSignerDocumentNumber: null,
    defaultMandateSignerIntegrityHash: null,
  },
];

describe("MandatosCatalogPanel configurador", () => {
  beforeEach(() => {
    listMandateOtConfigs.mockReset();
    deleteMandateOtConfig.mockReset();
    openPdfBlobInNewTab.mockReset();
    fetchMandatoTemplatePreview.mockReset();
    listMandatoFormats.mockReset();
    listMandatoFormats.mockResolvedValue(CATALOGO);
    listMandateOtConfigs.mockResolvedValue(sampleRows);
    openPdfBlobInNewTab.mockResolvedValue(undefined);
    fetchMandatoTemplatePreview.mockResolvedValue(new Blob(["%PDF"], { type: "application/pdf" }));
  });

  it("carga OTs desde la API y muestra Acciones", async () => {
    renderPanel();
    await esperarFilas();
    expect(screen.getAllByText("Sabaneta").length).toBeGreaterThan(0);
    expect(screen.getByText("Medellín")).toBeInTheDocument();
    expect(screen.getByText("Mandatario de la compañía · Institucional (organismo) (2) · Abierto (sin mandatario) (1)")).toBeInTheDocument();
    expect(screen.getAllByText("Mandatario de la compañía").length).toBeGreaterThan(0);
    expect(
      screen.getByRole("button", { name: /acciones de mandato para sabaneta/i }),
    ).toBeInTheDocument();
  });

  it("abre configuración del mandato desde el menú Acciones", async () => {
    const user = userEvent.setup();
    renderPanel();
    await esperarFilas();
    await user.click(screen.getByRole("button", { name: /acciones de mandato para sabaneta/i }));
    await user.click(screen.getByRole("menuitem", { name: /configuración del mandato/i }));
    expect(screen.getByTestId("mandato-ot-config-form")).toHaveAttribute("data-mode", "mandato");
    expect(screen.getByRole("heading", { name: /configurar mandato/i })).toBeInTheDocument();
  });

  it("abre configuración del mandatario desde el menú Acciones", async () => {
    const user = userEvent.setup();
    renderPanel();
    await esperarFilas();
    await user.click(screen.getByRole("button", { name: /acciones de mandato para sabaneta/i }));
    await user.click(screen.getByRole("menuitem", { name: /configuración del mandatario/i }));
    expect(screen.getByTestId("mandato-ot-config-form")).toHaveAttribute("data-mode", "mandatario");
    expect(screen.getByRole("heading", { name: /configurar mandatario/i })).toBeInTheDocument();
  });

  async function abrirRestablecer(user: ReturnType<typeof userEvent.setup>) {
    await esperarFilas();
    await user.click(screen.getByRole("button", { name: /acciones de mandato para sabaneta/i }));
    await user.click(screen.getByRole("menuitem", { name: /restablecer default/i }));
    return screen.findByTestId("mandatos-reset-dialog");
  }

  it("HU #13153: lista lo que se perderá y confirma con DELETE", async () => {
    const user = userEvent.setup();
    deleteMandateOtConfig.mockResolvedValue(undefined);
    listMandateOtConfigs
      .mockResolvedValueOnce([
        {
          ...sampleRows[0],
          configuredTemplateCode: "sabaneta",
          hasCustomTemplate: true,
          defaultMandateSignerId: "s1",
          defaultMandateSignerName: "Ana Pérez",
        },
        sampleRows[1],
      ])
      .mockResolvedValueOnce([
        { ...sampleRows[0], hasExplicitConfig: false, templateCode: "generico", rowVersion: null },
        sampleRows[1],
      ]);

    renderPanel();
    const dlg = await abrirRestablecer(user);
    expect(within(dlg).getByText(/redacción elegida/i)).toBeInTheDocument();
    expect(within(dlg).getByText(/mandatario general del OT \(Ana Pérez\)/i)).toBeInTheDocument();
    expect(within(dlg).getByText(/plantilla propia/i)).toBeInTheDocument();
    expect(within(dlg).getByText(/reglas por compañía no se eliminan/i)).toBeInTheDocument();
    expect(deleteMandateOtConfig).not.toHaveBeenCalled();
    await user.click(within(dlg).getByRole("button", { name: /^restablecer$/i }));
    await waitFor(() => expect(deleteMandateOtConfig).toHaveBeenCalledWith("o1"));
    expect(await screen.findByText(/se restableció el default/i)).toBeInTheDocument();
    expect(listMandateOtConfigs).toHaveBeenCalledTimes(2);
  });

  it("HU #13153: solo lista lo que existe", async () => {
    const user = userEvent.setup();
    renderPanel();
    const dlg = await abrirRestablecer(user);
    const items = within(dlg).getAllByRole("listitem");
    expect(items).toHaveLength(1);
    expect(items[0]).toHaveTextContent(/redacción elegida/i);
    expect(within(dlg).queryByText(/plantilla propia/i)).not.toBeInTheDocument();
    expect(within(dlg).queryByText(/mandatario general/i)).not.toBeInTheDocument();
  });

  it("HU #13153: cancelar no envía ninguna petición", async () => {
    const user = userEvent.setup();
    renderPanel();
    const dlg = await abrirRestablecer(user);
    await user.click(within(dlg).getByRole("button", { name: /cancelar/i }));
    expect(screen.queryByTestId("mandatos-reset-dialog")).not.toBeInTheDocument();
    expect(deleteMandateOtConfig).not.toHaveBeenCalled();
  });

  it("HU #13153: error del API muestra el aviso y conserva la fila", async () => {
    const user = userEvent.setup();
    deleteMandateOtConfig.mockRejectedValue(new Error("500"));
    renderPanel();
    const dlg = await abrirRestablecer(user);
    await user.click(within(dlg).getByRole("button", { name: /^restablecer$/i }));
    expect(await screen.findByText(/no se pudo restablecer la configuración/i)).toBeInTheDocument();
    expect(listMandateOtConfigs).toHaveBeenCalledTimes(1);
    expect(screen.getAllByText("Sabaneta").length).toBeGreaterThan(0);
  });

  it("HU #13153: organismo en default no ofrece Restablecer", async () => {
    const user = userEvent.setup();
    renderPanel();
    await esperarFilas();
    await user.click(screen.getByRole("button", { name: /acciones de mandato para medellín/i }));
    expect(screen.queryByRole("menuitem", { name: /restablecer default/i })).not.toBeInTheDocument();
  });

  it("HU #13152: la ayuda dice que el tipo por defecto es Mandatario de la compañía y nadie afirma que Abierto (sin mandatario) lo es", async () => {
    renderPanel("formatos");
    expect(
      await screen.findByText(/tipo por defecto de un organismo nuevo es mandatario de la compañía/i),
    ).toBeInTheDocument();
    expect(screen.queryByText(/abierto[^.]*es el (default|tipo por defecto)/i)).not.toBeInTheDocument();
  });

  it("abre preview de plantilla genérica", async () => {
    const user = userEvent.setup();
    renderPanel("formatos");
    await screen.findByRole("table", { name: /formatos de contrato de mandato/i });
    await pulsarAccion(user, /ver documento del formato genérico/i);
    await waitFor(() => expect(openPdfBlobInNewTab).toHaveBeenCalled());
  });

  it("pagina la tabla de organismos (DataTable + Pagination)", async () => {
    const user = userEvent.setup();
    const many = Array.from({ length: 12 }, (_, i) => ({
      ...sampleRows[1],
      officeId: `office-${i}`,
      code: `500100${i}`,
      name: `Organismo ${i + 1}`,
    }));
    listMandateOtConfigs.mockResolvedValue(many);

    renderPanel();
    expect(await screen.findByText("Organismo 1")).toBeInTheDocument();
    expect(screen.getByText("Organismo 10")).toBeInTheDocument();
    expect(screen.queryByText("Organismo 11")).not.toBeInTheDocument();
    const nav = screen.getByRole("navigation", { name: /paginación de configuración de mandato/i });
    expect(nav).toHaveTextContent(/Mostrando 1–10 de 12/i);

    await user.click(within(nav).getByRole("button", { name: /página siguiente/i }));
    expect(await screen.findByText("Organismo 11")).toBeInTheDocument();
    expect(screen.getByText("Organismo 12")).toBeInTheDocument();
    expect(screen.queryByText("Organismo 1")).not.toBeInTheDocument();
  });

  describe("HU #13174 formatos desde el backend", () => {
    it("la tabla de formatos sale del catálogo y no se inventan filas", async () => {
      listMandatoFormats.mockResolvedValue([
        formato("generico", "Genérico"),
        formato("bello", "Bello renombrado"),
      ]);
      renderPanel("formatos");
      const tabla = await screen.findByRole("table", { name: /formatos de contrato de mandato/i });
      expect(within(tabla).getByText("Bello renombrado")).toBeInTheDocument();
      expect(within(tabla).getByText("Genérico")).toBeInTheDocument();
      expect(within(tabla).queryByText(/Envigado/)).not.toBeInTheDocument();
    });

    it("la tabla muestra el nombre editado del formato de cada organismo", async () => {
      listMandatoFormats.mockResolvedValue([
        formato("sabaneta", "Sabaneta (editado)", "institutional"),
        formato("generico", "Genérico"),
      ]);
      renderPanel("formatos");
      await screen.findByText("Medellín");
      await waitFor(() => expect(screen.getAllByText("Sabaneta (editado)").length).toBeGreaterThan(1));
    });

    it("si el catálogo falla muestra el error con Reintentar y al reintentar carga", async () => {
      const user = userEvent.setup();
      listMandatoFormats.mockRejectedValueOnce(new Error("boom"));
      renderPanel("formatos");
      expect(await screen.findByTestId("mandatos-formatos-error")).toBeInTheDocument();
      expect(screen.queryByRole("table", { name: /formatos de contrato de mandato/i })).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: /reintentar/i }));
      expect(await screen.findByRole("table", { name: /formatos de contrato de mandato/i })).toBeInTheDocument();
      expect(listMandatoFormats).toHaveBeenCalledTimes(2);
    });

    it("mientras carga muestra un estado de carga", async () => {
      listMandatoFormats.mockReturnValue(new Promise(() => undefined));
      renderPanel("formatos");
      expect(await screen.findByTestId("mandatos-formatos-loading")).toBeInTheDocument();
    });

    it("la automática no ofrece Ver documento: delega en el organismo", async () => {
      const user = userEvent.setup();
      renderPanel("formatos");
      await screen.findByRole("table", { name: /formatos de contrato de mandato/i });
      expect(await hayAccion(user, /editar formato automática/i)).toBe(true);
      expect(await hayAccion(user, /ver documento del formato automática/i, undefined, false)).toBe(false);
      expect(await hayAccion(user, /ver documento del formato genérico/i)).toBe(true);
    });
  });

  describe("HU #13175 pantalla de formatos de contrato", () => {
    const completo = (code: string, name: string, over: Record<string, unknown> = {}) => ({
      ...formato(code, name),
      currentVersion: 2,
      hasCustomTemplate: true,
      rowVersion: 3,
      updatedAt: "2026-09-30T15:00:00Z",
      ...over,
    });

    it("lista nombre, tipo de mandato, versión vigente y fecha de la última edición, sin crear ni eliminar", async () => {
      const user = userEvent.setup();
      listMandatoFormats.mockResolvedValue([
        completo("generico", "Genérico"),
        completo("municipio", "Envigado, Funza y Medellín", { currentVersion: 0, updatedAt: null }),
      ]);
      renderPanel("formatos");
      const tabla = await screen.findByRole("table", { name: /formatos de contrato de mandato/i });
      expect(within(tabla).getByRole("columnheader", { name: /^nombre$/i })).toBeInTheDocument();
      expect(within(tabla).getByRole("columnheader", { name: /tipo de mandato/i })).toBeInTheDocument();
      expect(within(tabla).getByRole("columnheader", { name: /versión vigente/i })).toBeInTheDocument();
      expect(within(tabla).getByRole("columnheader", { name: /última edición/i })).toBeInTheDocument();
      expect(within(tabla).getByText("v2")).toBeInTheDocument();
      expect(within(tabla).getByText("De fábrica")).toBeInTheDocument();
      expect(within(tabla).getByText(/30\/09\/2026/)).toBeInTheDocument();
      // Cada fila tiene Editar con etiqueta accesible por formato.
      expect(await hayAccion(user, /editar formato genérico/i)).toBe(true);
      expect(await hayAccion(user, /editar formato envigado, funza y medellín/i)).toBe(true);
      // No hay acciones de crear ni eliminar formatos.
      expect(screen.queryByRole("button", { name: /(crear|nuevo|agregar|eliminar|borrar).*formato|formato.*(eliminar|borrar)/i })).not.toBeInTheDocument();
    });

    it("editar nombre y tipo llama al PUT con rowVersion y la fila muestra los valores nuevos", async () => {
      const user = userEvent.setup();
      const antes = completo("municipio", "Envigado");
      const despues = completo("municipio", "Envigado jurídico", { assignmentMode: "institutional", rowVersion: 4 });
      listMandatoFormats.mockResolvedValueOnce([antes]).mockResolvedValue([despues]);
      getMandatoFormat.mockResolvedValue({ format: antes, body: "Texto {{placa}}", versions: [] });
      updateMandatoFormat.mockResolvedValue({ format: despues, changed: true, publishedVersion: null });

      renderPanel("formatos");
      await screen.findByRole("table", { name: /formatos de contrato de mandato/i });
      await pulsarAccion(user, /editar formato envigado/i);
      const nombre = await screen.findByLabelText(/nombre del formato/i);
      await user.clear(nombre);
      await user.type(nombre, "Envigado jurídico");
      await user.selectOptions(screen.getByTestId("mandato-formato-tipo"), "institucional");
      await user.click(screen.getByRole("button", { name: /^guardar$/i }));

      await waitFor(() => expect(updateMandatoFormat).toHaveBeenCalledWith("municipio", {
        rowVersion: 3,
        name: "Envigado jurídico",
        assignmentMode: "institutional",
      }));
      const tabla = await screen.findByRole("table", { name: /formatos de contrato de mandato/i });
      await waitFor(() => expect(within(tabla).getByText("Envigado jurídico")).toBeInTheDocument());
      expect(within(tabla).getByText("Institucional (organismo)")).toBeInTheDocument();
      expect(screen.queryByTestId("mandato-formato-editor")).not.toBeInTheDocument();
    });
  });

  describe("Restablecer redacción de fábrica desde Formatos de contrato", () => {
    const completo = (code: string, name: string, over: Record<string, unknown> = {}) => ({
      ...formato(code, name),
      currentVersion: 2,
      hasCustomTemplate: true,
      rowVersion: 3,
      updatedAt: "2026-09-30T15:00:00Z",
      ...over,
    });

    beforeEach(() => resetMandatoFormatTemplate.mockReset());

    it("solo se ofrece en los formatos con plantilla editada, y no en la automática", async () => {
      const user = userEvent.setup();
      listMandatoFormats.mockResolvedValue([
        completo("auto", "Automática", { currentVersion: 0, hasCustomTemplate: false }),
        completo("generico", "Genérico"),
        completo("bello", "Bello", { currentVersion: 0, hasCustomTemplate: false }),
      ]);
      renderPanel("formatos");
      await screen.findByRole("table", { name: /formatos de contrato de mandato/i });

      expect(await hayAccion(user, /restablecer redacción de fábrica del formato genérico/i)).toBe(true);
      expect(await hayAccion(user, /restablecer redacción de fábrica del formato bello/i)).toBe(false);
      expect(await hayAccion(user, /restablecer redacción de fábrica del formato automática/i)).toBe(false);
    }, 20_000);

    it("confirma, llama al API con rowVersion y la fila vuelve a «De fábrica»", async () => {
      const user = userEvent.setup();
      const antes = completo("generico", "Genérico");
      const despues = completo("generico", "Genérico", { currentVersion: 0, rowVersion: 4 });
      listMandatoFormats.mockResolvedValueOnce([antes]).mockResolvedValue([despues]);
      resetMandatoFormatTemplate.mockResolvedValue({ format: despues, changed: true });

      renderPanel("formatos");
      await screen.findByRole("table", { name: /formatos de contrato de mandato/i });
      await pulsarAccion(user, /restablecer redacción de fábrica del formato genérico/i);
      const dialogo = await screen.findByTestId("mandatos-formato-reset-dialog");
      expect(dialogo).toHaveTextContent(/versión 2/);
      expect(dialogo).toHaveTextContent(/historial de versiones se conservan/i);
      await user.click(within(dialogo).getByRole("button", { name: /^restablecer$/i }));

      await waitFor(() => expect(resetMandatoFormatTemplate).toHaveBeenCalledWith("generico", 3));
      expect(await screen.findByText(/volvió a la redacción de fábrica/i)).toBeInTheDocument();
      const tabla = await screen.findByRole("table", { name: /formatos de contrato de mandato/i });
      await waitFor(() => expect(within(tabla).getByText("De fábrica")).toBeInTheDocument());
    });

    it("cancelar no llama al API", async () => {
      const user = userEvent.setup();
      listMandatoFormats.mockResolvedValue([completo("generico", "Genérico")]);
      renderPanel("formatos");
      await screen.findByRole("table", { name: /formatos de contrato de mandato/i });
      await pulsarAccion(user, /restablecer redacción de fábrica del formato genérico/i);
      await user.click(within(await screen.findByTestId("mandatos-formato-reset-dialog")).getByRole("button", { name: /cancelar/i }));

      expect(resetMandatoFormatTemplate).not.toHaveBeenCalled();
      expect(screen.queryByTestId("mandatos-formato-reset-dialog")).not.toBeInTheDocument();
    });

    it("el «Restablecer default» de Configuración por organismo sigue en su pestaña", async () => {
      const user = userEvent.setup();
      renderPanel("organismos");
      await esperarFilas();
      expect(await hayAccion(user, /restablecer default/i)).toBe(true);
    });
  });
});
