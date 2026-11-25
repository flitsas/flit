import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Reportes } from "@/components/reportes/Reportes";
import type { EstadoSesion } from "@/components/ImpuestosShell";
import type { SesionImpuestos } from "@/lib/sesion";

// Uso de ejemplo: <Reportes /> en /reportes?reporte=<tipo>. Cuatro reportes (D-REPORTES: detalle, lotes, deuda, errores)
// con GET /api/v1/impuestos/reportes/{tipo}?desde&hasta&…&page&pageSize; `columnas` trae el orden y la etiqueta de cada
// columna. Exportar Excel (≤ 5000) y PDF (≤ 1000) con los mismos filtros; más filas ⇒ 422 TOPE_EXPORTACION con `tope`
// (impuestos.v1.yaml, tag Reportes). Sin límite de exportaciones por usuario (DA-9b).

const replace = vi.fn();
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => "/reportes",
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => busqueda,
}));

let estadoSesion: EstadoSesion;
vi.mock("@/components/ImpuestosShell", () => ({ useSesion: () => estadoSesion }));

const EMPRESA = "00000000-0000-7000-8000-0000000000e1";
const OTRA = "00000000-0000-7000-8000-0000000000e2";

type Rol = "admin" | "gestor" | "sinPermiso" | "superadmin";
function sesion(rol: Rol): SesionImpuestos {
  const permisos =
    rol === "admin"
      ? ["impuestos.reportes.ver", "impuestos.reportes.exportar", "impuestos.usuarios.gestionar", "impuestos.configuracion.ver"]
      : rol === "gestor"
        ? ["impuestos.reportes.ver"]
        : rol === "sinPermiso"
          ? ["impuestos.liquidaciones.ver"]
          : [];
  return {
    usuario: { id: "u-1", email: "nombre@empresa.com", nombre: "Persona Demo" },
    empresa: { id: EMPRESA, nombre: "Compañía Demo" },
    roles: rol === "admin" ? ["admin_impuestos"] : [],
    permisos,
    esSuperAdmin: rol === "superadmin",
    aprovisionamiento: { estado: "LISTO", firmaConfirmada: true },
  };
}
const conSesion = (rol: Rol) => {
  estadoSesion = { sesion: sesion(rol), cargando: false, error: null, recargar: vi.fn() };
};

const COLUMNAS_DETALLE = [
  { clave: "placa", etiqueta: "Placa" },
  { clave: "tipo", etiqueta: "Tipo" },
  { clave: "estado", etiqueta: "Estado" },
  { clave: "propietarioNombre", etiqueta: "Propietario" },
  { clave: "documentoNumero", etiqueta: "Número de documento" },
  { clave: "totalAPagar", etiqueta: "Total a pagar" },
  { clave: "creadaEn", etiqueta: "Fecha" },
];
const FILA = (placa: string, totalAPagar: number | null, estado = "EXITOSO") => ({
  liquidacionId: "00000000-0000-7000-8000-00000000a001",
  itemId: `i-${placa}`,
  tipo: "INDIVIDUAL",
  placa,
  estado,
  departamento: "11",
  propietarioNombre: "Propietario Demo",
  documentoTipo: "CC",
  documentoNumero: "0000000",
  totalAPagar,
  resultadoStatus: "con_deuda",
  parcial: false,
  creadaEn: "2026-11-24T19:05:00Z",
  usuario: "Persona Demo",
});
const pagina = (tipo: string, items: unknown[], total = items.length, columnas: unknown[] = COLUMNAS_DETALLE) => ({ tipo, columnas, items, total, page: 1, pageSize: 50 });

type Llamada = { url: URL };
let llamadas: Llamada[];
function api(
  respuestas: {
    reporte?: (url: URL) => Response | Promise<Response>;
    exportar?: (url: URL) => Response;
  } = {},
) {
  return vi.fn(async (u: RequestInfo | URL) => {
    const url = new URL(String(u), "http://app");
    llamadas.push({ url });
    if (/\/reportes\/[a-z]+\/(excel|pdf)$/.test(url.pathname)) {
      return respuestas.exportar ? respuestas.exportar(url) : new Response(new Blob(["x"]), { status: 200, headers: { "Content-Disposition": 'attachment; filename="reporte.xlsx"' } });
    }
    if (url.pathname.startsWith("/api/v1/impuestos/reportes/")) {
      return respuestas.reporte ? respuestas.reporte(url) : Response.json(pagina(url.pathname.split("/").at(-1) as string, [FILA("ABC123", 1234567), FILA("XYZ789", null)]));
    }
    if (url.pathname === "/api/v1/impuestos/fuentes") return Response.json({ items: [{ codigo: "11", nombre: "Bogotá D.C." }, { codigo: "25", nombre: "Cundinamarca" }] });
    if (url.pathname === "/api/v1/impuestos/usuarios") return Response.json({ items: [{ id: "u-1", nombre: "Persona Demo", email: "nombre@empresa.com", rol: "admin_impuestos", estado: "ACTIVO" }], total: 1, page: 1, pageSize: 200 });
    return Response.json({});
  });
}
const pedidosReporte = () => llamadas.map((l) => l.url).filter((u) => /^\/api\/v1\/impuestos\/reportes\/[a-z]+$/.test(u.pathname));
const ultimoReporte = () => pedidosReporte().at(-1) as URL;
const exportaciones = () => llamadas.map((l) => l.url).filter((u) => /\/(excel|pdf)$/.test(u.pathname));

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-11-24T15:00:00Z"));
  replace.mockReset();
  busqueda = new URLSearchParams();
  llamadas = [];
  conSesion("admin");
  Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:x"), configurable: true });
  Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Reportes — detalle por defecto (F4b-CF1)", () => {
  it("pide el detalle de los últimos 30 días paginado y pinta las columnas que manda la API", async () => {
    vi.stubGlobal("fetch", api());
    render(<Reportes />);
    const tabla = await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    expect(ultimoReporte().pathname).toBe("/api/v1/impuestos/reportes/detalle");
    expect(ultimoReporte().searchParams.get("desde")).toBe("2026-10-26");
    expect(ultimoReporte().searchParams.get("hasta")).toBe("2026-11-24");
    expect(ultimoReporte().searchParams.get("page")).toBe("1");
    expect(ultimoReporte().searchParams.get("pageSize")).toBe("50");
    expect(ultimoReporte().searchParams.has("empresa")).toBe(false);
    for (const c of COLUMNAS_DETALLE) expect(within(tabla).getAllByText(c.etiqueta).length).toBeGreaterThan(0);
    expect(within(tabla).getAllByText("ABC123").length).toBeGreaterThan(0);
    expect(within(tabla).getAllByText("Exitosa").length).toBeGreaterThan(0);
    expect(within(tabla).getAllByText("Individual").length).toBeGreaterThan(0);
    expect(within(tabla).getAllByText(/1\.234\.567/).length).toBeGreaterThan(0);
    expect(within(tabla).getAllByText("Sin dato").length).toBeGreaterThan(0);
    expect(screen.getByText("2 registros")).toBeInTheDocument();
  });

  it("aviso de datos personales (Ley 1581) en detalle y el de registro en lotes", async () => {
    vi.stubGlobal("fetch", api());
    const { unmount } = render(<Reportes />);
    expect(await screen.findByText(/protegidos por la Ley 1581 de 2012/)).toBeInTheDocument();
    unmount();
    busqueda = new URLSearchParams("reporte=lotes");
    render(<Reportes />);
    await waitFor(() => expect(ultimoReporte().pathname).toBe("/api/v1/impuestos/reportes/lotes"));
    expect(screen.getByText("Cada descarga queda registrada con tu usuario.")).toBeInTheDocument();
    expect(screen.queryByText(/Ley 1581/)).not.toBeInTheDocument();
  });

  it("las cuatro pestañas del contrato; elegir una cambia ?reporte= y pide ese reporte", async () => {
    vi.stubGlobal("fetch", api());
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    const pestanas = screen.getAllByRole("tab").map((t) => t.textContent);
    expect(pestanas).toEqual(["Detalle de liquidaciones", "Resumen por lote", "Placas con deuda", "Errores de liquidación"]);
    fireEvent.click(screen.getByRole("tab", { name: "Placas con deuda" }));
    expect(replace).toHaveBeenCalledWith("/reportes?reporte=deuda", { scroll: false });
    await waitFor(() => expect(ultimoReporte().pathname).toBe("/api/v1/impuestos/reportes/deuda"));
  });

  it("lee ?reporte=errores&desde&hasta de la URL (enlaces del dashboard)", async () => {
    busqueda = new URLSearchParams("reporte=errores&desde=2026-11-01&hasta=2026-11-10");
    vi.stubGlobal("fetch", api());
    render(<Reportes />);
    await waitFor(() => expect(ultimoReporte().pathname).toBe("/api/v1/impuestos/reportes/errores"));
    expect(ultimoReporte().searchParams.get("desde")).toBe("2026-11-01");
    expect(ultimoReporte().searchParams.get("hasta")).toBe("2026-11-10");
  });

  it("filtros: tipo, estado, departamento, usuario y placa en mayúscula viajan a la API", async () => {
    vi.stubGlobal("fetch", api());
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    fireEvent.click(screen.getByRole("radio", { name: "Masivas" }));
    await waitFor(() => expect(ultimoReporte().searchParams.get("tipoLiquidacion")).toBe("MASIVA"));
    fireEvent.change(screen.getByLabelText("Estado"), { target: { value: "ERROR" } });
    await waitFor(() => expect(ultimoReporte().searchParams.get("estado")).toBe("ERROR"));
    fireEvent.change(await screen.findByLabelText("Departamento"), { target: { value: "25" } });
    expect(await screen.findByLabelText("Usuario")).toBeInTheDocument();
    await waitFor(() => expect(ultimoReporte().searchParams.get("departamento")).toBe("25"));
    const placa = screen.getByLabelText("Placa");
    fireEvent.change(placa, { target: { value: "abc1" } });
    expect(placa).toHaveValue("ABC1");
    fireEvent.submit(placa);
    await waitFor(() => expect(ultimoReporte().searchParams.get("placa")).toBe("ABC1"));
    expect(ultimoReporte().searchParams.get("page")).toBe("1");
  });
});

describe("Reportes — exportar y topes (5000 Excel / 1000 PDF)", () => {
  it("exportar Excel pide /excel con los mismos filtros, sin paginación", async () => {
    vi.stubGlobal("fetch", api());
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    fireEvent.click(screen.getByRole("button", { name: /Exportar Excel/ }));
    await waitFor(() => expect(exportaciones()).toHaveLength(1));
    const url = exportaciones()[0];
    expect(url.pathname).toBe("/api/v1/impuestos/reportes/detalle/excel");
    expect(url.searchParams.get("desde")).toBe("2026-10-26");
    expect(url.searchParams.has("page")).toBe(false);
    expect(screen.getByText("Se exportan los 2 registros del filtro")).toBeInTheDocument();
  });

  it("con más de 5000 registros los dos formatos quedan deshabilitados con el motivo visible y no exportan", async () => {
    vi.stubGlobal("fetch", api({ reporte: (url) => Response.json(pagina(url.pathname.split("/").at(-1) as string, [FILA("ABC123", 1)], 6000)) }));
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    const excel = screen.getByRole("button", { name: /Exportar Excel/ });
    const pdf = screen.getByRole("button", { name: /Exportar PDF/ });
    expect(excel).toHaveAttribute("aria-disabled", "true");
    expect(pdf).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByText("Acota el filtro: máximo 5.000 registros en Excel")).toBeInTheDocument();
    expect(screen.getByText("Acota el filtro: máximo 1.000 registros en PDF")).toBeInTheDocument();
    fireEvent.click(excel);
    expect(exportaciones()).toHaveLength(0);
  });

  it("con 1200 registros: Excel habilitado y PDF deshabilitado por su tope", async () => {
    vi.stubGlobal("fetch", api({ reporte: (url) => Response.json(pagina(url.pathname.split("/").at(-1) as string, [FILA("ABC123", 1)], 1200)) }));
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    expect(screen.getByRole("button", { name: /Exportar Excel/ })).not.toHaveAttribute("aria-disabled");
    expect(screen.getByRole("button", { name: /Exportar PDF/ })).toHaveAttribute("aria-disabled", "true");
  });

  it("un 422 TOPE_EXPORTACION muestra el literal del contrato con {tope} bajo los botones y la tabla sigue", async () => {
    vi.stubGlobal(
      "fetch",
      api({
        exportar: () =>
          Response.json({ code: "TOPE_EXPORTACION", error: "El reporte supera el máximo de 5000 filas para exportar: ajusta los filtros.", tope: 5000 }, { status: 422 }),
      }),
    );
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    fireEvent.click(screen.getByRole("button", { name: /Exportar Excel/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("El reporte supera el máximo de 5000 filas para exportar: ajusta los filtros.");
    expect(screen.getByRole("table", { name: "Detalle de liquidaciones" })).toBeInTheDocument();
  });

  it("un fallo de la exportación (500) dice «No se pudo generar el archivo. Intenta de nuevo.»", async () => {
    vi.stubGlobal("fetch", api({ exportar: () => new Response("{}", { status: 500 }) }));
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    fireEvent.click(screen.getByRole("button", { name: /Exportar PDF/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("No se pudo generar el archivo. Intenta de nuevo.");
  });

  it("los botones apuntan al aviso de datos personales (aria-describedby)", async () => {
    vi.stubGlobal("fetch", api());
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    const aviso = screen.getByText(/protegidos por la Ley 1581/).closest("[id]") as HTMLElement;
    expect(screen.getByRole("button", { name: /Exportar Excel/ }).getAttribute("aria-describedby")).toContain(aviso.id);
  });

  it("sin impuestos.reportes.exportar los botones no están en el DOM", async () => {
    conSesion("gestor");
    vi.stubGlobal("fetch", api());
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    expect(screen.queryByRole("button", { name: /Exportar/ })).not.toBeInTheDocument();
  });
});

describe("Reportes — cuatro estados", () => {
  it("cargando: contador «— registros» y exportar deshabilitado con «Espera a que cargue el reporte»", async () => {
    vi.stubGlobal("fetch", api({ reporte: () => new Promise<Response>(() => {}) }));
    render(<Reportes />);
    expect(await screen.findByText("— registros")).toBeInTheDocument();
    expect(screen.getByText("Espera a que cargue el reporte")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Exportar Excel/ })).toHaveAttribute("aria-disabled", "true");
  });

  it("vacío sin filtros: «No hay registros en este periodo» y exportar con «No hay registros para exportar»", async () => {
    vi.stubGlobal("fetch", api({ reporte: (url) => Response.json(pagina(url.pathname.split("/").at(-1) as string, [], 0)) }));
    render(<Reportes />);
    expect(await screen.findByText("No hay registros en este periodo")).toBeInTheDocument();
    expect(screen.getByText("No hay registros para exportar")).toBeInTheDocument();
  });

  it("vacío por filtro: «Ningún registro coincide con los filtros» y «Limpiar filtros» los quita", async () => {
    vi.stubGlobal("fetch", api({ reporte: (url) => Response.json(pagina(url.pathname.split("/").at(-1) as string, url.searchParams.has("placa") ? [] : [FILA("ABC123", 1)])) }));
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    const placa = screen.getByLabelText("Placa");
    fireEvent.change(placa, { target: { value: "ZZZ999" } });
    fireEvent.submit(placa);
    expect(await screen.findByText("Ningún registro coincide con los filtros")).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole("button", { name: "Limpiar filtros" })[0]);
    await waitFor(() => expect(ultimoReporte().searchParams.has("placa")).toBe(false));
  });

  it("error: «No se pudo cargar el reporte» y «Reintentar» vuelve a pedir", async () => {
    let n = 0;
    vi.stubGlobal("fetch", api({ reporte: (url) => (++n === 1 ? new Response("{}", { status: 500 }) : Response.json(pagina(url.pathname.split("/").at(-1) as string, [FILA("ABC123", 1)]))) }));
    render(<Reportes />);
    expect(await screen.findByText("No se pudo cargar el reporte")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    expect(pedidosReporte()).toHaveLength(2);
  });
});

describe("Reportes — Super Admin y permisos", () => {
  it("con ?empresa= la API recibe `empresa` en el reporte y en la exportación", async () => {
    conSesion("superadmin");
    busqueda = new URLSearchParams(`empresa=${OTRA}`);
    vi.stubGlobal("fetch", api());
    render(<Reportes />);
    await screen.findByRole("table", { name: "Detalle de liquidaciones" });
    expect(ultimoReporte().searchParams.get("empresa")).toBe(OTRA);
    fireEvent.click(screen.getByRole("button", { name: /Exportar Excel/ }));
    await waitFor(() => expect(exportaciones()[0]?.searchParams.get("empresa")).toBe(OTRA));
  });

  it("sin impuestos.reportes.ver lleva a la 403 y no pide nada", async () => {
    conSesion("sinPermiso");
    vi.stubGlobal("fetch", api());
    render(<Reportes />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO"));
    expect(pedidosReporte()).toHaveLength(0);
  });
});
