// HU #13401 (Feature #13398) — interruptor «Generación de improntas» en la ficha de compañía.
// AC1 visible solo para Super Admin · AC2 desactivar + guardar · AC3 error de guardado con reintento
// · AC4 estado por color y texto + accesibilidad (rol switch, aria-checked, etiqueta).
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyConfigTabs } from "../CompanyConfigTabs";
import type { TenantSettings } from "@/lib/api/types";

vi.mock("@/lib/api/platform", () => ({ listTenantProducts: vi.fn().mockResolvedValue([]), setTenantProduct: vi.fn() }));

const base: TenantSettings = {
  tenantId: "tenant-ajeno",
  switchesMatricula: {
    allowInitialRegistration: true,
    allowMiscNewVehicles: false,
    onlyOwnVehicles: false,
  },
  baulFirmasActivo: false,
  preasignacionPlacaActiva: false,
  enrutamientoSMTP: "FLIT_SMTP",
  metodosRecaudo: [],
};

const openTramites = () => fireEvent.click(screen.getByRole("tab", { name: "Trámites" }));

describe("Toggle «Generación de improntas» (HU #13401)", () => {
  it("AC1: el Super Admin lo ve", () => {
    render(<CompanyConfigTabs settings={base} onSaveSettings={vi.fn()} canConfigureImprontas />);
    openTramites();
    expect(screen.getByRole("switch", { name: "Generación de improntas" })).toBeInTheDocument();
  });

  it("AC1: otro rol (sin permiso) no lo ve", () => {
    render(<CompanyConfigTabs settings={base} onSaveSettings={vi.fn()} />);
    openTramites();
    expect(screen.queryByRole("switch", { name: "Generación de improntas" })).not.toBeInTheDocument();
  });

  it("AC1: el Administrador de Compañía (restringido) no ve ni la pestaña Trámites", () => {
    render(
      <CompanyConfigTabs settings={base} onSaveSettings={vi.fn()} restrictedToRepresentatives canConfigureImprontas />,
    );
    expect(screen.queryByRole("tab", { name: "Trámites" })).not.toBeInTheDocument();
    expect(screen.queryByRole("switch", { name: "Generación de improntas" })).not.toBeInTheDocument();
  });

  it("AC4: por defecto está habilitada, con rol switch, aria-checked y texto de estado", () => {
    render(<CompanyConfigTabs settings={base} onSaveSettings={vi.fn()} canConfigureImprontas />);
    openTramites();
    const sw = screen.getByRole("switch", { name: "Generación de improntas" });
    expect(sw).toHaveAttribute("aria-checked", "true");
    expect(screen.getByTestId("improntas-estado")).toHaveTextContent("Habilitada");
    expect(screen.getByText(/el radicador la carga a mano/i)).toBeInTheDocument();
  });

  it("AC2: desactivar y guardar envía generacionImprontas:false y confirma el cambio", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);
    render(<CompanyConfigTabs settings={{ ...base, generacionImprontas: true }} onSaveSettings={onSaveSettings} canConfigureImprontas />);
    openTramites();

    await user.click(screen.getByRole("switch", { name: "Generación de improntas" }));
    expect(screen.getByRole("switch", { name: "Generación de improntas" })).toHaveAttribute("aria-checked", "false");
    expect(screen.getByTestId("improntas-estado")).toHaveTextContent("Deshabilitada");

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("Generación de improntas")).toBeInTheDocument();
    expect(within(dialog).getByText("Deshabilitar")).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: /guardar cambios/i }));

    expect(onSaveSettings).toHaveBeenCalledTimes(1);
    expect(onSaveSettings.mock.calls[0][0]).toMatchObject({ generacionImprontas: false });
    expect(await screen.findByText("Cambios guardados")).toBeInTheDocument();
    expect(screen.getByRole("switch", { name: "Generación de improntas" })).toHaveAttribute("aria-checked", "false");
  });

  it("AC3: si el guardado falla muestra el error, conserva el valor y permite reintentar", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockRejectedValueOnce(new Error("boom")).mockResolvedValueOnce(undefined);
    render(<CompanyConfigTabs settings={base} onSaveSettings={onSaveSettings} canConfigureImprontas />);
    openTramites();

    await user.click(screen.getByRole("switch", { name: "Generación de improntas" }));
    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /guardar cambios/i }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/no se pudo guardar/i);
    expect(screen.getByRole("switch", { name: "Generación de improntas" })).toHaveAttribute("aria-checked", "false");

    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: /guardar cambios/i }));
    expect(onSaveSettings).toHaveBeenCalledTimes(2);
    expect(onSaveSettings.mock.calls[1][0]).toMatchObject({ generacionImprontas: false });
    expect(await screen.findByText("Cambios guardados")).toBeInTheDocument();
  });
});
