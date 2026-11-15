import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import Raiz from "@/app/(app)/page";
import Reportes from "@/app/(app)/reportes/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import { estadisticas } from "@/lib/estadisticas/__tests__/datos";
import { catalogoBackoffice, catalogoCompania, paginaVehiculos } from "@/lib/reportes/__tests__/datosReportes";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: la raíz (/) monta el Dashboard con la vista global si la sesión trae verEstadisticasGlobales; /reportes
// monta el módulo de reportes con verReportes (compañía) o verEstadisticasGlobales (vista global, Q4). Sin capacidad, 403.

const h = vi.hoisted(() => ({ replace: vi.fn(), capacidades: {} as Partial<CapacidadesDiagnostico> }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
  useRouter: () => ({ replace: h.replace, push: vi.fn(), refresh: vi.fn() }),
}));
vi.mock("@/components/DiagnosticoShell", () => ({
  useSesion: () => ({
    sesion: {
      usuario: { id: "u", email: "ana@empresa-prueba.co", esSuperAdmin: false },
      empresa: { id: "e", nombre: "Compañía Demo" },
      roles: [],
      permisos: [],
      capacidades: { verDiagnosticos: false, crearDiagnosticos: false, descargarReportes: false, backoffice: false, ...h.capacidades },
    },
    cargando: false,
    error: null,
    recargar: () => {},
  }),
}));

beforeEach(() => {
  h.replace.mockReset();
  h.capacidades = {};
});
afterEach(() => vi.unstubAllGlobals());

describe("FE-4 · raíz: el Dashboard", () => {
  it("con verDiagnosticos abre el Dashboard de la compañía (GET /estadisticas)", async () => {
    h.capacidades = { verDiagnosticos: true };
    const f = simularApi({ "GET /estadisticas": () => Response.json(estadisticas()) });
    render(<Raiz />);
    expect(screen.getByRole("heading", { level: 1, name: "Dashboard" })).toBeInTheDocument();
    expect(await screen.findByText("1.702 únicos")).toBeInTheDocument();
    expect(llamadas(f, "GET", "/estadisticas")).toHaveLength(1);
  });

  it("con verEstadisticasGlobales abre la vista global", async () => {
    h.capacidades = { verDiagnosticos: true, backoffice: true, verEstadisticasGlobales: true };
    const f = simularApi({
      "GET /backoffice/estadisticas/global": () => Response.json(estadisticas()),
      "GET /backoffice/empresas": () => Response.json([]),
    });
    render(<Raiz />);
    await waitFor(() => expect(llamadas(f, "GET", "/backoffice/estadisticas/global")).toHaveLength(1));
  });

  it("sin verDiagnosticos ni vista global va a la 403", async () => {
    simularApi({});
    render(<Raiz />);
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISOS_DIAGNOSTICO"));
    expect(screen.queryByRole("heading", { name: "Dashboard" })).not.toBeInTheDocument();
  });
});

describe("FE-4 · /reportes", () => {
  it("con verReportes monta el módulo de la compañía (GET /reportes) y exportar sigue a descargarReportes", async () => {
    h.capacidades = { verReportes: true, descargarReportes: false };
    const f = simularApi({ "GET /reportes": () => Response.json(catalogoCompania()), "GET /reportes/vehiculos": () => Response.json(paginaVehiculos()) });
    render(<Reportes />);
    expect(await screen.findByRole("table", { name: "Vehículos diagnosticados" })).toBeInTheDocument();
    expect(llamadas(f, "GET", "/reportes")).toHaveLength(1);
    expect(screen.queryByRole("button", { name: /Exportar/ })).not.toBeInTheDocument();
  });

  it("con verEstadisticasGlobales usa el catálogo del backoffice", async () => {
    h.capacidades = { backoffice: true, verEstadisticasGlobales: true, descargarReportes: true };
    const f = simularApi({
      "GET /backoffice/reportes": () => Response.json(catalogoBackoffice()),
      "GET /backoffice/reportes/vehiculos/global": () => Response.json(paginaVehiculos()),
      "GET /backoffice/empresas": () => Response.json([]),
    });
    render(<Reportes />);
    expect(await screen.findByRole("tab", { name: "Consumo por compañía" })).toBeInTheDocument();
    expect(llamadas(f, "GET", "/backoffice/reportes")).toHaveLength(1);
  });

  it("sin verReportes ni vista global va a la 403", async () => {
    h.capacidades = { verDiagnosticos: true };
    simularApi({});
    render(<Reportes />);
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO"));
  });
});
