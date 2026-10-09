import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CameraViewer } from "@/app/verificacion/[token]/_components/CameraViewer";

// Bug #13449 (hallazgo 2): la vista en vivo del rostro con la cámara frontal se ve en espejo.
// El autoencuadre del documento no es objeto de esta prueba.
vi.mock("@/lib/captura-manual/useDocumentAutoCapture", () => ({
  useDocumentAutoCapture: () => ({ state: null, progress: 0 }),
}));

function streamReporting(facingMode?: string) {
  const track = { stop: vi.fn(), getSettings: () => (facingMode ? { facingMode } : {}) };
  return { getTracks: () => [track], getVideoTracks: () => [track] } as unknown as MediaStream;
}

function mockMedia(stream: MediaStream) {
  Object.defineProperty(navigator, "mediaDevices", {
    configurable: true,
    value: { getUserMedia: vi.fn(() => Promise.resolve(stream)) },
  });
}

beforeEach(() => {
  HTMLMediaElement.prototype.play = vi.fn(() => Promise.resolve());
  Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
  Object.defineProperty(HTMLVideoElement.prototype, "videoWidth", { configurable: true, get: () => 2000 });
  Object.defineProperty(HTMLVideoElement.prototype, "videoHeight", { configurable: true, get: () => 1000 });
});

afterEach(() => {
  Reflect.deleteProperty(navigator, "mediaDevices");
});

async function videoWhenReady(captureLabel: string) {
  await waitFor(() => expect(screen.getByRole("button", { name: captureLabel })).toBeEnabled());
  return screen.getByLabelText("Vista en vivo de la cámara");
}

describe("CameraViewer · vista en espejo (Bug #13449)", () => {
  it("rostro con cámara frontal: la vista en vivo se invierte horizontalmente", async () => {
    mockMedia(streamReporting("user"));
    render(<CameraViewer shape="oval" facing="user" captureLabel="Capturar rostro" onContinue={vi.fn()} />);
    const video = await videoWhenReady("Capturar rostro");
    expect(video).toHaveAttribute("data-mirrored", "true");
    expect(video).toHaveClass("-scale-x-100");
  });

  it("rostro en computador (el navegador no reporta la cámara): se asume la frontal pedida y se invierte", async () => {
    mockMedia(streamReporting());
    render(<CameraViewer shape="oval" facing="user" captureLabel="Capturar rostro" onContinue={vi.fn()} />);
    expect(await videoWhenReady("Capturar rostro")).toHaveAttribute("data-mirrored", "true");
  });

  it("rostro con la cámara trasera abierta de verdad: no se invierte", async () => {
    mockMedia(streamReporting("environment"));
    render(<CameraViewer shape="oval" facing="user" captureLabel="Capturar rostro" onContinue={vi.fn()} />);
    const video = await videoWhenReady("Capturar rostro");
    expect(video).toHaveAttribute("data-mirrored", "false");
    expect(video).not.toHaveClass("-scale-x-100");
  });

  it("documento: nunca se invierte, aunque la cámara sea frontal (el texto debe leerse)", async () => {
    mockMedia(streamReporting("user"));
    render(<CameraViewer shape="rect" facing="environment" captureLabel="Capturar documento" onContinue={vi.fn()} />);
    expect(await videoWhenReady("Capturar documento")).toHaveAttribute("data-mirrored", "false");
  });
});
