import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CapturaManualFlow } from "@/app/verificacion/[token]/_components/CapturaManualFlow";
import type { ManualCaptureClient } from "@/lib/captura-manual/types";

const VIEW = {
  fullName: "Persona de Prueba",
  documentType: "CC",
  documentNumber: "1000000000",
  productName: "FLIT 2.0",
  expiresAt: "2030-01-01T00:00:00Z",
  consentTextVersion: "manual-ley1581-v2",
};

function fakeClient(): ManualCaptureClient {
  return {
    getManualCapture: vi.fn().mockResolvedValue(VIEW),
    postConsent: vi.fn().mockResolvedValue(undefined),
    submit: vi.fn().mockResolvedValue({ status: "pendiente_revision_manual" }),
  };
}

let gum: ReturnType<typeof vi.fn>;

beforeEach(() => {
  const track = { stop: vi.fn() };
  gum = vi.fn(() => Promise.resolve({ getTracks: () => [track] } as unknown as MediaStream));
  Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia: gum } });
  HTMLMediaElement.prototype.play = vi.fn(() => Promise.resolve());
  Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
  Object.defineProperty(HTMLVideoElement.prototype, "videoWidth", { configurable: true, get: () => 1280 });
  Object.defineProperty(HTMLVideoElement.prototype, "videoHeight", { configurable: true, get: () => 720 });
  HTMLCanvasElement.prototype.getContext = vi.fn(() => ({ drawImage: vi.fn() })) as never;
  HTMLCanvasElement.prototype.toBlob = vi.fn(function (this: HTMLCanvasElement, cb: BlobCallback, type?: string) {
    cb(new Blob(["sintetico"], { type }));
  }) as never;
  URL.createObjectURL = vi.fn(() => "blob:preview");
  URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  Reflect.deleteProperty(navigator, "mediaDevices");
});

async function toRostro(client = fakeClient()) {
  render(<CapturaManualFlow token="ok" client={client} />);
  fireEvent.click(await screen.findByRole("checkbox"));
  fireEvent.click(screen.getByRole("button", { name: "Iniciar verificación" }));
  await screen.findByRole("heading", { name: "Verificación facial" });
  return client;
}

async function capture(label: RegExp) {
  const btn = screen.getByRole("button", { name: label });
  await waitFor(() => expect(btn).toBeEnabled());
  fireEvent.click(btn);
  fireEvent.click(await screen.findByRole("button", { name: "Confirmar" }));
}

describe("pasos Rostro, Anverso y Reverso (HU #13294)", () => {
  it("cada paso monta el visor con la cámara y la guía correctas", async () => {
    await toRostro();
    expect(screen.getByText(/retira gafas/)).toBeInTheDocument();
    await waitFor(() => expect(gum).toHaveBeenCalledTimes(1));
    expect(gum.mock.calls[0][0]).toMatchObject({ video: { facingMode: { ideal: "user" } } });
    expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeInTheDocument();

    await capture(/Capturar rostro/);
    await screen.findByRole("heading", { name: "Documento — anverso" });
    expect(screen.getByText(/Encuádralo dentro del marco, siguiendo la guía/)).toBeInTheDocument();
    await waitFor(() => expect(gum).toHaveBeenCalledTimes(2));
    expect(gum.mock.calls[1][0]).toMatchObject({ video: { facingMode: { ideal: "environment" } } });
    await capture(/Capturar documento/);

    await screen.findByRole("heading", { name: "Documento — reverso" });
    await waitFor(() => expect(gum).toHaveBeenCalledTimes(3));
    expect(gum.mock.calls[2][0]).toMatchObject({ video: { facingMode: { ideal: "environment" } } });
    expect(screen.getByRole("button", { name: /Capturar documento/ })).toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).toBeNull();
  });

  it("la guía es un óvalo en el rostro y un rectángulo en el documento", async () => {
    await toRostro();
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    expect(document.querySelector("[class*='50%']")).not.toBeNull();
    await capture(/Capturar rostro/);
    await screen.findByRole("heading", { name: "Documento — anverso" });
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar documento/ })).toBeEnabled());
    expect(document.querySelector("[class*='50%']")).toBeNull();
    expect(screen.getByTestId("guia-documento")).toBeInTheDocument();
  });

  it("repetir vuelve al visor y el flujo no ofrece «Atrás» (como en Kyverum)", async () => {
    await toRostro();
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: /Capturar rostro/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Repetir" }));
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    await capture(/Capturar rostro/);

    await screen.findByRole("heading", { name: "Documento — anverso" });
    expect(screen.queryByRole("button", { name: "Atrás" })).not.toBeInTheDocument();
  });
});
