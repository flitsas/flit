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

function fillOrgAndOperador() {
  // orgNombre y operador ya vienen pre-cargados desde la sesión (tenant_name/display_name);
  // solo hace falta completar NIT y ciudad para dejar el formulario listo para enviar.
  return {
    orgNit: screen.getByLabelText(/^NIT/i),
    orgCiudad: screen.getByLabelText(/^Ciudad/i),
  };
}

describe("ImprontaFormPanel — HU #10469", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC1 pre-carga orgNombre y operador desde la sesión (tenant_name/display_name), editables", () => {
    render(<ImprontaFormPanel />);
    expect(screen.getByLabelText(/Nombre de la organización/i)).toHaveValue("Renting Demo S.A.S.");
    expect(screen.getByLabelText(/^Operador/i)).toHaveValue("Ana Operadora");
  });

  it("AC3 bloquea el envío y muestra error específico si placa y los tres identificadores están vacíos, sin invocar al backend", async () => {
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    expect(
      await screen.findByText(/La placa es obligatoria\./i),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Debes diligenciar al menos un identificador del vehículo/i),
    ).toBeInTheDocument();
    expect(generarImpronta).not.toHaveBeenCalled();
  });

  it("AC3 bloquea el envío si la placa está diligenciada pero motor/chasis/serie están vacíos", async () => {
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.type(screen.getByLabelText(/^Placa/i), "ABC123");
    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    expect(
      await screen.findByText(/Debes diligenciar al menos un identificador del vehículo/i),
    ).toBeInTheDocument();
    expect(generarImpronta).not.toHaveBeenCalled();
  });

  it("envía la solicitud y muestra el estado de éxito cuando el formulario es válido", async () => {
    vi.mocked(generarImpronta).mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.type(screen.getByLabelText(/^Placa/i), "abc123");
    await user.type(screen.getByLabelText(/Número de motor/i), "MTR-1");
    const { orgNit, orgCiudad } = fillOrgAndOperador();
    await user.type(orgNit, "900123456-7");
    await user.type(orgCiudad, "Bogotá D.C.");

    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    await waitFor(() => expect(generarImpronta).toHaveBeenCalledTimes(1));
    expect(generarImpronta).toHaveBeenCalledWith(
      expect.objectContaining({
        placa: "ABC123",
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
    await user.type(screen.getByLabelText(/Número de chasis/i), "CHS-1");
    const { orgNit, orgCiudad } = fillOrgAndOperador();
    await user.type(orgNit, "900123456-7");
    await user.type(orgCiudad, "Bogotá D.C.");

    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    expect(await screen.findByTestId("impronta-error")).toBeInTheDocument();
  });
});
