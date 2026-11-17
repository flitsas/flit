import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { GrillaBackoffice } from "@/components/backoffice/GrillaBackoffice";
import { GrillaSemaforos } from "@/components/tablero/GrillaSemaforos";
import PaginaConsultaBackoffice from "@/app/(app)/backoffice/diagnosticos/[id]/vehiculos/[vehiculoId]/consultas/[fuente]/page";
import { sesion } from "@/__tests__/configuracion/datos";
import type { GrillaBackoffice as TGrilla } from "@/lib/backoffice/api";
import type { Grilla } from "@/lib/tablero/api";
import { EMPRESA_A, LOTE, celdaBo, grillaBo, llamadas, respuestaLlenado, simularApi, vehiculoBo } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: <GrillaBackoffice diagnosticoId={id} empresa={empresa} /> en /backoffice/diagnosticos/{id}?empresa= pide
// GET /backoffice/diagnosticos/{id}/grilla?empresa=&desde=0 y reutiliza GrillaSemaforos con la marca «Manual» en las celdas de
// origen MANUAL (solo aquí, Q5). Cada semáforo abre en una pestaña nueva /backoffice/diagnosticos/{id}/vehiculos/{vid}/
// consultas/{fuente}?empresa= (HU #14087); en las celdas en ERROR llenables, con la capacidad llenarConsultas, la celda trae
// «Llenar a mano», que abre FormularioLlenadoManual como modal.

const h = vi.hoisted(() => ({ capacidades: { backoffice: true, llenarConsultas: true } as Record<string, boolean>, replace: vi.fn() }));
vi.mock("@/components/DiagnosticoShell", () => ({ useSesion: () => ({ sesion: sesion(h.capacidades), cargando: false, error: null, recargar: () => {} }) }));
vi.mock("next/navigation", () => ({ usePathname: () => "/backoffice/diagnosticos", useRouter: () => ({ replace: h.replace, push: vi.fn(), refresh: vi.fn() }) }));

beforeEach(() => {
  h.capacidades = { backoffice: true, llenarConsultas: true };
  h.replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

const RUTA_GRILLA = `GET /backoffice/diagnosticos/${LOTE}/grilla`;

function montar(g: TGrilla = grillaBo(), extra: Parameters<typeof simularApi>[0] = {}) {
  const fetchMock = simularApi({ [RUTA_GRILLA]: () => Response.json(g), ...extra });
  const r = render(<GrillaBackoffice diagnosticoId={LOTE} empresa={EMPRESA_A} />);
  return { fetchMock, ...r };
}

const tabla = () => screen.findByRole("table", { name: /semáforo de consultas/i });
const td = (container: HTMLElement, id: string) => container.querySelector<HTMLElement>(`[data-celda="${id}"]`)!;
const semaforo = (fuente: string, placa: string) => screen.getByRole("link", { name: new RegExp(`^Ver detalle de ${fuente} de ${placa}`) });
const llenar = (fuente: string, placa: string) => screen.queryByRole("button", { name: `Llenar a mano ${fuente} de ${placa}` });

describe("GrillaBackoffice — cuatro estados y empresa explícita", () => {
  it("cargando y luego la grilla, leída con ?empresa= y desde=0", async () => {
    const { fetchMock } = montar();
    expect(screen.getByRole("status", { name: /cargando/i })).toBeInTheDocument();
    await tabla();
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://localhost");
    expect(url.searchParams.get("empresa")).toBe(EMPRESA_A);
    expect(url.searchParams.get("desde")).toBe("0");
    expect(screen.getByRole("heading", { level: 1, name: "Lote octubre" })).toBeInTheDocument();
  });

  it("404: el lote no está en la compañía elegida", async () => {
    simularApi({});
    render(<GrillaBackoffice diagnosticoId={LOTE} empresa={EMPRESA_A} />);
    expect(await screen.findByText(/no encontramos este lote/i)).toBeInTheDocument();
  });

  it("falla: aviso con Reintentar que vuelve a leer", async () => {
    let n = 0;
    simularApi({ [RUTA_GRILLA]: () => (++n === 1 ? Response.json({ error: "x" }, { status: 500 }) : Response.json(grillaBo())) });
    render(<GrillaBackoffice diagnosticoId={LOTE} empresa={EMPRESA_A} />);
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    expect(await tabla()).toBeInTheDocument();
  });

  it("vacío: el lote no tiene vehículos", async () => {
    montar(grillaBo({ vehiculos: [], avance: { vehiculos: 0, terminados: 0 } }));
    expect(await screen.findByText(/no tiene vehículos/i)).toBeInTheDocument();
  });

  it("403 de la API lleva a la página 403", async () => {
    simularApi({ [RUTA_GRILLA]: () => new Response(null, { status: 403 }) });
    render(<GrillaBackoffice diagnosticoId={LOTE} empresa={EMPRESA_A} />);
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith(expect.stringMatching(/^\/403/)));
  });
});

describe("GrillaBackoffice — HU #14087 AC6: la misma tabla por pilares que la compañía", () => {
  it("cabecera por pilar, columnas de vehículo y documentos, y las 8 fuentes en cada fila", async () => {
    const { container } = montar();
    const t = await tabla();
    expect(within(t).getByRole("columnheader", { name: "RUNT" })).toHaveAttribute("colspan", "3");
    expect(within(t).getByRole("columnheader", { name: "SIMIT" })).toHaveAttribute("colspan", "3");
    for (const nombre of ["Placa o VIN", "Documento declarado", "Documento del propietario"]) expect(within(t).getByRole("columnheader", { name: nombre })).toBeInTheDocument();
    expect(container.querySelector('[data-vehiculo="vehiculo-1"]')!.querySelectorAll("[data-fuente]")).toHaveLength(8);
  });
});

describe("GrillaBackoffice — marca de origen manual solo en esta grilla", () => {
  it("la celda de origen MANUAL lleva el texto «Manual»; la de API no", async () => {
    const { container } = montar();
    await tabla();
    expect(within(td(container, "celda-2-simit_vehiculo")).getByText("Manual")).toBeInTheDocument();
    expect(within(td(container, "celda-1-runt")).queryByText("Manual")).toBeNull();
  });

  it("la grilla de la compañía con la misma celda no muestra ninguna marca manual", () => {
    render(<GrillaSemaforos grilla={grillaBo() as unknown as Grilla} puedeReprocesar={false} />);
    expect(screen.getByRole("table")).not.toHaveTextContent(/manual/i);
  });
});

describe("GrillaBackoffice — «Llenar a mano» solo en celdas en ERROR no bloqueadas de fuentes llenables", () => {
  it("SIMIT vehículo en ERROR: la celda trae «Llenar a mano» y no pide el detalle de la compañía", async () => {
    const { fetchMock, container } = montar();
    await tabla();
    const boton = within(td(container, "celda-1-simit_vehiculo")).getByRole("button", { name: "Llenar a mano SIMIT vehículo de TST001" });
    expect(boton).toHaveTextContent("Llenar a mano");
    expect(fetchMock.mock.calls.some(([u]) => String(u).includes("/vehiculos/"))).toBe(false);
  });

  it("HU #14087: el semáforo abre el detalle del backoffice en una pestaña nueva, con ?empresa=", async () => {
    montar();
    await tabla();
    const enlace = semaforo("SIMIT vehículo", "TST001");
    expect(enlace).toHaveAttribute("href", `/backoffice/diagnosticos/${LOTE}/vehiculos/vehiculo-1/consultas/SIMIT_VEHICULO?empresa=${EMPRESA_A}`);
    expect(enlace).toHaveAttribute("target", "_blank");
    expect(enlace).toHaveAttribute("rel", "noopener noreferrer");
    expect(enlace).toHaveAccessibleName(/\(se abre en una pestaña nueva\)$/);
    fireEvent.click(enlace);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it.each([
    ["SIMIT propietario", "TST002", "bloqueada por RUNT"],
    ["RUNT vehículo", "TST002", "RUNT (Q3)"],
    ["RUNT vehículo", "TST001", "completada"],
    ["SIMIT vehículo", "TST002", "ya llenada a mano"],
  ])("%s de %s (%s): sin «Llenar a mano»", async (fuente, placa) => {
    montar();
    await tabla();
    expect(llenar(fuente, placa)).toBeNull();
  });

  it("sin la capacidad llenarConsultas no hay «Llenar a mano» en ninguna celda", async () => {
    h.capacidades = { backoffice: true, llenarConsultas: false };
    montar();
    await tabla();
    expect(screen.queryAllByRole("button", { name: /^Llenar a mano/ })).toHaveLength(0);
  });
});

describe("GrillaBackoffice — AC2: llenar SIMIT vehículo y ver la celda en verde con la marca manual", () => {
  it("al confirmar, la celda pasa a «Ejecutado (OK)» con «Manual» y el lote se vuelve a leer desde su versión", async () => {
    const llena = celdaBo(1, "SIMIT_VEHICULO", "COMPLETADO", { origen: "MANUAL", usuario_manual_id: "u", version: 21 });
    const cambios = grillaBo({ version: 21, estado: "EN_PROCESO", vehiculos: [vehiculoBo(1, [llena])] });
    let lecturas = 0;
    const { fetchMock, container } = montar(grillaBo(), {
      [RUTA_GRILLA]: () => Response.json(++lecturas === 1 ? grillaBo() : cambios),
      "GET /backoffice/consultas/celda-1-simit_vehiculo/manual": () =>
        Response.json({ id: "celda-1-simit_vehiculo", diagnostico_id: LOTE, vehiculo_id: "vehiculo-1", fuente: "SIMIT_VEHICULO", estado: "ERROR", tipo_error: "TIMEOUT", version: 10, admite_manual: true, motivo: null, personas: null }),
      "PUT /backoffice/consultas/celda-1-simit_vehiculo/manual": () => Response.json(respuestaLlenado("SIMIT_VEHICULO")),
    });

    await tabla();
    fireEvent.click(llenar("SIMIT vehículo", "TST001")!);
    const form = await screen.findByRole("dialog", { name: /llenar a mano/i });
    fireEvent.change(await within(form).findByLabelText("Semáforo"), { target: { value: "VERDE" } });
    fireEvent.click(within(form).getByRole("button", { name: "Confirmar llenado" }));

    await waitFor(() => expect(td(container, "celda-1-simit_vehiculo")).toHaveAttribute("data-estado", "COMPLETADO"));
    const celda = td(container, "celda-1-simit_vehiculo");
    expect(within(celda).getByText("Ejecutado (OK)")).toBeInTheDocument();
    expect(within(celda).getByText("Manual")).toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: /llenar a mano/i })).toBeNull();
    expect(screen.getByText(/se llenó a mano SIMIT vehículo de TST001/i)).toBeInTheDocument();
    const desdes = llamadas(fetchMock, "GET", `/backoffice/diagnosticos/${LOTE}/grilla`).map(([u]) => new URL(String(u), "http://localhost").searchParams.get("desde"));
    expect(desdes).toEqual(["0", "20"]);
  });

  it("AC5: un valor fuera del esquema en el avalúo deja la celda en ERROR", async () => {
    const { fetchMock, container } = montar(grillaBo(), {
      "GET /backoffice/consultas/celda-1-avaluo/manual": () =>
        Response.json({ id: "celda-1-avaluo", diagnostico_id: LOTE, vehiculo_id: "vehiculo-1", fuente: "AVALUO", estado: "ERROR", tipo_error: "HTTP_5XX", version: 10, admite_manual: true, motivo: null, personas: null }),
    });
    await tabla();
    fireEvent.click(llenar("Avalúo comercial", "TST001")!);
    const form = await screen.findByRole("dialog", { name: /llenar a mano/i });
    fireEvent.change(await within(form).findByLabelText("Valor sugerido (pesos)"), { target: { value: "-5" } });
    fireEvent.click(within(form).getByRole("button", { name: "Confirmar llenado" }));

    await waitFor(() => expect(within(form).getByLabelText("Valor sugerido (pesos)")).toHaveAttribute("aria-invalid", "true"));
    expect(llamadas(fetchMock, "PUT", "/backoffice/consultas/celda-1-avaluo/manual")).toHaveLength(0);
    expect(td(container, "celda-1-avaluo")).toHaveAttribute("data-estado", "ERROR");
  });
});

describe("Backoffice — HU #14087: página del detalle en pestaña nueva", () => {
  async function abrirPagina(vehiculoId: string, fuente: string, g: TGrilla = grillaBo()) {
    const fetchMock = simularApi({ [RUTA_GRILLA]: () => Response.json(g) });
    const r = render(await PaginaConsultaBackoffice({ params: Promise.resolve({ id: LOTE, vehiculoId, fuente }), searchParams: Promise.resolve({ empresa: EMPRESA_A }) }));
    return { fetchMock, ...r };
  }

  it("lee con el cargador del backoffice (?empresa=) y titula con la fuente y la placa", async () => {
    const { fetchMock } = await abrirPagina("vehiculo-1", "SIMIT_VEHICULO");
    expect(await screen.findByRole("heading", { level: 1, name: "SIMIT vehículo · TST001" })).toBeInTheDocument();
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://localhost");
    expect(url.pathname).toMatch(new RegExp(`/backoffice/diagnosticos/${LOTE}/grilla$`));
    expect(url.searchParams.get("empresa")).toBe(EMPRESA_A);
    expect(screen.getByRole("region", { name: "Auditoría de consulta" })).toHaveTextContent("Error");
  });

  it("migas y «Volver al diagnóstico» se quedan en el backoffice con la compañía", async () => {
    await abrirPagina("vehiculo-1", "RUNT");
    await screen.findByRole("heading", { level: 1, name: "RUNT vehículo · TST001" });
    const migas = screen.getByRole("navigation", { name: "Migas de pan" });
    expect(within(migas).getByRole("link", { name: "Diagnósticos" })).toHaveAttribute("href", `/backoffice/diagnosticos?empresa=${EMPRESA_A}`);
    expect(within(migas).getByRole("link", { name: "Lote octubre" })).toHaveAttribute("href", `/backoffice/diagnosticos/${LOTE}?empresa=${EMPRESA_A}`);
    expect(screen.getByRole("link", { name: "Volver al diagnóstico" })).toHaveAttribute("href", `/backoffice/diagnosticos/${LOTE}?empresa=${EMPRESA_A}`);
  });

  it("el indicador de origen MANUAL sale solo aquí, en el backoffice", async () => {
    await abrirPagina("vehiculo-2", "SIMIT_VEHICULO");
    await screen.findByRole("heading", { level: 1, name: "SIMIT vehículo · TST002" });
    expect(screen.getByText(/llenado a mano desde el backoffice/i)).toBeInTheDocument();
  });

  it("celda que no está en la grilla: no disponible", async () => {
    await abrirPagina("vehiculo-9", "RUNT");
    expect(await screen.findByText(/este detalle no está disponible/i)).toBeInTheDocument();
  });

  it("403 de la API lleva a la página 403 del backoffice", async () => {
    simularApi({ [RUTA_GRILLA]: () => new Response(null, { status: 403 }) });
    render(await PaginaConsultaBackoffice({ params: Promise.resolve({ id: LOTE, vehiculoId: "vehiculo-1", fuente: "RUNT" }), searchParams: Promise.resolve({ empresa: EMPRESA_A }) }));
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO_BACKOFFICE"));
  });
});
