// AC2 — Formulario multi-pestaña con guardado atómico. "Guardar todo" dispara un
// único PUT con todos los campos; un 422 marca los campos inválidos.
//
// Uso de ejemplo:
//   render(<CompanyConfigTabs settings={settings} onSaveSettings={spy} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyConfigTabs } from "../CompanyConfigTabs";
import { ApiValidationError, type TenantSettings } from "@/lib/api/types";

const settings: TenantSettings = {
  tenantId: "t1",
  switchesMatricula: {
    allowInitialRegistration: true,
    allowMiscNewVehicles: false,
    onlyOwnVehicles: true,
  },
  baulFirmasActivo: true,
  enrutamientoSMTP: "FLIT_SMTP",
  notificationTarget: "COMPRADOR",
  metodosRecaudo: ["Pasarela FLIT"],
};

describe("CompanyConfigTabs (AC2)", () => {
  it("dispara un único PUT con todos los campos al pulsar 'Guardar todo'", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));

    expect(onSaveSettings).toHaveBeenCalledTimes(1);
    expect(onSaveSettings).toHaveBeenCalledWith({
      switchesMatricula: {
        allowInitialRegistration: true,
        allowMiscNewVehicles: false,
        onlyOwnVehicles: true,
      },
      baulFirmasActivo: true,
      enrutamientoSMTP: "FLIT_SMTP",
      notificationTarget: "COMPRADOR",
      metodosRecaudo: ["Pasarela FLIT"],
    });

    expect(await screen.findByText(/configuración guardada correctamente/i)).toBeInTheDocument();
  });

  it("incluye cambios de las pestañas en el PUT (toggle matrícula)", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    // Pestaña Matrícula Inicial activa por defecto: apaga "matrícula inicial".
    await user.click(screen.getByLabelText(/permitir matrícula inicial/i));
    await user.click(screen.getByRole("button", { name: /guardar todo/i }));

    expect(onSaveSettings).toHaveBeenCalledWith(
      expect.objectContaining({
        switchesMatricula: expect.objectContaining({ allowInitialRegistration: false }),
      }),
    );
  });

  it("marca los campos inválidos ante un 422", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi
      .fn()
      .mockRejectedValue(
        new ApiValidationError(
          [{ field: "enrutamientoSMTP", message: "Valor inválido para el canal." }],
          422,
        ),
      );

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));

    expect(await screen.findByText(/revisa los campos marcados/i)).toBeInTheDocument();

    // El detalle del campo se muestra en la pestaña Configuración Empresa.
    await user.click(screen.getByRole("tab", { name: /configuración empresa/i }));
    await waitFor(() =>
      expect(screen.getByText(/valor inválido para el canal/i)).toBeInTheDocument(),
    );
  });
});
