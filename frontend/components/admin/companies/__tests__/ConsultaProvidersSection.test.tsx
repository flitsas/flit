// HU #10478 — sección "Proveedores de consulta RUNT": 3 selectores (Kyverum default, Verifik,
// Intempo deshabilitado) + input de timeout de failover.
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ConsultaProvidersSection } from "../ConsultaProvidersSection";
import { formFromSettings, type SettingsForm } from "../settingsForm";
import type { TenantSettings } from "@/lib/api/types";

const baseSettings: TenantSettings = {
  tenantId: "t1",
  switchesMatricula: { allowInitialRegistration: true, allowMiscNewVehicles: true, onlyOwnVehicles: false },
  baulFirmasActivo: false,
  enrutamientoSMTP: "FLIT_SMTP",
  notificationTarget: "RADICADOR",
  metodosRecaudo: [],
};

const baseForm = (): SettingsForm => formFromSettings(baseSettings);

describe("ConsultaProvidersSection (HU #10478)", () => {
  it("muestra Kyverum como default en los 3 selectores y Intempo deshabilitado", () => {
    render(<ConsultaProvidersSection form={baseForm()} onChange={vi.fn()} />);

    expect(screen.getByLabelText<HTMLSelectElement>("Vehículo por VIN").value).toBe("kyverum_runt");
    expect(screen.getByLabelText<HTMLSelectElement>("Vehículo por placa").value).toBe("kyverum_runt");
    expect(screen.getByLabelText<HTMLSelectElement>("Conductor").value).toBe("kyverum_runt_conductor");

    // Intempo aparece pero no es seleccionable (aún no disponible).
    const intempoOptions = screen.getAllByRole("option", { name: /intempo/i }) as HTMLOptionElement[];
    expect(intempoOptions.length).toBeGreaterThan(0);
    expect(intempoOptions.every((o) => o.disabled)).toBe(true);
  });

  it("cambiar el selector de VIN emite onChange con el proveedor elegido", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ConsultaProvidersSection form={baseForm()} onChange={onChange} />);

    await user.selectOptions(screen.getByLabelText("Vehículo por VIN"), "verifik");

    expect(onChange).toHaveBeenCalledWith({ consultaVin: "verifik" });
  });

  it("edita el timeout de failover", () => {
    const onChange = vi.fn();
    render(<ConsultaProvidersSection form={baseForm()} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Timeout de failover (ms)"), { target: { value: "8000" } });

    expect(onChange).toHaveBeenCalledWith({ runtFailoverTimeoutMs: 8000 });
  });

  it("muestra el error de validación del backend en la config", () => {
    render(
      <ConsultaProvidersSection
        form={baseForm()}
        onChange={vi.fn()}
        fieldErrors={{ consultationProviderConfig: "Proveedor no permitido." }}
      />,
    );

    expect(screen.getByText(/proveedor no permitido/i)).toBeInTheDocument();
  });
});
