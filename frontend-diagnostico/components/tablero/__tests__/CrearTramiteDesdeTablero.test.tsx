import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import TableroDiagnostico from "@/app/(app)/diagnosticos/[id]/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { EMPRESA } from "@/__tests__/configuracion/datos";
import { ID, V1, V2, V3, desdesPedidos, grilla, llamadas, sesion, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: en el tablero /diagnosticos/{id}, con la capacidad `derivar`, cada placa elegible (VIABLE y sin trámite)
// tiene «Crear trámite» (Punto 4, incremento 2: AC 1, 3, 6, 7 y 8). En un lote Con análisis crea el borrador con el tipo del
// lote; en uno Simple, un modal pide el tipo una vez. Al terminar, la fila muestra «Ver trámite #N», que abre el borrador en
// Trámites en una pestaña nueva. La placa no elegible no tiene acción y muestra el motivo. Sin la capacidad no hay nada.

const navegacion = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => navegacion }));

const TRAMITES_URL = "https://tramites.prueba";
const DERIVA: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, derivar: true };
const SIN_DERIVAR: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, descargarReportes: true };
const GRILLA = `GET /diagnosticos/${ID}/grilla`;
const RUTA_CREAR = `/diagnosticos/${ID}/vehiculos/vehiculo-1/tramite`;
const CREAR = `POST ${RUTA_CREAR}`;
const TRAMITE_ID = "00000000-0000-7000-8000-00000000a001";

const avanzar = (ms = 0) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

type Rutas = Parameters<typeof simularApi>[0];

async function montar(rutas: Rutas, capacidades: Partial<CapacidadesDiagnostico> = DERIVA) {
  const api = simularApi({ "GET /sesion": () => Response.json(sesion(capacidades)), ...rutas });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl={TRAMITES_URL}>
      {await TableroDiagnostico({ params: Promise.resolve({ id: ID }), searchParams: Promise.resolve({}) })}
    </DiagnosticoShell>,
  );
  await avanzar();
  await avanzar();
  return api;
}

/** TST001 elegible; TST002 no VIABLE; TST003 ya con el trámite #41. Lote terminado (sin sondeo). */
function lote(cambios: Partial<Grilla> = {}, filas: Partial<Record<"v1" | "v2" | "v3", Partial<FilaVehiculo>>> = {}): Grilla {
  return grilla({
    estado: "COMPLETADO",
    modo: "CON_ANALISIS",
    vehiculos: [
      { ...V1, elegible_tramite: true, motivo_no_elegible: null, tramite: null, ...filas.v1 },
      { ...V2, terminado: true, celdas: V1.celdas.map((c) => ({ ...c, id: `${c.id}-2` })), elegible_tramite: false, motivo_no_elegible: "NO_VIABLE", tramite: null, ...filas.v2 },
      { ...V3, terminado: true, celdas: V1.celdas.map((c) => ({ ...c, id: `${c.id}-3` })), elegible_tramite: false, motivo_no_elegible: "YA_TIENE_TRAMITE", tramite: { id: "tramite-41", numero: "41" }, ...filas.v3 },
    ],
    ...cambios,
  });
}

const creado = (numero = "57", yaExistia = false) => () => Response.json({ tramite: { id: TRAMITE_ID, numero }, ya_existia: yaExistia }, { status: 201 });
const tabla = () => screen.getByRole("table", { name: /semáforo de consultas/i });
const botonCrear = (placa = "TST001") => within(tabla()).queryByRole("button", { name: `Crear trámite para la placa ${placa}` });

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
  navegacion.push.mockReset();
  navegacion.replace.mockReset();
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Punto 4 AC1/AC8 — la acción solo con la capacidad y en la fila elegible", () => {
  it("con `derivar`: «Crear trámite» en la placa elegible y no en la no elegible ni en la que ya tiene trámite", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()) });
    expect(botonCrear("TST001")).toBeEnabled();
    expect(botonCrear("TST002")).not.toBeInTheDocument();
    expect(botonCrear("TST003")).not.toBeInTheDocument();
  });

  it("la fila no elegible no tiene acción y expone el motivo en lenguaje de negocio, asociado a la placa", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()) });
    const fila = within(tabla()).getAllByRole("row").find((r) => r.getAttribute("data-vehiculo") === "vehiculo-2")!;
    const motivo = within(fila).getByText(/dictamen no es viable/i);
    expect(motivo).toBeVisible();
    expect(motivo.textContent).toMatch(/no se puede crear trámite para la placa TST002.*dictamen no es viable/i);
    expect(within(fila).queryByRole("button", { name: /crear trámite/i })).not.toBeInTheDocument();
  });

  it("la fila que ya tiene trámite muestra «Ver trámite #41», enlace al borrador en Trámites en una pestaña nueva", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()) });
    const enlace = within(tabla()).getByRole("link", { name: /ver trámite #41 de la placa TST003/i });
    expect(enlace).toHaveTextContent("Ver trámite #41");
    expect(enlace).toHaveAttribute("href", `${TRAMITES_URL}/tramites/tramite-41?t=${EMPRESA}`);
    expect(enlace).toHaveAttribute("target", "_blank");
    expect(enlace.getAttribute("rel")).toMatch(/noopener/);
  });

  it("sin la capacidad `derivar` no aparece nada: ni «Crear trámite», ni «Ver trámite», ni el motivo", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()) }, SIN_DERIVAR);
    expect(screen.queryByRole("button", { name: /crear trámite/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /ver trámite/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/dictamen no es viable/i)).not.toBeInTheDocument();
  });

  it("backend previo (la fila sin `elegible_tramite` ni `tramite`): no se pinta ninguna acción de trámite", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla({ estado: "COMPLETADO", vehiculos: [V1] })) });
    expect(screen.queryByRole("button", { name: /crear trámite/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /ver trámite/i })).not.toBeInTheDocument();
  });
});

describe("Punto 4 AC3 — «Crear trámite» individual crea el borrador y la fila pasa a «Ver trámite #N»", () => {
  it("lote Con análisis: POST sin pedir tipo, cargando mientras responde y luego el enlace al borrador", async () => {
    let responder: (r: Response) => void = () => {};
    const api = await montar({
      [GRILLA]: () => Response.json(lote()),
      [CREAR]: () => new Promise<Response>((r) => (responder = r)),
    });
    fireEvent.click(botonCrear()!);
    await avanzar();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    const enCurso = within(tabla()).getByRole("button", { name: "Crear trámite para la placa TST001" });
    expect(enCurso).toBeDisabled();
    expect(enCurso).toHaveAttribute("aria-busy", "true");
    expect(llamadas(api, "POST", RUTA_CREAR)).toHaveLength(1);
    const cuerpo = (llamadas(api, "POST", RUTA_CREAR)[0][1] as RequestInit).body;
    expect(cuerpo === undefined || JSON.parse(String(cuerpo)).tipo_tramite_codigo === undefined).toBe(true);

    responder(creado("57")());
    await avanzar();
    const enlace = within(tabla()).getByRole("link", { name: /ver trámite #57 de la placa TST001/i });
    expect(enlace).toHaveAttribute("href", `${TRAMITES_URL}/tramites/${TRAMITE_ID}?t=${EMPRESA}`);
    expect(botonCrear()).not.toBeInTheDocument();
  });

  it("lote Simple: un modal pide el tipo (catálogo GET /tipos-tramite) y el POST lleva tipo_tramite_codigo", async () => {
    const api = await montar({
      [GRILLA]: () => Response.json(lote({ modo: "SIMPLE", tipo_tramite_codigo: null } as Partial<Grilla>)),
      "GET /tipos-tramite": () =>
        Response.json({ tipos: [{ codigo: "TRASPASO", nombre: "Traspaso", familia: "TRASPASO", fuentesObligatorias: [] }, { codigo: "UNILATERAL", nombre: "Traspaso unilateral", familia: "TRASPASO", fuentesObligatorias: [] }] }),
      [CREAR]: creado("58"),
    });
    fireEvent.click(botonCrear()!);
    await avanzar();
    const dialogo = screen.getByRole("dialog", { name: /tipo de trámite/i });
    expect(llamadas(api, "POST", RUTA_CREAR)).toHaveLength(0);
    const confirmar = within(dialogo).getByRole("button", { name: /crear trámite/i });
    expect(confirmar).toBeDisabled();
    fireEvent.change(within(dialogo).getByLabelText(/tipo de trámite/i), { target: { value: "UNILATERAL" } });
    fireEvent.click(confirmar);
    await avanzar();
    const [, init] = llamadas(api, "POST", RUTA_CREAR)[0] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ tipo_tramite_codigo: "UNILATERAL" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(within(tabla()).getByRole("link", { name: /ver trámite #58 de la placa TST001/i })).toBeInTheDocument();
  });

  it("lote Simple con el catálogo caído: el modal muestra el error con Reintentar, que vuelve a pedirlo", async () => {
    let n = 0;
    const api = await montar({
      [GRILLA]: () => Response.json(lote({ modo: "SIMPLE" } as Partial<Grilla>)),
      "GET /tipos-tramite": () =>
        n++ === 0 ? Response.json({ error: "x" }, { status: 503 }) : Response.json({ tipos: [{ codigo: "TRASPASO", nombre: "Traspaso", familia: "TRASPASO", fuentesObligatorias: [] }] }),
    });
    fireEvent.click(botonCrear()!);
    await avanzar();
    const dialogo = screen.getByRole("dialog", { name: /tipo de trámite/i });
    expect(within(dialogo).getByRole("alert")).toHaveTextContent(/no se pudieron cargar los tipos de trámite/i);
    fireEvent.click(within(dialogo).getByRole("button", { name: "Reintentar" }));
    await avanzar();
    expect(llamadas(api, "GET", "/tipos-tramite")).toHaveLength(2);
    expect(within(dialogo).getByRole("option", { name: "Traspaso" })).toBeInTheDocument();
  });
});

describe("Punto 4 AC6 — un segundo intento sobre un vehículo con trámite no crea otro (409)", () => {
  it("409: avisa «ya tenía trámite» y vuelve a leer la grilla", async () => {
    let n = 0;
    const api = await montar({
      [GRILLA]: () =>
        Response.json(n++ === 0 ? lote() : lote({ desde: 12 }, { v1: { elegible_tramite: false, motivo_no_elegible: "YA_TIENE_TRAMITE", tramite: { id: "tramite-60", numero: "60" } } })),
      [CREAR]: () => Response.json({ code: "YA_TIENE_TRAMITE", title: "El vehículo ya tiene trámite" }, { status: 409 }),
    });
    const antes = desdesPedidos(api).length;
    fireEvent.click(botonCrear()!);
    await avanzar();
    await avanzar();
    expect(screen.getByRole("status", { name: /resultado de crear trámite para la placa TST001/i })).toHaveTextContent(/ya tenía trámite/i);
    expect(desdesPedidos(api).length).toBeGreaterThan(antes);
    expect(within(tabla()).getByRole("link", { name: /ver trámite #60 de la placa TST001/i })).toBeInTheDocument();
  });
});

describe("Punto 4 AC7/AC8 — errores de negocio y de permiso visibles en la fila", () => {
  it("422 (p. ej. ya no es VIABLE u OT no habilitado): el detalle de negocio queda visible y la acción sigue disponible", async () => {
    await montar({
      [GRILLA]: () => Response.json(lote()),
      [CREAR]: () => Response.json({ code: "NO_ELEGIBLE", title: "No elegible", detail: "OT no habilitado para la compañía" }, { status: 422 }),
    });
    fireEvent.click(botonCrear()!);
    await avanzar();
    expect(screen.getByRole("alert", { name: /resultado de crear trámite para la placa TST001/i })).toHaveTextContent("OT no habilitado para la compañía");
    expect(botonCrear()).toBeEnabled();
  });

  it("403: mensaje claro de permiso", async () => {
    await montar({
      [GRILLA]: () => Response.json(lote()),
      [CREAR]: () => Response.json({}, { status: 403 }),
    });
    fireEvent.click(botonCrear()!);
    await avanzar();
    expect(screen.getByRole("alert", { name: /resultado de crear trámite para la placa TST001/i })).toHaveTextContent(/no tienes permiso para crear trámites/i);
  });
});
