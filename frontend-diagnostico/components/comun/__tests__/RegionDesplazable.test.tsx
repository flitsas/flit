import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, within } from "@testing-library/react";
import { DataTable, type DataTableColumn } from "@flit/ui/DataTable";
import { RegionDesplazable, RegionDesplazableTabla } from "@/components/comun/RegionDesplazable";
import { simularDesborde } from "./desborde";

// Uso de ejemplo:
//   <RegionDesplazable nombre="Personas consultadas" className="rounded-xl border"><table …/></RegionDesplazable>
//   <RegionDesplazableTabla nombre="Salud de las fuentes"><DataTable ariaLabel="Salud de las fuentes" … /></RegionDesplazableTabla>
// Ronda 2 de E2E (axe scrollable-region-focusable en el Dashboard): el contenedor `overflow-x-auto` del `DataTable` del
// kit no es enfocable y el kit queda fuera del alcance U10; el envoltorio le aplica el mismo patrón que RegionDesplazable
// (region + nombre «…, desplazable» + tabIndex 0 + foco --di-action), solo mientras desborda.

interface Fila {
  id: string;
}
const COLUMNAS: DataTableColumn<Fila>[] = [{ key: "id", header: "Id", render: (f) => f.id }];
const FILAS: Fila[] = [{ id: "a" }, { id: "b" }];
const FOCO = /focus-visible:outline-\[var\(--di-action\)\]/;

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const tablaKit = (status: "loading" | "ready" = "ready") => (
  <RegionDesplazableTabla nombre="Salud de las fuentes">
    <DataTable<Fila> ariaLabel="Salud de las fuentes" columns={COLUMNAS} rows={FILAS} getRowKey={(f) => f.id} minWidth={720} tarjeta={false} status={status} />
  </RegionDesplazableTabla>
);

describe("RegionDesplazableTabla — contenedor del DataTable del kit", () => {
  it("con desborde, el contenedor de la tabla es región con nombre, parada de Tab y foco --di-action", async () => {
    simularDesborde({ ancho: 720 });
    render(tablaKit());
    const region = await screen.findByRole("region", { name: "Salud de las fuentes, desplazable" });
    expect(region).toHaveAttribute("tabindex", "0");
    expect(region).toHaveClass("overflow-x-auto");
    expect(region.className).toMatch(FOCO);
    expect(within(region).getByRole("table", { name: "Salud de las fuentes" })).toBeInTheDocument();
  });

  it("sin desborde es un div plano: sin rol, sin nombre y sin parada de Tab", () => {
    simularDesborde({ ancho: 300 });
    render(tablaKit());
    const contenedor = screen.getByRole("table").parentElement!;
    expect(contenedor).toHaveClass("overflow-x-auto");
    expect(contenedor).not.toHaveAttribute("role");
    expect(contenedor).not.toHaveAttribute("aria-label");
    expect(contenedor).not.toHaveAttribute("tabindex");
  });

  it("si deja de desbordar al redimensionar, retira rol, nombre y tabIndex", async () => {
    const d = simularDesborde({ ancho: 720 });
    render(tablaKit());
    await screen.findByRole("region", { name: "Salud de las fuentes, desplazable" });
    d.contenido.ancho = 300;
    act(() => d.redimensionar());
    expect(screen.queryByRole("region", { name: /desplazable/ })).not.toBeInTheDocument();
    expect(screen.getByRole("table").parentElement).not.toHaveAttribute("tabindex");
  });

  it("la tabla que se monta después de cargar también queda enfocable", async () => {
    simularDesborde({ ancho: 720 });
    const { rerender } = render(tablaKit("loading"));
    expect(screen.queryByRole("region", { name: /desplazable/ })).not.toBeInTheDocument();
    rerender(tablaKit("ready"));
    expect(await screen.findByRole("region", { name: "Salud de las fuentes, desplazable" })).toHaveAttribute("tabindex", "0");
  });
});

describe("RegionDesplazable — desborde vertical", () => {
  it("un contenedor con alto máximo cuyo contenido lo supera también es región enfocable", () => {
    simularDesborde({ ancho: 300, alto: 720 });
    render(
      <RegionDesplazable nombre="Datos de Uso en el tiempo" className="max-h-64 overflow-auto">
        <table aria-label="Uso en el tiempo">
          <tbody>
            <tr>
              <td>1</td>
            </tr>
          </tbody>
        </table>
      </RegionDesplazable>,
    );
    expect(screen.getByRole("region", { name: "Datos de Uso en el tiempo, desplazable" })).toHaveAttribute("tabindex", "0");
  });
});
