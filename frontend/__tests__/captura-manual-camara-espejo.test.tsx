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

  it("documento en computador (webcam frontal que no reporta la cámara): la vista sigue el gesto en espejo", async () => {
    mockMedia(streamReporting());
    render(<CameraViewer shape="rect" facing="environment" captureLabel="Capturar documento" onContinue={vi.fn()} />);
    const video = await videoWhenReady("Capturar documento");
    expect(video).toHaveAttribute("data-mirrored", "true");
    expect(video).toHaveClass("-scale-x-100");
  });

  it("documento con la cámara frontal del celular: también en espejo", async () => {
    mockMedia(streamReporting("user"));
    render(<CameraViewer shape="rect" facing="environment" captureLabel="Capturar documento" onContinue={vi.fn()} />);
    expect(await videoWhenReady("Capturar documento")).toHaveAttribute("data-mirrored", "true");
  });

  it("documento con la cámara trasera del celular: no se invierte", async () => {
    mockMedia(streamReporting("environment"));
    render(<CameraViewer shape="rect" facing="environment" captureLabel="Capturar documento" onContinue={vi.fn()} />);
    expect(await videoWhenReady("Capturar documento")).toHaveAttribute("data-mirrored", "false");
  });

  it("la foto capturada no se invierte: se dibuja el fotograma tal cual, sin escala negativa", async () => {
    mockMedia(streamReporting());
    const ctx = { drawImage: vi.fn(), scale: vi.fn(), setTransform: vi.fn(), translate: vi.fn() };
    HTMLCanvasElement.prototype.getContext = vi.fn(() => ctx) as never;
    HTMLCanvasElement.prototype.toBlob = vi.fn(function (this: HTMLCanvasElement, cb: BlobCallback, type?: string) {
      cb(new Blob(["x"], { type }));
    }) as never;
    URL.createObjectURL = vi.fn(() => "blob:preview");
    URL.revokeObjectURL = vi.fn();
    render(<CameraViewer shape="oval" facing="user" captureLabel="Capturar rostro" onContinue={vi.fn()} />);
    await videoWhenReady("Capturar rostro");
    screen.getByRole("button", { name: "Capturar rostro" }).click();
    await waitFor(() => expect(ctx.drawImage).toHaveBeenCalled());
    expect(ctx.scale).not.toHaveBeenCalled();
    expect(ctx.setTransform).not.toHaveBeenCalled();
    expect(ctx.translate).not.toHaveBeenCalled();
  });
});
