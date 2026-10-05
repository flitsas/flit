import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { PasoDatos } from "@/app/captura-manual/[token]/_components/PasoDatos";
import { CONSENT_TEXT, CONSENT_TEXT_VERSION } from "@/lib/captura-manual/consent";
import type { ManualCaptureClient } from "@/lib/captura-manual/types";

const VIEW = {
  fullName: "Persona de Prueba",
  documentType: "CC",
  documentNumber: "1000000000",
  productName: "FLIT 2.0",
  expiresAt: "2030-01-01T00:00:00Z",
  consentTextVersion: CONSENT_TEXT_VERSION,
};

function setup(postConsent = vi.fn().mockResolvedValue(undefined)) {
  const client: ManualCaptureClient = {
    getManualCapture: vi.fn(),
    postConsent,
    submit: vi.fn(),
  };
  const onDone = vi.fn();
  render(<PasoDatos token="tok" view={VIEW} client={client} onDone={onDone} />);
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

  it("el texto de consentimiento es el literal de Kyverum", () => {
    setup();
    expect(CONSENT_TEXT.endsWith("no conserva las imágenes. Aviso de privacidad.")).toBe(true);
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
    expect(postConsent).toHaveBeenCalledWith("tok", { accepted: true, textVersion: CONSENT_TEXT_VERSION });
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
});
