import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Dashboard } from "@/components/estadisticas/Dashboard";
import type { EstadoSesion } from "@/components/ImpuestosShell";
import type { SesionImpuestos } from "@/lib/sesion";

// Uso de ejemplo: <Dashboard /> en / (app/(app)/page.tsx). Pide GET /api/v1/impuestos/dashboard?desde&hasta (últimos 30
// días por defecto, D-DASH) con la compañía del token; el Super Admin sin ?empresa= ve la vista global con el ranking y,
// con ?empresa=<id>, el dashboard de esa compañía (la API recibe `empresa`). Contrato: impuestos.v1.yaml, schema Dashboard.

const replace = vi.fn();
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => busqueda,
}));

let estadoSesion: EstadoSesion;
vi.mock("@/components/ImpuestosShell", () => ({ useSesion: () => estadoSesion }));

const EMPRESA = "00000000-0000-7000-8000-0000000000e1";
const OTRA = "00000000-0000-7000-8000-0000000000e2";
const LOTE = "00000000-0000-7000-8000-00000000a001";

type Rol = "admin" | "gestor" | "sinPermiso" | "superadmin";
function sesion(rol: Rol, estado = "LISTO"): SesionImpuestos {
  const permisos =
    rol === "admin"
      ? ["impuestos.dashboard.ver", "impuestos.liquidaciones.ver", "impuestos.liquidaciones.crear", "impuestos.reportes.ver"]
      : rol === "gestor"
        ? ["impuestos.dashboard.ver", "impuestos.liquidaciones.ver"]
        : rol === "sinPermiso"
          ? ["impuestos.liquidaciones.ver"]
          : [];
  return {
    usuario: { id: "u-1", email: "nombre@empresa.com", nombre: "Persona Demo" },
    empresa: { id: EMPRESA, nombre: "Compañía Demo" },
    roles: rol === "admin" ? ["admin_impuestos"] : rol === "superadmin" ? [] : ["gestor_impuestos"],
    permisos,
    esSuperAdmin: rol === "superadmin",
    aprovisionamiento: { estado, firmaConfirmada: true },
  };
}
const conSesion = (rol: Rol, estado?: string) => {
  estadoSesion = { sesion: sesion(rol, estado), cargando: false, error: null, recargar: vi.fn() };
};

const contadoresLote = { exitosas: 3, conDeuda: 1, pazYSalvo: 2, error: 0, datosIncompletos: 0, departamentoNoHabilitado: 0, incierto: 0, sinResultado: 0, pendientes: 2 };

function dashboard(cambios: Record<string, unknown> = {}) {
  return {
    periodo: { desde: "2026-10-26", hasta: "2026-11-24" },
    contadores: { total: 12, individuales: 7, masivas: 5, lotesEnProceso: 1 },
    resultados: { exitosas: 8, conError: 2, pendientesWebhook: 1, pendientesConsulta: 1, datosIncompletos: 0, departamentoNoHabilitado: 0, inciertas: 1, sinResultado: 0 },
    deuda: { placasConDeuda: 4, placasAlDia: 4 },
    montoTotal: 1234567,
    liquidacionesSinMonto: 0,
    parcial: false,
    serie: [
      { fecha: "2026-11-23", total: 5 },
      { fecha: "2026-11-24", total: 7 },
    ],
    porDepartamento: [
      { codigo: "11", nombre: "Bogotá D.C.", total: 9 },
      { codigo: "25", nombre: "Cundinamarca", total: 3 },
    ],
    ultimosLotes: [
      {
        id: LOTE,
        tipo: "MASIVA",
        estado: "EN_PROCESO",
        totalItems: 5,
        contadores: contadoresLote,
        creadaEn: "2026-11-24T14:05:00Z",
        creadaPor: { id: "u-1", nombre: "Persona Demo" },
        empresa: { id: EMPRESA, nombre: "Compañía Demo" },
        nombreArchivo: "placas.csv",
        placa: null,
      },
    ],
    ranking: null,
    ...cambios,
  };
}

let llamadas: URL[];
function api(responder: (url: URL) => Response | Promise<Response> = () => Response.json(dashboard())) {
  return vi.fn(async (u: RequestInfo | URL) => {
    const url = new URL(String(u), "http://app");
    llamadas.push(url);
    return responder(url);
  });
}
const pedidos = () => llamadas.filter((l) => l.pathname === "/api/v1/impuestos/dashboard");
const ultimo = () => pedidos().at(-1) as URL;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-11-24T15:00:00Z"));
  replace.mockReset();
  busqueda = new URLSearchParams();
  llamadas = [];
  conSesion("admin");
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Dashboard — compañía del token (F4a-CF1)", () => {
  it("pide el dashboard de los últimos 30 días sin `empresa` y pinta los 6 KPIs con sus pies", async () => {
    vi.stubGlobal("fetch", api());
    render(<Dashboard />);
    await screen.findByText("Liquidaciones por día");
    expect(ultimo().searchParams.get("desde")).toBe("2026-10-26");
    expect(ultimo().searchParams.get("hasta")).toBe("2026-11-24");
    expect(ultimo().searchParams.has("empresa")).toBe(false);

    const kpi = (clave: string) => screen.getByTestId(`kpi-${clave}`);
    expect(within(kpi("liquidaciones")).getByText("Liquidaciones")).toBeInTheDocument();
    expect(within(kpi("liquidaciones")).getByText("12")).toBeInTheDocument();
    expect(within(kpi("liquidaciones")).getByText("7 por placa · 5 masivas")).toBeInTheDocument();
    expect(within(kpi("lotes")).getByText("1")).toBeInTheDocument();
    expect(within(kpi("exitosas")).getByText("8")).toBeInTheDocument();
    expect(within(kpi("errores")).getByText("2")).toBeInTheDocument();
    expect(within(kpi("pendientes")).getByText("2")).toBeInTheDocument();
    expect(within(kpi("pendientes")).getByText("1 inciertas")).toBeInTheDocument();
    expect(within(kpi("deuda")).getByText("4")).toBeInTheDocument();
    expect(within(kpi("deuda")).getByText("4 al día")).toBeInTheDocument();
    expect(screen.getByText("Resumen de Compañía Demo")).toBeInTheDocument();
    expect(screen.queryByText("Vista global")).not.toBeInTheDocument();
  });

  it("los KPIs enlazan a liquidaciones y a reportes con el periodo", async () => {
    vi.stubGlobal("fetch", api());
    render(<Dashboard />);
    await screen.findByText("Liquidaciones por día");
    expect(screen.getByTestId("kpi-liquidaciones").closest("a") ?? screen.getByTestId("kpi-liquidaciones")).toHaveAttribute(
      "href",
      "/liquidaciones?desde=2026-10-26&hasta=2026-11-24",
    );
    const errores = screen.getByTestId("kpi-errores");
    expect(errores.closest("a") ?? errores).toHaveAttribute("href", "/reportes?reporte=errores&desde=2026-10-26&hasta=2026-11-24");
  });

  it("gráfica por día con figcaption y «Ver datos», distribución por departamento y monto total", async () => {
    vi.stubGlobal("fetch", api());
    render(<Dashboard />);
    const figura = await screen.findByRole("figure", { name: "Liquidaciones por día" });
    expect(within(figura).getByText("Ver datos")).toBeInTheDocument();
    expect(within(figura).getByRole("table", { name: /Datos de Liquidaciones por día/ })).toBeInTheDocument();
    const dep = screen.getByRole("figure", { name: "Por departamento" });
    expect(within(dep).getAllByText("Bogotá D.C.").length).toBeGreaterThan(0);
    expect(within(screen.getByRole("region", { name: "Monto total liquidado" })).getByText(/1\.234\.567/)).toBeInTheDocument();
  });

  it("últimos lotes: tipo y fecha, chip de estado, avance «{t} de {n} placas» y «Ver lote»", async () => {
    vi.stubGlobal("fetch", api());
    render(<Dashboard />);
    const lista = await screen.findByRole("region", { name: "Últimos 5 lotes" });
    expect(within(lista).getByText((_, el) => el?.tagName === "SPAN" && el.textContent === "Masiva · 24/11/2026")).toBeInTheDocument();
    expect(within(lista).getByText("En proceso")).toBeInTheDocument();
    expect(within(lista).getByRole("progressbar")).toHaveAttribute("aria-valuetext", "3 de 5 placas");
    expect(within(lista).getByRole("link", { name: /Ver lote/ })).toHaveAttribute("href", `/liquidaciones/${LOTE}`);
  });

  it("monto con liquidaciones sin monto: marca «Parcial» (F4a-CF3)", async () => {
    vi.stubGlobal("fetch", api(() => Response.json(dashboard({ liquidacionesSinMonto: 2 }))));
    render(<Dashboard />);
    const monto = await screen.findByRole("region", { name: "Monto total liquidado" });
    expect(within(monto).getByText("Parcial")).toBeInTheDocument();
    expect(within(monto).getByText("2 liquidaciones sin monto")).toBeInTheDocument();
  });
});

describe("Dashboard — cuatro estados", () => {
  it("cargando: región ocupada con «Cargando el resumen…» y KPIs con «—»", async () => {
    vi.stubGlobal("fetch", api(() => new Promise<Response>(() => {})));
    render(<Dashboard />);
    expect(await screen.findByText("Cargando el resumen…")).toBeInTheDocument();
    expect(within(screen.getByTestId("kpi-liquidaciones")).getByText("—")).toBeInTheDocument();
  });

  it("vacío: «Aún no hay liquidaciones en este periodo», «Liquidar por placa» y «Ver los últimos 90 días» vuelve a pedir", async () => {
    vi.stubGlobal("fetch", api(() => Response.json(dashboard({ contadores: { total: 0, individuales: 0, masivas: 0, lotesEnProceso: 0 }, ultimosLotes: [], serie: [], porDepartamento: [] }))));
    render(<Dashboard />);
    expect(await screen.findByText("Aún no hay liquidaciones en este periodo")).toBeInTheDocument();
    expect(screen.getByText("Cambia el rango de fechas o liquida una placa.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Liquidar por placa" })).toHaveAttribute("href", "/liquidaciones/nueva");
    fireEvent.click(screen.getByRole("button", { name: "Ver los últimos 90 días" }));
    await waitFor(() => expect(ultimo().searchParams.get("desde")).toBe("2026-08-27"));
    expect(ultimo().searchParams.get("hasta")).toBe("2026-11-24");
  });

  it("vacío sin permiso de crear o con el producto suspendido: sin «Liquidar por placa»", async () => {
    conSesion("gestor");
    vi.stubGlobal("fetch", api(() => Response.json(dashboard({ contadores: { total: 0, individuales: 0, masivas: 0, lotesEnProceso: 0 } }))));
    const { unmount } = render(<Dashboard />);
    await screen.findByText("Aún no hay liquidaciones en este periodo");
    expect(screen.queryByRole("link", { name: "Liquidar por placa" })).not.toBeInTheDocument();
    unmount();

    conSesion("admin", "SUSPENDIDO");
    render(<Dashboard />);
    await screen.findByText("Aún no hay liquidaciones en este periodo");
    expect(screen.queryByRole("link", { name: "Liquidar por placa" })).not.toBeInTheDocument();
  });

  it.each(["EMPRESA_SOLO_SUPER_ADMIN", "EMPRESA_NO_PERMITIDA"])(
    "un 403 %s (contrato 1.1.0) se pinta en la página con su literal y no lleva a la 403",
    async (code) => {
      vi.stubGlobal("fetch", api(() => Response.json({ code, error: "detalle interno" }, { status: 403 })));
      render(<Dashboard />);
      expect(await screen.findByText("Solo el Super Admin puede elegir la compañía.")).toBeInTheDocument();
      expect(replace).not.toHaveBeenCalled();
      expect(document.body).not.toHaveTextContent("detalle interno");
    },
  );

  it("un 400 EMPRESA_REQUERIDA muestra «Elige una compañía.»", async () => {
    conSesion("superadmin");
    busqueda = new URLSearchParams("empresa=no-es-uuid");
    vi.stubGlobal("fetch", api(() => Response.json({ code: "EMPRESA_REQUERIDA", error: "x" }, { status: 400 })));
    render(<Dashboard />);
    expect(await screen.findByText("Elige una compañía.")).toBeInTheDocument();
  });

  it("otro 403 con código (PRODUCTO_NO_HABILITADO) sigue llevando a la 403 con su código", async () => {
    vi.stubGlobal("fetch", api(() => Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "x" }, { status: 403 })));
    render(<Dashboard />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=PRODUCTO_NO_HABILITADO"));
  });

  it("error: «No se pudo cargar el resumen» y «Reintentar» vuelve a pedir", async () => {
    let n = 0;
    vi.stubGlobal("fetch", api(() => (++n === 1 ? new Response("{}", { status: 500 }) : Response.json(dashboard()))));
    render(<Dashboard />);
    expect(await screen.findByText("No se pudo cargar el resumen")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await screen.findByText("Liquidaciones por día");
    expect(pedidos()).toHaveLength(2);
  });

  it("un bloque que llega null dice «Sin datos para este bloque», nunca ceros", async () => {
    vi.stubGlobal("fetch", api(() => Response.json(dashboard({ porDepartamento: null }))));
    render(<Dashboard />);
    await screen.findByText("Liquidaciones por día");
    expect(screen.getByText("Sin datos para este bloque")).toBeInTheDocument();
  });
});

describe("Dashboard — rango de fechas", () => {
  it("más de 366 días: error junto a «Hasta» con aria-invalid y sin petición", async () => {
    vi.stubGlobal("fetch", api());
    render(<Dashboard />);
    await screen.findByText("Liquidaciones por día");
    const antes = pedidos().length;
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2025-01-01" } });
    expect(await screen.findByText("El rango no puede superar un año")).toBeInTheDocument();
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("aria-invalid", "true");
    expect(pedidos()).toHaveLength(antes);
  });

  it("un rango válido se aplica al cambiar: pide con el nuevo «Desde»", async () => {
    vi.stubGlobal("fetch", api());
    render(<Dashboard />);
    await screen.findByText("Liquidaciones por día");
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2026-11-01" } });
    await waitFor(() => expect(ultimo().searchParams.get("desde")).toBe("2026-11-01"));
  });

  it("un 400 RANGO_INVALIDO del servidor se pinta junto a «Hasta»", async () => {
    vi.stubGlobal("fetch", api(() => Response.json({ code: "RANGO_INVALIDO", error: "El rango de fechas no es válido." }, { status: 400 })));
    render(<Dashboard />);
    expect(await screen.findByText(/Revisa el periodo/)).toBeInTheDocument();
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("aria-invalid", "true");
  });
});

describe("Dashboard — Super Admin (D-DASH: global con selector de compañía)", () => {
  it("sin ?empresa=: vista global, chip «Vista global» y ranking que elige la compañía", async () => {
    conSesion("superadmin");
    vi.stubGlobal(
      "fetch",
      api(() => Response.json(dashboard({ ranking: [{ empresa: { id: OTRA, nombre: "Compañía Uno" }, total: 40 }] }))),
    );
    render(<Dashboard />);
    await screen.findByText("Compañías con más liquidaciones");
    expect(ultimo().searchParams.has("empresa")).toBe(false);
    expect(screen.getByText("Vista global")).toBeInTheDocument();
    expect(screen.getByText("Resumen de todas las compañías")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Ver Compañía Uno" }));
    expect(replace).toHaveBeenCalledWith(`/?empresa=${OTRA}`);
  });

  it("con ?empresa=: la API recibe `empresa`, sin ranking ni chip global y el enlace del lote conserva la compañía", async () => {
    conSesion("superadmin");
    busqueda = new URLSearchParams(`empresa=${OTRA}`);
    vi.stubGlobal("fetch", api((url) => (url.pathname.endsWith("/dashboard") ? Response.json(dashboard()) : Response.json({ items: [], total: 0, page: 1, pageSize: 50 }))));
    render(<Dashboard />);
    await screen.findByText("Liquidaciones por día");
    expect(ultimo().searchParams.get("empresa")).toBe(OTRA);
    expect(screen.queryByText("Vista global")).not.toBeInTheDocument();
    expect(screen.queryByText("Compañías con más liquidaciones")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Ver lote/ })).toHaveAttribute("href", `/liquidaciones/${LOTE}?empresa=${OTRA}`);
  });
});

describe("Dashboard — permisos", () => {
  it("sin impuestos.dashboard.ver lleva a la 403 y no pide nada", async () => {
    conSesion("sinPermiso");
    vi.stubGlobal("fetch", api());
    render(<Dashboard />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO"));
    expect(pedidos()).toHaveLength(0);
  });
});
