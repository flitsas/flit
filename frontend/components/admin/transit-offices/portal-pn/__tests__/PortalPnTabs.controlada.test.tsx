// Portal Persona Natural — pestaña activa controlada desde la página (la URL `?tab=` manda), sin modo de sección única.
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

vi.mock("@/lib/api/admin-ot-portal-marca", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-ot-portal-marca")>("@/lib/api/admin-ot-portal-marca");
  return { ...actual, getPortalMarca: vi.fn(() => new Promise(() => undefined)) };
});

import { PortalPnTabs } from "../PortalPnTabs";

const TABLIST = { name: "Secciones del portal Persona Natural" };

describe("PortalPnTabs con pestaña controlada", () => {
  it("muestra siempre la fila con las cuatro pestañas y marca la activa que llega por props", () => {
    render(
      <PortalPnTabs
        transitOfficeId="ot-1"
        activeTab="caja"
        reportes={<p>Reportes aquí</p>}
        cobro={<p>Cobro aquí</p>}
        caja={<p>Caja aquí</p>}
      />,
    );
    const tabs = within(screen.getByRole("tablist", TABLIST)).getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual(["Reportes", "Cobro", "Caja", "Diseño"]);
    expect(screen.getByRole("tab", { name: "Caja" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Caja aquí")).toBeVisible();
  });

  it("al pulsar otra pestaña avisa a la página y no cambia sola si la página no actualiza la activa", async () => {
    const user = userEvent.setup({ applyAccept: false });
    const onTabChange = vi.fn();
    render(
      <PortalPnTabs
        transitOfficeId="ot-1"
        activeTab="reportes"
        onTabChange={onTabChange}
        reportes={<p>Reportes aquí</p>}
        cobro={<p>Cobro aquí</p>}
        caja={<p>Caja aquí</p>}
      />,
    );
    await user.click(screen.getByRole("tab", { name: "Cobro" }));
    expect(onTabChange).toHaveBeenCalledWith("cobro");
    expect(screen.getByRole("tab", { name: "Reportes" })).toHaveAttribute("aria-selected", "true");
  });

  it("sin activeTab sigue funcionando con estado propio (initialTab)", async () => {
    const user = userEvent.setup({ applyAccept: false });
    render(<PortalPnTabs transitOfficeId="ot-1" initialTab="cobro" cobro={<p>Cobro aquí</p>} caja={<p>Caja aquí</p>} />);
    expect(screen.getByRole("tab", { name: "Cobro" })).toHaveAttribute("aria-selected", "true");
    await user.click(screen.getByRole("tab", { name: "Caja" }));
    expect(screen.getByRole("tab", { name: "Caja" })).toHaveAttribute("aria-selected", "true");
  });
});
