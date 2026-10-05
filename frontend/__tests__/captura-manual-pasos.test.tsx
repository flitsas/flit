import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CapturaManualFlow } from "@/app/captura-manual/[token]/_components/CapturaManualFlow";
import { StepBar } from "@/app/captura-manual/[token]/_components/StepBar";
import {
  initialStepsState,
  progressText,
  statusOf,
  stepsReducer,
} from "@/lib/captura-manual/steps";
import { ManualCaptureError, type ManualCaptureClient } from "@/lib/captura-manual/types";

const VIEW = {
  fullName: "Persona de Prueba",
  documentType: "CC",
  documentNumber: "1000000000",
  productName: "FLIT 2.0",
  expiresAt: "2030-01-01T00:00:00Z",
  consentTextVersion: "v",
};

function fakeClient(over: Partial<ManualCaptureClient> = {}): ManualCaptureClient {
  return {
    getManualCapture: vi.fn().mockResolvedValue(VIEW),
    postConsent: vi.fn().mockResolvedValue(undefined),
    submit: vi.fn().mockResolvedValue({ status: "pendiente_revision_manual" }),
    ...over,
  };
}

describe("stepsReducer", () => {
  it("avanza marcando el paso completado", () => {
    const s = stepsReducer(initialStepsState, { type: "next" });
    expect(s.current).toBe(1);
    expect(s.completed).toEqual([0]);
    expect(statusOf(s, 0)).toBe("done");
    expect(statusOf(s, 1)).toBe("active");
    expect(statusOf(s, 2)).toBe("pending");
  });

  it("retrocede y des-completa el paso al que vuelve", () => {
    let s = initialStepsState;
    s = stepsReducer(s, { type: "next" });
    s = stepsReducer(s, { type: "next" });
    s = stepsReducer(s, { type: "back" });
    expect(s.current).toBe(1);
    expect(s.completed).toEqual([0]);
  });

  it("no retrocede del primer paso", () => {
    expect(stepsReducer(initialStepsState, { type: "back" })).toBe(initialStepsState);
  });

  it("termina al completar el quinto paso y ya no avanza", () => {
    let s = initialStepsState;
    for (let i = 0; i < 5; i++) s = stepsReducer(s, { type: "next" });
    expect(s.finished).toBe(true);
    expect(s.completed).toHaveLength(5);
    expect(stepsReducer(s, { type: "next" })).toBe(s);
  });

  it("anuncia «Paso N de 5»", () => {
    expect(progressText(initialStepsState)).toBe("Paso 1 de 5: Datos");
    expect(progressText(stepsReducer(initialStepsState, { type: "next" }))).toBe("Paso 2 de 5: Rostro");
  });
});

describe("StepBar", () => {
  it("muestra las 5 etiquetas, el activo con aria-current y el completado con check", () => {
    const s = stepsReducer(initialStepsState, { type: "next" });
    render(<StepBar state={s} />);
    for (const l of ["Datos", "Rostro", "Anverso", "Reverso", "Firma"]) {
      expect(screen.getByText(l)).toBeInTheDocument();
    }
    const items = screen.getAllByRole("listitem");
    expect(items).toHaveLength(5);
    expect(items[1]).toHaveAttribute("aria-current", "step");
    expect(items[0]).toHaveTextContent("completado");
    expect(screen.getByRole("status")).toHaveTextContent("Paso 2 de 5: Rostro");
  });
});

describe("CapturaManualFlow", () => {
  it("con sesión válida muestra la barra y avanza de paso", async () => {
    render(<CapturaManualFlow token="ok" client={fakeClient()} />);
    expect(await screen.findByRole("navigation", { name: /progreso/i })).toBeInTheDocument();
    expect(screen.getByAltText("flit")).toBeInTheDocument();
    expect(screen.getByText("Verify")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Iniciar verificación" }));
    await screen.findByRole("heading", { name: "Rostro" });
    expect(screen.getAllByRole("listitem")[0]).toHaveTextContent("completado");
  });

  it.each([
    [410, "expirada", "Este enlace venció"],
    [410, "reemplazado", "Este enlace fue reemplazado"],
    [409, "estado_invalido", "Este enlace ya no se puede usar"],
    [404, "not_found", "No encontramos este enlace"],
  ])("estado terminal %s %s sin mostrar pasos ni datos", async (status, code, title) => {
    const client = fakeClient({
      getManualCapture: vi.fn().mockRejectedValue(new ManualCaptureError(status, code, "x")),
    });
    render(<CapturaManualFlow token="t" client={client} />);
    expect(await screen.findByRole("heading", { name: title })).toBeInTheDocument();
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
    expect(screen.queryByText(/Persona de Prueba/)).not.toBeInTheDocument();
    expect(screen.getByText("¿Problemas? Escríbele a FLIT 2.0")).toBeInTheDocument();
  });

  it("un fallo de red ofrece reintentar y recupera", async () => {
    const get = vi.fn().mockRejectedValueOnce(new TypeError("net")).mockResolvedValue(VIEW);
    render(<CapturaManualFlow token="t" client={fakeClient({ getManualCapture: get })} />);
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(screen.getByRole("navigation")).toBeInTheDocument());
    expect(get).toHaveBeenCalledTimes(2);
  });
});
