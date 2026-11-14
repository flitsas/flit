import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import Inicio from "@/app/(app)/page";
import { sesion } from "@/__tests__/configuracion/datos";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo (HU #14577 AC20, spec-ui §4.1): el inicio pone debajo de las tarjetas Express y Masivo las pestañas
// «Diagnósticos» (el ListadoDiagnosticos de siempre) y «Vehículos» (ListadoVehiculos). La activa va en la URL:
// /?vista=vehiculos abre «Vehículos»; sin parámetro, «Diagnósticos». Solo se monta el panel activo (no se piden dos
// listados) y las pestañas solo aparecen con verDiagnosticos.

const h = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn(), params: new URLSearchParams() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useSearchParams: () => h.params,
  useRouter: () => ({ replace: h.replace, push: h.push, refresh: vi.fn() }),
}));

beforeEach(() => {
  h.replace.mockReset();
  h.push.mockReset();
  h.params = new URLSearchParams();
});
afterEach(() => vi.unstubAllGlobals());

function montar(capacidades: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, crearDiagnosticos: true }) {
  const api = simularApi({
    "GET /sesion": () => Response.json(sesion(capacidades)),
    "GET /diagnosticos": () => Response.json({ pagina: 1, tamano: 10, total: 0, diagnosticos: [] }),
    "GET /vehiculos": () => Response.json({ pagina: 1, tamano: 25, total: 0, vehiculos: [] }),
  });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      <Inicio />
    </DiagnosticoShell>,
  );
  return api;
}

const pestanas = () => screen.findByRole("tablist", { name: "Vista del inicio" });

describe("Inicio — AC20: pestañas «Diagnósticos» y «Vehículos»", () => {
  it("debajo de Express y Masivo, las dos pestañas del kit; «Diagnósticos» activa por defecto con el listado de siempre", async () => {
    const api = montar();
    const lista = await pestanas();
    const tabs = within(lista).getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual(["Diagnósticos", "Vehículos"]);
    expect(within(lista).getByRole("tab", { name: "Diagnósticos" })).toHaveAttribute("aria-selected", "true");
    expect(within(lista).getByRole("tab", { name: "Vehículos" })).toHaveAttribute("aria-selected", "false");
    const masivo = screen.getByRole("region", { name: "Diagnóstico masivo" });
    expect(masivo.compareDocumentPosition(lista) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(await screen.findByRole("heading", { name: "Aún no tienes diagnósticos" })).toBeInTheDocument();
    expect(screen.getByRole("tabpanel", { name: "Diagnósticos" })).toContainElement(screen.getByRole("heading", { name: "Aún no tienes diagnósticos" }));
    expect(llamadas(api, "GET", "/vehiculos")).toHaveLength(0);
  });

  it("elegir «Vehículos» pone ?vista=vehiculos en la URL y monta solo la grilla de placas", async () => {
    const api = montar();
    const lista = await pestanas();
    await screen.findByRole("heading", { name: "Aún no tienes diagnósticos" });
    const pedidosDiagnosticos = llamadas(api, "GET", "/diagnosticos").length;
    fireEvent.click(within(lista).getByRole("tab", { name: "Vehículos" }));
    expect(h.replace).toHaveBeenLastCalledWith("/?vista=vehiculos", { scroll: false });
    expect(within(lista).getByRole("tab", { name: "Vehículos" })).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByRole("heading", { name: "Aún no hay vehículos" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Aún no tienes diagnósticos" })).toBeNull();
    expect(llamadas(api, "GET", "/vehiculos")).toHaveLength(1);
    expect(llamadas(api, "GET", "/diagnosticos")).toHaveLength(pedidosDiagnosticos);
  });

  it("al recargar o compartir /?vista=vehiculos abre «Vehículos» sin pedir el listado de diagnósticos", async () => {
    h.params = new URLSearchParams("vista=vehiculos&placa=TST");
    const api = montar();
    const lista = await pestanas();
    expect(within(lista).getByRole("tab", { name: "Vehículos" })).toHaveAttribute("aria-selected", "true");
    await waitFor(() => expect(llamadas(api, "GET", "/vehiculos")).toHaveLength(1));
    expect(llamadas(api, "GET", "/diagnosticos")).toHaveLength(0);
    expect(screen.getByRole("tabpanel", { name: "Vehículos" })).toBeInTheDocument();
  });

  it("volver a «Diagnósticos» deja la URL sin parámetros de la otra pestaña", async () => {
    h.params = new URLSearchParams("vista=vehiculos&placa=TST");
    montar();
    const lista = await pestanas();
    fireEvent.click(within(lista).getByRole("tab", { name: "Diagnósticos" }));
    expect(h.replace).toHaveBeenLastCalledWith("/", { scroll: false });
    expect(await screen.findByRole("heading", { name: "Aún no tienes diagnósticos" })).toBeInTheDocument();
  });

  it("las flechas del teclado cambian de pestaña (patrón del kit) y actualizan la URL", async () => {
    montar();
    const lista = await pestanas();
    const diagnosticos = within(lista).getByRole("tab", { name: "Diagnósticos" });
    diagnosticos.focus();
    fireEvent.keyDown(diagnosticos, { key: "ArrowRight" });
    expect(h.replace).toHaveBeenLastCalledWith("/?vista=vehiculos", { scroll: false });
    expect(within(lista).getByRole("tab", { name: "Vehículos" })).toHaveFocus();
  });

  it("negativo: sin verDiagnosticos no hay pestañas ni se pide ninguno de los dos listados", async () => {
    const api = montar({ crearDiagnosticos: true });
    await screen.findByRole("heading", { level: 1, name: "Diagnóstico vehicular" });
    await screen.findByRole("region", { name: "Diagnóstico Express" });
    expect(screen.queryByRole("tablist")).toBeNull();
    expect(screen.queryByRole("tab", { name: "Vehículos" })).toBeNull();
    expect(llamadas(api, "GET", "/vehiculos")).toHaveLength(0);
    expect(llamadas(api, "GET", "/diagnosticos")).toHaveLength(0);
  });
});
