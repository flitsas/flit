import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MandatoFormatoEditor } from "@/components/admin/plataforma/MandatoFormatoEditor";
import { ApiError } from "@/lib/api/types";
import type { MandatoFormatDetail, MandatoFormatView } from "@/lib/api/admin-plataforma-mandatos";

// HU #13175 — editor de un formato de contrato de mandato (nombre, tipo y plantilla).

const getMandatoFormat = vi.fn();
const getMandatoFormatVersion = vi.fn();
const updateMandatoFormat = vi.fn();
const previewMandatoFormatDraft = vi.fn();
const openPdfBlobInNewTab = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-plataforma-mandatos")>();
  return {
    ...actual,
    getMandatoFormat: (...a: unknown[]) => getMandatoFormat(...a),
    getMandatoFormatVersion: (...a: unknown[]) => getMandatoFormatVersion(...a),
    updateMandatoFormat: (...a: unknown[]) => updateMandatoFormat(...a),
    previewMandatoFormatDraft: (...a: unknown[]) => previewMandatoFormatDraft(...a),
  };
});
vi.mock("@/lib/documents/open-document-tab", () => ({
  openPdfBlobInNewTab: (...a: unknown[]) => openPdfBlobInNewTab(...a),
}));

const envigado: MandatoFormatView = {
  code: "municipio",
  name: "Envigado, Funza y Medellín",
  assignmentMode: "signer",
  baseRedaction: "municipio",
  selectableAsRedaction: true,
  delegatesToOfficeTemplate: false,
  currentVersion: 2,
  hasCustomTemplate: true,
  rowVersion: 7,
  updatedAt: "2026-09-30T15:00:00Z",
};

const detail = (over: Partial<MandatoFormatDetail> = {}): MandatoFormatDetail => ({
  format: envigado,
  body: "Entre {{mandante_nombre}} y el mandatario.",
  versions: [
    { versionNumber: 2, sha256: "b", createdAt: "2026-09-30T15:00:00Z", createdBy: "11111111-2222-3333-4444-555555555555" },
    { versionNumber: 1, sha256: "a", createdAt: "2026-09-01T15:00:00Z", createdBy: null },
  ],
  ...over,
});

const onSaved = vi.fn();
const onConflict = vi.fn();
const onClose = vi.fn();

async function abrir(d: MandatoFormatDetail = detail()) {
  getMandatoFormat.mockResolvedValue(d);
  render(
    <MandatoFormatoEditor code={d.format.code} onSaved={onSaved} onConflict={onConflict} onClose={onClose} />,
  );
  return screen.findByTestId("mandato-formato-cuerpo").catch(() => screen.findByTestId("mandato-formato-editor"));
}

const guardar = () => screen.getByRole("button", { name: /^(guardar|publicar)$/i });

describe("MandatoFormatoEditor (HU #13175)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    updateMandatoFormat.mockResolvedValue({ format: envigado, changed: true, publishedVersion: null });
    previewMandatoFormatDraft.mockResolvedValue(new Blob(["%PDF"], { type: "application/pdf" }));
    openPdfBlobInNewTab.mockResolvedValue(undefined);
  });

  it("carga el formato y muestra nombre, tipo y la lista de variables permitidas sin fecha de firma, vigencia ni «fijo»", async () => {
    await abrir();
    expect(await screen.findByLabelText(/nombre del formato/i)).toHaveValue("Envigado, Funza y Medellín");
    expect(screen.getByLabelText(/tipo de mandato/i)).toHaveValue("persona_rl");
    const variables = screen.getByTestId("mandato-formato-variables");
    expect(within(variables).getByRole("button", { name: /insertar variable placa/i })).toBeInTheDocument();
    expect(variables.textContent).not.toMatch(/fecha_firma|fecha_hora_firma|vigencia|fijo/i);
  });

  it("muestra el error de carga con Reintentar", async () => {
    getMandatoFormat.mockRejectedValueOnce(new Error("x")).mockResolvedValueOnce(detail());
    const user = userEvent.setup();
    render(<MandatoFormatoEditor code="municipio" onSaved={onSaved} onConflict={onConflict} onClose={onClose} />);
    expect(await screen.findByTestId("mandato-formato-error")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByLabelText(/nombre del formato/i)).toBeInTheDocument();
  });

  it("estado de carga mientras llega el formato", () => {
    getMandatoFormat.mockReturnValue(new Promise(() => undefined));
    render(<MandatoFormatoEditor code="municipio" onSaved={onSaved} onConflict={onConflict} onClose={onClose} />);
    expect(screen.getByTestId("mandato-formato-loading")).toBeInTheDocument();
  });

  it("cambiar nombre y tipo envía el PUT con rowVersion y solo esos campos, sin confirmación", async () => {
    const user = userEvent.setup();
    await abrir();
    const nombre = await screen.findByLabelText(/nombre del formato/i);
    await user.clear(nombre);
    await user.type(nombre, "Envigado");
    await user.selectOptions(screen.getByTestId("mandato-formato-tipo"), "institucional");
    await user.click(guardar());
    await waitFor(() => expect(updateMandatoFormat).toHaveBeenCalledTimes(1));
    expect(updateMandatoFormat).toHaveBeenCalledWith("municipio", {
      rowVersion: 7,
      name: "Envigado",
      assignmentMode: "institutional",
    });
    expect(onSaved).toHaveBeenCalledWith(envigado, { published: null, changed: true });
    expect(screen.queryByTestId("mandato-formato-confirmacion")).not.toBeInTheDocument();
  });

  it("sin cambios el botón de guardar está deshabilitado", async () => {
    await abrir();
    await screen.findByLabelText(/nombre del formato/i);
    expect(guardar()).toBeDisabled();
  });

  it("insertar una variable la escribe en la plantilla", async () => {
    const user = userEvent.setup();
    await abrir();
    const cuerpo = (await screen.findByTestId("mandato-formato-cuerpo")) as HTMLTextAreaElement;
    await user.click(screen.getByRole("button", { name: /insertar variable placa/i }));
    expect(cuerpo.value).toContain("{{placa}}");
  });

  it("publicar una plantilla modificada pide confirmación con las tres advertencias y solo al confirmar envía", async () => {
    const user = userEvent.setup();
    updateMandatoFormat.mockResolvedValue({ format: envigado, changed: true, publishedVersion: 3 });
    await abrir();
    const cuerpo = await screen.findByTestId("mandato-formato-cuerpo");
    await user.type(cuerpo, " Nuevo texto.");
    await user.click(screen.getByRole("button", { name: /^publicar$/i }));
    const dialogo = await screen.findByTestId("mandato-formato-confirmacion");
    expect(dialogo).toHaveTextContent(/trámites nuevos/i);
    expect(dialogo).toHaveTextContent(/contratos ya emitidos no cambian/i);
    expect(dialogo).toHaveTextContent(/PO y de jurídico/i);
    expect(updateMandatoFormat).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /confirmar y publicar/i }));
    await waitFor(() => expect(updateMandatoFormat).toHaveBeenCalledTimes(1));
    expect(updateMandatoFormat.mock.calls[0][1]).toMatchObject({ rowVersion: 7, body: expect.stringContaining("Nuevo texto.") });
    expect(onSaved).toHaveBeenCalledWith(envigado, { published: 3, changed: true });
  });

  it("cancelar la confirmación no envía ninguna petición y conserva el texto", async () => {
    const user = userEvent.setup();
    await abrir();
    const cuerpo = (await screen.findByTestId("mandato-formato-cuerpo")) as HTMLTextAreaElement;
    await user.type(cuerpo, " Cambio.");
    await user.click(screen.getByRole("button", { name: /^publicar$/i }));
    const dialogo = await screen.findByTestId("mandato-formato-confirmacion");
    await user.click(within(dialogo).getByRole("button", { name: /^cancelar$/i }));
    expect(screen.queryByTestId("mandato-formato-confirmacion")).not.toBeInTheDocument();
    expect(updateMandatoFormat).not.toHaveBeenCalled();
    expect(cuerpo.value).toContain("Cambio.");
  });

  it("variable inválida: muestra la lista del API y no da el formato por guardado", async () => {
    const user = userEvent.setup();
    updateMandatoFormat.mockRejectedValue(
      new ApiError(400, "plantilla_variable_invalida", {
        error: "plantilla_variable_invalida",
        unknownVariables: [{ name: "fecha_x", line: 2, column: 5, index: 10 }],
      }),
    );
    await abrir();
    await user.type(await screen.findByTestId("mandato-formato-cuerpo"), " {{fecha_x}}");
    await user.click(screen.getByRole("button", { name: /^publicar$/i }));
    await user.click(await screen.findByRole("button", { name: /confirmar y publicar/i }));
    const lista = await screen.findByTestId("mandato-formato-variables-invalidas");
    expect(lista).toHaveTextContent("{{fecha_x}}");
    expect(lista).toHaveTextContent(/línea 2/i);
    expect(onSaved).not.toHaveBeenCalled();
  });

  it("vista previa: abre el PDF de muestra del borrador sin guardar nada", async () => {
    const user = userEvent.setup();
    await abrir();
    await screen.findByTestId("mandato-formato-cuerpo");
    await user.click(screen.getByRole("button", { name: /vista previa/i }));
    await waitFor(() => expect(openPdfBlobInNewTab).toHaveBeenCalled());
    const factory = openPdfBlobInNewTab.mock.calls[0][0] as () => Promise<Blob>;
    await factory();
    expect(previewMandatoFormatDraft).toHaveBeenCalledWith("municipio", expect.stringContaining("mandante_nombre"));
    expect(updateMandatoFormat).not.toHaveBeenCalled();
  });

  it("vista previa con variable inválida muestra la lista devuelta por el API", async () => {
    const user = userEvent.setup();
    openPdfBlobInNewTab.mockImplementation(async (factory: () => Promise<Blob>) => {
      await factory();
    });
    previewMandatoFormatDraft.mockRejectedValue(
      new ApiError(400, "x", { error: "plantilla_variable_invalida", unknownVariables: [{ name: "nope" }] }),
    );
    await abrir();
    await screen.findByTestId("mandato-formato-cuerpo");
    await user.click(screen.getByRole("button", { name: /vista previa/i }));
    expect(await screen.findByTestId("mandato-formato-variables-invalidas")).toHaveTextContent("{{nope}}");
  });

  it("vista previa con variable inválida: el helper real envuelve el ApiError en `cause` y igual se ve la lista con su posición", async () => {
    const user = userEvent.setup();
    openPdfBlobInNewTab.mockImplementation(async (factory: () => Promise<Blob>) => {
      try {
        await factory();
      } catch (err) {
        throw new Error("document_preview_failed", { cause: err });
      }
    });
    previewMandatoFormatDraft.mockRejectedValue(
      new ApiError(400, "plantilla_variable_invalida", {
        error: "plantilla_variable_invalida",
        unknownVariables: [{ name: "cedula_inventada", line: 3, column: 7 }],
      }),
    );
    await abrir();
    await screen.findByTestId("mandato-formato-cuerpo");
    await user.click(screen.getByRole("button", { name: /vista previa/i }));
    const lista = await screen.findByTestId("mandato-formato-variables-invalidas");
    expect(lista).toHaveTextContent("{{cedula_inventada}}");
    expect(lista).toHaveTextContent(/línea 3/i);
    expect(screen.queryByText(/no se pudo completar la operación/i)).not.toBeInTheDocument();
  });

  it("409: avisa con claridad, recarga la fila y no pierde el texto escrito", async () => {
    const user = userEvent.setup();
    updateMandatoFormat.mockRejectedValue(new ApiError(409, "row_version_conflict", { error: "row_version_conflict" }));
    await abrir();
    const cuerpo = (await screen.findByTestId("mandato-formato-cuerpo")) as HTMLTextAreaElement;
    await user.type(cuerpo, " Mi texto.");
    await user.click(screen.getByRole("button", { name: /^publicar$/i }));
    await user.click(await screen.findByRole("button", { name: /confirmar y publicar/i }));
    expect(await screen.findByTestId("mandato-formato-mensaje")).toHaveTextContent(/otro usuario editó/i);
    expect(onConflict).toHaveBeenCalledTimes(1);
    expect(getMandatoFormat).toHaveBeenCalledTimes(2); // recarga del detalle (nueva rowVersion)
    expect(cuerpo.value).toContain("Mi texto.");
  });

  it("mientras guarda deshabilita los controles y muestra el loader", async () => {
    const user = userEvent.setup();
    let resolver: (v: unknown) => void = () => undefined;
    updateMandatoFormat.mockReturnValue(new Promise((r) => (resolver = r)));
    await abrir();
    const nombre = await screen.findByLabelText(/nombre del formato/i);
    await user.type(nombre, " X");
    await user.click(guardar());
    await waitFor(() => expect(nombre).toBeDisabled());
    expect(screen.getByLabelText(/tipo de mandato/i)).toBeDisabled();
    expect(screen.getByTestId("mandato-formato-cuerpo")).toBeDisabled();
    expect(screen.getByText("Guardando…")).toBeInTheDocument();
    resolver({ format: envigado, changed: true, publishedVersion: null });
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it("historial: lista número, autor y fecha; abre el texto de una versión anterior sin restaurarla", async () => {
    const user = userEvent.setup();
    getMandatoFormatVersion.mockResolvedValue({
      versionNumber: 1,
      sha256: "a",
      createdAt: "2026-09-01T15:00:00Z",
      createdBy: null,
      body: "Texto de la primera versión",
    });
    await abrir();
    await user.click(await screen.findByTestId("mandato-formato-historial-toggle"));
    const tabla = screen.getByTestId("mandato-formato-historial");
    expect(within(tabla).getByText("v2")).toBeInTheDocument();
    expect(within(tabla).getByText("11111111")).toBeInTheDocument();
    expect(within(tabla).getAllByText(/\d{2}\/\d{2}\/\d{4}/, { selector: "td" })).toHaveLength(2);
    await user.click(screen.getByRole("button", { name: /ver el texto de la versión 1/i }));
    expect(await screen.findByRole("textbox", { name: /^texto de la versión 1$/i })).toHaveValue("Texto de la primera versión");
    expect(updateMandatoFormat).not.toHaveBeenCalled();
  });

  it("la automática no tiene plantilla: solo nombre y tipo", async () => {
    const auto: MandatoFormatView = {
      ...envigado,
      code: "auto",
      name: "Automática",
      baseRedaction: null,
      selectableAsRedaction: false,
      delegatesToOfficeTemplate: true,
      currentVersion: 0,
    };
    await abrir({ format: auto, body: null, versions: [] });
    await screen.findByLabelText(/nombre del formato/i);
    expect(screen.queryByTestId("mandato-formato-cuerpo")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /vista previa/i })).not.toBeInTheDocument();
  });
});
