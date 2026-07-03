// HU #10469 — Formulario de captura del módulo "Generación de improntas".
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ImprontaFormPanel } from "../ImprontaFormPanel";

vi.mock("@/lib/api/client", () => ({
  getToken: vi.fn().mockReturnValue("fake-token"),
}));

vi.mock("@/lib/auth/jwt", () => ({
  decodeJwtPayload: vi.fn().mockReturnValue({
    tenant_name: "Renting Demo S.A.S.",
    display_name: "Ana Operadora",
    email: "ana@example.com",
  }),
}));

vi.mock("@/lib/api/admin-improntas", () => ({
  generarImpronta: vi.fn(),
}));

import { generarImpronta } from "@/lib/api/admin-improntas";

describe("ImprontaFormPanel — HU #10469", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC1 pre-carga orgNombre y operador desde la sesión (tenant_name/display_name), editables", () => {
    render(<ImprontaFormPanel />);
    expect(screen.getByLabelText(/Nombre de la organización/i)).toHaveValue("Renting Demo S.A.S.");
    expect(screen.getByLabelText(/^Operador/i)).toHaveValue("Ana Operadora");
  });

  it("AC3 bloquea el envío y muestra errores de placa y documento si están vacíos, sin invocar al backend", async () => {
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    expect(await screen.findByText(/La placa es obligatoria\./i)).toBeInTheDocument();
    expect(
      screen.getByText(/El documento del propietario es obligatorio\./i),
    ).toBeInTheDocument();
    expect(generarImpronta).not.toHaveBeenCalled();
  });

  it("NO bloquea el envío si motor/chasis/serie y NIT/ciudad están vacíos (opcionales tras verificar contra el proveedor real)", async () => {
    vi.mocked(generarImpronta).mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.type(screen.getByLabelText(/^Placa/i), "abc123");
    await user.type(screen.getByLabelText(/Documento del propietario/i), "1040326572");

    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    await waitFor(() => expect(generarImpronta).toHaveBeenCalledTimes(1));
    expect(generarImpronta).toHaveBeenCalledWith(
      expect.objectContaining({
        placa: "ABC123",
        documento: "1040326572",
        numMotor: undefined,
        numChasis: undefined,
        numSerie: undefined,
        orgNit: undefined,
        orgCiudad: undefined,
        operador: "Ana Operadora",
      }),
    );
    expect(await screen.findByTestId("impronta-success")).toBeInTheDocument();
  });

  it("envía la solicitud completa (con motor, NIT y ciudad) y muestra el estado de éxito", async () => {
    vi.mocked(generarImpronta).mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.type(screen.getByLabelText(/^Placa/i), "abc123");
    await user.type(screen.getByLabelText(/Documento del propietario/i), "1040326572");
    await user.type(screen.getByLabelText(/Número de motor/i), "MTR-1");
    await user.type(screen.getByLabelText(/^NIT/i), "900123456-7");
    await user.type(screen.getByLabelText(/^Ciudad/i), "Bogotá D.C.");

    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    await waitFor(() => expect(generarImpronta).toHaveBeenCalledTimes(1));
    expect(generarImpronta).toHaveBeenCalledWith(
      expect.objectContaining({
        placa: "ABC123",
        documento: "1040326572",
        numMotor: "MTR-1",
        orgNit: "900123456-7",
        orgCiudad: "Bogotá D.C.",
        operador: "Ana Operadora",
      }),
    );
    expect(await screen.findByTestId("impronta-success")).toBeInTheDocument();
  });

  it("muestra un estado de error genérico cuando la llamada al backend falla", async () => {
    vi.mocked(generarImpronta).mockRejectedValue(new Error("boom"));
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.type(screen.getByLabelText(/^Placa/i), "abc123");
    await user.type(screen.getByLabelText(/Documento del propietario/i), "1040326572");

    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    expect(await screen.findByTestId("impronta-error")).toBeInTheDocument();
  });
});
