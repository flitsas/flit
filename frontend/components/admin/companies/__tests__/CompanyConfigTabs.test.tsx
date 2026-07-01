// AC2 — Formulario multi-pestaña con guardado atómico. "Guardar todo" detecta los cambios y
// abre una ventana de confirmación; al confirmar dispara un único PUT con todos los campos.
// Un 422 cierra la confirmación y marca los campos inválidos.
//
// Uso de ejemplo:
//   render(<CompanyConfigTabs settings={settings} onSaveSettings={spy} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
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
  it("confirma y dispara un único PUT con todos los campos", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    // "Guardar todo" no guarda directo: abre la confirmación.
    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText(/no se detectaron cambios/i)).toBeInTheDocument();
    expect(onSaveSettings).not.toHaveBeenCalled();

    // Al confirmar sí persiste (un único PUT con todo el payload).
    await user.click(within(dialog).getByRole("button", { name: /guardar cambios/i }));

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

    // El resultado se muestra en la misma ventana (fase éxito), no como banner fijo.
    expect(await screen.findByText(/cambios guardados/i)).toBeInTheDocument();
  });

  it("detecta el cambio real (activar/desactivar) por módulo y lo incluye en el PUT", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    // Pestaña Matrícula Inicial activa por defecto. Estado inicial: inicial=ON, misceláneas=OFF.
    await user.click(screen.getByLabelText(/permitir matrícula inicial/i)); // ON → OFF (Desactivar)
    await user.click(screen.getByLabelText(/permitir vehículos de categorías misceláneas/i)); // OFF → ON (Activar)
    await user.click(screen.getByRole("button", { name: /guardar todo/i }));

    // La confirmación agrupa por módulo y describe el cambio REAL de cada campo.
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("Matrícula Inicial")).toBeInTheDocument();
    expect(within(dialog).getByText(/permitir matrícula inicial/i)).toBeInTheDocument();
    expect(within(dialog).getByText("Desactivar")).toBeInTheDocument();
    expect(within(dialog).getByText("Activar")).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: /guardar cambios/i }));

    expect(onSaveSettings).toHaveBeenCalledWith(
      expect.objectContaining({
        switchesMatricula: expect.objectContaining({
          allowInitialRegistration: false,
          allowMiscNewVehicles: true,
        }),
      }),
    );
  });

  it("cancela la confirmación sin guardar", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: /cancelar/i }));

    expect(onSaveSettings).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("ante un 422 cierra la confirmación y marca los campos inválidos", async () => {
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
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: /guardar cambios/i }));

    // 422 → se cierra la confirmación y aparece el aviso de campos inválidos.
    expect(await screen.findByText(/revisa los campos marcados/i)).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    // El detalle del campo se muestra en la pestaña Configuración Empresa.
    await user.click(screen.getByRole("tab", { name: /configuración empresa/i }));
    await waitFor(() =>
      expect(screen.getByText(/valor inválido para el canal/i)).toBeInTheDocument(),
    );
  });
});
