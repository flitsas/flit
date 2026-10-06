import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CapturaManualFlow } from "@/app/captura-manual/[token]/_components/CapturaManualFlow";
import { createHttpClient } from "@/lib/captura-manual/client";
import { ManualCaptureError, type ManualCaptureClient } from "@/lib/captura-manual/types";

const VIEW = {
  fullName: "Persona de Prueba",
  documentType: "CC",
  documentNumber: "1000000000",
  productName: "FLIT 2.0",
  expiresAt: "2030-01-01T00:00:00Z",
  consentTextVersion: "manual-ley1581-v1",
};

function fakeClient(submit = vi.fn().mockResolvedValue({ status: "pendiente_revision_manual" })): ManualCaptureClient {
  return {
    getManualCapture: vi.fn().mockResolvedValue(VIEW),
    postConsent: vi.fn().mockResolvedValue(undefined),
    submit,
  };
}

beforeEach(() => {
  const track = { stop: vi.fn() };
  const gum = vi.fn(() => Promise.resolve({ getTracks: () => [track] } as unknown as MediaStream));
  Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia: gum } });
  HTMLMediaElement.prototype.play = vi.fn(() => Promise.resolve());
  Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
  Object.defineProperty(HTMLVideoElement.prototype, "videoWidth", { configurable: true, get: () => 1280 });
  Object.defineProperty(HTMLVideoElement.prototype, "videoHeight", { configurable: true, get: () => 720 });
  HTMLCanvasElement.prototype.getContext = vi.fn(() => ({
    drawImage: vi.fn(),
    fillRect: vi.fn(),
    beginPath: vi.fn(),
    moveTo: vi.fn(),
    lineTo: vi.fn(),
    stroke: vi.fn(),
  })) as never;
  HTMLCanvasElement.prototype.toBlob = vi.fn(function (this: HTMLCanvasElement, cb: BlobCallback, type?: string) {
    cb(new Blob(["sintetico"], { type }));
  }) as never;
  URL.createObjectURL = vi.fn(() => "blob:preview");
  URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  Reflect.deleteProperty(navigator, "mediaDevices");
});

async function capture(label: RegExp) {
  const btn = screen.getByRole("button", { name: label });
  await waitFor(() => expect(btn).toBeEnabled());
  fireEvent.click(btn);
  fireEvent.click(await screen.findByRole("button", { name: "Continuar" }));
}

async function toFirma(client: ManualCaptureClient) {
  render(<CapturaManualFlow token="ok" client={client} />);
  fireEvent.click(await screen.findByRole("checkbox"));
  fireEvent.click(screen.getByRole("button", { name: "Iniciar verificación" }));
  await screen.findByRole("heading", { name: "Verificación facial" });
  await capture(/Capturar rostro/);
  await screen.findByRole("heading", { name: "Anverso del documento" });
  await capture(/Capturar anverso/);
  await screen.findByRole("heading", { name: "Reverso del documento" });
  await capture(/Capturar reverso/);
  await screen.findByRole("heading", { name: "Tu firma" });
}

function sign() {
  const canvas = screen.getByLabelText(/Lienzo para trazar tu firma/);
  fireEvent.pointerDown(canvas, { pointerId: 1, clientX: 5, clientY: 5, button: 0 });
  fireEvent.pointerMove(canvas, { pointerId: 1, clientX: 40, clientY: 20 });
  fireEvent.pointerUp(canvas, { pointerId: 1 });
}

const finalizar = () => screen.getByRole("button", { name: /Finalizar|Enviando/ });

async function signAndSend() {
  sign();
  await waitFor(() => expect(finalizar()).toBeEnabled());
  fireEvent.click(finalizar());
}

describe("paso Firma y envío (HU #13295)", () => {
  it("Finalizar está deshabilitado sin trazo, se habilita al firmar y Borrar lo deshabilita", async () => {
    await toFirma(fakeClient());
    expect(finalizar()).toBeDisabled();
    sign();
    await waitFor(() => expect(finalizar()).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Borrar" }));
    await waitFor(() => expect(finalizar()).toBeDisabled());
  });

  it("el lienzo no hace scroll al trazar, tiene etiqueta y la nota de que no hay alternativa de teclado", async () => {
    await toFirma(fakeClient());
    expect(screen.getByLabelText(/Lienzo para trazar tu firma/).className).toContain("touch-none");
    expect(screen.getByText(/no tiene alternativa de teclado/)).toBeInTheDocument();
  });

  it("no existe ninguna opción de subir archivo en todo el flujo", async () => {
    await toFirma(fakeClient());
    expect(document.querySelector('input[type="file"]')).toBeNull();
    expect(screen.queryByText(/cargar|subir|seleccionar un archivo|png con la firma/i)).not.toBeInTheDocument();
  });

  it("envía un solo submit con las 4 capturas, ignora el doble clic y muestra el éxito sin pasos siguientes", async () => {
    let resolve!: (v: { status: string }) => void;
    const submit = vi.fn(() => new Promise<{ status: string }>((r) => (resolve = r)));
    await toFirma(fakeClient(submit));
    await signAndSend();
    fireEvent.click(finalizar());
    expect(submit).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Enviando…" })).toBeDisabled();
    const files = (submit.mock.calls[0] as unknown as [string, Record<string, Blob>])[1];
    expect(Object.keys(files).sort()).toEqual(["anverso", "firma", "reverso", "rostro"]);
    expect(files.firma.type).toBe("image/png");
    resolve({ status: "pendiente_revision_manual" });
    expect(await screen.findByText("Recibimos tu información")).toBeInTheDocument();
    expect(screen.getByText(/Será revisada/)).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("un fallo de red conserva las capturas y permite reintentar sin repetir nada", async () => {
    const submit = vi.fn().mockRejectedValueOnce(new TypeError("net")).mockResolvedValue({ status: "ok" });
    await toFirma(fakeClient(submit));
    await signAndSend();
    expect(await screen.findByRole("alert")).toHaveTextContent(/no tienes que repetir nada/);
    await waitFor(() => expect(finalizar()).toBeEnabled());
    fireEvent.click(finalizar());
    await screen.findByText("Recibimos tu información");
    expect(submit).toHaveBeenCalledTimes(2);
    expect(submit.mock.calls[1][1]).toBe(submit.mock.calls[0][1]);
  });

  it.each([
    [413, "payload_too_large", "demasiado grande"],
    [415, "unsupported_media_type", "formato"],
  ])("%s sin parte identificada: aviso para repetir esa captura, en el paso Firma", async (status, code, text) => {
    const submit = vi.fn().mockRejectedValue(new ManualCaptureError(status, code, "x"));
    await toFirma(fakeClient(submit));
    await signAndSend();
    expect(await screen.findByRole("alert")).toHaveTextContent(new RegExp(`${text}.*Repite esa captura`, "s"));
    expect(screen.getByRole("heading", { name: "Tu firma" })).toBeInTheDocument();
  });

  it("413 que nombra el anverso vuelve a ese paso conservando lo capturado", async () => {
    const submit = vi.fn().mockRejectedValue(new ManualCaptureError(413, "anverso_demasiado_grande", "x"));
    await toFirma(fakeClient(submit));
    await signAndSend();
    await screen.findByRole("heading", { name: "Anverso del documento" });
    expect(screen.getByRole("alert")).toHaveTextContent(/Repite esa captura/);
    expect(screen.getByAltText("Vista previa de la foto capturada")).toBeInTheDocument();
  });

  it("409 consentimiento_requerido vuelve a Datos y exige volver a registrar el consentimiento", async () => {
    const submit = vi.fn().mockRejectedValue(new ManualCaptureError(409, "consentimiento_requerido", "x"));
    const client = fakeClient(submit);
    await toFirma(client);
    await signAndSend();
    await screen.findByRole("heading", { name: /Hola Persona de Prueba/ });
    expect(screen.getByRole("alert")).toHaveTextContent(/autorización/);
    const check = screen.getByRole("checkbox");
    expect(check).not.toBeChecked();
    fireEvent.click(check);
    fireEvent.click(screen.getByRole("button", { name: "Iniciar verificación" }));
    await screen.findByRole("heading", { name: "Verificación facial" });
    expect(client.postConsent).toHaveBeenCalledTimes(2);
  });

  it.each([
    [409, "estado_invalido", "Este enlace ya no se puede usar"],
    [410, "expirada", "Este enlace venció"],
    [404, "not_found", "No encontramos este enlace"],
  ])("%s %s muestra la pantalla terminal del enlace", async (status, code, title) => {
    const submit = vi.fn().mockRejectedValue(new ManualCaptureError(status, code, "x"));
    await toFirma(fakeClient(submit));
    await signAndSend();
    expect(await screen.findByRole("heading", { name: title })).toBeInTheDocument();
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
  });
});

describe("cliente HTTP: submit multipart", () => {
  it("envía rostro, anverso, reverso y firma en un solo POST", async () => {
    const fetchImpl = vi
      .fn()
      .mockResolvedValue(new Response(JSON.stringify({ status: "pendiente_revision_manual" }), { status: 200 }));
    const client = createHttpClient(fetchImpl as unknown as typeof fetch);
    const b = (t: string) => new Blob(["x"], { type: t });
    await client.submit("tok", { rostro: b("image/jpeg"), anverso: b("image/jpeg"), reverso: b("image/jpeg"), firma: b("image/png") });
    expect(fetchImpl).toHaveBeenCalledTimes(1);
    const body = fetchImpl.mock.calls[0][1].body as FormData;
    expect([...body.keys()].sort()).toEqual(["anverso", "firma", "reverso", "rostro"]);
    expect((body.get("firma") as File).name).toBe("firma.png");
  });
});
