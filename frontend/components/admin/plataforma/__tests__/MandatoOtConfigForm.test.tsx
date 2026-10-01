import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { MandatoFormatosState } from "@/hooks/useMandatoFormatos";
import { MandatoOtConfigForm } from "@/components/admin/plataforma/MandatoOtConfigForm";
import type { MandateOtConfigView, MandatoFormatView } from "@/lib/api/admin-plataforma-mandatos";

const listCompanyOtMandateRules = vi.fn();
const fetchMandateSigners = vi.fn();
const upsertMandateOtConfig = vi.fn();
const upsertCompanyOtMandateRule = vi.fn();
const deleteCompanyOtMandateRule = vi.fn();

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
    upsertCompanyOtMandateRule: (...a: unknown[]) => upsertCompanyOtMandateRule(...a),
    deleteCompanyOtMandateRule: (...a: unknown[]) => deleteCompanyOtMandateRule(...a),
    setOtDefaultSigner: vi.fn(),
    setCompanyDefaultSigner: vi.fn(),
  };
});

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: (...a: unknown[]) => fetchMandateSigners(...a),
}));

const fmt = (code: string, name: string, assignmentMode = "signer"): MandatoFormatView => ({
  code,
  name,
  assignmentMode,
  baseRedaction: code === "auto" ? null : code,
  selectableAsRedaction: code !== "auto",
  delegatesToOfficeTemplate: code === "auto", currentVersion: 0, hasCustomTemplate: false, rowVersion: 1, updatedAt: null,
});
const formatosOk: MandatoFormatosState = {
  status: "ready",
  reload: () => undefined,
  formatos: [
    fmt("auto", "Automática (según el organismo)"),
    fmt("generico", "Genérico"),
    fmt("sabaneta", "Sabaneta", "institutional"),
    fmt("bello", "Bello"),
    fmt("municipio", "Envigado, Funza y Medellín"),
  ],
};

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
        formatos={formatosOk}
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
  describe("HU #13174 formatos desde el catálogo del backend", () => {
    const renderCon = (formatos: MandatoFormatosState, office = funza) =>
      render(
        <MandatoOtConfigForm
          office={office}
          mode="mandato"
          formatos={formatos}
          onClose={() => undefined}
          onSaved={() => undefined}
        />,
      );

    it("las opciones y nombres salen de la respuesta, con el nombre editado", async () => {
      renderCon({
        ...formatosOk,
        formatos: [fmt("auto", "Automática"), fmt("municipio", "Envigado renombrado")],
      });
      const select = await screen.findByTestId("mandato-template-select");
      const labels = Array.from(select.querySelectorAll("option")).map((o) => o.textContent);
      expect(labels).toEqual(["Automática", "Envigado renombrado"]);
    });

    it("con el catálogo en error muestra el mensaje con Reintentar y no ofrece opciones", async () => {
      const user = userEvent.setup();
      const reload = vi.fn();
      renderCon({ formatos: [], status: "error", reload });
      expect(await screen.findByTestId("mandato-formatos-error")).toHaveTextContent(/no se pudo cargar/i);
      expect(screen.queryByTestId("mandato-template-select")).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: /reintentar/i }));
      expect(reload).toHaveBeenCalledTimes(1);
    });

    it("mientras carga muestra un estado de carga y no el selector", () => {
      renderCon({ formatos: [], status: "loading", reload: () => undefined });
      expect(screen.getByTestId("mandato-formatos-loading")).toBeInTheDocument();
      expect(screen.queryByTestId("mandato-template-select")).not.toBeInTheDocument();
    });

    it("un código guardado que ya no está en el catálogo se muestra sin romper la pantalla", async () => {
      renderCon(formatosOk, {
        ...funza,
        templateCode: "retirado",
        configuredTemplateCode: "retirado",
      } as MandateOtConfigView);
      const select = (await screen.findByTestId("mandato-template-select")) as HTMLSelectElement;
      expect(select.value).toBe("retirado");
      expect(screen.getByRole("option", { name: /retirado/i })).toBeInTheDocument();
    });

    it("los campos institucionales siguen el tipo del formato del catálogo, no el texto del código", async () => {
      const user = userEvent.setup();
      renderCon({
        ...formatosOk,
        formatos: [fmt("auto", "Automática"), fmt("municipio", "Municipal", "institutional"), fmt("generico", "Genérico")],
      });
      await user.selectOptions(await screen.findByTestId("mandato-template-select"), "municipio");
      expect(screen.getByLabelText(/^mandatario institucional \/ UT$/i)).toBeInTheDocument();
      await user.selectOptions(screen.getByTestId("mandato-template-select"), "generico");
      expect(screen.queryByLabelText(/^mandatario institucional \/ UT$/i)).not.toBeInTheDocument();
    });
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
        formatos={formatosOk}
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
      fireEvent.change(screen.getByLabelText(/^mandatario institucional \/ UT$/i), { target: { value: "UT-SETSA" } });
      fireEvent.change(screen.getByLabelText(/^nit$/i), { target: { value: "900273813-7" } });
      fireEvent.change(screen.getByLabelText(/ciudad cámara/i), { target: { value: "Medellín" } });
      fireEvent.change(screen.getByLabelText(/sigla/i), { target: { value: "SETSA" } });
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
  describe("HU #13151 editar el tipo de mandato de una compañía", () => {
    const base = {
      companyTenantId: "cia-1",
      companyName: "Gestora Uno",
      assignmentMode: "signer",
      mandataryFamily: "individuo",
      institutionalMandataryName: null,
      institutionalMandataryNit: null,
      chamberCity: null,
      mandatarySigla: null,
      hasExplicitRule: true,
      defaultMandateSignerId: null,
      rowVersion: 3,
    };

    const abrir = (editable = true) =>
      render(
        <MandatoOtConfigForm
          office={funza}
          mode="mandatario"
          editableCompanyType={editable}
          onClose={() => undefined}
          onSaved={() => undefined}
        />,
      );

    const abrirEditor = async (user: ReturnType<typeof userEvent.setup>) => {
      await user.click(
        await screen.findByRole("button", { name: /editar tipo de mandato de gestora uno/i }),
      );
      return screen.findByTestId("mandato-tipo-editor");
    };

    beforeEach(() => {
      listCompanyOtMandateRules.mockResolvedValue([base]);
    });

    it("cambia a Persona jurídica: PUT institutional con la entidad y rowVersion", async () => {
      const user = userEvent.setup();
      upsertCompanyOtMandateRule.mockResolvedValue({
        ...base,
        assignmentMode: "institutional",
        institutionalMandataryName: "UT-SETSA",
        rowVersion: 4,
      });
      abrir();
      await abrirEditor(user);
      await user.selectOptions(screen.getByTestId("mandato-tipo-select"), "institucional");
      await user.type(screen.getByLabelText(/nombre de la entidad/i), "UT-SETSA");
      await user.type(screen.getByLabelText(/^nit$/i), "900-1");
      await user.type(screen.getByLabelText(/ciudad de cámara/i), "Medellín");
      await user.type(screen.getByLabelText(/^sigla$/i), "SETSA");
      await user.click(screen.getByRole("button", { name: /^guardar$/i }));
      await waitFor(() => expect(upsertCompanyOtMandateRule).toHaveBeenCalled());
      expect(upsertCompanyOtMandateRule).toHaveBeenCalledWith(
        funza.officeId,
        "cia-1",
        expect.objectContaining({
          assignmentMode: "institutional",
          institutionalMandataryName: "UT-SETSA",
          institutionalMandataryNit: "900-1",
          chamberCity: "Medellín",
          mandatarySigla: "SETSA",
          rowVersion: 3,
        }),
      );
      expect(await screen.findByTestId("mandato-company-tipo-cia-1")).toHaveTextContent("Persona jurídica");
    });

    it("con Persona natural o Mandato abierto no muestra ni envía datos de la entidad", async () => {
      const user = userEvent.setup();
      upsertCompanyOtMandateRule.mockResolvedValue({ ...base, rowVersion: 4 });
      listCompanyOtMandateRules.mockResolvedValue([
        {
          ...base,
          assignmentMode: "institutional",
          institutionalMandataryName: "UT-SETSA",
          chamberCity: "Medellín",
          mandatarySigla: "SETSA",
        },
      ]);
      abrir();
      await abrirEditor(user);
      expect(screen.getByTestId("mandato-tipo-entidad")).toBeInTheDocument();
      await user.selectOptions(screen.getByTestId("mandato-tipo-select"), "persona_rl");
      expect(screen.queryByTestId("mandato-tipo-entidad")).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: /^guardar$/i }));
      await waitFor(() => expect(upsertCompanyOtMandateRule).toHaveBeenCalled());
      expect(upsertCompanyOtMandateRule.mock.calls[0][2]).toMatchObject({
        assignmentMode: "signer",
        institutionalMandataryName: null,
        institutionalMandataryNit: null,
        chamberCity: null,
        mandatarySigla: null,
      });
      // El selector de mandatario default sigue disponible.
      expect(screen.getByTestId("mandato-company-default-signer-cia-1")).toBeInTheDocument();
    });

    it("Mandato abierto pide confirmación y solo al confirmar envía", async () => {
      const user = userEvent.setup();
      upsertCompanyOtMandateRule.mockResolvedValue({ ...base, assignmentMode: "open", rowVersion: 4 });
      abrir();
      await abrirEditor(user);
      await user.selectOptions(screen.getByTestId("mandato-tipo-select"), "abierto");
      await user.click(screen.getByRole("button", { name: /^guardar$/i }));
      const confirm = await screen.findByTestId("mandato-abierto-confirm");
      expect(confirm).toHaveTextContent(/dejará de exigir firma de mandatario/i);
      expect(upsertCompanyOtMandateRule).not.toHaveBeenCalled();
      await user.click(screen.getByRole("button", { name: /confirmar cambio/i }));
      await waitFor(() => expect(upsertCompanyOtMandateRule).toHaveBeenCalledTimes(1));
      expect(upsertCompanyOtMandateRule.mock.calls[0][2]).toMatchObject({ assignmentMode: "open" });
    });

    it("cancelar la confirmación no envía y conserva el tipo anterior", async () => {
      const user = userEvent.setup();
      abrir();
      await abrirEditor(user);
      await user.selectOptions(screen.getByTestId("mandato-tipo-select"), "abierto");
      await user.click(screen.getByRole("button", { name: /^guardar$/i }));
      await screen.findByTestId("mandato-abierto-confirm");
      await user.click(screen.getByRole("button", { name: /cancelar/i }));
      expect(upsertCompanyOtMandateRule).not.toHaveBeenCalled();
      await user.click(screen.getByRole("button", { name: /cancelar/i }));
      expect(screen.getByTestId("mandato-company-tipo-cia-1")).toHaveTextContent("Persona natural");
    });

    it("nombre de entidad vacío no envía; el código del API muestra el mismo mensaje", async () => {
      const user = userEvent.setup();
      abrir();
      await abrirEditor(user);
      await user.selectOptions(screen.getByTestId("mandato-tipo-select"), "institucional");
      await user.click(screen.getByRole("button", { name: /^guardar$/i }));
      expect(await screen.findByText(/nombre de la entidad es obligatorio/i)).toBeInTheDocument();
      expect(upsertCompanyOtMandateRule).not.toHaveBeenCalled();

      upsertCompanyOtMandateRule.mockRejectedValue(
        new ApiError(400, "x", { error: "mandatario_institucional_requerido" }),
      );
      await user.type(screen.getByLabelText(/nombre de la entidad/i), "   a");
      await user.click(screen.getByRole("button", { name: /^guardar$/i }));
      expect(await screen.findByText(/nombre de la entidad es obligatorio/i)).toBeInTheDocument();
    });

    it("409 row_version_conflict: mensaje claro, recarga la fila y conserva lo escrito", async () => {
      const user = userEvent.setup();
      upsertCompanyOtMandateRule.mockRejectedValue(
        new ApiError(409, "conflict", { error: "row_version_conflict" }),
      );
      abrir();
      await abrirEditor(user);
      await user.selectOptions(screen.getByTestId("mandato-tipo-select"), "institucional");
      await user.type(screen.getByLabelText(/nombre de la entidad/i), "Mi entidad");
      listCompanyOtMandateRules.mockResolvedValue([
        { ...base, assignmentMode: "open", rowVersion: 9 },
      ]);
      await user.click(screen.getByRole("button", { name: /^guardar$/i }));
      expect(await screen.findByTestId("mandato-tipo-error")).toHaveTextContent(/otra persona modificó la regla/i);
      await waitFor(() => expect(listCompanyOtMandateRules).toHaveBeenCalledTimes(2));
      expect(screen.getByLabelText(/nombre de la entidad/i)).toHaveValue("Mi entidad");
      expect(await screen.findByTestId("mandato-company-tipo-cia-1")).toHaveTextContent("Mandato abierto");
      expect(screen.getByRole("button", { name: /recargar el tipo actual/i })).toBeInTheDocument();
    });

    it("Volver al default: confirma y llama DELETE con rowVersion", async () => {
      const user = userEvent.setup();
      deleteCompanyOtMandateRule.mockResolvedValue(undefined);
      abrir();
      await user.click(
        await screen.findByRole("button", { name: /volver al default de gestora uno/i }),
      );
      await screen.findByTestId("mandato-volver-default");
      listCompanyOtMandateRules.mockResolvedValue([
        { ...base, hasExplicitRule: false, rowVersion: null },
      ]);
      await user.click(screen.getByRole("button", { name: /^volver al default$/i }));
      await waitFor(() =>
        expect(deleteCompanyOtMandateRule).toHaveBeenCalledWith(funza.officeId, "cia-1", undefined, 3),
      );
      expect(await screen.findByText("Default")).toBeInTheDocument();
    });

    it("no ofrece Volver al default sin regla propia ni el control fuera de Plataforma", async () => {
      listCompanyOtMandateRules.mockResolvedValue([{ ...base, hasExplicitRule: false, rowVersion: null }]);
      const { unmount } = abrir();
      await screen.findByRole("button", { name: /editar tipo de mandato de gestora uno/i });
      expect(screen.queryByRole("button", { name: /volver al default de/i })).not.toBeInTheDocument();
      unmount();
      abrir(false);
      await screen.findByTestId("mandato-company-tipo-cia-1");
      expect(screen.queryByRole("button", { name: /editar tipo de mandato de/i })).not.toBeInTheDocument();
    });

    it("mientras guarda, deshabilita los controles y muestra el loader", async () => {
      const user = userEvent.setup();
      let resolve: (v: unknown) => void = () => undefined;
      upsertCompanyOtMandateRule.mockReturnValue(new Promise((r) => (resolve = r)));
      abrir();
      await abrirEditor(user);
      await user.click(screen.getByRole("button", { name: /^guardar$/i }));
      await waitFor(() => expect(screen.getByRole("button", { name: /^guardar$/i })).toBeDisabled());
      expect(screen.getByTestId("mandato-tipo-select")).toBeDisabled();
      expect(screen.getByRole("button", { name: /editar tipo de mandato de gestora uno/i })).toBeDisabled();
      resolve({ ...base, rowVersion: 4 });
      await waitFor(() => expect(screen.queryByTestId("mandato-tipo-editor")).not.toBeInTheDocument());
    });
  });
});
