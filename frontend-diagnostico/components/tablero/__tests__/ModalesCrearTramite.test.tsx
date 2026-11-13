import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import TableroDiagnostico from "@/app/(app)/diagnosticos/[id]/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { EMPRESA } from "@/__tests__/configuracion/datos";
import { ID, V1, grilla, llamadas, sesion, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: en el tablero /diagnosticos/{id}, la fila elegible ofrece «Crear trámite» (HU #14562 AC37, spec 05).
// En un lote Simple, el modal «Crear trámite para {placa}» pide el tipo y continúa con «Ir al wizard». En un lote Con
// análisis IA y veredicto Viable, el modal «¿Crear el {trámite} de {placa}?» confirma con «Crear {trámite}». En los dos, el
// borrador se crea y se abre en Trámites en una pestaña nueva; la fila pasa a «Ver trámite #N».

const navegacion = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => navegacion }));

const TRAMITES_URL = "https://tramites.prueba";
const DERIVA: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, derivar: true };
const GRILLA = `GET /diagnosticos/${ID}/grilla`;
const RUTA_CREAR = `/diagnosticos/${ID}/vehiculos/vehiculo-1/tramite`;
const CREAR = `POST ${RUTA_CREAR}`;
const TRAMITE_ID = "00000000-0000-7000-8000-00000000a001";
const URL_TRAMITE = `${TRAMITES_URL}/tramites/${TRAMITE_ID}?t=${EMPRESA}`;
const TIPOS = {
  tipos: [
    { codigo: "TRASPASO", nombre: "Traspaso", familia: "TRASPASO", fuentesObligatorias: [] },
    { codigo: "UNILATERAL", nombre: "Traspaso unilateral", familia: "TRASPASO", fuentesObligatorias: [] },
  ],
};

const avanzar = (ms = 0) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

type Rutas = Parameters<typeof simularApi>[0];

async function montar(rutas: Rutas) {
  const api = simularApi({ "GET /sesion": () => Response.json(sesion(DERIVA)), ...rutas });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl={TRAMITES_URL}>
      {await TableroDiagnostico({ params: Promise.resolve({ id: ID }), searchParams: Promise.resolve({}) })}
    </DiagnosticoShell>,
  );
  await avanzar();
  await avanzar();
  return api;
}

const FILA: FilaVehiculo = { ...V1, elegible_tramite: true, motivo_no_elegible: null, tramite: null };

function loteSimple(): Grilla {
  return grilla({
    estado: "COMPLETADO",
    modo: "SIMPLE",
    tipo_tramite_codigo: null,
    lote: { modo: "SIMPLE", tipo_tramite: null, creado_en: "2026-11-12T15:00:00Z", es_express: false, usuario_creador: { id: "u1", nombre: null } },
    vehiculos: [FILA],
  } as Partial<Grilla>);
}

function loteIa(): Grilla {
  return grilla({
    estado: "COMPLETADO",
    modo: "CON_ANALISIS",
    lote: {
      modo: "CON_ANALISIS",
      tipo_tramite: { codigo: "TRASPASO_STANDARD", nombre: "Traspaso" },
      creado_en: "2026-11-12T15:00:00Z",
      es_express: false,
      usuario_creador: { id: "u1", nombre: null },
    },
    vehiculos: [{ ...FILA, veredicto: "VIABLE" }],
  } as Partial<Grilla>);
}

const creado = (numero = "57") => () => Response.json({ tramite: { id: TRAMITE_ID, numero }, ya_existia: false }, { status: 201 });
const tabla = () => screen.getByRole("table", { name: /semáforo de consultas/i });
const botonFila = () => within(tabla()).getByRole("button", { name: "Crear trámite para la placa TST001" });

/** Pestaña que abre el navegador: la URL del borrador se le asigna cuando el POST responde. */
function pestanaFalsa() {
  const pestana = { opener: {} as unknown, location: { href: "" }, close: vi.fn() };
  const abrir = vi.fn(() => pestana as unknown as Window);
  vi.stubGlobal("open", abrir);
  return { pestana, abrir };
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
  navegacion.push.mockReset();
  navegacion.replace.mockReset();
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("HU #14562 AC37 — lote Simple: «Crear trámite para {placa}» pide el tipo y continúa con «Ir al wizard»", () => {
  it("el modal lleva el título con la placa, el tipo, el aviso del wizard, Cancelar e «Ir al wizard» (deshabilitado sin tipo)", async () => {
    const api = await montar({ [GRILLA]: () => Response.json(loteSimple()), "GET /tipos-tramite": () => Response.json(TIPOS) });
    fireEvent.click(botonFila());
    await avanzar();
    const dialogo = screen.getByRole("dialog", { name: "Crear trámite para TST001" });
    expect(within(dialogo).getByLabelText("Tipo de trámite")).toBeInTheDocument();
    expect(within(dialogo).getByText(/abriremos el wizard de trámites en una pestaña nueva/i)).toBeInTheDocument();
    expect(within(dialogo).getByRole("button", { name: "Cancelar" })).toBeEnabled();
    expect(within(dialogo).getByRole("button", { name: "Ir al wizard" })).toBeDisabled();
    expect(llamadas(api, "POST", RUTA_CREAR)).toHaveLength(0);
  });

  it("«Ir al wizard» crea el borrador con el tipo elegido y lo abre en una pestaña nueva, sin opener", async () => {
    const { pestana, abrir } = pestanaFalsa();
    const api = await montar({ [GRILLA]: () => Response.json(loteSimple()), "GET /tipos-tramite": () => Response.json(TIPOS), [CREAR]: creado("58") });
    fireEvent.click(botonFila());
    await avanzar();
    const dialogo = screen.getByRole("dialog", { name: "Crear trámite para TST001" });
    fireEvent.change(within(dialogo).getByLabelText("Tipo de trámite"), { target: { value: "UNILATERAL" } });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Ir al wizard" }));
    // La pestaña se abre en el mismo clic (si no, el navegador la bloquea) y recibe la URL cuando el POST responde.
    expect(abrir).toHaveBeenCalledTimes(1);
    expect(pestana.opener).toBeNull();
    await avanzar();
    const [, init] = llamadas(api, "POST", RUTA_CREAR)[0] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ tipo_tramite_codigo: "UNILATERAL" });
    expect(pestana.location.href).toBe(URL_TRAMITE);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(within(tabla()).getByRole("link", { name: /ver trámite #58 de la placa TST001/i })).toHaveAttribute("href", URL_TRAMITE);
  });

  it("Cancelar cierra el modal sin crear nada ni abrir pestañas y devuelve el foco al botón de la fila", async () => {
    const { abrir } = pestanaFalsa();
    const api = await montar({ [GRILLA]: () => Response.json(loteSimple()), "GET /tipos-tramite": () => Response.json(TIPOS) });
    fireEvent.click(botonFila());
    await avanzar();
    fireEvent.click(within(screen.getByRole("dialog", { name: "Crear trámite para TST001" })).getByRole("button", { name: "Cancelar" }));
    await avanzar();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(abrir).not.toHaveBeenCalled();
    expect(llamadas(api, "POST", RUTA_CREAR)).toHaveLength(0);
    expect(botonFila()).toHaveFocus();
  });
});

describe("HU #14562 AC37 — lote IA con veredicto Viable: «Crear traspaso» abre el trámite en una pestaña nueva", () => {
  it("el modal «¿Crear el traspaso de TST001?» muestra Viable para Traspaso, el aviso de la pestaña nueva y no crea nada aún", async () => {
    const api = await montar({ [GRILLA]: () => Response.json(loteIa()) });
    fireEvent.click(botonFila());
    await avanzar();
    const dialogo = screen.getByRole("dialog", { name: "¿Crear el traspaso de TST001?" });
    expect(within(dialogo).getByText("Viable")).toBeInTheDocument();
    expect(within(dialogo).getByText("para Traspaso")).toBeInTheDocument();
    expect(within(dialogo).getByText(/se abre en una pestaña nueva/i)).toBeInTheDocument();
    expect(within(dialogo).getByRole("button", { name: "Cancelar" })).toBeEnabled();
    expect(within(dialogo).getByRole("button", { name: "Crear traspaso" })).toBeEnabled();
    expect(llamadas(api, "POST", RUTA_CREAR)).toHaveLength(0);
  });

  it("«Crear traspaso» crea el borrador con el tipo del lote (sin pedirlo) y lo abre en una pestaña nueva", async () => {
    const { pestana, abrir } = pestanaFalsa();
    const api = await montar({ [GRILLA]: () => Response.json(loteIa()), [CREAR]: creado("57") });
    fireEvent.click(botonFila());
    await avanzar();
    fireEvent.click(within(screen.getByRole("dialog", { name: "¿Crear el traspaso de TST001?" })).getByRole("button", { name: "Crear traspaso" }));
    expect(abrir).toHaveBeenCalledTimes(1);
    await avanzar();
    const cuerpo = (llamadas(api, "POST", RUTA_CREAR)[0][1] as RequestInit).body;
    expect(cuerpo === undefined || JSON.parse(String(cuerpo)).tipo_tramite_codigo === undefined).toBe(true);
    expect(pestana.location.href).toBe(URL_TRAMITE);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(within(tabla()).getByRole("link", { name: /ver trámite #57 de la placa TST001/i })).toBeInTheDocument();
  });

  it("si la API rechaza la creación, cierra la pestaña vacía y deja el motivo en la fila", async () => {
    const { pestana } = pestanaFalsa();
    await montar({
      [GRILLA]: () => Response.json(loteIa()),
      [CREAR]: () => Response.json({ code: "NO_ELEGIBLE", title: "No elegible", detail: "OT no habilitado para la compañía" }, { status: 422 }),
    });
    fireEvent.click(botonFila());
    await avanzar();
    fireEvent.click(within(screen.getByRole("dialog", { name: "¿Crear el traspaso de TST001?" })).getByRole("button", { name: "Crear traspaso" }));
    await avanzar();
    expect(pestana.close).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("alert", { name: /resultado de crear trámite para la placa TST001/i })).toHaveTextContent("OT no habilitado para la compañía");
  });

  it("con la pestaña bloqueada por el navegador, el borrador se crea igual y la fila ofrece «Ver trámite #N»", async () => {
    vi.stubGlobal("open", vi.fn(() => null));
    await montar({ [GRILLA]: () => Response.json(loteIa()), [CREAR]: creado("59") });
    fireEvent.click(botonFila());
    await avanzar();
    fireEvent.click(within(screen.getByRole("dialog", { name: "¿Crear el traspaso de TST001?" })).getByRole("button", { name: "Crear traspaso" }));
    await avanzar();
    expect(within(tabla()).getByRole("link", { name: /ver trámite #59 de la placa TST001/i })).toHaveAttribute("href", URL_TRAMITE);
  });
});

// HU #14562 F-05 (auditoría §4.4): desde el panel del dictamen de un vehículo VIABLE y elegible, «Crear trámite» cierra el
// panel y abre el mismo modal de la fila (AC37), sin apilar un modal sobre el panel. Sin elegibilidad no hay atajo.
describe("HU #14562 F-05 — «Crear trámite» desde el pie del panel del dictamen", () => {
  const RUTA_DICTAMEN = `GET /diagnosticos/${ID}/vehiculos/vehiculo-1/dictamen`;
  const VIABLE = { version: 1, veredicto: "VIABLE", estado_redaccion: "LISTA", alertamientos: [], catalogo_version: 1, creado_en: "2026-11-12T15:00:00Z" };
  const abrirPanel = async () => {
    fireEvent.click(within(tabla()).getByRole("button", { name: "Ver dictamen de TST001" }));
    await avanzar();
    return screen.getByRole("dialog", { name: "Dictamen · TST001" });
  };

  it("lote IA: cierra el panel y abre «¿Crear el traspaso de TST001?», que crea el borrador", async () => {
    pestanaFalsa();
    const api = await montar({ [GRILLA]: () => Response.json(loteIa()), [RUTA_DICTAMEN]: () => Response.json(VIABLE), [CREAR]: creado() });
    const panel = await abrirPanel();
    fireEvent.click(within(panel).getByRole("button", { name: "Crear trámite" }));
    await avanzar();
    expect(screen.queryByRole("dialog", { name: "Dictamen · TST001" })).toBeNull();
    const modal = screen.getByRole("dialog", { name: "¿Crear el traspaso de TST001?" });
    fireEvent.click(within(modal).getByRole("button", { name: "Crear traspaso" }));
    await avanzar();
    expect(llamadas(api, "POST", RUTA_CREAR)).toHaveLength(1);
  });

  it("lote Simple: abre «Crear trámite para TST001», que pide el tipo", async () => {
    await montar({ [GRILLA]: () => Response.json(loteSimple()), [RUTA_DICTAMEN]: () => Response.json(VIABLE), "GET /tipos-tramite": () => Response.json(TIPOS) });
    const panel = await abrirPanel();
    fireEvent.click(within(panel).getByRole("button", { name: "Crear trámite" }));
    await avanzar();
    expect(screen.queryByRole("dialog", { name: "Dictamen · TST001" })).toBeNull();
    expect(screen.getByRole("dialog", { name: "Crear trámite para TST001" })).toBeInTheDocument();
  });

  it("vehículo no elegible (ya con trámite): el panel no ofrece el atajo", async () => {
    const g = loteIa();
    g.vehiculos = [{ ...g.vehiculos[0], elegible_tramite: false, motivo_no_elegible: "YA_TIENE_TRAMITE", tramite: { id: "tramite-41", numero: "41" } }];
    await montar({ [GRILLA]: () => Response.json(g), [RUTA_DICTAMEN]: () => Response.json(VIABLE) });
    const panel = await abrirPanel();
    expect(within(panel).getByText("Viable")).toBeInTheDocument();
    expect(within(panel).queryByRole("button", { name: /crear trámite/i })).toBeNull();
  });
});
