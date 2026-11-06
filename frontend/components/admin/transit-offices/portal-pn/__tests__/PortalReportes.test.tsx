// HU #14399 (Feature #14396, Épica #14297) — Reporte del portal: columnas, filtros, estados, exportación y alcance.
// La API es un doble inyectado: no se llama al servidor.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";
import type {
  FilaReportePortal,
  PaginaReportePortal,
  ReportePortalApi,
} from "@/lib/api/admin-portal-reportes";
import { PortalReportes, REPORTE_PORTAL_VACIO } from "../PortalReportes";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useParams: () => ({ id: "ot-1" }),
}));

const fila = (over: Partial<FilaReportePortal> = {}): FilaReportePortal => ({
  canal: "pn_web",
  organismo: "Movilidad de Bogotá",
  tipo: "Traspaso",
  radicado: "RAD-001",
  estado: "En revisión",
  fechaCaida: "2026-11-05T15:30:00Z",
  modalidad: "Digital",
  valorTramite: 120000,
  valorGestion: 35000,
  ...over,
});

const pagina = (items: FilaReportePortal[], totalCount = items.length): PaginaReportePortal => ({
  items,
  totalCount,
  page: 1,
  pageSize: 10,
});

function crearApi(over: Partial<ReportePortalApi> = {}): ReportePortalApi {
  return {
    listar: vi.fn().mockResolvedValue(pagina([fila()])),
    exportar: vi.fn().mockResolvedValue(undefined),
    tipos: vi.fn().mockResolvedValue([{ id: "t1", nombre: "Traspaso" }]),
    organismos: vi.fn().mockResolvedValue([{ id: "o1", nombre: "Movilidad de Bogotá" }]),
    ...over,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
});

describe("columnas y datos", () => {
  it("pinta las nueve columnas con los nombres visibles del canal y la fecha en el formato estándar", async () => {
    render(<PortalReportes api={crearApi()} incluirFiltroOrganismo />);

    const tabla = await screen.findByRole("table", { name: "Trámites del portal Persona Natural" });
    const encabezados = within(tabla)
      .getAllByRole("columnheader")
      .map((h) => h.textContent ?? "");
    for (const titulo of [
      "Canal",
      "Organismo",
      "Tipo",
      "Radicado",
      "Estado",
      "Fecha en que cayó",
      "Modalidad",
      "Valor del trámite",
      "Valor de la gestión",
    ]) {
      expect(encabezados.some((e) => e.includes(titulo))).toBe(true);
    }
    const celdas = within(tabla)
      .getAllByRole("cell")
      .map((c) => c.textContent);
    expect(celdas).toContain("Persona Natural Web");
    expect(celdas).toContain("RAD-001");
    expect(celdas).toContain("05/11/2026 10:30");
    expect(celdas).toContain("$ 120.000");
    expect(celdas).toContain("$ 35.000");
  });

  it("deja vacío lo que viene nulo, sin inventar", async () => {
    const api = crearApi({
      listar: vi
        .fn()
        .mockResolvedValue(
          pagina([
            fila({
              canal: "pn_punto",
              organismo: null,
              radicado: null,
              modalidad: null,
              valorTramite: null,
              valorGestion: null,
            }),
          ]),
        ),
    });
    render(<PortalReportes api={api} />);

    const tabla = await screen.findByRole("table");
    const celdas = within(tabla)
      .getAllByRole("cell")
      .map((c) => c.textContent);
    expect(celdas).toContain("Persona Natural OT");
    expect(celdas.filter((c) => c === "")).toHaveLength(5);
    expect(celdas).not.toContain("$ 0");
  });
});

describe("estados", () => {
  it("cargando muestra «Armando el reporte…»", async () => {
    let resolver: (p: PaginaReportePortal) => void = () => undefined;
    render(
      <PortalReportes
        api={crearApi({
          listar: vi.fn().mockReturnValue(new Promise<PaginaReportePortal>((r) => (resolver = r))),
        })}
      />,
    );
    expect(await screen.findByText("Armando el reporte…")).toBeInTheDocument();
    resolver(pagina([fila()]));
    expect(await screen.findByText("RAD-001")).toBeInTheDocument();
    expect(screen.queryByText("Armando el reporte…")).not.toBeInTheDocument();
  });

  it("sin trámites muestra el texto vacío exacto", async () => {
    render(<PortalReportes api={crearApi({ listar: vi.fn().mockResolvedValue(pagina([])) })} />);
    expect(
      await screen.findByText("No hay trámites del portal en FLIT 2.0 con esos filtros."),
    ).toBeInTheDocument();
    expect(REPORTE_PORTAL_VACIO).toBe("No hay trámites del portal en FLIT 2.0 con esos filtros.");
  });

  it("si falla muestra el motivo exacto del servidor y deja reintentar", async () => {
    const listar = vi
      .fn()
      .mockRejectedValueOnce(new ApiError(403, "Tu usuario no pertenece a este organismo."))
      .mockResolvedValueOnce(pagina([fila()]));
    const user = userEvent.setup();
    render(<PortalReportes api={crearApi({ listar })} />);

    expect(
      await screen.findByText("Tu usuario no pertenece a este organismo."),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("RAD-001")).toBeInTheDocument();
  });
});

describe("filtros, paginación y exportación", () => {
  it("al llegar el catálogo de tipos no vuelve a pedir el reporte", async () => {
    const listar = vi.fn().mockResolvedValue(pagina([fila()]));
    render(<PortalReportes api={crearApi({ listar })} />);
    await screen.findByText("RAD-001");
    await new Promise((r) => setTimeout(r, 50));
    expect(listar).toHaveBeenCalledTimes(1);
  });

  it("pide la primera página con el tamaño estándar y sin filtros", async () => {
    const api = crearApi();
    render(<PortalReportes api={api} />);
    await screen.findByText("RAD-001");
    expect(api.listar).toHaveBeenCalledWith({ page: 1, pageSize: 10 }, expect.anything());
  });

  it("filtrar por canal vuelve a consultar con el código y exportar respeta ese filtro sin paginar", async () => {
    const api = crearApi();
    const user = userEvent.setup();
    render(<PortalReportes api={api} />);
    await screen.findByText("RAD-001");

    await user.click(screen.getByRole("button", { name: /filtrar por canal/i }));
    await user.click(screen.getByRole("checkbox", { name: "Persona Natural OT" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));

    await waitFor(() =>
      expect(api.listar).toHaveBeenLastCalledWith(
        { canal: "pn_punto", page: 1, pageSize: 10 },
        expect.anything(),
      ),
    );
    expect(screen.queryByRole("checkbox", { name: "Persona Natural Web" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Exportar a Excel" }));
    await waitFor(() => expect(api.exportar).toHaveBeenCalledWith({ canal: "pn_punto" }));
  });

  it("los canales ofrecidos son los del portal y la aplicación: nunca el gestor", async () => {
    const user = userEvent.setup();
    render(<PortalReportes api={crearApi()} />);
    await screen.findByText("RAD-001");
    await user.click(screen.getByRole("button", { name: /filtrar por canal/i }));
    const dialogo = screen.getByRole("dialog");
    const opciones = within(dialogo)
      .getAllByRole("checkbox")
      .map((c) => c.getAttribute("aria-label") ?? c.parentElement?.textContent);
    expect(opciones.join("|")).toContain("Persona Natural Web");
    expect(opciones.join("|")).toContain("Aplicación");
    expect(opciones.join("|")).not.toContain("Trámites");
  });

  it("si exportar falla muestra el motivo exacto", async () => {
    const api = crearApi({
      exportar: vi.fn().mockRejectedValue(new ApiError(500, "No se pudo generar el Excel.")),
    });
    const user = userEvent.setup();
    render(<PortalReportes api={api} />);
    await screen.findByText("RAD-001");
    await user.click(screen.getByRole("button", { name: "Exportar a Excel" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("No se pudo generar el Excel.");
  });
});

describe("filtro de organismo", () => {
  it("solo se ofrece cuando se pide (SuperAdmin en Plataforma)", async () => {
    const { unmount } = render(<PortalReportes api={crearApi()} incluirFiltroOrganismo />);
    await screen.findByText("RAD-001");
    expect(screen.getByRole("button", { name: /filtrar por organismo/i })).toBeInTheDocument();
    unmount();

    const apiOt = crearApi();
    render(<PortalReportes api={apiOt} />);
    await screen.findByText("RAD-001");
    expect(
      screen.queryByRole("button", { name: /filtrar por organismo/i }),
    ).not.toBeInTheDocument();
    expect(apiOt.organismos).not.toHaveBeenCalled();
  });
});
