// Feature #14307 (HUs #14308 y #14309): la captura manual valida cada foto en el dispositivo, muestra el motivo,
// permite continuar tras 3 intentos fallidos o sin soporte, y envía el resultado. Motor simulado: nada de
// imágenes reales ni de visión por computador en jsdom.
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from "vitest";
import { CapturaManualFlow } from "@/app/verificacion/[token]/_components/CapturaManualFlow";
import { createHttpClient } from "@/lib/captura-manual/client";
import { ManualCaptureError, type ManualCaptureClient, type ManualCaptureView } from "@/lib/captura-manual/types";
import { SinSoporteError, type CajaRostro, type MotorFotos } from "@/lib/captura-manual/validacionFotos";

const VIEW: ManualCaptureView = {
  fullName: "Persona de Prueba",
  documentType: "CC",
  documentNumber: "1000000000",
  productName: "FLIT 2.0",
  expiresAt: "2030-01-01T00:00:00Z",
  consentTextVersion: "manual-ley1581-v2",
};

const SELFIE_OK: CajaRostro = { x: 540, y: 210, w: 200, h: 260 };
const RETRATO: CajaRostro = { x: 700, y: 200, w: 110, h: 140 };

type Overrides = { [K in keyof MotorFotos]?: ReturnType<typeof vi.fn> };

/** Motor simulado: por defecto todo pasa (un rostro en la selfie, retrato en el anverso, código en el reverso). */
function motorOk(o: Overrides = {}): MotorFotos {
  const rostros = vi.fn(async () => ({ imgW: 1280, imgH: 720, cajas: [SELFIE_OK] }));
  return {
    rostros,
    hayCodigo: vi.fn(async () => true),
    hayDocumento: vi.fn(async () => true),
    precargar: vi.fn(),
    ...o,
  } as MotorFotos;
}

/** El rostro de la selfie y el del anverso no comparten cajas: se decide por el tamaño de la imagen. */
function rostrosPorPaso(selfie: CajaRostro[], anverso: CajaRostro[] | (() => CajaRostro[])) {
  return vi.fn(async () => {
    const paso = document.querySelector("h1")?.textContent ?? "";
    if (paso.includes("facial")) return { imgW: 1280, imgH: 720, cajas: selfie };
    return { imgW: 1000, imgH: 630, cajas: typeof anverso === "function" ? anverso() : anverso };
  });
}

function fakeClient(view: ManualCaptureView = VIEW, submit: Mock = vi.fn().mockResolvedValue({ status: "pendiente_revision_manual" })): ManualCaptureClient {
  return {
    getManualCapture: vi.fn().mockResolvedValue(view),
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
  vi.restoreAllMocks();
});

async function aRostro(motor: MotorFotos, client = fakeClient()) {
  render(<CapturaManualFlow token="ok" client={client} motorFotos={motor} />);
  fireEvent.click(await screen.findByRole("checkbox"));
  fireEvent.click(screen.getByRole("button", { name: "Iniciar verificación" }));
  await screen.findByRole("heading", { name: "Verificación facial" });
  return client;
}

/** Toca «Capturar …» y espera a que termine la revisión (aparece Confirmar o el rechazo). */
async function capturar(label: RegExp) {
  const btn = screen.getByRole("button", { name: label });
  await waitFor(() => expect(btn).toBeEnabled());
  fireEvent.click(btn);
  await waitFor(() => expect(screen.queryByText("Revisando tu foto…")).not.toBeInTheDocument());
}
function sign() {
  const canvas = screen.getByLabelText(/Lienzo para trazar tu firma/);
  fireEvent.pointerDown(canvas, { pointerId: 1, clientX: 5, clientY: 5, button: 0 });
  fireEvent.pointerMove(canvas, { pointerId: 1, clientX: 40, clientY: 20 });
  fireEvent.pointerUp(canvas, { pointerId: 1 });
}
const confirmar = () => screen.getByRole("button", { name: "Confirmar" });
const repetir = () => screen.getByRole("button", { name: "Repetir" });

describe("selfie (HU #14308)", () => {
  it("AC1: un rostro dentro del óvalo → se puede confirmar y continuar", async () => {
    await aRostro(motorOk());
    await capturar(/Capturar rostro/);
    expect(screen.queryByTestId("rechazo-foto")).not.toBeInTheDocument();
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Documento — anverso" });
  });

  it("AC2: sin rostro → «No vemos tu rostro» en una región aria-live y se repite; no hay Confirmar", async () => {
    const motor = motorOk({ rostros: vi.fn(async () => ({ imgW: 1280, imgH: 720, cajas: [] })) });
    await aRostro(motor);
    await capturar(/Capturar rostro/);
    const aviso = screen.getByTestId("rechazo-foto");
    expect(aviso).toHaveTextContent("No vemos tu rostro");
    expect(aviso).toHaveAttribute("aria-live", "assertive");
    expect(aviso).toHaveAttribute("role", "alert");
    expect(screen.queryByRole("button", { name: "Confirmar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Continuar sin verificar/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Repetir" }));
    expect(await screen.findByRole("button", { name: /Capturar rostro/ })).toBeInTheDocument();
  });

  it("AC3: dos rostros → «Debe aparecer solo una persona»", async () => {
    const otro: CajaRostro = { x: 560, y: 220, w: 150, h: 200 };
    await aRostro(motorOk({ rostros: vi.fn(async () => ({ imgW: 1280, imgH: 720, cajas: [SELFIE_OK, otro] })) }));
    await capturar(/Capturar rostro/);
    expect(screen.getByTestId("rechazo-foto")).toHaveTextContent("Debe aparecer solo una persona");
  });

  it("AC4: tras 3 fotos que no pasan, la siguiente puede continuar como no verificada", async () => {
    const motor = motorOk({ rostros: vi.fn(async () => ({ imgW: 1280, imgH: 720, cajas: [] })) });
    const submit = vi.fn().mockResolvedValue({ status: "ok" });
    await aRostro(motor, fakeClient(VIEW, submit));
    for (let i = 1; i <= 3; i++) {
      await capturar(/Capturar rostro/);
      expect(screen.queryByRole("button", { name: /Continuar sin verificar/ })).not.toBeInTheDocument();
      if (i === 3) expect(screen.getByText("Si no pasa otra vez, podrás continuar.")).toBeInTheDocument();
      fireEvent.click(repetir());
      await screen.findByRole("button", { name: /Capturar rostro/ });
    }
    await capturar(/Capturar rostro/);
    expect(screen.getByTestId("rechazo-foto")).toHaveTextContent("No vemos tu rostro");
    fireEvent.click(screen.getByRole("button", { name: /Continuar sin verificar/ }));
    await screen.findByRole("heading", { name: "Documento — anverso" });
  });

  it("AC5: si el dispositivo no puede validar, se puede continuar y se avisa sin bloquear", async () => {
    const motor = motorOk({ rostros: vi.fn(async () => Promise.reject(new SinSoporteError())) });
    await aRostro(motor);
    await capturar(/Capturar rostro/);
    expect(screen.getByText("No pudimos revisar esta foto. Puedes continuar.")).toBeInTheDocument();
    expect(screen.queryByTestId("rechazo-foto")).not.toBeInTheDocument();
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Documento — anverso" });
  });

  it("AC6: la validación no envía imágenes a la red (ni fetch ni sendBeacon) y los motores se precargan", async () => {
    const fetchSpy = vi.fn();
    vi.stubGlobal("fetch", fetchSpy);
    const beacon = vi.fn();
    Object.defineProperty(navigator, "sendBeacon", { configurable: true, value: beacon });
    const motor = motorOk();
    await aRostro(motor);
    await capturar(/Capturar rostro/);
    expect(motor.precargar).toHaveBeenCalledWith("rostro", false);
    expect(motor.rostros).toHaveBeenCalledTimes(1);
    expect(fetchSpy).not.toHaveBeenCalled();
    expect(beacon).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
    Reflect.deleteProperty(navigator, "sendBeacon");
  });
});

describe("documento (HU #14309)", () => {
  async function aAnverso(motor: MotorFotos, view = VIEW, submit?: Mock) {
    const client = fakeClient(view, submit);
    await aRostro(motor, client);
    await capturar(/Capturar rostro/);
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Documento — anverso" });
    return client;
  }

  it("AC1: anverso con un rostro visible → se puede confirmar", async () => {
    await aAnverso(motorOk({ rostros: rostrosPorPaso([SELFIE_OK], [RETRATO]) }));
    await capturar(/Capturar documento/);
    expect(screen.queryByTestId("rechazo-foto")).not.toBeInTheDocument();
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Documento — reverso" });
  });

  it("AC2: anverso sin rostro → «Muestra el lado de tu documento con tu foto»", async () => {
    await aAnverso(motorOk({ rostros: rostrosPorPaso([SELFIE_OK], []) }));
    await capturar(/Capturar documento/);
    expect(screen.getByTestId("rechazo-foto")).toHaveTextContent("Muestra el lado de tu documento con tu foto");
  });

  it("AC5: sin documento → pide encuadrar el documento y se repite", async () => {
    await aAnverso(motorOk({ rostros: rostrosPorPaso([SELFIE_OK], []), hayDocumento: vi.fn(async () => false) }));
    await capturar(/Capturar documento/);
    expect(screen.getByTestId("rechazo-foto")).toHaveTextContent(/Encuadra el documento/);
    expect(screen.getByRole("button", { name: "Repetir" })).toBeInTheDocument();
  });

  it("AC3 y AC4: reverso con código pasa; sin código → «Muestra el lado con el código de barras»", async () => {
    const hayCodigo = vi.fn().mockResolvedValueOnce(false).mockResolvedValue(true);
    await aAnverso(motorOk({ rostros: rostrosPorPaso([SELFIE_OK], [RETRATO]), hayCodigo }));
    await capturar(/Capturar documento/);
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Documento — reverso" });
    await capturar(/Capturar documento/);
    expect(screen.getByTestId("rechazo-foto")).toHaveTextContent("Muestra el lado con el código de barras");
    fireEvent.click(repetir());
    await capturar(/Capturar documento/);
    expect(screen.queryByTestId("rechazo-foto")).not.toBeInTheDocument();
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Autorización de trámite digital" });
  });

  it("AC6: tras 3 fotos del anverso que no pasan, la siguiente continúa; el contador sobrevive al volver a repetir ese paso", async () => {
    // El anverso falla dos veces, luego pasa y se confirma; el envío devuelve 413 y el flujo vuelve al anverso.
    const cajas = { anverso: [] as CajaRostro[] };
    const submit = vi.fn().mockRejectedValueOnce(new ManualCaptureError(413, "archivo_grande", "anverso demasiado grande"));
    const motor = motorOk({ rostros: rostrosPorPaso([SELFIE_OK], () => cajas.anverso) });
    await aAnverso(motor, VIEW, submit);
    for (let i = 0; i < 2; i++) {
      await capturar(/Capturar documento/);
      expect(screen.getByTestId("rechazo-foto")).toBeInTheDocument();
      fireEvent.click(repetir());
      await screen.findByRole("button", { name: /Capturar documento/ });
    }
    cajas.anverso = [RETRATO]; // el tercer intento pasa
    await capturar(/Capturar documento/);
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Documento — reverso" });
    await capturar(/Capturar documento/);
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Autorización de trámite digital" });
    sign();
    const firmar = screen.getByRole("button", { name: /Firmar y autorizar/ });
    await waitFor(() => expect(firmar).toBeEnabled());
    fireEvent.click(firmar);
    // De vuelta en el anverso (foto ya confirmada, vista previa): «Repetir» y fallar de nuevo cuenta como el 3.º fallo.
    await screen.findByRole("heading", { name: "Documento — anverso" });
    cajas.anverso = [];
    fireEvent.click(repetir());
    await screen.findByRole("button", { name: /Capturar documento/ });
    await capturar(/Capturar documento/);
    expect(screen.queryByRole("button", { name: /Continuar sin verificar/ })).not.toBeInTheDocument();
    expect(screen.getByText("Si no pasa otra vez, podrás continuar.")).toBeInTheDocument(); // el contador llegó a 3, no a 1
    fireEvent.click(repetir());
    // Cuarta foto: ya puede continuar.
    await capturar(/Capturar documento/);
    fireEvent.click(screen.getByRole("button", { name: /Continuar sin verificar/ }));
    await screen.findByRole("heading", { name: "Documento — reverso" });
  });

  it("AC7: pasaporte (sin reverso): valida que sea un documento, no el lado, y el flujo sigue con las tres fotos", async () => {
    const motor = motorOk({ rostros: rostrosPorPaso([SELFIE_OK], []), hayCodigo: vi.fn(async () => false) });
    await aAnverso(motor, { ...VIEW, documentType: "PAS" });
    await capturar(/Capturar documento/); // anverso sin rostro: pasa porque es un documento
    expect(screen.queryByTestId("rechazo-foto")).not.toBeInTheDocument();
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Documento — reverso" });
    await capturar(/Capturar documento/); // reverso sin código: pasa
    expect(screen.queryByTestId("rechazo-foto")).not.toBeInTheDocument();
    expect(motor.hayCodigo).not.toHaveBeenCalled();
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Autorización de trámite digital" });
  });
});

describe("envío del resultado (contrato validacionFotos)", () => {
  it("manda el estado de cada foto: la verificada y las que continuaron sin pasar", async () => {
    const submit = vi.fn().mockResolvedValue({ status: "pendiente_revision_manual" });
    const motor = motorOk({
      rostros: rostrosPorPaso([SELFIE_OK], []), // anverso sin rostro → lado_incorrecto
      hayCodigo: vi.fn(async () => Promise.reject(new SinSoporteError())), // reverso: sin soporte
    });
    await aRostro(motor, fakeClient(VIEW, submit));
    await capturar(/Capturar rostro/);
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Documento — anverso" });
    for (let i = 0; i < 3; i++) {
      await capturar(/Capturar documento/);
      fireEvent.click(repetir());
      await screen.findByRole("button", { name: /Capturar documento/ });
    }
    await capturar(/Capturar documento/);
    fireEvent.click(screen.getByRole("button", { name: /Continuar sin verificar/ }));
    await screen.findByRole("heading", { name: "Documento — reverso" });
    await capturar(/Capturar documento/);
    fireEvent.click(confirmar());
    await screen.findByRole("heading", { name: "Autorización de trámite digital" });
    sign();
    const firmar = screen.getByRole("button", { name: /Firmar y autorizar/ });
    await waitFor(() => expect(firmar).toBeEnabled());
    fireEvent.click(firmar);
    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1));
    expect(submit.mock.calls[0][2]).toEqual({
      rostro: { estado: "verificada", motivo: null },
      anverso: { estado: "no_verificada", motivo: "lado_incorrecto" },
      reverso: { estado: "no_verificada", motivo: "sin_soporte" },
    });
  });

  it("el cliente HTTP agrega validacionFotos como texto JSON al multipart", async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: "ok" }), { status: 200 }));
    const client = createHttpClient(fetchImpl as unknown as typeof fetch);
    const b = (t: string) => new Blob(["x"], { type: t });
    const validacion = {
      rostro: { estado: "verificada" as const, motivo: null },
      anverso: { estado: "no_verificada" as const, motivo: "lado_incorrecto" as const },
      reverso: { estado: "no_verificada" as const, motivo: "sin_soporte" as const },
    };
    await client.submit("tok", { rostro: b("image/jpeg"), anverso: b("image/jpeg"), reverso: b("image/jpeg"), firma: b("image/png") }, validacion);
    const form = fetchImpl.mock.calls[0][1].body as FormData;
    expect(typeof form.get("validacionFotos")).toBe("string");
    expect(JSON.parse(form.get("validacionFotos") as string)).toEqual(validacion);
    expect(form.get("rostro")).toBeInstanceOf(Blob);
  });
});
