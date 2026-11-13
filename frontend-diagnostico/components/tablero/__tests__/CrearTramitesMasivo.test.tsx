import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import TableroDiagnostico from "@/app/(app)/diagnosticos/[id]/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { ID, V1, V2, V3, celda, desdesPedidos, grilla, llamadas, sesion, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: en el tablero /diagnosticos/{id}, con la capacidad `derivar`, cada placa elegible tiene su casilla (Punto 4,
// incremento 3: AC 1, 2, 4, 5, 6 y 7). No hay preselección; «Seleccionar todas las elegibles», «Quitar selección» y el
// contador «N seleccionadas». «Crear trámites (N)» encola la operación (en lote Simple pide el tipo una vez) y muestra el
// progreso, que se sondea con espera creciente; al terminar, el resumen de creadas, omitidas con motivo y fallidas con
// «Reintentar fallidas». Al volver a la pantalla se retoma la operación activa del lote. Sin la capacidad no hay nada.

const navegacion = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => navegacion }));

const TRAMITES_URL = "https://tramites.prueba";
const DERIVA: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, derivar: true };
const SIN_DERIVAR: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, descargarReportes: true };
const OP = "00000000-0000-7000-8000-0000000000f1";
const GRILLA = `GET /diagnosticos/${ID}/grilla`;
const RUTA_MASIVO = `/diagnosticos/${ID}/tramites`;
const MASIVO = `POST ${RUTA_MASIVO}`;
const RUTA_OPERACIONES = `/diagnosticos/${ID}/tramites/operaciones`;
const ULTIMA = `GET ${RUTA_OPERACIONES}`;
const RUTA_PROGRESO = `${RUTA_OPERACIONES}/${OP}`;
const PROGRESO = `GET ${RUTA_PROGRESO}`;
const RUTA_REINTENTAR = `${RUTA_PROGRESO}/reintentar-fallidas`;
const REINTENTAR = `POST ${RUTA_REINTENTAR}`;

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

const terminada = (n: number) => V1.celdas.map((c) => ({ ...c, id: `${c.id}-${n}` }));
const derivacion = (elegible: boolean, motivo: string | null = null, extra: Record<string, unknown> = {}) => ({
  derivacion: { elegible, motivo_no_elegible: motivo, estado: null, tramite: null, ...extra },
});

/**
 * Contrato real de la fila (incremento 2): `derivacion: { elegible, motivo_no_elegible, estado, tramite }`. TST001 y TST004
 * elegibles; TST002 no VIABLE; TST003 ya con el trámite TR-41. Lote terminado (sin sondeo de la grilla).
 */
function lote(cambios: Partial<Grilla> = {}): Grilla {
  return grilla({
    estado: "COMPLETADO",
    modo: "CON_ANALISIS",
    vehiculos: [
      { ...V1, ...derivacion(true) } as FilaVehiculo,
      { ...V2, terminado: true, celdas: terminada(2), ...derivacion(false, "NO_VIABLE") } as FilaVehiculo,
      {
        ...V3,
        terminado: true,
        celdas: terminada(3),
        ...derivacion(false, "YA_TIENE_TRAMITE", {
          estado: "CREADO",
          tramite: { id: "tramite-41", numero: "TR-41", url: `${TRAMITES_URL}/tramites/tramite-41?t=x` },
        }),
      } as FilaVehiculo,
      vehiculo(4, [celda(4, "RUNT", "COMPLETADO"), celda(4, "SIMIT_PROPIETARIO", "COMPLETADO")], derivacion(true) as Partial<FilaVehiculo>),
    ],
    ...cambios,
  });
}

const encolada = (extra: Record<string, unknown> = {}) => () =>
  Response.json({ operacion_id: OP, total: 2, encolados: 2, no_elegibles: [], ...extra }, { status: 202 });

type ItemProgreso = { vehiculo_id: string; placa: string; estado: string; motivo_codigo: string | null; motivo: string | null; tramite_numero: string | null };

function progreso(cambios: Record<string, unknown> & { items?: ItemProgreso[] } = {}) {
  return { operacion_id: OP, estado: "EN_CURSO", total: 2, creadas: 0, omitidas: 0, fallidas: 0, pendientes: 2, items: [] as ItemProgreso[], ...cambios };
}

const tabla = () => screen.getByRole("table", { name: /semáforo de consultas/i });
const casilla = (placa: string) => within(tabla()).getByRole("checkbox", { name: `Seleccionar la placa ${placa}` });
const barra = () => screen.getByRole("group", { name: /selección para crear trámites/i });
const botonMasivo = () => within(barra()).getByRole("button", { name: /^crear trámites \(\d+\)$/i });
const panel = () => screen.getByRole("region", { name: /creación de trámites/i });

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
  navegacion.push.mockReset();
  navegacion.replace.mockReset();
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Punto 4 AC1/AC2 — casillas por fila, sin preselección", () => {
  it("sin preselección: todas las casillas sin marcar, «0 seleccionadas» y «Crear trámites (0)» deshabilitado", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()) });
    within(tabla())
      .getAllByRole("checkbox")
      .forEach((c) => expect(c).not.toBeChecked());
    // HU #14562 AC32: la barra de acciones en bloque (BarraSeleccion) solo aparece con algo seleccionado.
    expect(screen.queryByRole("region", { name: "Selección" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^crear trámites/i })).not.toBeInTheDocument();
  });

  it("solo las elegibles tienen la casilla activa; las demás, deshabilitadas con el motivo como descripción accesible", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()) });
    expect(casilla("TST001")).toBeEnabled();
    expect(casilla("TST004")).toBeEnabled();
    expect(casilla("TST002")).toBeDisabled();
    expect(casilla("TST002")).toHaveAccessibleDescription(/dictamen no es viable/i);
    expect(casilla("TST003")).toBeDisabled();
    expect(casilla("TST003")).toHaveAccessibleDescription(/ya tenía trámite/i);
  });

  it("marcar una casilla suma al contador y al botón", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()) });
    fireEvent.click(casilla("TST004"));
    expect(casilla("TST004")).toBeChecked();
    expect(within(barra()).getByText("1 vehículo seleccionado")).toBeInTheDocument();
    expect(botonMasivo()).toHaveTextContent("Crear trámites (1)");
    expect(botonMasivo()).toBeEnabled();
  });

  it("«Seleccionar todas las elegibles» marca solo las elegibles y «Quitar selección» las desmarca", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()) });
    fireEvent.click(within(barra()).getByRole("button", { name: /seleccionar todos los elegibles/i }));
    expect(casilla("TST001")).toBeChecked();
    expect(casilla("TST004")).toBeChecked();
    expect(casilla("TST002")).not.toBeChecked();
    expect(casilla("TST003")).not.toBeChecked();
    expect(within(barra()).getByText("2 vehículos seleccionados")).toBeInTheDocument();
    expect(botonMasivo()).toHaveTextContent("Crear trámites (2)");

    fireEvent.click(within(barra()).getByRole("button", { name: /quitar selección/i }));
    expect(casilla("TST001")).not.toBeChecked();
    expect(casilla("TST004")).not.toBeChecked();
    expect(screen.queryByRole("region", { name: "Selección" })).not.toBeInTheDocument();
  });
});

describe("Punto 4 AC8 — sin la capacidad o con la derivación apagada no aparece nada", () => {
  it("sin `derivar`: ni casillas, ni barra de selección, ni consulta de la operación activa", async () => {
    const api = await montar({ [GRILLA]: () => Response.json(lote()) }, SIN_DERIVAR);
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: /selección para crear trámites/i })).not.toBeInTheDocument();
    expect(llamadas(api, "GET", RUTA_OPERACIONES)).toHaveLength(0);
  });

  it("con `derivacion: null` en todas las filas (derivación apagada): ni casillas ni barra", async () => {
    const apagada = lote({ vehiculos: lote().vehiculos.map((v) => ({ ...v, derivacion: null }) as FilaVehiculo) });
    await montar({ [GRILLA]: () => Response.json(apagada) });
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: /selección para crear trámites/i })).not.toBeInTheDocument();
  });
});

describe("Punto 4 AC4 — «Crear trámites (N)» encola y muestra el progreso", () => {
  it("lote Con análisis: POST masivo con los ids seleccionados, sin modal, y panel de progreso con barra y contadores", async () => {
    const api = await montar({
      [GRILLA]: () => Response.json(lote()),
      [MASIVO]: encolada(),
      [PROGRESO]: () => Response.json(progreso({ creadas: 1, pendientes: 1 })),
    });
    fireEvent.click(casilla("TST001"));
    fireEvent.click(casilla("TST004"));
    fireEvent.click(botonMasivo());
    await avanzar();
    await avanzar();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    const [, init] = llamadas(api, "POST", RUTA_MASIVO)[0] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ vehiculo_ids: ["vehiculo-1", "vehiculo-4"] });

    const estado = within(panel()).getByRole("status");
    expect(estado).toHaveTextContent(/1 de 2 procesadas/i);
    expect(estado).toHaveTextContent(/1 creadas/i);
    expect(estado).toHaveTextContent(/1 en curso/i);
    const barraProgreso = within(panel()).getByRole("progressbar", { name: /avance de la creación de trámites/i });
    expect(barraProgreso).toHaveAttribute("aria-valuenow", "1");
    expect(barraProgreso).toHaveAttribute("aria-valuemax", "2");
    // La selección se vacía y las placas encoladas ya no se pueden volver a marcar.
    expect(screen.queryByRole("region", { name: "Selección" })).not.toBeInTheDocument();
    expect(casilla("TST001")).toBeDisabled();
    expect(casilla("TST004")).toBeDisabled();
  });

  it("lote Simple: el modal del tipo se abre una sola vez y el POST lleva tipo_tramite_codigo para todas", async () => {
    const api = await montar({
      [GRILLA]: () => Response.json(lote({ modo: "SIMPLE" })),
      "GET /tipos-tramite": () =>
        Response.json({ tipos: [{ codigo: "TRASPASO", nombre: "Traspaso", familia: "TRASPASO", fuentesObligatorias: [] }, { codigo: "UNILATERAL", nombre: "Traspaso unilateral", familia: "TRASPASO", fuentesObligatorias: [] }] }),
      [MASIVO]: encolada(),
      [PROGRESO]: () => Response.json(progreso()),
    });
    fireEvent.click(within(barra()).getByRole("button", { name: /seleccionar todos los elegibles/i }));
    fireEvent.click(botonMasivo());
    await avanzar();
    const dialogos = screen.getAllByRole("dialog", { name: /tipo de trámite/i });
    expect(dialogos).toHaveLength(1);
    expect(llamadas(api, "POST", RUTA_MASIVO)).toHaveLength(0);
    fireEvent.change(within(dialogos[0]).getByLabelText(/tipo de trámite/i), { target: { value: "UNILATERAL" } });
    fireEvent.click(within(dialogos[0]).getByRole("button", { name: /crear trámites \(2\)/i }));
    await avanzar();
    expect(llamadas(api, "POST", RUTA_MASIVO)).toHaveLength(1);
    const [, init] = llamadas(api, "POST", RUTA_MASIVO)[0] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ vehiculo_ids: ["vehiculo-1", "vehiculo-4"], tipo_tramite_codigo: "UNILATERAL" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("modo desconocido y la API responde 400 TIPO_TRAMITE_REQUERIDO: abre el modal del tipo", async () => {
    await montar({
      [GRILLA]: () => Response.json(lote({ modo: undefined })),
      "GET /tipos-tramite": () => Response.json({ tipos: [{ codigo: "TRASPASO", nombre: "Traspaso", familia: "TRASPASO", fuentesObligatorias: [] }] }),
      [MASIVO]: () => Response.json({ code: "TIPO_TRAMITE_REQUERIDO", error: "Elige el tipo de trámite", campo: "tipo_tramite_codigo" }, { status: 400 }),
    });
    fireEvent.click(casilla("TST001"));
    fireEvent.click(botonMasivo());
    await avanzar();
    await avanzar();
    expect(screen.getByRole("dialog", { name: /tipo de trámite/i })).toBeInTheDocument();
  });

  it("el sondeo del progreso espera 3 s y, si no cambia, alarga la espera (backoff)", async () => {
    const api = await montar({
      [GRILLA]: () => Response.json(lote()),
      [MASIVO]: encolada(),
      [PROGRESO]: () => Response.json(progreso()),
    });
    fireEvent.click(casilla("TST001"));
    fireEvent.click(botonMasivo());
    await avanzar();
    await avanzar();
    const lecturas = () => llamadas(api, "GET", RUTA_PROGRESO).length;
    expect(lecturas()).toBe(1);
    await avanzar(3000);
    expect(lecturas()).toBe(2);
    // Sin cambios: la siguiente lectura ya no llega a los 3 s.
    await avanzar(3000);
    expect(lecturas()).toBe(2);
    await avanzar(7000);
    expect(lecturas()).toBe(3);
  });

  it("el error al encolar se muestra en lenguaje claro y conserva la selección", async () => {
    await montar({
      [GRILLA]: () => Response.json(lote()),
      [MASIVO]: () => Response.json({ code: "TRAMITES_NO_DISPONIBLE" }, { status: 503, headers: { "Retry-After": "30" } }),
    });
    fireEvent.click(casilla("TST001"));
    fireEvent.click(botonMasivo());
    await avanzar();
    await avanzar();
    expect(screen.getByRole("alert", { name: /resultado de crear trámites/i })).not.toHaveTextContent(/[A-Z]+_[A-Z]+/);
    expect(casilla("TST001")).toBeChecked();
  });
});

describe("Punto 4 AC4/AC5/AC6/AC7 — resumen al terminar con omitidas por motivo y «Reintentar fallidas»", () => {
  const final = progreso({
    estado: "TERMINADA",
    total: 4,
    creadas: 1,
    omitidas: 2,
    fallidas: 1,
    pendientes: 0,
    items: [
      { vehiculo_id: "vehiculo-1", placa: "TST001", estado: "CREADO", motivo_codigo: null, motivo: null, tramite_numero: "TR-70" },
      { vehiculo_id: "vehiculo-4", placa: "TST004", estado: "OMITIDO", motivo_codigo: "OT_NO_HABILITADO", motivo: null, tramite_numero: null },
      { vehiculo_id: "vehiculo-5", placa: "TST005", estado: "OMITIDO", motivo_codigo: "NO_VIABLE", motivo: null, tramite_numero: null },
      { vehiculo_id: "vehiculo-6", placa: "TST006", estado: "FALLIDO", motivo_codigo: "TRAMITES_NO_DISPONIBLE", motivo: null, tramite_numero: null },
    ],
  });

  async function hastaElResumen(extra: Rutas = {}) {
    const api = await montar({
      [GRILLA]: () => Response.json(lote()),
      [MASIVO]: encolada({ total: 2, no_elegibles: [{ vehiculo_id: "vehiculo-3", motivo_codigo: "YA_TIENE_TRAMITE" }] }),
      [PROGRESO]: () => Response.json(final),
      ...extra,
    });
    fireEvent.click(casilla("TST001"));
    fireEvent.click(casilla("TST004"));
    fireEvent.click(botonMasivo());
    await avanzar();
    await avanzar();
    return api;
  }

  it("resumen con creadas, omitidas con su motivo de negocio (OT no habilitado, no VIABLE, ya tenía trámite) y fallidas", async () => {
    await hastaElResumen();
    const resumen = within(panel());
    expect(resumen.getByRole("heading", { name: /resumen de la creación de trámites/i })).toBeInTheDocument();
    expect(resumen.getByText(/1 creadas/i)).toBeInTheDocument();
    const omitidas = resumen.getByRole("list", { name: /placas omitidas/i });
    expect(within(omitidas).getByText(/TST004/)).toBeInTheDocument();
    expect(omitidas).toHaveTextContent("OT no habilitado para la compañía");
    expect(omitidas).toHaveTextContent(/TST005.*dictamen no es viable/i);
    expect(omitidas).toHaveTextContent(/TST003.*ya tenía trámite/i);
    const fallidas = resumen.getByRole("list", { name: /placas fallidas/i });
    expect(fallidas).toHaveTextContent(/TST006.*trámites no está disponible/i);
    expect(panel()).not.toHaveTextContent(/OT_NO_HABILITADO|NO_VIABLE|TRAMITES_NO_DISPONIBLE|YA_TIENE_TRAMITE/);
  });

  it("HU #14562 AC30: al terminar no recarga la grilla completa; la fila llega por su versión en el sondeo incremental", async () => {
    const api = await hastaElResumen();
    const desdes = desdesPedidos(api);
    expect(desdes.length).toBeGreaterThan(1);
    expect(desdes.slice(1)).not.toContain(0);
  });

  it("«Reintentar fallidas» reencola las fallidas y vuelve a sondear el progreso", async () => {
    let n = 0;
    const api = await hastaElResumen({
      [PROGRESO]: () => Response.json(n++ === 0 ? final : progreso({ total: 4, creadas: 1, omitidas: 2, fallidas: 0, pendientes: 1 })),
      [REINTENTAR]: () => Response.json({ reencolados: 1 }, { status: 202 }),
    });
    fireEvent.click(within(panel()).getByRole("button", { name: /reintentar fallidas \(1\)/i }));
    await avanzar();
    await avanzar();
    expect(llamadas(api, "POST", RUTA_REINTENTAR)).toHaveLength(1);
    expect(llamadas(api, "GET", RUTA_PROGRESO).length).toBeGreaterThanOrEqual(2);
    expect(within(panel()).getByRole("status")).toHaveTextContent(/1 en curso/i);
  });

  it("sin fallidas no hay «Reintentar fallidas»", async () => {
    await hastaElResumen({ [PROGRESO]: () => Response.json({ ...final, fallidas: 0, items: final.items.filter((i) => i.estado !== "FALLIDO") }) });
    expect(within(panel()).queryByRole("button", { name: /reintentar fallidas/i })).not.toBeInTheDocument();
  });
});

describe("Punto 4 AC4 — se puede salir de la pantalla: al volver se retoma la operación activa del lote", () => {
  it("GET …/operaciones?ultima=true con una operación EN_CURSO: muestra el panel y sigue sondeando", async () => {
    const api = await montar({
      [GRILLA]: () => Response.json(lote()),
      [ULTIMA]: () => Response.json([progreso({ total: 5, creadas: 2, pendientes: 3 })]),
      [PROGRESO]: () => Response.json(progreso({ total: 5, creadas: 3, pendientes: 2 })),
    });
    await avanzar();
    const [url] = llamadas(api, "GET", RUTA_OPERACIONES)[0] as [string];
    expect(new URL(url, "http://localhost").searchParams.get("ultima")).toBe("true");
    expect(within(panel()).getByRole("status")).toHaveTextContent(/2 de 5 procesadas/i);
    await avanzar(3000);
    expect(llamadas(api, "GET", RUTA_PROGRESO).length).toBeGreaterThanOrEqual(1);
    expect(within(panel()).getByRole("status")).toHaveTextContent(/3 de 5 procesadas/i);
  });

  it("sin operación (lista vacía o el backend no la expone): no hay panel", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()), [ULTIMA]: () => Response.json([]) });
    expect(screen.queryByRole("region", { name: /creación de trámites/i })).not.toBeInTheDocument();
  });
});

// HU #14562 AC32: «Seleccionar todos los elegibles» (Viables sin trámite), «Creando trámites» y «Quitar selección» en BarraSeleccion.
describe("HU #14562 AC32 — selección y acciones en bloque", () => {
  it("la barra de selección es una región con el conteo, «Crear trámites (N)», «Quitar selección» y la regla de elegibilidad", async () => {
    await montar({ [GRILLA]: () => Response.json(lote()) });
    fireEvent.click(within(barra()).getByRole("button", { name: /seleccionar todos los elegibles/i }));
    const region = screen.getByRole("region", { name: "Selección" });
    expect(region).toHaveTextContent("2 vehículos seleccionados");
    expect(region).toHaveTextContent(/solo se seleccionan vehículos viables y sin trámite/i);
    expect(within(region).getByRole("button", { name: "Crear trámites (2)" })).toBeEnabled();
    fireEvent.click(within(region).getByRole("button", { name: /quitar selección/i }));
    expect(screen.queryByRole("region", { name: "Selección" })).not.toBeInTheDocument();
  });

  it("al pulsar «Crear trámites» se ve «Creando trámites» hasta que la operación termina", async () => {
    let soltar: (r: Response) => void = () => {};
    const pendiente = new Promise<Response>((r) => {
      soltar = r;
    });
    await montar({ [GRILLA]: () => Response.json(lote()), [MASIVO]: () => pendiente, [PROGRESO]: () => Response.json(progreso()) });
    fireEvent.click(within(barra()).getByRole("button", { name: /seleccionar todos los elegibles/i }));
    fireEvent.click(botonMasivo());
    await avanzar();
    expect(within(barra()).getByRole("button", { name: /creando trámites/i })).toHaveAttribute("aria-busy", "true");
    await act(async () => {
      soltar(encolada()());
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(within(panel()).getByText(/creando trámites/i)).toBeInTheDocument();
  });
});
