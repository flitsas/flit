import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CapturaManualFlow } from "@/app/captura-manual/[token]/_components/CapturaManualFlow";
import { AUTORIZACION_FIRMA_TEXT } from "@/lib/captura-manual/consent";
import type { ManualCaptureClient } from "@/lib/captura-manual/types";

// Diseño de los pasos 2–5 alineado con el flujo real de Kyverum (datos sintéticos).
const VIEW = {
  fullName: "Persona de Prueba",
  documentType: "CC",
  documentNumber: "1000000000",
  productName: "FLIT 2.0",
  expiresAt: "2030-01-01T00:00:00Z",
  consentTextVersion: "manual-ley1581-v1",
};

function fakeClient(submit = vi.fn().mockResolvedValue({ status: "pendiente_revision_manual" })): ManualCaptureClient {
  return { getManualCapture: vi.fn().mockResolvedValue(VIEW), postConsent: vi.fn().mockResolvedValue(undefined), submit };
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

async function takePhoto(label: RegExp) {
  const btn = screen.getByRole("button", { name: label });
  await waitFor(() => expect(btn).toBeEnabled());
  fireEvent.click(btn);
}

async function toRostro(client = fakeClient()) {
  render(<CapturaManualFlow token="ok" client={client} />);
  fireEvent.click(await screen.findByRole("checkbox"));
  fireEvent.click(screen.getByRole("button", { name: "Iniciar verificación" }));
  await screen.findByRole("heading", { name: "Verificación facial" });
}

async function toAnverso() {
  await toRostro();
  await takePhoto(/Capturar rostro/);
  fireEvent.click(await screen.findByRole("button", { name: "Confirmar" }));
  await screen.findByRole("heading", { name: "Documento — anverso" });
}

describe("visor de documento y vista previa (HU #13293, #13294)", () => {
  it("anverso: textos del PO, silueta guía, aviso de encuadre y botón «Capturar documento»", async () => {
    await toAnverso();
    expect(
      screen.getByText("Muestra el lado de tu documento con tu FOTO. Encuádralo dentro del marco, siguiendo la guía."),
    ).toBeInTheDocument();
    const capturar = screen.getByRole("button", { name: "Capturar documento" });
    await waitFor(() => expect(capturar).toBeEnabled());
    expect(capturar).toHaveClass("bg-flit-brand", "text-white");
    const guia = screen.getByRole("img", { name: /Guía del anverso/ });
    expect(guia).toHaveAttribute("data-testid", "guia-documento");
    expect(within(guia as unknown as HTMLElement).getByText("tu foto")).toBeInTheDocument();
    expect(screen.getByTestId("aviso-encuadre")).toHaveTextContent(
      "Encuadra el documento completo, sin reflejos ni dedos sobre los datos, y toca Capturar",
    );
  });

  it("la vista previa muestra dos botones lado a lado, Repetir y Confirmar, del mismo alto", async () => {
    await toAnverso();
    await takePhoto(/Capturar documento/);
    const repetir = await screen.findByRole("button", { name: "Repetir" });
    const confirmar = screen.getByRole("button", { name: "Confirmar" });
    expect(repetir.parentElement).toBe(confirmar.parentElement);
    expect(repetir.parentElement).toHaveClass("flex", "gap-3");
    for (const b of [repetir, confirmar]) expect(b).toHaveClass("flex-1", "min-h-12", "min-w-0");
    expect(repetir).toHaveClass("border-flit-brand", "text-flit-brand");
    expect(confirmar).toHaveClass("bg-flit-brand", "text-white");
    expect(confirmar).toBeEnabled();
    expect(screen.queryByRole("button", { name: "Continuar" })).not.toBeInTheDocument();
    expect(screen.getByAltText("Vista previa de la foto capturada")).toHaveClass("w-full", "rounded-2xl");
  });

  it("reverso: texto del PO, silueta propia de reverso, mismo aviso y botón «Capturar documento»", async () => {
    await toAnverso();
    await takePhoto(/Capturar documento/);
    fireEvent.click(await screen.findByRole("button", { name: "Confirmar" }));
    await screen.findByRole("heading", { name: "Documento — reverso" });
    expect(
      screen.getByText("Ahora el reverso: el lado del código de barras (o QR). Encuádralo dentro del marco."),
    ).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("button", { name: "Capturar documento" })).toBeEnabled());
    expect(screen.getByRole("img", { name: /Guía del reverso/ })).toHaveAttribute("data-side", "reverso");
    expect(screen.getByTestId("aviso-encuadre")).toHaveTextContent(/Encuadra el documento completo/);
  });

  it("rostro: la vista previa también ofrece Repetir | Confirmar y no hay selector de archivos", async () => {
    await toRostro();
    await takePhoto(/Capturar rostro/);
    expect(await screen.findByRole("button", { name: "Repetir" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirmar" })).toBeEnabled();
    expect(document.querySelector('input[type="file"]')).toBeNull();
  });
});

describe("paso Firma alineado con Kyverum (HU #13295)", () => {
  async function toFirma(client = fakeClient()) {
    await toRostro(client);
    for (const [h, label] of [
      ["Verificación facial", /Capturar rostro/],
      ["Documento — anverso", /Capturar documento/],
      ["Documento — reverso", /Capturar documento/],
    ] as const) {
      await screen.findByRole("heading", { name: h });
      await takePhoto(label);
      fireEvent.click(await screen.findByRole("button", { name: "Confirmar" }));
    }
    await screen.findByRole("heading", { name: "Autorización de trámite digital" });
  }

  function sign() {
    const canvas = screen.getByLabelText(/Lienzo para trazar tu firma/);
    fireEvent.pointerDown(canvas, { pointerId: 1, clientX: 5, clientY: 5, button: 0 });
    fireEvent.pointerMove(canvas, { pointerId: 1, clientX: 40, clientY: 20 });
    fireEvent.pointerUp(canvas, { pointerId: 1 });
  }

  it("muestra el cuadro de autorización con scroll interno y sin mencionar a Kyverum", async () => {
    await toFirma();
    const box = screen.getByRole("region", { name: "Texto de autorización" });
    expect(box).toHaveClass("overflow-y-auto", "max-h-40", "bg-slate-50", "rounded-2xl", "border");
    expect(box).toHaveAttribute("tabindex", "0");
    expect(box).toHaveTextContent(AUTORIZACION_FIRMA_TEXT);
    expect(AUTORIZACION_FIRMA_TEXT).not.toMatch(/kyverum/i);
    expect(document.body.textContent).not.toMatch(/kyverum/i);
    expect(
      screen.getByText("Firma dentro del recuadro con el dedo o el mouse para autorizar y continuar con tu trámite."),
    ).toBeInTheDocument();
  });

  it("«Borrar» y «Firmar y autorizar» van lado a lado; el segundo está deshabilitado sin trazo y es el que envía", async () => {
    const submit = vi.fn().mockResolvedValue({ status: "pendiente_revision_manual" });
    await toFirma(fakeClient(submit));
    const borrar = screen.getByRole("button", { name: "Borrar" });
    const firmar = screen.getByRole("button", { name: "Firmar y autorizar" });
    expect(borrar.parentElement).toBe(firmar.parentElement);
    expect(firmar).toBeDisabled();
    expect(firmar).toHaveClass("bg-flit-brand", "text-white");
    expect(screen.queryByRole("button", { name: "Finalizar" })).not.toBeInTheDocument();
    fireEvent.click(firmar);
    expect(submit).not.toHaveBeenCalled();
    sign();
    await waitFor(() => expect(firmar).toBeEnabled());
    fireEvent.click(firmar);
    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("Recibimos tu información")).toBeInTheDocument();
  });

  it("el lienzo tiene borde punteado y no existe ningún selector de archivos", async () => {
    await toFirma();
    expect(screen.getByLabelText(/Lienzo para trazar tu firma/).parentElement).toHaveClass("border-dashed", "rounded-2xl");
    expect(document.querySelector('input[type="file"]')).toBeNull();
    expect(screen.queryByText(/No puedo firmar dibujando/)).not.toBeInTheDocument();
  });

  it("durante el envío: texto «Enviando tu información…», barra con los 5 pasos completados y sin botones; éxito al terminar", async () => {
    let resolve!: (v: { status: string }) => void;
    const submit = vi.fn(() => new Promise<{ status: string }>((r) => (resolve = r)));
    await toFirma(fakeClient(submit));
    sign();
    const firmar = screen.getByRole("button", { name: "Firmar y autorizar" });
    await waitFor(() => expect(firmar).toBeEnabled());
    fireEvent.click(firmar);
    expect(await screen.findByText("Enviando tu información… suele tomar unos segundos.")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
    const nav = screen.getByRole("navigation", { name: "Progreso de la verificación" });
    expect(within(nav).getAllByText("completado,", { exact: false })).toHaveLength(5);
    expect(screen.getAllByTestId("step-line").every((l) => l.className.includes("bg-flit-brand"))).toBe(true);
    expect(document.body.textContent).not.toMatch(/kyverum|verificando/i);
    expect(screen.getByTestId("enviando-info").firstElementChild).toHaveClass("motion-safe:animate-spin");
    resolve({ status: "pendiente_revision_manual" });
    expect(await screen.findByText("Recibimos tu información")).toBeInTheDocument();
    expect(screen.queryByTestId("enviando-info")).not.toBeInTheDocument();
  });

  it("tras un error de red vuelve al paso Firma con la firma y el mensaje, y se puede reintentar", async () => {
    const submit = vi.fn().mockRejectedValueOnce(new TypeError("net")).mockResolvedValue({ status: "ok" });
    await toFirma(fakeClient(submit));
    sign();
    const firmar = screen.getByRole("button", { name: "Firmar y autorizar" });
    await waitFor(() => expect(firmar).toBeEnabled());
    fireEvent.click(firmar);
    expect(await screen.findByRole("alert")).toHaveTextContent(/no tienes que repetir nada/);
    expect(screen.queryByTestId("enviando-info")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Autorización de trámite digital" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("button", { name: "Firmar y autorizar" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Firmar y autorizar" }));
    expect(await screen.findByText("Recibimos tu información")).toBeInTheDocument();
    expect(submit).toHaveBeenCalledTimes(2);
  });
});
