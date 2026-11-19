import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { AccionesDp } from "@/components/detalle/AccionesDp";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
import { ToastProvider } from "@/components/Toast";
import type { VistaPreviaDp } from "@/lib/dp";

// HU #14619: «Ver cómo llegaría» en el detalle del comparendo. Solo correos falsos (@ejemplo.test): nada se envía.
const apiFetch = vi.fn();
const apiGetBlob = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
  apiGetBlob: (...a: unknown[]) => apiGetBlob(...a),
}));

URL.createObjectURL = () => "blob:dp";
URL.revokeObjectURL = () => {};

const ctx = {
  detalle: { id: "c1", activo: true, pagado: false, dp: null, infraccion: { codigo: "C29", descripcion: "Exceso de velocidad", fechaImposicion: null, fechaNotificacion: null } },
  recargar: () => {},
  soloLectura: false,
  sesion: {
    usuario: { id: "u1", email: "gestora@ejemplo.test", esSuperAdmin: false },
    permisos: ["comparendos.dp.generar", "comparendos.dp.enviar"],
    empresa: { id: "e1", nombre: "Empresa", capacidades: { derechosPeticion: true, consultaContraventor: true, cambioContraventor: true, ocr: false, reportes: false } },
  },
} as unknown as ContextoDetalle;

const vistaBase: VistaPreviaDp = {
  puedeEnviar: true,
  bloqueos: [],
  advertencias: [],
  radicado: "DP-0001",
  organismo: "Secretaría de Movilidad",
  destinatarios: ["radicacion@ejemplo.test"],
  cc: ["copia@ejemplo.test"],
  bcc: [],
  representante: { nombres: "Rosa Representante", cargo: "Gerente", numeroDocumento: "1" },
  anexos: ["Cédula del representante"],
  estadoLegal: "EnTiempo",
  correo: { asunto: "Derecho de petición DP-0001", html: "<p>Cuerpo del correo</p>" },
};

function simular(vista: Partial<VistaPreviaDp> = {}) {
  apiGetBlob.mockResolvedValue(new Blob(["%PDF"], { type: "application/pdf" }));
  apiFetch.mockImplementation(async (ruta: string) => {
    if (ruta === "/comparendos/c1/dp/decision") return { elegible: true, elegida: "DPR", tipologiaIdElegida: "t1", motivo: null, aplicables: [] };
    if (ruta === "/comparendos/c1/dp/tipologias") return [{ id: "t1", codigo: "DPR", nombre: "Prescripción", descripcionJuridica: "d", estadosLegalesPermitidos: [], permiteMasivo: false, procede: true }];
    if (ruta === "/comparendos/c1/dp/vista-previa") return { ...vistaBase, ...vista };
    throw new Error(`ruta sin simular: ${String(ruta)}`);
  });
}

async function elegirTipologia() {
  render(
    <ToastProvider>
      <AccionesDp ctx={ctx} />
    </ToastProvider>,
  );
  fireEvent.change(await screen.findByLabelText("Tipología"), { target: { value: "t1" } });
}

const envios = () => apiFetch.mock.calls.filter(([ruta, op]) => ruta === "/comparendos/c1/dp" && op?.method === "POST");

describe("«Ver cómo llegaría» en el detalle del comparendo (HU #14619)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
    apiGetBlob.mockReset();
  });
  afterEach(cleanup);

  it("AC1: el botón aparece al elegir la tipología, a la vista y con su aviso", async () => {
    simular();
    render(
      <ToastProvider>
        <AccionesDp ctx={ctx} />
      </ToastProvider>,
    );
    await screen.findByLabelText("Tipología");
    expect(screen.queryByRole("button", { name: "Ver cómo llegaría" })).toBeNull();
    fireEvent.change(screen.getByLabelText("Tipología"), { target: { value: "t1" } });
    await screen.findByRole("button", { name: "Ver cómo llegaría" });
    expect(screen.getByText(/no se envía nada/)).toBeTruthy();
  });

  it("AC2: muestra el PDF y el correo (asunto y cuerpo), marcado VISTA PREVIA, con destinatarios, copias y anexos", async () => {
    simular();
    await elegirTipologia();
    fireEvent.click(await screen.findByRole("button", { name: "Ver cómo llegaría" }));
    const dialogo = await screen.findByRole("dialog");
    await waitFor(() => expect(within(dialogo).getByTitle("Vista previa del documento")).toBeTruthy());
    expect(within(dialogo).getByText(/VISTA PREVIA/)).toBeTruthy();
    expect(within(dialogo).getByText(/no se envía nada ni se reserva radicado/)).toBeTruthy();
    expect(within(dialogo).getByText(/Derecho de petición DP-0001/)).toBeTruthy();
    expect((within(dialogo).getByTitle("Vista previa del correo") as HTMLIFrameElement).getAttribute("srcdoc")).toContain("Cuerpo del correo");
    for (const t of ["radicacion@ejemplo.test", "copia@ejemplo.test", "Cédula del representante"]) expect(within(dialogo).getByText(t)).toBeTruthy();
    expect(apiGetBlob).toHaveBeenCalledWith("/comparendos/c1/dp/vista-previa.pdf", { tipologiaId: "t1" }, expect.anything());
  });

  it("AC3: el correo va en un iframe con sandbox sin permisos, nunca como HTML crudo", async () => {
    simular({ correo: { asunto: "x", html: "<script>alert(1)</script><p>Hola</p>" } });
    await elegirTipologia();
    fireEvent.click(await screen.findByRole("button", { name: "Ver cómo llegaría" }));
    const correo = (await screen.findByTitle("Vista previa del correo")) as HTMLIFrameElement;
    expect(correo.getAttribute("sandbox")).toBe("");
    expect(document.querySelector("script")).toBeNull();
    expect(screen.queryByText("Hola")).toBeNull();
  });

  it("AC4: con bloqueos sigue mostrando la vista previa y los motivos; y nunca envía nada", async () => {
    simular({ puedeEnviar: false, bloqueos: [{ codigo: "SIN_REPRESENTANTE", mensaje: "La empresa no tiene un representante legal con firma." }] });
    await elegirTipologia();
    fireEvent.click(await screen.findByRole("button", { name: "Ver cómo llegaría" }));
    const dialogo = await screen.findByRole("dialog");
    expect(within(dialogo).getByText("La empresa no tiene un representante legal con firma.")).toBeTruthy();
    await within(dialogo).findByTitle("Vista previa del correo");
    await within(dialogo).findByTitle("Vista previa del documento");
    expect(envios()).toHaveLength(0);
  });

  it("sin plantilla no se pide el PDF, se explica, y el correo sigue a la vista", async () => {
    simular({ puedeEnviar: false, bloqueos: [{ codigo: "SIN_PLANTILLA", mensaje: "No hay plantilla publicada." }] });
    await elegirTipologia();
    fireEvent.click(await screen.findByRole("button", { name: "Ver cómo llegaría" }));
    const dialogo = await screen.findByRole("dialog");
    expect(within(dialogo).getByText(/No se puede armar el documento/)).toBeTruthy();
    expect(apiGetBlob).not.toHaveBeenCalled();
    expect(within(dialogo).getByTitle("Vista previa del correo")).toBeTruthy();
  });

  it("si la API no manda correo lo dice en vez de dejar un hueco", async () => {
    simular({ correo: null });
    await elegirTipologia();
    fireEvent.click(await screen.findByRole("button", { name: "Ver cómo llegaría" }));
    const dialogo = await screen.findByRole("dialog");
    expect(within(dialogo).getByText(/Todavía no hay un correo publicado/)).toBeTruthy();
  });
});
