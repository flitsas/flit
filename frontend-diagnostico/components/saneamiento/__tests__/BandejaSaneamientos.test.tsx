import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import PaginaSaneamientos from "@/app/(app)/backoffice/saneamientos/page";
import { BandejaSaneamientos } from "@/components/backoffice/BandejaSaneamientos";
import { sesion } from "@/__tests__/configuracion/datos";
import { filtrosBandejaDesdeUrl } from "@/lib/backoffice/saneamientos";
import { ResumenSaneamientosContext } from "@/lib/backoffice/useResumenSaneamientos";
import { BASE, EMPRESAS, EMPRESA_A, EMPRESA_DESCONOCIDA, LOTE_BO, filaBandeja, listaBandeja, llamadas, simularApi } from "./datosBackoffice";

// Uso de ejemplo: /backoffice/saneamientos?estado=SOLICITADO&empresa=… monta <BandejaSaneamientos filtros={…} /> dentro
// de AccesoSaneamientos (capacidades backoffice y gestionarSaneamiento). Pide GET /backoffice/saneamientos con los filtros
// de la URL, pinta la tabla con «Tomar (N)» para las solicitudes sin tomar y «Descargar Excel» con los mismos filtros; cada
// fila lleva a la gestión /backoffice/saneamientos/{sid}?empresa=.

const h = vi.hoisted(() => ({
  capacidades: { backoffice: true, gestionarSaneamiento: true } as Record<string, boolean>,
  replace: vi.fn(),
  push: vi.fn(),
}));
vi.mock("@/components/DiagnosticoShell", () => ({ useSesion: () => ({ sesion: sesion(h.capacidades), cargando: false, error: null, recargar: () => {} }) }));
vi.mock("next/navigation", () => ({ usePathname: () => "/backoffice/saneamientos", useRouter: () => ({ replace: h.replace, push: h.push, refresh: vi.fn() }) }));

const json = (cuerpo: unknown, status = 200) => Response.json(cuerpo, { status });
const conEmpresas = (rutas: Parameters<typeof simularApi>[0]) => simularApi({ "GET /backoffice/empresas": () => json(EMPRESAS), ...rutas });

/** Parámetros de cada GET de la bandeja (empresas, resumen y Excel no cuentan). */
const consultas = (fetchMock: { mock: { calls: unknown[][] } }) =>
  fetchMock.mock.calls
    .map(([u, init]) => ({ url: new URL(String(u), "http://localhost"), metodo: (init as RequestInit | undefined)?.method ?? "GET" }))
    .filter(({ url, metodo }) => metodo === "GET" && url.pathname === `/api/v1/diagnostico${BASE}`)
    .map(({ url }) => url.searchParams.toString());

const montar = (consulta = "", refrescar = vi.fn()) =>
  render(
    <ResumenSaneamientosContext.Provider value={{ solicitados: 3, refrescar }}>
      <BandejaSaneamientos filtros={filtrosBandejaDesdeUrl(new URLSearchParams(consulta))} />
    </ResumenSaneamientosContext.Provider>,
  );

/** El texto también se anuncia en la región viva: se busca el visible. */
const SIN_REGION = "script, style, [data-region-viva] *";
const tabla = () => screen.findByRole("table", { name: "Saneamientos" });

beforeEach(() => {
  h.capacidades = { backoffice: true, gestionarSaneamiento: true };
  h.replace.mockReset();
  h.push.mockReset();
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("AC24 — la ruta solo para FLIT con gestionarSaneamiento (negativo)", () => {
  it("sin gestionarSaneamiento, aunque tenga backoffice, va a la 403 y no pide la bandeja", async () => {
    h.capacidades = { backoffice: true, gestionarSaneamiento: false };
    const api = conEmpresas({});
    render(await PaginaSaneamientos({ searchParams: Promise.resolve({}) }));
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO_SANEAMIENTO"));
    expect(llamadas(api, "GET", BASE)).toHaveLength(0);
    expect(screen.queryByRole("table", { name: "Saneamientos" })).toBeNull();
  });

  it("sin backoffice (admin de una compañía) va a la 403 del backoffice y no pide nada", async () => {
    h.capacidades = { backoffice: false, gestionarSaneamiento: false };
    const api = conEmpresas({});
    render(await PaginaSaneamientos({ searchParams: Promise.resolve({}) }));
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO_BACKOFFICE"));
    expect(llamadas(api, "GET", BASE)).toHaveLength(0);
  });

  it("con la capacidad, la página pasa los filtros de la URL a la bandeja", async () => {
    const api = conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)])) });
    render(await PaginaSaneamientos({ searchParams: Promise.resolve({ estado: ["SOLICITADO", "EN_GESTION"], placa: "tst" }) }));
    await tabla();
    expect(consultas(api)[0]).toBe("estado=SOLICITADO&estado=EN_GESTION&placa=TST&pagina=1&tamano=10");
  });
});

describe("AC26 / AC40 — cuatro estados de la bandeja", () => {
  it("cargando: el cargador del kit con «Cargando saneamientos…»", () => {
    conEmpresas({ [`GET ${BASE}`]: () => new Promise<Response>(() => {}) });
    montar();
    expect(screen.getByRole("status", { name: "Cargando saneamientos…" })).toBeInTheDocument();
  });

  it("error: «No pudimos cargar la bandeja» con Reintentar que vuelve a pedir", async () => {
    let n = 0;
    conEmpresas({ [`GET ${BASE}`]: () => (++n === 1 ? json({ error: "x" }, 500) : json(listaBandeja([filaBandeja(1)]))) });
    montar();
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No pudimos cargar la bandeja");
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    expect(await tabla()).toBeInTheDocument();
  });

  it("vacío con el filtro por defecto: «No hay solicitudes por tomar»", async () => {
    conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([])) });
    montar();
    expect(await screen.findByRole("heading", { name: "No hay solicitudes por tomar" })).toBeInTheDocument();
    expect(screen.getByText("Cuando una compañía pida un saneamiento aparecerá aquí.")).toBeInTheDocument();
  });

  it("vacío con otros filtros: «Ningún saneamiento coincide» y «Limpiar filtros» vuelve al de por defecto", async () => {
    const api = conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([])) });
    montar("estado=SANEADO&placa=TST9");
    expect(await screen.findByRole("heading", { name: "Ningún saneamiento coincide" })).toBeInTheDocument();
    const vacio = screen.getByRole("heading", { name: "Ningún saneamiento coincide" }).closest("div")!;
    fireEvent.click(within(vacio).getByRole("button", { name: "Limpiar filtros" }));
    await waitFor(() => expect(consultas(api).at(-1)).toBe("estado=SOLICITADO&pagina=1&tamano=10"));
    expect(h.replace).toHaveBeenLastCalledWith("/backoffice/saneamientos", { scroll: false });
  });

  it("403 de la API lleva a la página 403", async () => {
    conEmpresas({ [`GET ${BASE}`]: () => json({ code: "SIN_PERMISO" }, 403) });
    montar();
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO_SANEAMIENTO"));
  });
});

describe("AC26 — tabla y filtros", () => {
  it("por defecto filtra SOLICITADO, ordena como la API y muestra las columnas del contrato", async () => {
    const api = conEmpresas({
      [`GET ${BASE}`]: () =>
        json(
          listaBandeja(
            [
              filaBandeja(1, { renglones_total: 3, renglones_pendientes: 2, estado: "EN_GESTION", tomado_en: "2026-11-04T15:00:00+00:00" }),
              filaBandeja(2, { empresa: EMPRESA_DESCONOCIDA, modo: "SIMPLE", veredicto: null }),
            ],
            { total: 2 },
          ),
        ),
    });
    montar();
    const t = await tabla();
    expect(consultas(api)[0]).toBe("estado=SOLICITADO&pagina=1&tamano=10");
    const cabeceras = within(t).getAllByRole("columnheader").map((c) => c.textContent?.trim());
    expect(cabeceras).toEqual(
      expect.arrayContaining(["Placa", "Compañía", "Diagnóstico", "Modo", "Dictamen", "Estado", "Solicitado", "Tomado", "Cerrado", "Pendientes"]),
    );
    const [, f1, f2] = within(t).getAllByRole("row");
    expect(within(f1).getByRole("rowheader")).toHaveTextContent("TST001");
    await waitFor(() => expect(f1).toHaveTextContent("Transportes A SAS"));
    expect(f1).toHaveTextContent("Lote octubre");
    expect(f1).toHaveTextContent("Con análisis IA");
    expect(f1).toHaveTextContent("Bloqueado");
    expect(f1).toHaveTextContent("En saneamiento");
    expect(f1).toHaveTextContent("2 de 3");
    expect(f2).toHaveTextContent("Compañía 0000000c");
    expect(f2).toHaveTextContent("Simple");
    expect(f2).toHaveTextContent("Saneamiento solicitado");
    expect(f2.querySelector("[data-label='Pendientes']")).toHaveTextContent(/^—$/);
    expect(screen.getByText("2 saneamientos")).toBeInTheDocument();
  });

  it("chips de estado multiselección: por defecto solo «Saneamiento solicitado»; al sumar otro recarga y lo lleva a la URL", async () => {
    const api = conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)])) });
    montar();
    await tabla();
    const grupo = screen.getByRole("group", { name: "Filtrar por estado" });
    expect(within(grupo).getByRole("button", { name: "Saneamiento solicitado" })).toHaveAttribute("aria-pressed", "true");
    expect(within(grupo).getByRole("button", { name: "Anulado" })).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(within(grupo).getByRole("button", { name: "En saneamiento" }));
    await waitFor(() => expect(consultas(api).at(-1)).toBe("estado=SOLICITADO&estado=EN_GESTION&pagina=1&tamano=10"));
    expect(h.replace).toHaveBeenLastCalledWith("/backoffice/saneamientos?estado=SOLICITADO&estado=EN_GESTION", { scroll: false });
  });

  it("todos los chips apagados equivale a todos: la consulta no lleva estado", async () => {
    const api = conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)])) });
    montar();
    await tabla();
    fireEvent.click(screen.getByRole("button", { name: "Saneamiento solicitado" }));
    await waitFor(() => expect(consultas(api).at(-1)).toBe("pagina=1&tamano=10"));
    expect(h.replace).toHaveBeenLastCalledWith("/backoffice/saneamientos?estado=", { scroll: false });
  });

  it("placa (al enviar) y fechas recargan con esos filtros; la placa viaja en mayúsculas", async () => {
    const api = conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)])) });
    montar();
    await tabla();
    fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "tst001" } });
    fireEvent.submit(screen.getByRole("search", { name: "Filtros de la bandeja" }));
    await waitFor(() => expect(consultas(api).at(-1)).toBe("estado=SOLICITADO&placa=TST001&pagina=1&tamano=10"));
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2026-11-01" } });
    await waitFor(() => expect(consultas(api).at(-1)).toBe("estado=SOLICITADO&placa=TST001&desde=2026-11-01&pagina=1&tamano=10"));
    fireEvent.change(screen.getByLabelText("Hasta"), { target: { value: "2026-11-30" } });
    await waitFor(() => expect(consultas(api).at(-1)).toContain("hasta=2026-11-30"));
  });

  it("compañía con SelectorEmpresaBackoffice: elegir una recarga con empresa y «Todas las compañías» la quita", async () => {
    const api = conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)])) });
    montar();
    await tabla();
    const selector = await screen.findByRole("combobox", { name: "Empresa" });
    fireEvent.change(selector, { target: { value: "Transportes" } });
    fireEvent.click(screen.getByRole("option", { name: /Transportes A SAS/ }));
    await waitFor(() => expect(consultas(api).at(-1)).toBe(`estado=SOLICITADO&empresa=${EMPRESA_A}&pagina=1&tamano=10`));
    fireEvent.click(screen.getByRole("button", { name: "Todas las compañías" }));
    await waitFor(() => expect(consultas(api).at(-1)).toBe("estado=SOLICITADO&pagina=1&tamano=10"));
  });

  it("paginación en el servidor: la página 2 pide pagina=2", async () => {
    const api = conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)], { total: 60 })) });
    montar();
    await tabla();
    expect(screen.getByText("60 saneamientos")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /página 2/i }));
    await waitFor(() => expect(consultas(api).at(-1)).toBe("estado=SOLICITADO&pagina=2&tamano=10"));
  });
});

describe("AC27 — «Tomar (N)» y Excel", () => {
  const filas = () => [filaBandeja(1), filaBandeja(2, { empresa: "00000000-0000-7000-8000-0000000000b2" }), filaBandeja(3, { estado: "EN_GESTION" })];

  it("solo las filas SOLICITADO se seleccionan; «Tomar (N)» cuenta esas y la de EN_GESTION queda deshabilitada con motivo", async () => {
    conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja(filas())) });
    montar("estado=");
    await tabla();
    const casillaGestion = screen.getByRole("checkbox", { name: "Seleccionar la placa TST003" });
    expect(casillaGestion).toBeDisabled();
    expect(casillaGestion).toHaveAccessibleDescription("Solo se toman las solicitudes sin tomar");
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar las solicitudes sin tomar" }));
    expect(screen.getByRole("button", { name: "Tomar (2)" })).toBeInTheDocument();
    expect(casillaGestion).not.toBeChecked();
  });

  it("«Tomar (N)» sin confirmación: envía {empresa, id}, informa tomados y omitidos, recarga la tabla y la insignia y lleva el foco al aviso", async () => {
    const [f1, f2] = filas();
    const api = conEmpresas({
      [`GET ${BASE}`]: () => json(listaBandeja(filas())),
      [`POST ${BASE}/tomar`]: () => json({ tomados: 1, omitidos: [{ id: f2.id, motivo: "TRANSICION_INVALIDA" }] }),
    });
    const refrescar = vi.fn();
    montar("estado=", refrescar);
    await tabla();
    fireEvent.click(screen.getByRole("checkbox", { name: "Seleccionar la placa TST001" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Seleccionar la placa TST002" }));
    fireEvent.click(screen.getByRole("button", { name: "Tomar (2)" }));
    const aviso = await screen.findByText("Se tomó 1 saneamiento. 1 no se pudo tomar porque cambió de estado.", { ignore: SIN_REGION });
    const [, init] = llamadas(api, "POST", `${BASE}/tomar`)[0] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ items: [{ empresa: f1.empresa, id: f1.id }, { empresa: f2.empresa, id: f2.id }] });
    await waitFor(() => expect(consultas(api)).toHaveLength(2));
    expect(refrescar).toHaveBeenCalled();
    expect(aviso.closest("[tabindex='-1']")).toHaveFocus();
    // UA-4: un solo canal. El foco va al aviso, así que ni la región viva ni el aviso se anuncian por su cuenta.
    expect(document.querySelector("[data-region-viva]")).toHaveTextContent("");
    const contenedor = aviso.closest("[tabindex='-1']") as HTMLElement;
    expect(contenedor.querySelector("[role='status'], [role='alert'], [aria-live]")).toBeNull();
    // UA-9: el destino del foco conserva el indicador visible.
    expect(contenedor.className).not.toContain("focus:outline-none");
    expect(contenedor.className).toContain("focus-visible:outline-2");
  });

  it("los omitidos por NO_ENCONTRADO se explican aparte, en español", async () => {
    const [f1, f2] = filas();
    conEmpresas({
      [`GET ${BASE}`]: () => json(listaBandeja(filas())),
      [`POST ${BASE}/tomar`]: () => json({ tomados: 0, omitidos: [{ id: f1.id, motivo: "NO_ENCONTRADO" }, { id: f2.id, motivo: "NO_ENCONTRADO" }] }),
    });
    montar("estado=");
    await tabla();
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar las solicitudes sin tomar" }));
    fireEvent.click(screen.getByRole("button", { name: "Tomar (2)" }));
    expect(await screen.findByText("No se tomó ningún saneamiento. 2 no se pudieron tomar porque ya no existen.", { ignore: SIN_REGION })).toBeInTheDocument();
  });

  it("«Descargar Excel» pide el Excel con los filtros actuales", async () => {
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:prueba"), configurable: true });
    Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
    const api = conEmpresas({
      [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)])),
      [`GET ${BASE}/excel`]: () => new Response("xlsx", { headers: { "Content-Disposition": 'attachment; filename="saneamientos.xlsx"' } }),
    });
    montar("estado=SOLICITADO&estado=SANEADO&placa=tst");
    await tabla();
    fireEvent.click(screen.getByRole("link", { name: "Descargar Excel" }));
    await waitFor(() => expect(llamadas(api, "GET", `${BASE}/excel`)).toHaveLength(1));
    const url = new URL(String(llamadas(api, "GET", `${BASE}/excel`)[0][0]), "http://localhost");
    expect(url.searchParams.toString()).toBe("estado=SOLICITADO&estado=SANEADO&placa=TST");
  });

  it("422 DEMASIADAS_FILAS: pide acotar los filtros", async () => {
    conEmpresas({
      [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)])),
      [`GET ${BASE}/excel`]: () => json({ code: "DEMASIADAS_FILAS", error: "x" }, 422),
    });
    montar();
    await tabla();
    fireEvent.click(screen.getByRole("link", { name: "Descargar Excel" }));
    expect(await screen.findByText("Hay más de 5.000 saneamientos con estos filtros. Acota las fechas o el estado para descargar.")).toHaveAttribute(
      "role",
      "alert",
    );
  });
});

describe("Auditoría UI (UA-1, UA-2, UA-10, UA-13)", () => {
  it("UA-1: el <thead> no lleva controles; «Seleccionar las solicitudes sin tomar» es un botón visible antes de la barra", async () => {
    conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1), filaBandeja(2), filaBandeja(3, { estado: "EN_GESTION" })])) });
    montar("estado=");
    const t = await tabla();
    const thead = t.querySelector("thead") as HTMLElement;
    expect(within(thead).queryByRole("checkbox")).toBeNull();
    expect(within(thead).queryAllByRole("button")).toHaveLength(0);
    expect(within(thead).getByText("Selección")).toHaveClass("sr-only");
    const todas = screen.getByRole("button", { name: "Seleccionar las solicitudes sin tomar" });
    expect(todas).toBeEnabled();
    fireEvent.click(todas);
    expect(screen.getByRole("checkbox", { name: "Seleccionar la placa TST001" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Seleccionar la placa TST002" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Seleccionar la placa TST003" })).not.toBeChecked();
  });

  it("UA-1: sin solicitudes sin tomar en la página, el botón queda deshabilitado", async () => {
    conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1, { estado: "EN_GESTION" })])) });
    montar("estado=");
    await tabla();
    expect(screen.getByRole("button", { name: "Seleccionar las solicitudes sin tomar" })).toBeDisabled();
  });

  it("UA-2: «Filas por página» del kit con 10/20/30/50; el cambio vuelve a la página 1 con el tamaño nuevo", async () => {
    const api = conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)], { total: 60, pagina: 2 })) });
    montar("pagina=2");
    await tabla();
    const selector = screen.getByRole("combobox", { name: "Filas por página" });
    expect(within(selector).getAllByRole("option").map((o) => o.textContent)).toEqual(["10", "20", "30", "50"]);
    fireEvent.change(selector, { target: { value: "20" } });
    await waitFor(() => expect(consultas(api).at(-1)).toBe("estado=SOLICITADO&pagina=1&tamano=20"));
    expect(h.replace).toHaveBeenLastCalledWith("/backoffice/saneamientos?tamano=20", { scroll: false });
  });

  it("UA-10: sin placa muestra el VIN o el identificador, o «Sin placa»; nunca el vehiculo_id", async () => {
    conEmpresas({
      [`GET ${BASE}`]: () =>
        json(
          listaBandeja([
            filaBandeja(1, { placa: null, vin: "VINPRUEBA0000001" }),
            filaBandeja(2, { placa: null, identificador: "MOTOR-PRUEBA-2" }),
            filaBandeja(3, { placa: null }),
          ]),
        ),
    });
    montar();
    const t = await tabla();
    expect(t).not.toHaveTextContent(/vehiculo-\d/);
    expect(within(t).getByRole("link", { name: "VINPRUEBA0000001" })).toBeInTheDocument();
    expect(within(t).getByRole("link", { name: "MOTOR-PRUEBA-2" })).toBeInTheDocument();
    expect(within(t).getByRole("link", { name: "Sin placa" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Seleccionar la placa VINPRUEBA0000001" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Seleccionar el vehículo sin placa" })).toBeInTheDocument();
  });

  it("AP-2 (N-1): «Sin placa» va en texto normal; solo la placa, el VIN o el identificador heredan las mayúsculas", async () => {
    conEmpresas({
      [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1), filaBandeja(2, { placa: null, vin: "VINPRUEBA0000002" }), filaBandeja(3, { placa: null })])),
    });
    montar();
    const t = await tabla();
    const sinPlaca = within(t).getByRole("link", { name: "Sin placa" });
    expect(sinPlaca).not.toHaveClass("uppercase");
    expect(sinPlaca).not.toHaveClass("tracking-wider");
    expect(sinPlaca.querySelector(".uppercase")).toBeNull();
    expect(within(t).getByRole("link", { name: "TST001" })).toHaveClass("uppercase", "tracking-wider");
    expect(within(t).getByRole("link", { name: "VINPRUEBA0000002" })).toHaveClass("uppercase", "tracking-wider");
  });

  it("AP-1: la fila usa placa || vin || identificador; la placa gana al VIN y el VIN al identificador", async () => {
    conEmpresas({
      [`GET ${BASE}`]: () =>
        json(
          listaBandeja([
            filaBandeja(1, { placa: "TST001", vin: "VINPRUEBA0000011", identificador: "MOTOR-PRUEBA-11" }),
            filaBandeja(2, { placa: null, vin: "VINPRUEBA0000012", identificador: "MOTOR-PRUEBA-12" }),
            filaBandeja(3, { placa: "", vin: null, identificador: "MOTOR-PRUEBA-13" }),
          ]),
        ),
    });
    montar();
    const t = await tabla();
    expect(within(t).getByRole("link", { name: "TST001" })).toBeInTheDocument();
    expect(within(t).getByRole("link", { name: "VINPRUEBA0000012" })).toBeInTheDocument();
    expect(within(t).getByRole("link", { name: "MOTOR-PRUEBA-13" })).toBeInTheDocument();
    expect(t).not.toHaveTextContent("VINPRUEBA0000011");
    expect(t).not.toHaveTextContent("MOTOR-PRUEBA-11");
    expect(t).not.toHaveTextContent("MOTOR-PRUEBA-12");
    expect(screen.getByRole("checkbox", { name: "Seleccionar la placa VINPRUEBA0000012" })).toBeInTheDocument();
  });

  it("UA-13: «Tomar (N)» es el Button del kit: en vuelo queda aria-busy con el girador y conserva el foco", async () => {
    let soltar: (r: Response) => void = () => {};
    conEmpresas({
      [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)])),
      [`POST ${BASE}/tomar`]: () => new Promise<Response>((r) => (soltar = r)),
    });
    montar();
    await tabla();
    fireEvent.click(screen.getByRole("checkbox", { name: "Seleccionar la placa TST001" }));
    const boton = screen.getByRole("button", { name: "Tomar (1)" });
    fireEvent.click(boton);
    const enVuelo = await screen.findByRole("button", { name: /Tomando…/ });
    expect(enVuelo).toHaveAttribute("aria-busy", "true");
    expect(enVuelo).toHaveAttribute("aria-disabled", "true");
    expect(enVuelo).not.toBeDisabled();
    soltar(json({ tomados: 1, omitidos: [] }));
    expect(await screen.findByText("Se tomó 1 saneamiento.", { ignore: SIN_REGION })).toBeInTheDocument();
  });
});

describe("AC28 — ir a la gestión", () => {
  it("la placa es un enlace a /backoffice/saneamientos/{sid}?empresa= y el clic en la fila navega allí", async () => {
    const fila = filaBandeja(1);
    conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([fila])) });
    montar();
    const t = await tabla();
    const destino = `/backoffice/saneamientos/${fila.id}?empresa=${EMPRESA_A}`;
    expect(within(t).getByRole("link", { name: /TST001/ })).toHaveAttribute("href", destino);
    fireEvent.click(within(t).getByText("Lote octubre"));
    expect(h.push).toHaveBeenCalledWith(destino);
  });

  it("el clic en la casilla no navega", async () => {
    conEmpresas({ [`GET ${BASE}`]: () => json(listaBandeja([filaBandeja(1)])) });
    montar();
    await tabla();
    fireEvent.click(screen.getByRole("checkbox", { name: "Seleccionar la placa TST001" }));
    expect(h.push).not.toHaveBeenCalled();
    expect(LOTE_BO).toBeTruthy();
  });
});
