import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CameraViewer } from "@/app/captura-manual/[token]/_components/CameraViewer";

function makeStream() {
  const track = { stop: vi.fn() };
  return { stream: { getTracks: () => [track] } as unknown as MediaStream, track };
}

function mockMedia(impl: () => Promise<MediaStream>) {
  const getUserMedia = vi.fn(impl as (c?: MediaStreamConstraints) => Promise<MediaStream>);
  Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia } });
  return getUserMedia;
}

const domError = (name: string) => Object.assign(new Error(name), { name });

beforeEach(() => {
  HTMLMediaElement.prototype.play = vi.fn(() => Promise.resolve());
  Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
  Object.defineProperty(HTMLVideoElement.prototype, "videoWidth", { configurable: true, get: () => 2000 });
  Object.defineProperty(HTMLVideoElement.prototype, "videoHeight", { configurable: true, get: () => 1000 });
  HTMLCanvasElement.prototype.getContext = vi.fn(() => ({ drawImage: vi.fn() })) as never;
  HTMLCanvasElement.prototype.toBlob = vi.fn(function (this: HTMLCanvasElement, cb: BlobCallback, type?: string) {
    cb(new Blob(["x"], { type }));
  }) as never;
  URL.createObjectURL = vi.fn(() => "blob:preview");
  URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  Reflect.deleteProperty(navigator, "mediaDevices");
});

const view = (onContinue = vi.fn()) =>
  render(<CameraViewer shape="oval" facing="user" captureLabel="Capturar rostro" hint="Ubica tu rostro dentro del óvalo" onContinue={onContinue} />);

describe("CameraViewer", () => {
  it("abre la cámara frontal, muestra «abriendo» y luego queda lista con la guía y la pista", async () => {
    const { stream } = makeStream();
    const gum = mockMedia(() => Promise.resolve(stream));
    view();
    expect(screen.getByText(/Abriendo cámara/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeDisabled();
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    expect(gum.mock.calls[0][0]).toMatchObject({ video: { facingMode: { ideal: "user" } }, audio: false });
    expect(screen.getByText("Ubica tu rostro dentro del óvalo")).toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).toBeNull();
  });

  it("permiso denegado: explica cómo habilitarla, reintenta y no ofrece subir archivo", async () => {
    const { stream } = makeStream();
    const gum = mockMedia(() => Promise.reject(domError("NotAllowedError")));
    view();
    expect(await screen.findByText(/No tenemos permiso/)).toBeInTheDocument();
    expect(screen.getByText(/activa Cámara/)).toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).toBeNull();
    gum.mockImplementation(() => Promise.resolve(stream));
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    expect(gum).toHaveBeenCalledTimes(2);
  });

  it("sin cámara muestra el mensaje de que no se puede continuar", async () => {
    mockMedia(() => Promise.reject(domError("NotFoundError")));
    view();
    expect(await screen.findByText(/no detectamos ninguna cámara/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
  });

  it("sin HTTPS o sin mediaDevices muestra «no se puede continuar en este dispositivo»", async () => {
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: false });
    view();
    expect(await screen.findByText("No se puede continuar en este dispositivo")).toBeInTheDocument();
  });

  it("sin navigator.mediaDevices también es no compatible", async () => {
    view();
    expect(await screen.findByText("No se puede continuar en este dispositivo")).toBeInTheDocument();
  });

  it("captura un JPEG, permite repetir (reabre la cámara) y continuar con el Blob", async () => {
    const first = makeStream();
    const second = makeStream();
    const streams = [first.stream, second.stream];
    const gum = mockMedia(() => Promise.resolve(streams.shift() as MediaStream));
    const onContinue = vi.fn();
    view(onContinue);
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: /Capturar rostro/ }));
    expect(await screen.findByAltText("Vista previa de la foto capturada")).toBeInTheDocument();
    expect(first.track.stop).toHaveBeenCalled();
    const canvas = HTMLCanvasElement.prototype.toBlob as unknown as ReturnType<typeof vi.fn>;
    expect(canvas.mock.calls[0][1]).toBe("image/jpeg");

    fireEvent.click(screen.getByRole("button", { name: "Repetir" }));
    await waitFor(() => expect(gum).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: /Capturar rostro/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirmar" }));
    expect(onContinue).toHaveBeenCalledTimes(1);
    expect(onContinue.mock.calls[0][0]).toBeInstanceOf(Blob);
    expect(onContinue.mock.calls[0][0].type).toBe("image/jpeg");
  });

  it("limita la resolución de captura a 1280 px de lado mayor", async () => {
    mockMedia(() => Promise.resolve(makeStream().stream));
    const created: HTMLCanvasElement[] = [];
    const orig = document.createElement.bind(document);
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      const el = orig(tag);
      if (tag === "canvas") created.push(el as HTMLCanvasElement);
      return el;
    });
    view();
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: /Capturar rostro/ }));
    await screen.findByAltText("Vista previa de la foto capturada");
    expect(created[0].width).toBe(1280);
    expect(created[0].height).toBe(640);
    vi.restoreAllMocks();
  });

  it("mantiene «Abriendo cámara» y el botón deshabilitado hasta que el video tenga dimensiones (loadedmetadata tardío)", async () => {
    let width = 0;
    Object.defineProperty(HTMLVideoElement.prototype, "videoWidth", { configurable: true, get: () => width });
    mockMedia(() => Promise.resolve(makeStream().stream));
    view();
    await act(async () => undefined);
    expect(screen.getByText(/Abriendo cámara/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeDisabled();
    // Un evento sin dimensiones aún no habilita.
    act(() => {
      document.querySelector("video")?.dispatchEvent(new Event("loadedmetadata"));
    });
    expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeDisabled();
    width = 2000;
    act(() => {
      document.querySelector("video")?.dispatchEvent(new Event("loadedmetadata"));
    });
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    expect(screen.queryByText(/Abriendo cámara/)).not.toBeInTheDocument();
  });

  it("si capture no puede tomar el fotograma (sin dimensiones) muestra un mensaje claro en vez de fallar en silencio", async () => {
    let width = 2000;
    Object.defineProperty(HTMLVideoElement.prototype, "videoWidth", { configurable: true, get: () => width });
    mockMedia(() => Promise.resolve(makeStream().stream));
    view();
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    width = 0;
    fireEvent.click(screen.getByRole("button", { name: /Capturar rostro/ }));
    expect(await screen.findByText("La cámara aún no está lista, inténtalo de nuevo")).toBeInTheDocument();
    expect(screen.queryByAltText("Vista previa de la foto capturada")).not.toBeInTheDocument();
  });

  describe("ciclo de vida de la URL blob de la vista previa", () => {
    let n = 0;
    beforeEach(() => {
      n = 0;
      URL.createObjectURL = vi.fn(() => `blob:preview-${++n}`);
    });
    const revoked = () => (URL.revokeObjectURL as unknown as ReturnType<typeof vi.fn>).mock.calls.map((c) => c[0]);

    it("no revoca la URL mientras la vista previa la usa, y la revoca al repetir (reemplazo)", async () => {
      mockMedia(() => Promise.resolve(makeStream().stream));
      view();
      await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
      fireEvent.click(screen.getByRole("button", { name: /Capturar rostro/ }));
      const img = await screen.findByAltText("Vista previa de la foto capturada");
      expect(img).toHaveAttribute("src", "blob:preview-1");
      expect(revoked()).not.toContain("blob:preview-1");
      fireEvent.click(screen.getByRole("button", { name: "Repetir" }));
      expect(revoked()).toContain("blob:preview-1");
      await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
      fireEvent.click(screen.getByRole("button", { name: /Capturar rostro/ }));
      const img2 = await screen.findByAltText("Vista previa de la foto capturada");
      expect(img2).toHaveAttribute("src", "blob:preview-2");
      expect(revoked()).not.toContain("blob:preview-2");
    });

    it("al volver con «Atrás» (initialBlob) la URL en uso no se revoca, ni siquiera en StrictMode, y se revoca al desmontar", () => {
      const { unmount } = render(
        <StrictMode>
          <CameraViewer shape="oval" facing="user" captureLabel="Capturar rostro" onContinue={vi.fn()} initialBlob={new Blob(["x"])} />
        </StrictMode>,
      );
      const img = screen.getByAltText("Vista previa de la foto capturada");
      const current = img.getAttribute("src") as string;
      expect(current).toMatch(/^blob:preview-/);
      expect(revoked()).not.toContain(current);
      unmount();
      expect(revoked()).toContain(current);
    });
  });

  it("detiene todos los tracks al desmontar", async () => {
    const { stream, track } = makeStream();
    mockMedia(() => Promise.resolve(stream));
    const { unmount } = view();
    await waitFor(() => expect(screen.getByRole("button", { name: /Capturar rostro/ })).toBeEnabled());
    expect(track.stop).not.toHaveBeenCalled();
    unmount();
    expect(track.stop).toHaveBeenCalledTimes(1);
  });

  it("si se desmonta antes de que llegue el stream, también lo detiene", async () => {
    const { stream, track } = makeStream();
    let resolve!: (s: MediaStream) => void;
    mockMedia(() => new Promise<MediaStream>((r) => (resolve = r)));
    const { unmount } = view();
    unmount();
    await act(async () => resolve(stream));
    expect(track.stop).toHaveBeenCalledTimes(1);
  });
});
