import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { GrillaBackoffice } from "@/components/backoffice/GrillaBackoffice";
import { GrillaSemaforos } from "@/components/tablero/GrillaSemaforos";
import { sesion } from "@/__tests__/configuracion/datos";
import type { GrillaBackoffice as TGrilla } from "@/lib/backoffice/api";
import type { Grilla } from "@/lib/tablero/api";
import { EMPRESA_A, LOTE, celdaBo, grillaBo, llamadas, respuestaLlenado, simularApi, vehiculoBo } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: <GrillaBackoffice diagnosticoId={id} empresa={empresa} /> en /backoffice/diagnosticos/{id}?empresa= pide
// GET /backoffice/diagnosticos/{id}/grilla?empresa=&desde=0 y reutiliza GrillaSemaforos con la marca «Manual» en las celdas de
// origen MANUAL (solo aquí, Q5). Cada semáforo abre DetalleCelda; en las celdas en ERROR llenables, con la capacidad
// llenarConsultas, el pie trae «Llenar a mano», que abre FormularioLlenadoManual.

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
const semaforo = (fuente: string, placa: string) => screen.getByRole("button", { name: new RegExp(`^Ver detalle de ${fuente} de ${placa}`) });

async function abrirDetalle(fuente: string, placa: string) {
  await tabla();
  fireEvent.click(semaforo(fuente, placa));
  return screen.findByRole("dialog");
}

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
  it("SIMIT vehículo en ERROR: el detalle trae «Llenar a mano» y no pide el detalle de la compañía", async () => {
    const { fetchMock } = montar();
    const d = await abrirDetalle("SIMIT vehículo", "TST001");
    expect(within(d).getByRole("button", { name: "Llenar a mano" })).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([u]) => String(u).includes("/vehiculos/"))).toBe(false);
  });

  it.each([
    ["SIMIT propietario", "TST002", "bloqueada por RUNT"],
    ["RUNT vehículo", "TST002", "RUNT (Q3)"],
    ["RUNT vehículo", "TST001", "completada"],
    ["SIMIT vehículo", "TST002", "ya llenada a mano"],
  ])("%s de %s (%s): sin «Llenar a mano»", async (fuente, placa) => {
    montar();
    const d = await abrirDetalle(fuente, placa);
    expect(within(d).queryByRole("button", { name: "Llenar a mano" })).toBeNull();
  });

  it("sin la capacidad llenarConsultas no hay «Llenar a mano» en ninguna celda", async () => {
    h.capacidades = { backoffice: true, llenarConsultas: false };
    montar();
    const d = await abrirDetalle("SIMIT vehículo", "TST001");
    expect(within(d).queryByRole("button", { name: "Llenar a mano" })).toBeNull();
  });
});

describe("GrillaBackoffice — AC2: llenar SIMIT vehículo y ver la celda en verde con la marca manual", () => {
  it("al confirmar, la celda pasa a Completada con «Manual» y el lote se vuelve a leer desde su versión", async () => {
    const llena = celdaBo(1, "SIMIT_VEHICULO", "COMPLETADO", { origen: "MANUAL", usuario_manual_id: "u", version: 21 });
    const cambios = grillaBo({ version: 21, estado: "EN_PROCESO", vehiculos: [vehiculoBo(1, [llena])] });
    let lecturas = 0;
    const { fetchMock, container } = montar(grillaBo(), {
      [RUTA_GRILLA]: () => Response.json(++lecturas === 1 ? grillaBo() : cambios),
      "GET /backoffice/consultas/celda-1-simit_vehiculo/manual": () =>
        Response.json({ id: "celda-1-simit_vehiculo", diagnostico_id: LOTE, vehiculo_id: "vehiculo-1", fuente: "SIMIT_VEHICULO", estado: "ERROR", tipo_error: "TIMEOUT", version: 10, admite_manual: true, motivo: null, personas: null }),
      "PUT /backoffice/consultas/celda-1-simit_vehiculo/manual": () => Response.json(respuestaLlenado("SIMIT_VEHICULO")),
    });

    const d = await abrirDetalle("SIMIT vehículo", "TST001");
    fireEvent.click(within(d).getByRole("button", { name: "Llenar a mano" }));
    const form = await screen.findByRole("dialog", { name: /llenar a mano/i });
    fireEvent.change(await within(form).findByLabelText("Semáforo"), { target: { value: "VERDE" } });
    fireEvent.click(within(form).getByRole("button", { name: "Confirmar llenado" }));

    await waitFor(() => expect(td(container, "celda-1-simit_vehiculo")).toHaveAttribute("data-estado", "COMPLETADO"));
    const celda = td(container, "celda-1-simit_vehiculo");
    expect(within(celda).getByText("Completada")).toBeInTheDocument();
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
    const d = await abrirDetalle("Avalúo comercial", "TST001");
    fireEvent.click(within(d).getByRole("button", { name: "Llenar a mano" }));
    const form = await screen.findByRole("dialog", { name: /llenar a mano/i });
    fireEvent.change(await within(form).findByLabelText("Valor sugerido (pesos)"), { target: { value: "-5" } });
    fireEvent.click(within(form).getByRole("button", { name: "Confirmar llenado" }));

    await waitFor(() => expect(within(form).getByLabelText("Valor sugerido (pesos)")).toHaveAttribute("aria-invalid", "true"));
    expect(llamadas(fetchMock, "PUT", "/backoffice/consultas/celda-1-avaluo/manual")).toHaveLength(0);
    expect(td(container, "celda-1-avaluo")).toHaveAttribute("data-estado", "ERROR");
  });
});
