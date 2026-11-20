import { readFileSync } from "node:fs";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { ListadoVehiculos, type ListadoVehiculosProps } from "@/components/vehiculos/ListadoVehiculos";
import type { FilaVehiculoListado, ListaVehiculos } from "@/lib/vehiculos/listado";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";
import { deudaSinPendientes, saneamientoEnGestion } from "@/components/saneamiento/__tests__/datosDeuda";
import { esperarCabecera12px, esperarTablaPorDefecto } from "@/components/comun/__tests__/tablaPorDefecto";

// Uso de ejemplo (HU #14577 AC21–AC23, spec-ui §4): pestaña «Vehículos» del inicio.
//   <ListadoVehiculos puedeCrear={crearDiagnosticos} puedeSanear={puedeSanear(capacidades)} puedeDescargar={descargarReportes} />
// Lee los filtros de la URL (?vista=vehiculos&placa=&estado=&…&pagina=&tamano=), pide GET /vehiculos y pinta la tabla con la
// bandera por fila, «Abrir diagnóstico», «Ver deuda» (PS-2) y la selección con «Sanear (N)» y «Descargar deuda (N)».
// Datos ficticios: placas TST### y lotes de prueba.

const h = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn(), params: new URLSearchParams("vista=vehiculos") }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/diagnosticos",
  useSearchParams: () => h.params,
  useRouter: () => ({ replace: h.replace, push: h.push, refresh: vi.fn() }),
}));

const LOTE = "00000000-0000-7000-8000-00000000e001";
const placaDe = (n: number) => `TST${String(n).padStart(3, "0")}`;

function filaVeh(n: number, cambios: Partial<FilaVehiculoListado> = {}): FilaVehiculoListado {
  return {
    vehiculo_id: `vehiculo-${n}`,
    placa: placaDe(n),
    diagnostico_id: LOTE,
    diagnostico_nombre: `Flota de prueba ${n}`,
    diagnostico_creado_en: "2026-11-10T14:00:00Z",
    modo: "CON_ANALISIS",
    estado: "TERMINADO",
    veredicto: "BLOQUEADO",
    saneamiento: { estado: null, elegible: true, motivo_no_elegible: null },
    tramite: null,
    ...cambios,
  };
}

const lista = (vehiculos: FilaVehiculoListado[], extra: Partial<ListaVehiculos> = {}): ListaVehiculos => ({
  pagina: 1,
  tamano: 10,
  total: vehiculos.length,
  vehiculos,
  ...extra,
});

/** TST001 terminado y saneable; TST002 en proceso (no elegible); TST003 EN_GESTION con trámite; TST004 SOLICITADO, VIABLE. */
const filasBase = () => [
  filaVeh(1),
  filaVeh(2, { estado: "EN_PROCESO", veredicto: null, saneamiento: { estado: null, elegible: false, motivo_no_elegible: "NO_TERMINADO" } }),
  filaVeh(3, { saneamiento: { estado: "EN_GESTION", elegible: false, motivo_no_elegible: "YA_SOLICITADO" }, tramite: { id: "t-3", numero: "123" } }),
  filaVeh(4, { veredicto: "VIABLE", modo: "SIMPLE", saneamiento: { estado: "SOLICITADO", elegible: false, motivo_no_elegible: "YA_SOLICITADO" } }),
];

const TODAS_CAPACIDADES: Partial<ListadoVehiculosProps> = { puedeSanear: true, puedeDescargar: true };
const pintar = (props: Partial<ListadoVehiculosProps> = TODAS_CAPACIDADES) => render(<ListadoVehiculos puedeCrear={false} puedeSanear={false} puedeDescargar={false} {...props} />);

const tabla = () => screen.findByRole("table", { name: "Vehículos de la compañía" });
const fila = (placa: string) => screen.getByRole("rowheader", { name: new RegExp(placa) }).closest("tr")!;
const consultas = (fetchMock: { mock: { calls: unknown[][] } }) =>
  llamadas(fetchMock, "GET", "/vehiculos").map(([u]) => Object.fromEntries(new URL(String(u), "http://localhost").searchParams));
const region = () => document.querySelector("[data-region-viva-vehiculos] [role='status']") as HTMLElement;
const rutaVeh = (n: number, final: string) => `/diagnosticos/${LOTE}/vehiculos/vehiculo-${n}/${final}`;
const sinSan = () => Response.json({ code: "SIN_SANEAMIENTO", error: "x" }, { status: 404 });

let descargados: string[];
beforeEach(() => {
  h.replace.mockReset();
  h.push.mockReset();
  h.params = new URLSearchParams("vista=vehiculos");
  descargados = [];
  Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:prueba"), configurable: true });
  Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
    descargados.push(this.download);
  });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("ListadoVehiculos — AC21: tabla de placas con paginación en el servidor", () => {
  it("columnas de la spec, placa como encabezado de fila y los datos de la fila en español", async () => {
    simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase())) });
    pintar();
    const t = await tabla();
    const cabeceras = within(t).getAllByRole("columnheader").map((c) => c.textContent);
    expect(cabeceras).toEqual(["Selección", "Placa", "Diagnóstico", "Fecha", "Modo", "Estado", "Dictamen", "Saneamiento", "Trámite", "Acciones"]);
    const f1 = fila("TST001");
    expect(within(f1).getByRole("rowheader")).toHaveAttribute("scope", "row");
    expect(within(f1).getByText("Flota de prueba 1")).toBeInTheDocument();
    expect(within(f1).getByText("10/11/2026 09:00")).toBeInTheDocument();
    expect(within(f1).getByText("Con análisis")).toBeInTheDocument();
    expect(within(f1).getByText("Terminado")).toBeInTheDocument();
    expect(within(f1).getByText("Bloqueado")).toBeInTheDocument();
    const f2 = fila("TST002");
    expect(within(f2).getByText("En proceso")).toBeInTheDocument();
    expect(within(f2).getByText("Sin dictamen")).toBeInTheDocument();
    const f3 = fila("TST003");
    expect(within(f3).getByText("En saneamiento")).toBeInTheDocument();
    expect(within(f3).getByText("#123")).toBeInTheDocument();
    expect(within(fila("TST004")).getByText("Viable")).toBeInTheDocument();
    expect(within(fila("TST004")).getByText("Diagnóstico Simple")).toBeInTheDocument();
    expect(screen.getByText("4 vehículos")).toBeInTheDocument();
  });

  it("sin filtros pide la página 1 de 10; con filtros en la URL los manda normalizados y los muestra en los controles", async () => {
    h.params = new URLSearchParams("vista=vehiculos&placa=tst&veredicto=BLOQUEADO&saneamiento_estado=SOLICITADO&con_tramite=false&pagina=2&tamano=50");
    const api = simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase(), { pagina: 2, tamano: 50, total: 60 })) });
    pintar();
    await tabla();
    expect(consultas(api)).toEqual([
      { placa: "TST", veredicto: "BLOQUEADO", saneamiento: "CON", saneamiento_estado: "SOLICITADO", con_tramite: "false", pagina: "2", tamano: "50" },
    ]);
    expect(screen.getByRole("combobox", { name: "Saneamiento" })).toHaveValue("SOLICITADO");
    expect(screen.getByRole("textbox", { name: "Placa" })).toHaveValue("TST");
  });

  it("cambiar un filtro reescribe la URL de la pestaña, vuelve a la página 1 y recarga con el total", async () => {
    h.params = new URLSearchParams("vista=vehiculos&pagina=3");
    let n = 0;
    const api = simularApi({ "GET /vehiculos": () => Response.json(lista(n++ === 0 ? filasBase() : [filaVeh(1)], { total: n === 1 ? 80 : 1 })) });
    pintar();
    await tabla();
    fireEvent.change(screen.getByRole("combobox", { name: "Dictamen" }), { target: { value: "VIABLE" } });
    expect(h.replace).toHaveBeenLastCalledWith("/diagnosticos?vista=vehiculos&veredicto=VIABLE", { scroll: false });
    await waitFor(() => expect(screen.getByText("1 vehículo")).toBeInTheDocument());
    expect(consultas(api).at(-1)).toEqual({ veredicto: "VIABLE", pagina: "1", tamano: "10" });
  });

  it("«Actualizar» vuelve a pedir la misma consulta; no hay refresco automático", async () => {
    const api = simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase())) });
    pintar();
    await tabla();
    expect(consultas(api)).toHaveLength(1);
    fireEvent.click(screen.getByRole("button", { name: "Actualizar" }));
    await waitFor(() => expect(consultas(api)).toHaveLength(2));
    expect(consultas(api)[1]).toEqual(consultas(api)[0]);
    await act(async () => {
      await new Promise((r) => setTimeout(r, 60));
    });
    expect(consultas(api)).toHaveLength(2);
    expect(h.replace).not.toHaveBeenCalled();
  });

  it("cambiar de página: URL con la página, foco en la tabla y anuncio «Página 2 de 3»", async () => {
    const api = simularApi({
      "GET /vehiculos": (url) => Response.json(lista([filaVeh(Number(url.searchParams.get("pagina")))], { total: 30, pagina: Number(url.searchParams.get("pagina")) })),
    });
    pintar();
    await tabla();
    const nav = screen.getByRole("navigation", { name: "Paginación de vehículos" });
    fireEvent.click(within(nav).getByRole("button", { name: "Página 2" }));
    expect(h.replace).toHaveBeenLastCalledWith("/diagnosticos?vista=vehiculos&pagina=2", { scroll: false });
    await waitFor(() => expect(screen.getByRole("rowheader", { name: /TST002/ })).toBeInTheDocument());
    expect(consultas(api).at(-1)).toMatchObject({ pagina: "2" });
    await waitFor(() => expect(screen.getByRole("table", { name: "Vehículos de la compañía" })).toHaveFocus());
    expect(region()).toHaveTextContent("Página 2 de 3");
  });

  it("UA-2: «Filas por página» es el del pie del kit (10/20/30/50): cambiar el tamaño vuelve a la página 1", async () => {
    const api = simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase(), { total: 300 })) });
    pintar();
    await tabla();
    const selectores = screen.getAllByRole("combobox", { name: "Filas por página" });
    expect(selectores).toHaveLength(1);
    const tamano = selectores[0];
    expect(tamano.closest(".kit-paginacion")).not.toBeNull();
    expect(within(tamano).getAllByRole("option").map((o) => o.textContent)).toEqual(["10", "20", "30", "50"]);
    fireEvent.change(tamano, { target: { value: "30" } });
    expect(h.replace).toHaveBeenLastCalledWith("/diagnosticos?vista=vehiculos&tamano=30", { scroll: false });
    await waitFor(() => expect(consultas(api).at(-1)).toEqual({ pagina: "1", tamano: "30" }));
  });
});

describe("ListadoVehiculos — AC40/AC43: cuatro estados", () => {
  it("cargando: «Cargando vehículos…»", () => {
    simularApi({ "GET /vehiculos": () => new Promise<Response>(() => {}) });
    pintar();
    expect(screen.getByRole("status", { name: "Cargando vehículos…" })).toBeInTheDocument();
  });

  it("error: aviso en español sin código ni mensaje técnico, y «Reintentar» vuelve a pedir", async () => {
    let n = 0;
    const api = simularApi({
      "GET /vehiculos": () => (n++ === 0 ? Response.json({ code: "ERROR_INTERNO_X", error: "NpgsqlException: timeout 57P01" }, { status: 500 }) : Response.json(lista(filasBase()))),
    });
    pintar();
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No pudimos cargar los vehículos");
    expect(alerta).toHaveTextContent("Revisa tu conexión e intenta de nuevo.");
    expect(alerta).not.toHaveTextContent(/ERROR_INTERNO_X|Npgsql|57P01|500/);
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    await tabla();
    expect(consultas(api)).toHaveLength(2);
  });

  it("403: lleva a la página de acceso negado con el código", async () => {
    simularApi({ "GET /vehiculos": () => Response.json({ code: "SIN_PERMISOS_DIAGNOSTICO", error: "x" }, { status: 403 }) });
    pintar();
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISOS_DIAGNOSTICO"));
  });

  it("400 con campo: el filtro queda inválido con su mensaje y la tabla pide revisar los filtros", async () => {
    h.params = new URLSearchParams("vista=vehiculos&desde=2026-11-20&hasta=2026-11-01");
    simularApi({ "GET /vehiculos": () => Response.json({ code: "RANGO_INVALIDO", campo: "hasta", error: "x" }, { status: 400 }) });
    pintar();
    expect(await screen.findByText("Revisa los filtros")).toBeInTheDocument();
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Hasta")).toHaveAccessibleDescription("La fecha final no puede ser anterior a la inicial.");
  });

  it("vacío sin filtros: «Aún no hay vehículos» con «Diagnosticar un vehículo» solo si puede crear", async () => {
    simularApi({ "GET /vehiculos": () => Response.json(lista([])) });
    const { unmount } = pintar({ puedeCrear: true });
    expect(await screen.findByRole("heading", { name: "Aún no hay vehículos" })).toBeInTheDocument();
    expect(screen.getByText("Aquí verás todas las placas de tus diagnósticos, en proceso y terminadas.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Diagnosticar un vehículo" })).toHaveAttribute("href", "/diagnosticos/nuevo?modo=express");
    unmount();
    pintar({ puedeCrear: false });
    await screen.findByRole("heading", { name: "Aún no hay vehículos" });
    expect(screen.queryByRole("link", { name: "Diagnosticar un vehículo" })).toBeNull();
  });

  it("vacío con filtros: «Ningún vehículo coincide» anunciado y «Limpiar filtros» deja solo la pestaña en la URL", async () => {
    h.params = new URLSearchParams("vista=vehiculos&placa=ZZZ");
    simularApi({ "GET /vehiculos": () => Response.json(lista([])) });
    pintar();
    const titulo = await screen.findByRole("heading", { name: "Ningún vehículo coincide" }, { timeout: 2000 });
    expect(titulo.closest("[role='status']")).not.toBeNull();
    expect(screen.getByText("Prueba con otros filtros.")).toBeInTheDocument();
    const limpiar = screen.getAllByRole("button", { name: "Limpiar filtros" }).at(-1)!;
    fireEvent.click(limpiar);
    expect(h.replace).toHaveBeenLastCalledWith("/diagnosticos?vista=vehiculos", { scroll: false });
  });
});

describe("ListadoVehiculos — AC23: acciones por fila", () => {
  it("bandera de PS-1, «Abrir diagnóstico» al tablero del lote y «Ver deuda» solo en terminados; la fila no es clicable", async () => {
    simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase())) });
    pintar();
    await tabla();
    const f1 = fila("TST001");
    expect(within(f1).getByRole("button", { name: "Solicitar saneamiento de la placa TST001" })).toHaveAttribute("aria-pressed", "false");
    expect(within(f1).getByRole("link", { name: "Abrir el diagnóstico Flota de prueba 1" })).toHaveAttribute("href", `/diagnosticos/${LOTE}`);
    expect(within(f1).getByRole("button", { name: "Ver deuda de la placa TST001" })).toHaveAttribute("aria-haspopup", "dialog");
    expect(within(fila("TST002")).queryByRole("button", { name: /^Ver deuda/ })).toBeNull();
    expect(within(fila("TST002")).getByRole("button", { name: "Solicitar saneamiento de la placa TST002" })).toHaveAttribute("aria-disabled", "true");
    expect(within(fila("TST003")).getByRole("button", { name: "Ver saneamiento de la placa TST003: En saneamiento" })).toBeInTheDocument();
    fireEvent.click(within(f1).getByText("Flota de prueba 1"));
    expect(h.push).not.toHaveBeenCalled();
  });

  it("la bandera marca con PUT al lote de la fila: la etiqueta aparece en la fila y se anuncia en la región viva", async () => {
    const api = simularApi({
      "GET /vehiculos": () => Response.json(lista(filasBase())),
      [`PUT ${rutaVeh(1, "saneamiento")}`]: () => Response.json({ id: "s1", estado: "SOLICITADO" }, { status: 201 }),
    });
    pintar();
    await tabla();
    const bandera = within(fila("TST001")).getByRole("button", { name: "Solicitar saneamiento de la placa TST001" });
    fireEvent.click(bandera);
    expect(bandera).toHaveAttribute("aria-pressed", "true");
    expect(within(fila("TST001")).getByText("Saneamiento solicitado")).toBeInTheDocument();
    await waitFor(() => expect(region()).toHaveTextContent("Saneamiento solicitado para TST001."));
    expect(llamadas(api, "PUT", rutaVeh(1, "saneamiento"))).toHaveLength(1);
    expect(region()).toHaveClass("sr-only");
  });

  it("«Ver deuda» abre PS-2 del vehículo de esa fila y al cerrarlo el foco vuelve al botón", async () => {
    const api = simularApi({
      "GET /vehiculos": () => Response.json(lista(filasBase())),
      [`GET ${rutaVeh(1, "deuda")}`]: () => Response.json(deudaSinPendientes()),
      [`GET ${rutaVeh(1, "saneamiento")}`]: sinSan,
    });
    pintar();
    await tabla();
    const ver = within(fila("TST001")).getByRole("button", { name: "Ver deuda de la placa TST001" });
    ver.focus();
    fireEvent.click(ver);
    const panel = await screen.findByRole("dialog", { name: "Deuda y saneamiento" });
    expect(await within(panel).findByRole("heading", { name: "Deuda por fuente" })).toBeInTheDocument();
    expect(llamadas(api, "GET", rutaVeh(1, "deuda"))).toHaveLength(1);
    expect(within(panel).getByRole("button", { name: "Solicitar saneamiento de la placa TST001" })).toBeInTheDocument();
    fireEvent.click(within(panel).getAllByRole("button", { name: /cerrar/i })[0]);
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(within(fila("TST001")).getByRole("button", { name: "Ver deuda de la placa TST001" })).toHaveFocus();
  });

  it("«Ver saneamiento» (EN_GESTION) abre PS-2; marcar desde la cabecera del panel actualiza la fila de la grilla", async () => {
    let marcado = false;
    simularApi({
      "GET /vehiculos": () => Response.json(lista(filasBase())),
      [`GET ${rutaVeh(3, "deuda")}`]: () => Response.json(deudaSinPendientes({ vehiculo: { id: "vehiculo-3", placa: "TST003" } })),
      [`GET ${rutaVeh(3, "saneamiento")}`]: () => Response.json(saneamientoEnGestion()),
      [`GET ${rutaVeh(1, "deuda")}`]: () => Response.json(deudaSinPendientes()),
      [`GET ${rutaVeh(1, "saneamiento")}`]: () =>
        marcado ? Response.json(saneamientoEnGestion({ estado: "SOLICITADO", tomado_en: null, renglones: [], historial: [], observacion_flit: null })) : sinSan(),
      [`PUT ${rutaVeh(1, "saneamiento")}`]: () => {
        marcado = true;
        return Response.json({ id: "s1", estado: "SOLICITADO" }, { status: 201 });
      },
    });
    pintar();
    await tabla();
    fireEvent.click(within(fila("TST003")).getByRole("button", { name: "Ver saneamiento de la placa TST003: En saneamiento" }));
    const panel = await screen.findByRole("dialog", { name: "Deuda y saneamiento" });
    expect(await within(panel).findByRole("region", { name: "Saneamiento" })).toBeInTheDocument();
    fireEvent.click(within(panel).getAllByRole("button", { name: /cerrar/i })[0]);
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());

    fireEvent.click(within(fila("TST001")).getByRole("button", { name: "Ver deuda de la placa TST001" }));
    const panel1 = await screen.findByRole("dialog", { name: "Deuda y saneamiento" });
    fireEvent.click(await within(panel1).findByRole("button", { name: "Solicitar saneamiento de la placa TST001" }));
    await waitFor(() => expect(within(panel1).getByText("Saneamiento solicitado para TST001.")).toBeInTheDocument());
    expect(within(fila("TST001")).getByText("Saneamiento solicitado")).toBeInTheDocument();
  });

  it("AC8 (negativo): sin la capacidad sanear no hay bandera en las filas sin saneamiento ni casillas para sanear", async () => {
    simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase())) });
    pintar({ puedeSanear: false, puedeDescargar: false });
    await tabla();
    expect(screen.queryAllByRole("button", { name: /^Solicitar saneamiento/ })).toHaveLength(0);
    expect(screen.queryAllByRole("checkbox")).toHaveLength(0);
    expect(screen.queryByRole("columnheader", { name: "Selección" })).toBeNull();
    expect(within(fila("TST003")).getByRole("button", { name: "Ver saneamiento de la placa TST003: En saneamiento" })).toBeInTheDocument();
  });
});

describe("ListadoVehiculos — AC22: selección, «Sanear (N)» y «Descargar deuda (N)»", () => {
  const casilla = (placa: string) => within(fila(placa)).getByRole("checkbox", { name: `Seleccionar la placa ${placa}` });
  const barra = () => screen.getByRole("region", { name: "Selección" });

  it("casilla habilitada si es elegible para sanear o está terminada para descargar; la deshabilitada dice por qué", async () => {
    simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase())) });
    pintar();
    await tabla();
    expect(casilla("TST001")).toBeEnabled();
    expect(casilla("TST003")).toBeEnabled();
    expect(casilla("TST002")).toBeDisabled();
    expect(casilla("TST002")).toHaveAccessibleDescription("Faltan consultas por terminar");
    fireEvent.click(casilla("TST001"));
    fireEvent.click(casilla("TST003"));
    expect(barra()).toHaveTextContent("2 vehículos seleccionados");
    expect(within(barra()).getByRole("button", { name: "Sanear (1)" })).toBeInTheDocument();
    expect(within(barra()).getByRole("button", { name: "Descargar deuda (2)" })).not.toHaveAttribute("aria-disabled");
  });

  it("«Descargar deuda (N)» manda un solo POST /vehiculos/deuda/excel con los ids, baja el Excel y anuncia «Descarga lista.»", async () => {
    const api = simularApi({
      "GET /vehiculos": () => Response.json(lista(filasBase())),
      "POST /vehiculos/deuda/excel": () =>
        new Response(new Blob(["xlsx"]), { status: 200, headers: { "Content-Disposition": 'attachment; filename="deuda-vehiculos.xlsx"' } }),
    });
    pintar();
    await tabla();
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }));
    fireEvent.click(within(barra()).getByRole("button", { name: "Descargar deuda (3)" }));
    await waitFor(() => expect(region()).toHaveTextContent("Descarga lista."));
    const posts = llamadas(api, "POST", "/vehiculos/deuda/excel");
    expect(posts).toHaveLength(1);
    expect(JSON.parse(String((posts[0][1] as RequestInit).body))).toEqual({ vehiculo_ids: ["vehiculo-1", "vehiculo-3", "vehiculo-4"] });
    expect(descargados).toEqual(["deuda-vehiculos.xlsx"]);
  });

  it("con más de 200 placas «Descargar deuda (N)» queda aria-disabled con «Máximo 200 placas por descarga» y no se envía", async () => {
    const muchas = Array.from({ length: 201 }, (_, i) => filaVeh(i + 1));
    const api = simularApi({ "GET /vehiculos": () => Response.json(lista(muchas, { tamano: 100, total: 201 })) });
    pintar({ puedeDescargar: true });
    await tabla();
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }));
    const descargar = within(barra()).getByRole("button", { name: "Descargar deuda (201)" });
    expect(descargar).toHaveAttribute("aria-disabled", "true");
    expect(descargar).toHaveAccessibleDescription("Máximo 200 placas por descarga");
    fireEvent.click(descargar);
    await act(async () => {
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(llamadas(api, "POST", "/vehiculos/deuda/excel")).toHaveLength(0);
    // 201 filas completas en jsdom tardan más que el tope por defecto de 5 s (el tope de la grilla es 100 por página, D-106).
  }, 30_000);

  it("si la descarga falla: mensaje en español bajo la barra, sin código, y la selección se conserva", async () => {
    simularApi({
      "GET /vehiculos": () => Response.json(lista(filasBase())),
      "POST /vehiculos/deuda/excel": () => Response.json({ code: "LISTA_INVALIDA", error: "vehiculo_ids" }, { status: 400 }),
    });
    pintar();
    await tabla();
    fireEvent.click(casilla("TST001"));
    fireEvent.click(within(barra()).getByRole("button", { name: "Descargar deuda (1)" }));
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No pudimos generar el archivo. Intenta de nuevo.");
    expect(alerta).not.toHaveTextContent(/LISTA_INVALIDA|vehiculo_ids|400/);
    expect(casilla("TST001")).toBeChecked();
  });

  it("«Sanear (N)» como AC11: POST /saneamientos/solicitudes, mensaje con omitidos, las solicitadas con su etiqueta y las omitidas siguen marcadas", async () => {
    const api = simularApi({
      "GET /vehiculos": () => Response.json(lista([filaVeh(1), filaVeh(5)])),
      "POST /saneamientos/solicitudes": () => Response.json({ solicitados: 1, omitidos: [{ vehiculo_id: "vehiculo-5", motivo: "YA_SOLICITADO" }] }),
    });
    pintar();
    await tabla();
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }));
    const sanear = within(barra()).getByRole("button", { name: "Sanear (2)" });
    sanear.focus();
    fireEvent.click(sanear);
    const texto = "Se solicitó el saneamiento de 1 vehículo. 1 se omitió: 1 que ya tenía saneamiento.";
    await waitFor(() => expect(region()).toHaveTextContent(texto));
    const [, init] = llamadas(api, "POST", "/saneamientos/solicitudes")[0] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ vehiculo_ids: ["vehiculo-1", "vehiculo-5"] });
    expect(within(fila("TST001")).getByText("Saneamiento solicitado")).toBeInTheDocument();
    expect(casilla("TST005")).toBeChecked();
    // UA-4: el foco se queda en «Sanear», así que el único anuncio es la región viva; el aviso visible no es región viva.
    const visibles = screen.getAllByText(texto).filter((n) => !n.closest("[data-region-viva-vehiculos]"));
    expect(visibles).toHaveLength(1);
    expect(visibles[0].closest("[role='alert'], [role='status'], [aria-live]")).toBeNull();
    expect(screen.getByRole("button", { name: /^Sanear \(/ })).toHaveFocus();
  });

  it("UA-7: con 0 solicitados el título dice «No se solicitó el saneamiento»", async () => {
    simularApi({
      "GET /vehiculos": () => Response.json(lista([filaVeh(1)])),
      "POST /saneamientos/solicitudes": () => Response.json({ solicitados: 0, omitidos: [{ vehiculo_id: "vehiculo-1", motivo: "NO_ENCONTRADO" }] }),
    });
    pintar({ puedeSanear: true });
    await tabla();
    fireEvent.click(casilla("TST001"));
    fireEvent.click(within(barra()).getByRole("button", { name: "Sanear (1)" }));
    const texto = "No se solicitó el saneamiento de ningún vehículo. 1 se omitió: 1 no disponible.";
    const visibles = (await screen.findAllByText(texto)).filter((n) => !n.closest("[data-region-viva-vehiculos]"));
    const destino = visibles[0].closest("[tabindex='-1']") as HTMLElement;
    expect(within(destino).getByText("No se solicitó el saneamiento")).toBeInTheDocument();
    expect(within(destino).queryByText("Saneamiento solicitado")).toBeNull();
  });

  it("UA-4: todas solicitadas (selección vacía): foco en el aviso, sin región viva y con el título «Saneamiento solicitado»", async () => {
    simularApi({
      "GET /vehiculos": () => Response.json(lista([filaVeh(1)])),
      "POST /saneamientos/solicitudes": () => Response.json({ solicitados: 1, omitidos: [] }),
    });
    pintar({ puedeSanear: true });
    await tabla();
    fireEvent.click(casilla("TST001"));
    fireEvent.click(within(barra()).getByRole("button", { name: "Sanear (1)" }));
    const aviso = await screen.findByText("Se solicitó el saneamiento de 1 vehículo.");
    const destino = aviso.closest("[tabindex='-1']") as HTMLElement;
    await waitFor(() => expect(destino).toHaveFocus());
    expect(destino.querySelector("[role='alert'], [role='status'], [aria-live]")).toBeNull();
    expect(within(destino).getByText("Saneamiento solicitado")).toBeInTheDocument();
    expect(region()).not.toHaveTextContent("Se solicitó el saneamiento");
  });

  it("«Sanear (N)» con error: «No pudimos solicitar el saneamiento. Intenta de nuevo.» y la selección se conserva", async () => {
    simularApi({
      "GET /vehiculos": () => Response.json(lista(filasBase())),
      "POST /saneamientos/solicitudes": () => Response.json({ code: "LISTA_INVALIDA", error: "x" }, { status: 400 }),
    });
    pintar();
    await tabla();
    fireEvent.click(casilla("TST001"));
    fireEvent.click(within(barra()).getByRole("button", { name: "Sanear (1)" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("No pudimos solicitar el saneamiento. Intenta de nuevo.");
    expect(casilla("TST001")).toBeChecked();
  });

  it("la selección se limpia al cambiar de página o de filtros", async () => {
    simularApi({ "GET /vehiculos": (url) => Response.json(lista(filasBase(), { total: 60, pagina: Number(url.searchParams.get("pagina")) })) });
    pintar();
    await tabla();
    fireEvent.click(casilla("TST001"));
    expect(barra()).toBeInTheDocument();
    fireEvent.click(within(screen.getByRole("navigation", { name: "Paginación de vehículos" })).getByRole("button", { name: "Página 2" }));
    await tabla();
    expect(screen.queryByRole("region", { name: "Selección" })).toBeNull();
    fireEvent.click(casilla("TST001"));
    fireEvent.change(screen.getByRole("combobox", { name: "Modo" }), { target: { value: "SIMPLE" } });
    await tabla();
    expect(screen.queryByRole("region", { name: "Selección" })).toBeNull();
  });

  it("solo con descargarReportes la barra no ofrece «Sanear»; solo con sanear no ofrece «Descargar deuda»", async () => {
    simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase())) });
    const { unmount } = pintar({ puedeDescargar: true });
    await tabla();
    fireEvent.click(casilla("TST001"));
    expect(within(barra()).queryByRole("button", { name: /^Sanear/ })).toBeNull();
    expect(within(barra()).getByRole("button", { name: "Descargar deuda (1)" })).toBeInTheDocument();
    unmount();
    pintar({ puedeSanear: true });
    await tabla();
    fireEvent.click(casilla("TST001"));
    expect(within(barra()).queryByRole("button", { name: /^Descargar deuda/ })).toBeNull();
    expect(within(barra()).getByRole("button", { name: "Sanear (1)" })).toBeInTheDocument();
    expect(casilla("TST003")).toBeDisabled();
  });
});

describe("ListadoVehiculos — UA-5: vehículos sin placa", () => {
  it("con VIN o identificador: se ve en el título y va en los nombres accesibles; sin ninguno, «sin placa», nunca vacío", async () => {
    simularApi({
      "GET /vehiculos": () =>
        Response.json(
          lista([
            filaVeh(1, { placa: null, vin: "VINPRUEBA0000001" }),
            filaVeh(2, { placa: null, vin: null, identificador: "MOTOR-PRUEBA-2" }),
            filaVeh(3, { placa: null }),
          ]),
        ),
    });
    pintar();
    await tabla();
    const conVin = fila("VINPRUEBA0000001");
    expect(within(conVin).getByRole("rowheader")).toHaveTextContent("VINPRUEBA0000001");
    expect(within(conVin).getByRole("checkbox", { name: "Seleccionar la placa VINPRUEBA0000001" })).toBeInTheDocument();
    expect(within(conVin).getByRole("button", { name: "Solicitar saneamiento de la placa VINPRUEBA0000001" })).toBeInTheDocument();
    expect(within(conVin).getByRole("button", { name: "Ver deuda de la placa VINPRUEBA0000001" })).toBeInTheDocument();
    const conId = fila("MOTOR-PRUEBA-2");
    expect(within(conId).getByRole("checkbox", { name: "Seleccionar la placa MOTOR-PRUEBA-2" })).toBeInTheDocument();
    const sinNada = fila("Sin placa");
    expect(within(sinNada).getByRole("checkbox", { name: "Seleccionar el vehículo sin placa" })).toBeInTheDocument();
    expect(within(sinNada).getByRole("button", { name: "Ver deuda del vehículo sin placa" })).toBeInTheDocument();
    expect(within(sinNada).getByRole("button", { name: "Solicitar saneamiento del vehículo sin placa" })).toBeInTheDocument();
    for (const b of screen.getAllByRole("button")) expect(b.getAttribute("aria-label") ?? "").not.toMatch(/placa $/);
  });

  it("«Ver deuda» de un vehículo con VIN abre PS-2 con el VIN como descripción", async () => {
    simularApi({
      "GET /vehiculos": () => Response.json(lista([filaVeh(1, { placa: null, vin: "VINPRUEBA0000001" })])),
      [`GET ${rutaVeh(1, "deuda")}`]: () => Response.json(deudaSinPendientes()),
      [`GET ${rutaVeh(1, "saneamiento")}`]: sinSan,
    });
    pintar();
    await tabla();
    fireEvent.click(screen.getByRole("button", { name: "Ver deuda de la placa VINPRUEBA0000001" }));
    const panel = await screen.findByRole("dialog");
    expect(within(panel).getAllByText("VINPRUEBA0000001").length).toBeGreaterThan(0);
  });
});

describe("ListadoVehiculos — AC23/AC42: 360 px y tokens", () => {
  const clases = (el: Element) => (el.getAttribute("class") ?? "").split(/\s+/);

  it("por debajo de 1024 px la tabla pasa a tarjetas: recetas TABLA_MOVIL_*, data-label por celda y la placa de título", async () => {
    simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase())) });
    pintar();
    const t = await tabla();
    for (const c of TABLA_MOVIL_TABLE_CLS.split(" ")) expect(clases(t)).toContain(c);
    for (const c of TABLA_MOVIL_THEAD_CLS.split(" ")) expect(clases(t.querySelector("thead")!)).toContain(c);
    for (const c of TABLA_MOVIL_TBODY_CLS.split(" ")) expect(clases(t.querySelector("tbody")!)).toContain(c);
    const f = fila("TST001");
    for (const c of TABLA_MOVIL_ROW_CLS.split(" ")) expect(clases(f)).toContain(c);
    const celdas = [...f.querySelectorAll("td, th")];
    expect(celdas.map((c) => c.getAttribute("data-label"))).toEqual(["Selección", "Placa", "Diagnóstico", "Fecha", "Modo", "Estado", "Dictamen", "Saneamiento", "Trámite", "Acciones"]);
    for (const c of TABLA_MOVIL_CELL_CLS.split(" ")) expect(clases(celdas[1])).toContain(c);
    for (const c of TABLA_MOVIL_CELL_TITULO_CLS.split(" ")) expect(clases(celdas[1])).toContain(c);
    expect(t).toHaveAttribute("tabindex", "-1");
  });

  it("los archivos de la pestaña no tienen colores sueltos (solo tokens)", () => {
    const fuente = readFileSync(join(__dirname, "..", "ListadoVehiculos.tsx"), "utf8");
    expect(fuente).not.toMatch(/#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b/);
  });
});

// HU #14585 (spec-ui §6.5, §7; contrato 1.6.0 §1.2 `FilaPlaca.cambio_propietario`). Uso de ejemplo: GET /vehiculos con una fila
// { …, cambio_propietario: true } → ícono «Cambio de propietario» junto a la placa; la tabla con la receta por defecto.
describe("ListadoVehiculos — HU #14585: tabla por defecto e ícono de cambio de propietario", () => {
  it("A7.1: contenedor, cabecera de 12 px y hover de fila de la tabla por defecto", async () => {
    simularApi({ "GET /vehiculos": () => Response.json(lista(filasBase())) });
    pintar();
    esperarTablaPorDefecto(await tabla());
    esperarCabecera12px();
  });

  it("A6.7: el ícono va en la celda de la placa solo con true", async () => {
    simularApi({ "GET /vehiculos": () => Response.json(lista([filaVeh(1, { cambio_propietario: true }), filaVeh(2, { cambio_propietario: false }), filaVeh(3)])) });
    pintar();
    await tabla();
    expect(within(within(fila("TST001")).getByRole("rowheader")).getByRole("button", { name: "Cambio de propietario" })).toBeInTheDocument();
    expect(within(fila("TST002")).queryByRole("button", { name: "Cambio de propietario" })).toBeNull();
    expect(within(fila("TST003")).queryByRole("button", { name: "Cambio de propietario" })).toBeNull();
  });
});
