import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { PasoDatos } from "@/app/verificacion/[token]/_components/PasoDatos";
import { CONSENT_TEXT, RENDERED_CONSENT_TEXT_VERSION } from "@/lib/captura-manual/consent";
import type { ManualCaptureClient, ManualCaptureView } from "@/lib/captura-manual/types";

const VIEW: ManualCaptureView = {
  fullName: "Persona de Prueba",
  documentType: "CC",
  documentNumber: "1000000000",
  productName: "FLIT 2.0",
  expiresAt: "2030-01-01T00:00:00Z",
  consentTextVersion: RENDERED_CONSENT_TEXT_VERSION,
};

function setup(postConsent = vi.fn().mockResolvedValue(undefined), view: ManualCaptureView = VIEW) {
  const client: ManualCaptureClient = {
    getManualCapture: vi.fn(),
    postConsent,
    submit: vi.fn(),
  };
  const onDone = vi.fn();
  render(<PasoDatos token="tok" view={view} client={client} onDone={onDone} />);
  return { postConsent, onDone };
}

const button = () => screen.getByRole("button", { name: "Iniciar verificación" });

describe("PasoDatos", () => {
  it("muestra saludo, tabla de solo lectura, consejos y pie legal", () => {
    setup();
    expect(screen.getByRole("heading", { name: "Hola Persona de Prueba" })).toBeInTheDocument();
    const table = screen.getByRole("table");
    expect(table).toHaveTextContent("Nombre");
    expect(table).toHaveTextContent("CC 1000000000");
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    expect(screen.getByText("Para que salga bien")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(3);
    expect(screen.getByText(/y tu firma/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "samuel.cardenas@flitsas.com" })).toBeInTheDocument();
  });

  it("el texto de consentimiento no menciona a Kyverum", () => {
    setup();
    expect(CONSENT_TEXT.endsWith("No estoy obligado(a). Aviso de privacidad.")).toBe(true);
    expect(CONSENT_TEXT).not.toMatch(/kyverum/i);
    expect(document.body.textContent).not.toMatch(/kyverum/i);
    expect(screen.getByLabelText(/Autorizo de forma libre, previa y expresa a Flit/)).toBeInTheDocument();
  });

  it("el botón está deshabilitado hasta marcar la casilla", () => {
    setup();
    expect(button()).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox"));
    expect(button()).toBeEnabled();
  });

  it("al iniciar registra el consentimiento con la versión y avanza", async () => {
    const { postConsent, onDone } = setup();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(button());
    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    expect(postConsent).toHaveBeenCalledWith("tok", { accepted: true, textVersion: "manual-ley1581-v2" });
  });

  it("si falla el registro muestra error, conserva la casilla y permite reintentar", async () => {
    const post = vi.fn().mockRejectedValueOnce(new Error("net")).mockResolvedValue(undefined);
    const { onDone } = setup(post);
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(button());
    expect(await screen.findByRole("alert")).toHaveTextContent("No pudimos registrar");
    expect(screen.getByRole("checkbox")).toBeChecked();
    expect(onDone).not.toHaveBeenCalled();
    fireEvent.click(button());
    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    expect(post).toHaveBeenCalledTimes(2);
  });

  it("la versión enviada es la que devolvió el GET y coincide con la del backend", async () => {
    expect(RENDERED_CONSENT_TEXT_VERSION).toBe("manual-ley1581-v2");
    const { postConsent, onDone } = setup(undefined, { ...VIEW, consentTextVersion: RENDERED_CONSENT_TEXT_VERSION });
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(button());
    await waitFor(() => expect(onDone).toHaveBeenCalled());
    expect(postConsent).toHaveBeenCalledWith("tok", { accepted: true, textVersion: "manual-ley1581-v2" });
  });

  it("si el backend usa otra versión del texto bloquea con error claro y no llama a postConsent", () => {
    const { postConsent, onDone } = setup(undefined, { ...VIEW, consentTextVersion: "manual-ley1581-v1" });
    expect(screen.getByRole("alert")).toHaveTextContent("El texto de consentimiento cambió; recarga la página.");
    fireEvent.click(screen.getByRole("checkbox"));
    expect(button()).toBeDisabled();
    fireEvent.click(button());
    expect(postConsent).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
  });

  it("productName null o vacío cae a «FLIT 2.0»; con valor lo muestra", () => {
    const { unmount } = render(
      <PasoDatos token="t" view={{ ...VIEW, productName: null }} client={{} as ManualCaptureClient} onDone={vi.fn()} />,
    );
    expect(screen.getByText(/^FLIT 2\.0 necesita verificar tu identidad/)).toBeInTheDocument();
    unmount();
    render(
      <PasoDatos token="t" view={{ ...VIEW, productName: "  " }} client={{} as ManualCaptureClient} onDone={vi.fn()} />,
    );
    expect(screen.getByText(/^FLIT 2\.0 necesita/)).toBeInTheDocument();
    cleanup();
    render(
      <PasoDatos token="t" view={{ ...VIEW, productName: "Traspaso" }} client={{} as ManualCaptureClient} onDone={vi.fn()} />,
    );
    expect(screen.getByText(/^Traspaso necesita/)).toBeInTheDocument();
  });
});
