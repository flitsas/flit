// Cabeza Marca Blanca: la tabla de clientes hijos y la identidad de marca (con el dominio) son
// pestañas propias de la ficha, no cajas apiladas bajo las pestañas. Sin slot, no aparecen.
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyConfigTabs } from "../CompanyConfigTabs";
import type { TenantSettings } from "@/lib/api/types";

vi.mock("@/lib/api/platform", () => ({ listTenantProducts: vi.fn().mockResolvedValue([]), setTenantProduct: vi.fn() }));

const settings: TenantSettings = {
  tenantId: "t1",
  switchesMatricula: {
    allowInitialRegistration: true,
    allowMiscNewVehicles: false,
    onlyOwnVehicles: true,
    onlyOwnVehiclesByFamily: { matriculas: true, traspaso: true, otros: true },
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  },
  baulFirmasActivo: true,
  preasignacionPlacaActiva: false,
  validarSoatConRunt: false,
  enrutamientoSMTP: "FLIT_SMTP",
  notificationTarget: "COMPRADOR",
  metodosRecaudo: ["Pasarela FLIT"],
};

describe("CompanyConfigTabs — cabeza Marca Blanca", () => {
  it("muestra «Clientes hijos» e «Identidad de marca» antes del historial y cada una solo con lo suyo", async () => {
    const user = userEvent.setup();
    render(
      <CompanyConfigTabs
        settings={settings}
        onSaveSettings={vi.fn()}
        auditSlot={<p>Historial</p>}
        childrenSlot={<p>Tabla de clientes hijos</p>}
        brandingSlot={<p>Configurador y dominio</p>}
      />,
    );

    const names = screen.getAllByRole("tab").map((t) => t.textContent?.trim());
    expect(names.slice(-3)).toEqual(["Clientes hijos", "Identidad de marca", "Historial de Cambios"]);
    expect(screen.queryByText("Tabla de clientes hijos")).not.toBeInTheDocument();
    expect(screen.queryByText("Configurador y dominio")).not.toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Identidad de marca" }));
    expect(screen.getByText("Configurador y dominio")).toBeInTheDocument();
    expect(screen.queryByText("Tabla de clientes hijos")).not.toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Clientes hijos" }));
    expect(screen.getByText("Tabla de clientes hijos")).toBeInTheDocument();
  });

  it("sin slots (compañía que no es cabeza Marca Blanca) las pestañas no aparecen", () => {
    render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn()} />);

    expect(screen.queryByRole("tab", { name: "Clientes hijos" })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Identidad de marca" })).not.toBeInTheDocument();
  });
});
