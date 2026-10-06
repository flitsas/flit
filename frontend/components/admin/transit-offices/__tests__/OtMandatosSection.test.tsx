import { render, screen, waitFor, within, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { accionDeshabilitada, hayAccion, pulsarAccion } from "@/lib/test-acciones";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { OtMandatosSection } from "@/components/admin/transit-offices/OtMandatosSection";
import { ToastProvider } from "@/components/admin/Toast";
import type { CompanyOtMandateRuleView } from "@/lib/api/admin-plataforma-mandatos";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

function loginAs(roleCode: string) {
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({
      sub: "u-1",
      exp: Math.floor(Date.now() / 1000) + 3600,
      roles: [{ id: "r-1", code: roleCode }],
      role: roleCode,
      entity_type: "TRANSIT_OFFICE",
    }),
  );
}

const fetchMandateOtConfig = vi.fn();
const listCompanyOtMandateRules = vi.fn();
const fetchCompanyTransitOffices = vi.fn();
const fetchRepresentedCompanies = vi.fn();
const fetchOtAssociableCompanies = vi.fn();
const createMandateSigner = vi.fn();
const fetchMandateSigners = vi.fn();
const fetchMandateSignerSignatureImage = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", () => ({
  fetchMandateOtConfig: (...a: unknown[]) => fetchMandateOtConfig(...a),
  listCompanyOtMandateRules: (...a: unknown[]) => listCompanyOtMandateRules(...a),
  upsertMandateOtConfig: vi.fn(),
  upsertCompanyOtMandateRule: vi.fn(),
  deleteCompanyOtMandateRule: vi.fn(),
  fetchMandateOtPreview: vi.fn(),
  fetchMandatoTemplatePreview: vi.fn(),
  fetchMandateSigners: vi.fn(),
  listOtMandatoFormats: vi.fn().mockResolvedValue([
    { code: "generico", name: "Genérico del catálogo", assignmentMode: "signer" },
  ]),
}));

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: (...a: unknown[]) => fetchMandateSigners(...a),
  fetchMandateSignerSignatureImage: (...a: unknown[]) => fetchMandateSignerSignatureImage(...a),
  fetchCompanyTransitOffices: (...a: unknown[]) => fetchCompanyTransitOffices(...a),
  fetchRepresentedCompanies: (...a: unknown[]) => fetchRepresentedCompanies(...a),
  createMandateSigner: (...a: unknown[]) => createMandateSigner(...a),
  fetchOtAssociableCompanies: (...a: unknown[]) => fetchOtAssociableCompanies(...a),
}));

vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

const office = {
  officeId: "ot-1",
  code: "11001000",
  name: "OT Bogotá",
  templateCode: "generico",
  configuredTemplateCode: "generico",
  requiresForNaturalPerson: false,
  mandataryFamily: "individuo",
  assignmentMode: "open",
  institutionalMandataryName: null,
  institutionalMandataryNit: null,
  chamberCity: null,
  mandatarySigla: null,
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
};

function pagina(
  items: { id: string; name: string; nit: string }[],
  total = items.length,
) {
  return { items, total, page: 1, pageSize: 10, aplicaSoloASuCompania: false };
}

const GESTORA = { id: "cia-1", name: "Gestora de Prueba S.A.S.", nit: "900123456" };

function companyRow(overrides: Partial<CompanyOtMandateRuleView> = {}): CompanyOtMandateRuleView {
  return {
    companyTenantId: "cia-1",
    companyName: "Gestora de Prueba S.A.S.",
    assignmentMode: "open",
    mandataryFamily: "individuo",
    institutionalMandataryName: null,
    institutionalMandataryNit: null,
    chamberCity: null,
    mandatarySigla: null,
    hasExplicitRule: false,
    defaultMandateSignerId: null,
    companyTaxId: "900123456",
    companyCode: "CIA-1",
    defaultMandateSignerName: null,
    defaultMandateSignerDocumentType: null,
    defaultMandateSignerDocumentNumber: null,
    defaultMandateSignerIntegrityHash: null,
    rowVersion: null,
    ...overrides,
  };
}

describe("OtMandatosSection", () => {
  beforeEach(() => {
    fetchMandateOtConfig.mockReset();
    listCompanyOtMandateRules.mockReset();
    fetchMandateSigners.mockReset();
    fetchMandateSignerSignatureImage.mockReset();
    fetchCompanyTransitOffices.mockReset();
    fetchRepresentedCompanies.mockReset();
    fetchOtAssociableCompanies.mockReset();
    fetchOtAssociableCompanies.mockResolvedValue(pagina([GESTORA]));
    createMandateSigner.mockReset();
    window.localStorage.clear();
    loginAs("ot_admin");
    listCompanyOtMandateRules.mockResolvedValue([]);
    fetchMandateSigners.mockResolvedValue([]);
    fetchMandateSignerSignatureImage.mockResolvedValue(new Blob(["png"], { type: "image/png" }));
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:firma-mock");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
    fetchCompanyTransitOffices.mockResolvedValue([
      { transitOfficeId: "ot-1", code: "11001000", name: "OT Bogotá" },
    ]);
    fetchRepresentedCompanies.mockResolvedValue([]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("lista empresas habilitadas y permite editar el mandatario", async () => {
    const user = userEvent.setup();
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([
      companyRow({
        defaultMandateSignerName: "Carlos Pérez",
        defaultMandateSignerDocumentType: "CC",
        defaultMandateSignerDocumentNumber: "1020304050",
        defaultMandateSignerIntegrityHash: "a".repeat(64),
      }),
    ]);
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("tab", { name: /^Compañías/ }));
    expect(await screen.findByTestId("ot-mandatos-company-table")).toBeInTheDocument();
    expect(await screen.findByText("Gestora de Prueba S.A.S.")).toBeInTheDocument();
    expect(screen.getByText("900123456")).toBeInTheDocument();
    expect(screen.queryByText("CIA-1")).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: /^código$/i })).not.toBeInTheDocument();
    expect(screen.getByText("Carlos Pérez")).toBeInTheDocument();
    expect(await hayAccion(user, /editar mandatario de gestora de prueba/i)).toBe(true);
  });

  it("muestra Sin definir cuando el OT no tiene mandatario general", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow()]);
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByTestId("ot-mandatos-general-signer")).toHaveTextContent(/todavía no hay un mandatario general/i);
  });

  async function abrirFormularioGeneral(user: ReturnType<typeof userEvent.setup>) {
    await pulsarAccion(user, /editar mandatario general del organismo/i);
    await user.click(await screen.findByTestId("mandato-ot-register-signer"));
    return screen.findByRole("dialog", { name: /registrar mandatario/i });
  }

  async function diligenciar(user: ReturnType<typeof userEvent.setup>) {
    await user.type(screen.getByLabelText("Nombre completo"), "Ana Mandataria");
    await user.type(screen.getByLabelText("Número de documento"), "52123456");
    // HU #13248: con validación de identidad el correo es obligatorio.
    await user.type(screen.getByLabelText(/^Correo/), "ana@ot.test");
    // HU #13132: la Persona natural exige forma de firma.
    await user.click(screen.getByRole("radio", { name: "Validación de identidad" }));
  }

  it("con onFormatoChange la sección no pinta la tarjeta del formato: se la entrega a quien la pase", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow()]);
    const onFormatoChange = vi.fn();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" onFormatoChange={onFormatoChange} />
      </ToastProvider>,
    );
    await screen.findByTestId("ot-mandatos-section");
    expect(screen.queryByTestId("ot-formato-contrato")).not.toBeInTheDocument();
    await waitFor(() =>
      expect(onFormatoChange).toHaveBeenLastCalledWith({ nombre: "Genérico del catálogo", fallo: false }),
    );
  });

  it("AC1/AC2/AC3/AC6 ot_admin registra solo contra el endpoint del OT, sin baúl ni empresas representadas", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow()]);
    createMandateSigner.mockResolvedValue({ id: "ms-9", integrityHash: "h" });
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await abrirFormularioGeneral(user)).toBeInTheDocument();
    expect(screen.getByTestId("ot-formato-contrato")).toHaveTextContent("Genérico del catálogo");
    // AC6: sin selector del baúl ni bloque de empresas representadas.
    expect(screen.queryByLabelText(/firma del baúl/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/firma solo para/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/firma de forma física/i)).not.toBeInTheDocument();
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(createMandateSigner).toHaveBeenCalledTimes(1));
    expect(createMandateSigner).toHaveBeenCalledWith(
      "ot-1",
      expect.objectContaining({
        fullName: "Ana Mandataria",
        documentNumber: "52123456",
        companyTenantIds: ["cia-1"],
        transitOfficeIds: ["ot-1"],
        signerModel: "natural",
        signatureMethod: "biometria",
        validityKind: "fixed",
      }),
    );
    expect(await screen.findByText(/mandatario registrado/i)).toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: /registrar mandatario/i })).not.toBeInTheDocument();
    // AC3: ninguna lectura de rutas de la compañía.
    expect(fetchCompanyTransitOffices).not.toHaveBeenCalled();
    expect(fetchRepresentedCompanies).not.toHaveBeenCalled();
  });

  it("AC4 muestra el mensaje del 422 en lenguaje claro y deja el formulario abierto", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow()]);
    createMandateSigner.mockRejectedValue(
      new ApiValidationError(
        [{ field: "transitOfficeIds", message: "Ya existe un mandatario para esta empresa en este organismo." }],
        422,
      ),
    );
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    await abrirFormularioGeneral(user);
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Ya existe un mandatario para esta empresa en este organismo.",
    );
    expect(screen.getByRole("dialog", { name: /registrar mandatario/i })).toBeInTheDocument();
  });

  it("AC5 muestra falta de permiso ante 403 y no cierra el formulario", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow()]);
    createMandateSigner.mockRejectedValue(new ApiError(403, "Forbidden"));
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    await abrirFormularioGeneral(user);
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/no tienes permiso/i);
    expect(screen.getByRole("dialog", { name: /registrar mandatario/i })).toBeInTheDocument();
  });

  it("un Operador OT (gestor_tramites_ot) no ve «Registrar mandatario»", async () => {
    loginAs("gestor_tramites_ot");
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow()]);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(screen.queryByRole("button", { name: "Nuevo mandatario" })).not.toBeInTheDocument();
    await pulsarAccion(user, /editar mandatario general del organismo/i);
    await screen.findByTestId("mandato-ot-config-form");
    expect(screen.queryByTestId("mandato-ot-register-signer")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^registrar mandatario$/i })).not.toBeInTheDocument();
  });

  it("el Admin OT registra un mandatario desde la pestaña, con el organismo fijo y sin compañía obligatoria", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    createMandateSigner.mockResolvedValue({ id: "ms-nuevo", integrityHash: "h" });
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    await user.click(await screen.findByRole("button", { name: "Nuevo mandatario" }));
    expect(await screen.findByRole("dialog", { name: /registrar mandatario/i })).toBeInTheDocument();
    expect(screen.getByTestId("mandatario-organismo-fijo")).toHaveTextContent("OT Bogotá");
    expect(screen.getByText(/queda en el organismo/i)).toBeInTheDocument();
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(createMandateSigner).toHaveBeenCalledTimes(1));
    expect(createMandateSigner).toHaveBeenCalledWith(
      "ot-1",
      expect.objectContaining({
        fullName: "Ana Mandataria",
        companyTenantIds: [],
        transitOfficeIds: ["ot-1"],
      }),
    );
    expect(await screen.findByText(/mandatario registrado/i)).toBeInTheDocument();
  });

  it("abre la configuración de mandatario de la empresa", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow({ assignmentMode: "signer", hasExplicitRule: true })]);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("tab", { name: /^Compañías/ }));
    await pulsarAccion(user, /editar mandatario de gestora de prueba/i);
    expect(await screen.findByTestId("mandato-ot-config-form")).toHaveAttribute("data-mode", "mandatario");
  });

  it("al cerrar el panel de empresa recarga la grilla", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockImplementation(() => {
      const call = listCompanyOtMandateRules.mock.calls.length;
      const name = call >= 3 ? "Ana López" : "Carlos Pérez";
      return Promise.resolve([companyRow({ defaultMandateSignerName: name })]);
    });
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("tab", { name: /^Compañías/ }));
    expect(await screen.findByText("Carlos Pérez")).toBeInTheDocument();
    await pulsarAccion(user, /editar mandatario de gestora de prueba/i);
    const closeButtons = await screen.findAllByRole("button", { name: /^cerrar$/i });
    const footerClose = closeButtons.find((el) => el.textContent?.trim() === "Cerrar");
    expect(footerClose).toBeDefined();
    await user.click(footerClose!);
    expect(await screen.findByText("Ana López")).toBeInTheDocument();
    expect(listCompanyOtMandateRules.mock.calls.length).toBeGreaterThanOrEqual(3);
  });

  it("muestra vacío cuando no hay compañías activas", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    fetchOtAssociableCompanies.mockResolvedValue(pagina([]));
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("tab", { name: /^Compañías/ }));
    expect(await screen.findByText(/no hay compañías activas/i)).toBeInTheDocument();
    expect(screen.getByText(/no hay mandatarios creados en este organismo/i)).toBeInTheDocument();
  });

  it("HU #13182 AC1/AC2: lista compañías sin trámites con nombre y NIT, sin mandatario definido, y abre su configuración", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([]);
    fetchOtAssociableCompanies.mockResolvedValue(
      pagina([GESTORA, { id: "cia-2", name: "Nunca Radicó Ltda.", nit: "800555111" }], 2),
    );
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("tab", { name: /^Compañías/ }));
    const tabla = await screen.findByRole("table", { name: "Compañías activas" });
    expect(within(tabla).getByText("Nunca Radicó Ltda.")).toBeInTheDocument();
    expect(within(tabla).getByText("800555111")).toBeInTheDocument();
    // Sin reglas, sin general ni mandatarios activos: HU #13139 la marca «Sin mandatario».
    expect(within(tabla).getAllByTestId("ot-mandatos-sin-mandatario")).toHaveLength(2);
    expect(fetchOtAssociableCompanies).toHaveBeenCalledWith(
      "ot-1",
      expect.objectContaining({ page: 1, pageSize: 10 }),
      expect.anything(),
    );
    // «Filas por página» del modelo de trámites.
    expect(screen.getAllByText(/filas por página/i).length).toBeGreaterThan(0);
    await pulsarAccion(user, /editar mandatario de nunca radicó ltda/i);
    expect(await screen.findByTestId("mandato-ot-config-form")).toHaveAttribute("data-mode", "mandatario");
    expect((await screen.findAllByText("Nunca Radicó Ltda.")).length).toBeGreaterThan(0);
  });

  it("HU #13182 AC1: busca por nombre o NIT en el servidor (mínimo 2 caracteres) y vuelve a la página 1", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("tab", { name: /^Compañías/ }));
    await screen.findByRole("table", { name: "Compañías activas" });
    fetchOtAssociableCompanies.mockClear();
    const buscador = screen.getByTestId("ot-mandatos-company-search");
    await user.type(buscador, "9");
    expect(await screen.findByTestId("ot-mandatos-busqueda-ayuda")).toHaveTextContent(/al menos 2/i);
    await user.type(buscador, "00");
    await waitFor(() =>
      expect(fetchOtAssociableCompanies).toHaveBeenCalledWith(
        "ot-1",
        expect.objectContaining({ search: "900", page: 1 }),
        expect.anything(),
      ),
    );
    // Nunca se envía una búsqueda de un solo carácter (el servidor respondería 422).
    expect(
      fetchOtAssociableCompanies.mock.calls.some(([, q]) => (q as { search?: string }).search === "9"),
    ).toBe(false);
  });

  it("HU #13182 AC4: sin coincidencias muestra «Sin resultados»", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("tab", { name: /^Compañías/ }));
    await screen.findByRole("table", { name: "Compañías activas" });
    fetchOtAssociableCompanies.mockResolvedValue(pagina([]));
    await user.type(screen.getByTestId("ot-mandatos-company-search"), "zzz");
    expect(await screen.findByText("Sin resultados")).toBeInTheDocument();
  });

  it("HU #13182 AC4: un fallo de consulta muestra el error con reintento, no una lista vacía", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    fetchOtAssociableCompanies.mockRejectedValueOnce(new ApiError(500, "boom"));
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("tab", { name: /^Compañías/ }));
    expect(await screen.findByText("No se pudieron cargar las compañías.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByRole("table", { name: "Compañías activas" })).toBeInTheDocument();
  });

  it("lista mandatarios creados con tipo de firma y permite verla", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    fetchMandateSigners.mockResolvedValue([
      {
        id: "ms-1",
        transitOfficeId: "ot-1",
        fullName: "Hugo Mandatario",
        documentType: "CC",
        documentNumber: "52123456",
        integrityHash: "h".repeat(64),
        email: "hugo@ot.test",
        userId: null,
        identityStatus: "none",
        signatureVaultId: "vault-1",
        registeredAt: "2026-01-01T00:00:00Z",
        isActive: true,
        companyTenantIds: [],
        signerModel: "natural",
        validityKind: "fixed",
        validityStatus: "vigente",
      },
    ]);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByRole("table", { name: "Mandatarios del organismo" })).toBeInTheDocument();
    expect(screen.getByText("Hugo Mandatario")).toBeInTheDocument();
    expect(screen.getByText("CC")).toBeInTheDocument();
    expect(screen.getByText("52123456")).toBeInTheDocument();
    expect(screen.getByText("Baúl de firmas")).toBeInTheDocument();
    // HU #13133 — modelo y estado de vigencia en la fila; ninguna opción de firma física.
    expect(screen.getByText("Persona natural")).toBeInTheDocument();
    expect(screen.getByTestId("mandatario-vigencia")).toHaveTextContent("Vigente");
    expect(screen.queryByText(/firma a mano|f[ií]sica/i)).not.toBeInTheDocument();
    await pulsarAccion(user, /ver firma de hugo mandatario/i);
    expect(await screen.findByRole("dialog", { name: /firma de hugo mandatario/i })).toBeInTheDocument();
    expect(await screen.findByRole("img", { name: /firma de hugo mandatario/i })).toBeInTheDocument();
    expect(fetchMandateSignerSignatureImage).toHaveBeenCalledWith("ot-1", "ms-1");
  });

  it("HU #13133 AC1-AC4: modelo y vigencia por fila; jurídica y formato en blanco sin vigencia ni «Ver firma»; sin firma física", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    const base = {
      transitOfficeId: "ot-1",
      documentType: "CC",
      integrityHash: "h".repeat(64),
      email: null,
      userId: null,
      identityStatus: "none",
      signatureVaultId: null,
      registeredAt: "2026-01-01T00:00:00Z",
      isActive: true,
      companyTenantIds: [],
    };
    fetchMandateSigners.mockResolvedValue([
      { ...base, id: "a", fullName: "Ana Vigente", documentNumber: "1", signerModel: "natural", validityStatus: "vigente" },
      { ...base, id: "b", fullName: "Beto Porvencer", documentNumber: "2", signerModel: "natural", validityStatus: "por_vencer", validityKind: "range", validTo: "2026-10-05" },
      { ...base, id: "c", fullName: "Carla Vencida", documentNumber: "3", signerModel: "natural", validityStatus: "vencido" },
      { ...base, id: "d", fullName: "Dario Inactivo", documentNumber: "4", signerModel: "natural", validityStatus: "inactivo", isActive: false },
      { ...base, id: "e", fullName: "Entidad SAS", documentNumber: "9001", signerModel: "juridica" },
      { ...base, id: "f", fullName: "Sin nombre propio", documentNumber: null, signerModel: "formato_blanco" },
    ]);
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    const tabla = await screen.findByRole("table", { name: "Mandatarios del organismo" });
    const fila = (nombre: string) => within(tabla).getByText(nombre).closest("tr") as HTMLElement;
    expect(within(fila("Ana Vigente")).getByTestId("mandatario-vigencia")).toHaveTextContent("Vigente");
    const porVencer = within(fila("Beto Porvencer")).getByTestId("mandatario-vigencia");
    expect(porVencer).toHaveTextContent("Por vencer");
    expect(porVencer).toHaveTextContent("Hasta 05/10/2026");
    expect(within(fila("Carla Vencida")).getByTestId("mandatario-vigencia")).toHaveTextContent("Vencido");
    expect(within(fila("Dario Inactivo")).getByTestId("mandatario-vigencia")).toHaveTextContent("Inactivo");
    const juridica = fila("Entidad SAS");
    expect(within(juridica).getByText("Persona jurídica")).toBeInTheDocument();
    expect(within(juridica).getByTestId("mandatario-vigencia")).toHaveAttribute("data-estado", "sin_vigencia");
    expect(within(juridica).queryByRole("button", { name: /ver firma/i })).not.toBeInTheDocument();
    expect(within(fila("Sin nombre propio")).getByTestId("mandatario-vigencia")).toHaveAttribute(
      "data-estado",
      "sin_vigencia",
    );
    expect(within(tabla).queryByText(/firma a mano|f[ií]sica/i)).not.toBeInTheDocument();
  });

  it("muestra error con reintentar", async () => {
    fetchMandateOtConfig.mockRejectedValue(new Error("boom"));
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    await waitFor(() => expect(fetchMandateOtConfig).toHaveBeenCalledTimes(2));
  });
});
