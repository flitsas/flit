import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StrictMode } from "react";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { GrillaBackoffice } from "@/components/backoffice/GrillaBackoffice";
import { GrillaSemaforos } from "@/components/tablero/GrillaSemaforos";
import PaginaConsultaBackoffice from "@/app/(app)/backoffice/diagnosticos/[id]/vehiculos/[vehiculoId]/consultas/[fuente]/page";
import { sesion } from "@/__tests__/configuracion/datos";
import type { GrillaBackoffice as TGrilla } from "@/lib/backoffice/api";
import type { Grilla } from "@/lib/tablero/api";
import { TABLA_MOVIL_TABLE_CLS } from "@flit/ui/table-styles";
import { EMPRESA_A, LOTE, celdaBo, grillaBo, llamadas, respuestaLlenado, simularApi, vehiculoBo } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: <GrillaBackoffice diagnosticoId={id} empresa={empresa} /> en /backoffice/diagnosticos/{id}?empresa= pide
// GET /backoffice/diagnosticos/{id}/grilla?empresa=&desde=0 y reutiliza GrillaSemaforos con la marca «Manual» en las celdas de
// origen MANUAL (solo aquí, Q5). Cada semáforo abre en una pestaña nueva /backoffice/diagnosticos/{id}/vehiculos/{vid}/
// consultas/{fuente}?empresa= (HU #14087); en las celdas en ERROR llenables, con la capacidad llenarConsultas, la celda trae
// «Llenar a mano», que abre FormularioLlenadoManual como modal.

// Perfil FLIT: el llenado manual exige además `backofficeOperacion` (ola 2026-11-11, punto 1; el caso sin ella está en
// OperacionBackoffice.test.tsx).
const h = vi.hoisted(() => ({ capacidades: { backoffice: true, backofficeOperacion: true, llenarConsultas: true } as Record<string, boolean>, replace: vi.fn() }));
vi.mock("@/components/DiagnosticoShell", () => ({ useSesion: () => ({ sesion: sesion(h.capacidades), cargando: false, error: null, recargar: () => {} }) }));
vi.mock("next/navigation", () => ({ usePathname: () => "/backoffice/diagnosticos", useRouter: () => ({ replace: h.replace, push: vi.fn(), refresh: vi.fn() }) }));

beforeEach(() => {
  h.capacidades = { backoffice: true, backofficeOperacion: true, llenarConsultas: true };
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
/** Abre el menú «Acciones» de la fila (HU #14584 §6.4) y devuelve su disparador. */
function abrirMenu(placa: string) {
  const disparador = screen.getByRole("button", { name: `Acciones de la placa ${placa}` });
  disparador.focus();
  fireEvent.click(disparador);
  return disparador;
}
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
  it("cabecera por pilar, Placa o VIN sin columnas de documento (HU #14585), «Acciones» al final y las 8 fuentes en cada fila", async () => {
    const { container } = montar();
    const t = await tabla();
    // Una columna por sujeto (dos badges RUNT/SIMIT), sin las cabeceras de grupo «RUNT» y «SIMIT».
    for (const sujeto of ["Vehículo", "Propietario", "Locatario"]) expect(within(t).getByRole("columnheader", { name: sujeto })).toBeInTheDocument();
    expect(within(t).queryByRole("columnheader", { name: /^(RUNT|SIMIT)$/ })).toBeNull();
    expect(within(t).getByRole("columnheader", { name: "Placa o VIN" })).toBeInTheDocument();
    expect(within(t).getByRole("columnheader", { name: "Acciones" })).toBeInTheDocument();
    expect(within(t).queryByRole("columnheader", { name: /documento/i })).toBeNull();
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
  it("al confirmar, la celda pasa a «Ejecutado» con «Manual» y el lote se vuelve a leer desde su versión", async () => {
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
    expect(within(celda).getByText("SIMIT · Ejecutado")).toBeInTheDocument();
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

describe("HU14562 AC47 y D6 en la grilla del backoffice", () => {
  it("D6: el vehículo con dictamen ajustado lleva la marca «Ajustado por FLIT»", async () => {
    const g = grillaBo();
    g.vehiculos[0] = { ...g.vehiculos[0], dictamen_ajustado: true };
    montar(g);
    await tabla();
    expect(screen.getAllByText("Ajustado por FLIT")).toHaveLength(1);
  });

  it("AC47: con verAuditoria, «Auditoría del dictamen de PLACA» abre el panel con las versiones", async () => {
    h.capacidades = { ...h.capacidades, verAuditoria: true };
    montar(grillaBo(), {
      [`GET /backoffice/diagnosticos/${LOTE}/vehiculos/vehiculo-1/dictamen/versiones`]: () => Response.json({ versiones: [] }),
    });
    await tabla();
    abrirMenu("TST001");
    fireEvent.click(screen.getByRole("menuitem", { name: "Auditoría del dictamen de TST001" }));
    const panel = await screen.findByRole("dialog", { name: "Auditoría del dictamen · TST001" });
    expect(await within(panel).findByText("Este vehículo aún no tiene dictamen.")).toBeInTheDocument();
    // FE-1: modal de la suite de 640 px; sin campos, el velo cierra como el panel.
    expect(panel.firstElementChild?.className).toContain("max-w-[640px]");
    fireEvent.mouseDown(panel);
    expect(screen.queryByRole("dialog", { name: "Auditoría del dictamen · TST001" })).toBeNull();
  });

  it("FE-1: Escape en «Auditoría del dictamen» devuelve el foco al disparador «Acciones» también con StrictMode (next dev)", async () => {
    h.capacidades = { ...h.capacidades, verAuditoria: true };
    simularApi({
      [RUTA_GRILLA]: () => Response.json(grillaBo()),
      [`GET /backoffice/diagnosticos/${LOTE}/vehiculos/vehiculo-1/dictamen/versiones`]: () => Response.json({ versiones: [] }),
    });
    render(
      <StrictMode>
        <GrillaBackoffice diagnosticoId={LOTE} empresa={EMPRESA_A} />
      </StrictMode>,
    );
    await tabla();
    const disparador = abrirMenu("TST001");
    fireEvent.click(screen.getByRole("menuitem", { name: "Auditoría del dictamen de TST001" }));
    const panel = await screen.findByRole("dialog", { name: "Auditoría del dictamen · TST001" });
    await within(panel).findByText("Este vehículo aún no tiene dictamen.");
    expect(panel.contains(document.activeElement)).toBe(true);
    fireEvent.keyDown(document, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(document.activeElement).toBe(disparador);
  });

  it("AC47: sin verAuditoria no se ofrece la auditoría", async () => {
    montar();
    await tabla();
    abrirMenu("TST001");
    expect(screen.queryByRole("menuitem", { name: /^Auditoría del dictamen/ })).toBeNull();
    expect(screen.queryByRole("button", { name: /^Auditoría del dictamen/ })).toBeNull();
  });
});

describe("GrillaBackoffice — HU #14584 §6.4 (A6.6): Dictamen y Auditoría en el menú «Acciones»", () => {
  it("la celda de la placa no tiene botones de Dictamen ni Auditoría; quedan solo las marcas", async () => {
    h.capacidades = { ...h.capacidades, verAuditoria: true };
    const { container } = montar();
    await tabla();
    const placa = within(container.querySelector<HTMLElement>('[data-vehiculo="vehiculo-1"]')!).getByRole("rowheader");
    expect(within(placa).queryByRole("button")).toBeNull();
  });

  it("«Ver dictamen de PLACA» en el menú abre el panel del dictamen y la auditoría solo con verAuditoria", async () => {
    h.capacidades = { ...h.capacidades, verAuditoria: true };
    montar(grillaBo(), {
      [`GET /backoffice/diagnosticos/${LOTE}/vehiculos/vehiculo-1/dictamen`]: () => Response.json({ code: "SIN_DICTAMEN", error: "Sin dictamen" }, { status: 404 }),
    });
    await tabla();
    abrirMenu("TST001");
    const items = screen.getAllByRole("menuitem").map((i) => i.getAttribute("aria-label") ?? i.textContent);
    expect(items).toEqual(["Ver dictamen de TST001", "Auditoría del dictamen de TST001"]);
    fireEvent.click(screen.getByRole("menuitem", { name: "Ver dictamen de TST001" }));
    expect(await screen.findByRole("dialog", { name: "Dictamen · TST001" })).toBeInTheDocument();
  });

  it("el ícono de cambio de propietario va junto a la placa también en el backoffice", async () => {
    const g = grillaBo();
    g.vehiculos[0] = { ...g.vehiculos[0], cambio_propietario: true };
    montar(g);
    await tabla();
    expect(screen.getByRole("button", { name: "Cambio de propietario" })).toBeInTheDocument();
  });

  it("decisión del orquestador: la misma barra de filtros que el cliente (fuente + estado y Limpiar filtros)", async () => {
    montar();
    await tabla();
    expect(screen.getByLabelText("Buscar por placa o VIN")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Fuente" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Estado" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Limpiar filtros" })).toBeDisabled();
  });

  it("CR-3: «Limpiar filtros» del vacío por filtro restablece la grilla y lleva el foco al buscador", async () => {
    montar();
    await tabla();
    fireEvent.change(screen.getByLabelText("Buscar por placa o VIN"), { target: { value: "ZZZ999" } });
    expect(await screen.findByText("Ningún vehículo coincide")).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole("button", { name: "Limpiar filtros" }).at(-1)!);
    expect(screen.getByLabelText("Buscar por placa o VIN")).toHaveValue("");
    expect(screen.getByLabelText("Buscar por placa o VIN")).toHaveFocus();
    expect(await tabla()).toBeInTheDocument();
  });
});

// HU #14562 AC56 (auditoría B2/M6): la grilla del backoffice es la misma GrillaSemaforos, así que pasa a tarjetas bajo 900 px
// y no deja columnas fijas allí.
describe("GrillaBackoffice — HU #14562 AC56: tarjetas por debajo de 900 px", () => {
  it("la tabla lleva las recetas TABLA_MOVIL_* y la placa no queda fija bajo 900 px", async () => {
    montar();
    const t = await tabla();
    const clases = (el: Element) => (el.getAttribute("class") ?? "").split(/\s+/);
    for (const c of TABLA_MOVIL_TABLE_CLS.split(" ")) expect(clases(t)).toContain(c);
    for (const placa of within(t).getAllByRole("rowheader")) {
      expect(clases(placa)).not.toContain("sticky");
      expect(placa).toHaveAttribute("data-label", "Placa o VIN");
    }
  });
});

describe("GrillaBackoffice — HU #14577 AC38: etiqueta del saneamiento con enlace a la gestión", () => {
  const conSaneamiento = (estado: string) => {
    const g = grillaBo();
    const [v1, v2] = g.vehiculos;
    return grillaBo({ vehiculos: [{ ...v1, saneamiento: { estado: estado as never, id: "00000000-0000-7000-8000-00000000d001" } }, { ...v2, saneamiento: null }] });
  };

  it("con gestionarSaneamiento, la fila con saneamiento muestra la etiqueta enlazada a PS-5 con ?empresa=", async () => {
    h.capacidades = { ...h.capacidades, gestionarSaneamiento: true };
    montar(conSaneamiento("EN_GESTION"));
    await tabla();
    const enlace = screen.getByRole("link", { name: "Gestionar saneamiento de TST001: En saneamiento" });
    expect(enlace).toHaveAttribute("href", `/backoffice/saneamientos/00000000-0000-7000-8000-00000000d001?empresa=${EMPRESA_A}`);
    expect(enlace).toHaveTextContent("En saneamiento");
    expect(screen.queryAllByRole("link", { name: /^Gestionar saneamiento de TST002/ })).toHaveLength(0);
  });

  it("sin gestionarSaneamiento, la etiqueta sin enlace", async () => {
    montar(conSaneamiento("SOLICITADO"));
    await tabla();
    expect(screen.getByText("Saneamiento solicitado")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /^Gestionar saneamiento/ })).toBeNull();
  });

  it("el backoffice no ofrece la bandera para marcar", async () => {
    h.capacidades = { ...h.capacidades, gestionarSaneamiento: true, sanear: true };
    montar(conSaneamiento("SANEADO"));
    await tabla();
    expect(screen.queryByRole("button", { name: /Solicitar saneamiento|Sanear/ })).toBeNull();
  });
});

// AP-8 (security-fase3 SF-6): ni la carga ni la relectura muestran el mensaje del servidor.
const SECRETO_AP8 = "Npgsql detalle interno 23514 en diagnostico.consultas";

describe("GrillaBackoffice — AP-8: textos fijos", () => {
  it("400 al cargar: aviso con texto fijo, sin el mensaje de la API", async () => {
    simularApi({ [RUTA_GRILLA]: () => Response.json({ code: "OTRO_ERROR", error: SECRETO_AP8 }, { status: 400 }) });
    render(<GrillaBackoffice diagnosticoId={LOTE} empresa={EMPRESA_A} />);
    const aviso = await screen.findByRole("alert");
    expect(aviso).toHaveTextContent("Intenta de nuevo en un momento.");
    expect(aviso).not.toHaveTextContent(SECRETO_AP8);
  });

  it("falla al volver a leer con «Actualizar»: aviso con texto fijo, sin el mensaje de la API", async () => {
    let n = 0;
    simularApi({
      [RUTA_GRILLA]: () => (++n === 1 ? Response.json(grillaBo()) : Response.json({ code: "OTRO_ERROR", error: SECRETO_AP8 }, { status: 400 })),
    });
    render(<GrillaBackoffice diagnosticoId={LOTE} empresa={EMPRESA_A} />);
    await tabla();
    fireEvent.click(screen.getByRole("button", { name: "Actualizar" }));
    expect(await screen.findByText("No se pudo volver a leer la grilla. Intenta de nuevo en un momento.")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(SECRETO_AP8);
  });
});
