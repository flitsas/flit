import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import TableroDiagnostico from "@/app/(app)/diagnosticos/[id]/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { Grilla } from "@/lib/tablero/api";
import { FALLOS_ANTES_DE_DETENER, INTERVALO_REPOSO_MS, INTERVALO_SONDEO_MS } from "@/lib/tablero/useGrillaIncremental";
import { ID, V1, celda, desdesPedidos, grilla, llamadas, loteTerminadoConErrores, sesion, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: /diagnosticos/{id} (a donde navegan la confirmación de la carga y el Express) abre el tablero del lote:
// tarjeta título con el nombre, estado global y avance, la grilla de semáforos con sondeo cada 3 s y «Cerrar panel»
// cuando el lote terminó con errores. Cuatro estados: cargando, error con Reintentar, vacío (sin vehículos) y lleno.

const navegacion = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => navegacion }));

const USUARIO: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true };
const ADMIN: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, reprocesarConsultas: true, editarConfiguracion: true };
const GRILLA = `GET /diagnosticos/${ID}/grilla`;
const CERRAR = `POST /diagnosticos/${ID}/cerrar-panel`;

const avanzar = (ms = 0) => act(async () => {
  await vi.advanceTimersByTimeAsync(ms);
});

type Rutas = Parameters<typeof simularApi>[0];

async function montar(rutas: Rutas, capacidades: Partial<CapacidadesDiagnostico> = USUARIO, id = ID, desde?: string | string[]) {
  const api = simularApi({ "GET /sesion": () => Response.json(sesion(capacidades)), ...rutas });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      {await TableroDiagnostico({ params: Promise.resolve({ id }), searchParams: Promise.resolve(desde === undefined ? {} : { desde }) })}
    </DiagnosticoShell>,
  );
  await avanzar();
  await avanzar();
  return api;
}

/** Responde en orden; la última se repite. */
const enOrden = (...grillas: Grilla[]) => {
  let n = 0;
  return () => Response.json(grillas[Math.min(n++, grillas.length - 1)]);
};

const estadoGlobal = () => screen.getByRole("status", { name: /estado del lote/i });

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
  navegacion.push.mockReset();
  navegacion.replace.mockReset();
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("/diagnosticos/{id} — la ruta de la carga y del Express abre el tablero", () => {
  it("la página existe y pide la grilla del id de la ruta (no termina en not-found)", async () => {
    const api = await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(screen.getByRole("heading", { level: 1, name: "Flota norte" })).toBeInTheDocument();
    expect(desdesPedidos(api)).toEqual([0]);
  });
});

describe("/diagnosticos/{id} — los cuatro estados", () => {
  it("cargando: el loader mientras llega la grilla", async () => {
    await montar({ [GRILLA]: () => new Promise<Response>(() => {}) });
    expect(screen.getByRole("status", { name: /cargando el tablero/i })).toBeInTheDocument();
  });

  it("error: aviso con Reintentar, que vuelve a pedir la grilla", async () => {
    let n = 0;
    const api = await montar({ [GRILLA]: () => (n++ === 0 ? Response.json({ error: "x" }, { status: 500 }) : Response.json(grilla({ vehiculos: [V1] }))) });
    const alerta = screen.getByRole("alert");
    expect(alerta).toHaveTextContent(/no se pudo cargar el tablero/i);
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    await avanzar();
    expect(desdesPedidos(api)).toEqual([0, 0]);
    expect(screen.getByRole("table", { name: /semáforo de consultas/i })).toBeInTheDocument();
  });

  it("error 404: el diagnóstico no existe o no es visible, sin Reintentar", async () => {
    await montar({ [GRILLA]: () => Response.json({ error: "No existe" }, { status: 404 }) });
    expect(screen.getByRole("heading", { name: /no encontramos este diagnóstico/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
  });

  it("vacío: un lote sin vehículos lo dice y no muestra la grilla", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla({ estado: "CREADO", vehiculos: [], avance: { vehiculos: 0, terminados: 0 } })) });
    expect(screen.getByRole("heading", { name: /todavía no tiene vehículos/i })).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("sin permiso de ver diagnósticos: estado vacío y ninguna consulta de la grilla", async () => {
    const api = await montar({ [GRILLA]: () => Response.json(grilla()) }, {});
    expect(screen.getByRole("heading", { name: /no tienes permiso para ver este diagnóstico/i })).toBeInTheDocument();
    expect(desdesPedidos(api)).toEqual([]);
  });

  it("lleno: estado global, avance del lote y la grilla", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(estadoGlobal()).toHaveTextContent("En proceso");
    expect(screen.getByRole("progressbar", { name: /vehículos terminados/i })).toHaveAttribute("aria-valuenow", "1");
    expect(screen.getByText("1 de 3 vehículos terminados")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: /semáforo de consultas/i })).toBeInTheDocument();
  });
});

describe("/diagnosticos/{id} — HU #14562 AC25: progreso del lote", () => {
  it("la barra expone aria-valuetext «N de M vehículos terminados» y el avance vive en una región aria-live", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    const barra = screen.getByRole("progressbar", { name: /vehículos terminados/i });
    expect(barra).toHaveAttribute("aria-valuetext", "1 de 3 vehículos terminados");
    // Solo el texto del avance se anuncia: el sondeo no lo cambia si N no cambia.
    expect(screen.getByText("1 de 3 vehículos terminados")).toHaveAttribute("aria-live", "polite");
  });
});

describe("/diagnosticos/{id} — AC2: se actualiza sola sin recargar", () => {
  it("a los 3 s pide solo los cambios desde su versión y pinta la celda que cambió", async () => {
    const cambio = grilla({ version: 13, vehiculos: [vehiculo(3, [celda(3, "RUNT", "COMPLETADO", { version: 13 })])] });
    const api = await montar({ [GRILLA]: enOrden(grilla(), cambio) });
    await avanzar(INTERVALO_SONDEO_MS);
    expect(desdesPedidos(api)).toEqual([0, 12]);
    const fila = screen.getByRole("rowheader", { name: /TST003/ }).closest("tr")!;
    expect(within(fila).getByText("Ejecutado")).toBeInTheDocument();
    expect(navegacion.push).not.toHaveBeenCalled();
    expect(navegacion.refresh).not.toHaveBeenCalled();
  });
});

describe("/diagnosticos/{id} — AC4/AC5: acciones por perfil", () => {
  it("AC4: el usuario sin capacidad de reprocesar no ve «Reprocesar»; sí puede abrir el dictamen", async () => {
    await montar({ [GRILLA]: () => Response.json(loteTerminadoConErrores({ estado: "INCOMPLETO" })) });
    const tabla = screen.getByRole("table");
    expect(within(tabla).queryAllByRole("button", { name: /reprocesar/i })).toHaveLength(0);
    expect(within(tabla).getAllByRole("button", { name: /^Ver dictamen de / })).toHaveLength(2);
  });

  it("AC5: el admin reprocesa una roja de un lote INCOMPLETO: la celda vuelve a gris, el lote a EN_PROCESO y el sondeo se reanuda", async () => {
    const lote = loteTerminadoConErrores({ estado: "INCOMPLETO" });
    const api = await montar(
      {
        // Respuesta incremental: sin vehículos cambiados, pero el avance siempre es el del lote completo.
        [GRILLA]: enOrden(lote, grilla({ estado: "EN_PROCESO", version: 21, vehiculos: [], avance: lote.avance })),
        "POST /consultas/celda-4-runt/reprocesar": () => new Response(null, { status: 202 }),
      },
      ADMIN,
    );
    expect(estadoGlobal()).toHaveTextContent("Incompleto");
    await avanzar(INTERVALO_SONDEO_MS * 2);
    expect(desdesPedidos(api)).toEqual([0]);

    fireEvent.click(screen.getByRole("button", { name: "Reprocesar RUNT vehículo de TST004" }));
    await avanzar();

    expect(llamadas(api, "POST", "/consultas/celda-4-runt/reprocesar")).toHaveLength(1);
    const runt = document.querySelector<HTMLElement>('[data-celda="celda-4-runt"]')!;
    expect(runt).toHaveAttribute("data-estado", "PENDIENTE");
    expect(within(runt).getByText("Sin ejecutar")).toBeInTheDocument();
    expect(estadoGlobal()).toHaveTextContent("En proceso");

    await avanzar(INTERVALO_SONDEO_MS);
    expect(desdesPedidos(api)).toEqual([0, 20]);
  });

  it("AC5: un 409 del reproceso se avisa y vuelve a leer la grilla", async () => {
    const api = await montar(
      {
        [GRILLA]: () => Response.json(loteTerminadoConErrores({ estado: "INCOMPLETO" })),
        "POST /consultas/celda-4-runt/reprocesar": () => Response.json({ code: "CONSULTA_NO_REPROCESABLE" }, { status: 409 }),
      },
      ADMIN,
    );
    fireEvent.click(screen.getByRole("button", { name: /reprocesar/i }));
    await avanzar();
    expect(screen.getByRole("alert")).toHaveTextContent(/ya no se puede reprocesar/i);
    expect(desdesPedidos(api)).toEqual([0, 20]);
  });
});

/** HU #14562 AC24: «Cerrar lote» abre la confirmación; el cierre se pide desde el botón del modal. */
async function confirmarCierre() {
  fireEvent.click(screen.getByRole("button", { name: "Cerrar lote" }));
  const modal = screen.getByRole("dialog", { name: "¿Cerrar el lote?" });
  fireEvent.click(within(modal).getByRole("button", { name: "Cerrar lote" }));
}

describe("/diagnosticos/{id} — HU #14562 AC24: «Cerrar lote» con confirmación", () => {
  it("abre un modal que explica el efecto; al cancelar no llama al API y el lote sigue igual", async () => {
    const api = await montar({
      [GRILLA]: () => Response.json(loteTerminadoConErrores()),
      [CERRAR]: () => Response.json({ id: ID, estado: "INCOMPLETO", cerrado_en: "2026-10-21T16:00:00Z" }),
    });
    fireEvent.click(screen.getByRole("button", { name: "Cerrar lote" }));
    const modal = screen.getByRole("dialog", { name: "¿Cerrar el lote?" });
    expect(modal).toHaveTextContent(/las consultas con error quedan sin resultado/i);
    fireEvent.click(within(modal).getByRole("button", { name: "Cancelar" }));
    await avanzar();
    expect(screen.queryByRole("dialog", { name: "¿Cerrar el lote?" })).not.toBeInTheDocument();
    expect(llamadas(api, "POST", `/diagnosticos/${ID}/cerrar-panel`)).toHaveLength(0);
    expect(estadoGlobal()).toHaveTextContent("En proceso");
  });

  it("al confirmar llama una sola vez a cerrar-panel y el tablero refleja el nuevo estado", async () => {
    // Tras cerrar, el tablero vuelve a leer el lote una vez: la segunda lectura ya trae INCOMPLETO.
    const cerrado = loteTerminadoConErrores({ estado: "INCOMPLETO", vehiculos: [], avance: { vehiculos: 2, terminados: 1 } });
    const api = await montar({
      [GRILLA]: enOrden(loteTerminadoConErrores(), cerrado),
      [CERRAR]: () => Response.json({ id: ID, estado: "INCOMPLETO", cerrado_en: "2026-10-21T16:00:00Z" }),
    });
    await confirmarCierre();
    await avanzar();
    await avanzar();
    expect(llamadas(api, "POST", `/diagnosticos/${ID}/cerrar-panel`)).toHaveLength(1);
    expect(estadoGlobal()).toHaveTextContent("Incompleto");
    expect(screen.queryByRole("dialog", { name: "¿Cerrar el lote?" })).not.toBeInTheDocument();
  });
});

describe("/diagnosticos/{id} — AC7: cerrar panel", () => {
  it("el lote terminado con errores pasa a INCOMPLETO y muestra el estimado frente a lo consumido", async () => {
    const cerrado = loteTerminadoConErrores({
      estado: "INCOMPLETO",
      vehiculos: [],
      // Respuesta incremental: sin vehículos cambiados, pero el avance siempre es el del lote completo.
      avance: { vehiculos: 2, terminados: 1 },
      estimado_consultas: [
        { fuente: "RUNT", estimadas: 2, consumidas: 2 },
        { fuente: "SIMIT_PROPIETARIO", estimadas: 2, consumidas: 1 },
      ],
    });
    const api = await montar({
      [GRILLA]: enOrden(loteTerminadoConErrores(), cerrado),
      [CERRAR]: () => Response.json({ id: ID, estado: "INCOMPLETO", cerrado_en: "2026-10-21T16:00:00Z" }),
    });
    expect(estadoGlobal()).toHaveTextContent("En proceso");
    expect(screen.queryByRole("region", { name: /estimado frente a consumido/i })).not.toBeInTheDocument();

    await confirmarCierre();
    await avanzar();
    await avanzar();

    expect(llamadas(api, "POST", `/diagnosticos/${ID}/cerrar-panel`)).toHaveLength(1);
    expect(estadoGlobal()).toHaveTextContent("Incompleto");
    const estimado = screen.getByRole("region", { name: /estimado frente a consumido/i });
    // HU #14562 AC33: es una tarjeta plegable; se abre para leer la tabla.
    fireEvent.click(within(estimado).getByRole("button", { name: /estimado frente a consumido/i }));
    const runt = within(estimado).getByRole("row", { name: /RUNT vehículo/ });
    expect(within(runt).getAllByRole("cell").map((c) => c.textContent)).toEqual(["2", "2"]);
    const simit = within(estimado).getByRole("row", { name: /SIMIT propietario/ });
    expect(within(simit).getAllByRole("cell").map((c) => c.textContent)).toEqual(["2", "1"]);
    expect(screen.queryByRole("button", { name: "Cerrar lote" })).not.toBeInTheDocument();
  });

  it("no ofrece Cerrar lote mientras quedan celdas pendientes o si el lote no tiene errores", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(screen.queryByRole("button", { name: "Cerrar lote" })).not.toBeInTheDocument();
  });

  it.each([
    ["LOTE_NO_EN_PROCESO", /ya no está en proceso/i],
    ["CELDAS_SIN_TERMINAR", /todavía hay consultas pendientes o en curso/i],
    ["LOTE_SIN_ERRORES", /no tiene consultas con error/i],
    ["LOTE_MODIFICADO", /el lote cambió mientras cerrabas el panel/i],
  ])("un 409 %s se explica con un mensaje claro y vuelve a leer el lote", async (code, mensaje) => {
    const api = await montar({
      [GRILLA]: () => Response.json(loteTerminadoConErrores()),
      [CERRAR]: () => Response.json({ code, error: "detalle técnico" }, { status: 409 }),
    });
    await confirmarCierre();
    await avanzar();
    expect(screen.getByRole("alert")).toHaveTextContent(mensaje);
    expect(desdesPedidos(api)).toEqual([0, 20]);
  });
});

describe("/diagnosticos/{id} — HU #13851: descargas del tablero", () => {
  const DESCARGA: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, descargarReportes: true };

  it("AC1: lote EN_PROCESO, el PDF del vehículo terminado está habilitado y el del que sigue en curso deshabilitado con su ayuda", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) }, DESCARGA);
    const tabla = screen.getByRole("table", { name: /semáforo de consultas/i });
    expect(within(tabla).getByRole("columnheader", { name: "Reporte" })).toBeInTheDocument();
    const pdf1 = within(tabla).getByRole("link", { name: "Descargar PDF de TST001" });
    expect(pdf1).toHaveAttribute("href", `/api/v1/diagnostico/diagnosticos/${ID}/vehiculos/vehiculo-1/pdf`);
    expect(pdf1).toHaveAttribute("download");
    const pdf2 = within(tabla).getByRole("button", { name: "Descargar PDF de TST002" });
    expect(pdf2).toBeDisabled();
    expect(pdf2).toHaveAccessibleDescription("Disponible cuando terminen las consultas de este vehículo");
    const barra = screen.getByRole("region", { name: "Descargas del lote" });
    expect(within(barra).getByRole("link", { name: "Excel del lote" })).toBeInTheDocument();
    expect(within(barra).getByRole("button", { name: "PDF de todos (ZIP)" })).toBeInTheDocument();
  });

  it("AC6: lote INCOMPLETO, el PDF del vehículo con celdas en error está habilitado con la ayuda de «no disponible»", async () => {
    await montar({ [GRILLA]: () => Response.json(loteTerminadoConErrores({ estado: "INCOMPLETO" })) }, DESCARGA);
    const pdf4 = screen.getByRole("link", { name: "Descargar PDF de TST004" });
    expect(pdf4).toHaveAccessibleDescription("Las consultas no disponibles aparecerán así en el PDF");
  });

  it("AC4: sin la capacidad descargarReportes no hay ningún control de descarga ni la columna Reporte", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) }, USUARIO);
    expect(screen.getByRole("table", { name: /semáforo de consultas/i })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Descargas del lote" })).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Reporte" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /descargar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /descargar/i })).not.toBeInTheDocument();
  });
});

describe("/diagnosticos/{id} — HU #14087: migas y «Volver a diagnósticos» con los filtros del listado", () => {
  const volver = () => screen.getByRole("link", { name: "Volver a diagnósticos" });

  it("migas «Diagnósticos › {nombre del lote}» con el último en aria-current", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    const migas = screen.getByRole("navigation", { name: "Migas de pan" });
    const items = within(migas).getAllByRole("listitem");
    expect(items.map((i) => i.textContent?.replace("›", "").trim())).toEqual(["Diagnósticos", grilla().nombre]);
    expect(within(migas).getByRole("link", { name: "Diagnósticos" })).toHaveAttribute("href", "/");
    expect(within(items[1]).getByText(grilla().nombre)).toHaveAttribute("aria-current", "page");
  });

  it("sin ?desde= vuelve a /", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(volver()).toHaveAttribute("href", "/");
  });

  it("con ?desde= vuelve al listado con sus filtros como search params de /", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) }, USUARIO, ID, "desde=2026-10-01&estado=EN_PROCESO&nombre=Flota+prueba&pagina=2");
    const href = volver().getAttribute("href")!;
    expect(href.startsWith("/?")).toBe(true);
    const q = new URL(href, "http://localhost").searchParams;
    expect(q.get("desde")).toBe("2026-10-01");
    expect(q.get("estado")).toBe("EN_PROCESO");
    expect(q.get("nombre")).toBe("Flota prueba");
    expect(q.get("pagina")).toBe("2");
    expect(within(screen.getByRole("navigation", { name: "Migas de pan" })).getByRole("link", { name: "Diagnósticos" })).toHaveAttribute("href", href);
  });

  it.each(["https://malicioso.example/robo", "//malicioso.example", "javascript:alert(1)", "/\malicioso.example"])("sin open redirect: ?desde=%s nunca sale de /", async (malo) => {
    await montar({ [GRILLA]: () => Response.json(grilla()) }, USUARIO, ID, malo);
    const href = volver().getAttribute("href")!;
    expect(href === "/" || href.startsWith("/?")).toBe(true);
    expect(new URL(href, "http://localhost").origin).toBe("http://localhost");
    expect(href).not.toMatch(/malicioso|javascript/);
  });

  it("también en los estados de error: el botón de volver está siempre", async () => {
    await montar({ [GRILLA]: () => Response.json({ error: "x" }, { status: 500 }) });
    expect(volver()).toHaveAttribute("href", "/");
  });
});

// HU #14562 F-04: cabecera con metadatos (AC23), tarjetas-filtro (AC27/AC31), columna Dictamen y búsqueda (AC28), cambio de
// dictamen en el sondeo de reposo (AC29), aviso de datos desactualizados (AC26), estimado plegable (AC33) y enmascarado (AC34).
const LOTE_META = {
  modo: "CON_ANALISIS" as const,
  tipo_tramite: { codigo: "TRASPASO_STANDARD", nombre: "Traspaso" },
  creado_en: "2026-11-12T14:42:00Z",
  es_express: false,
  usuario_creador: { id: "00000000-0000-7000-8000-0000000000a1", nombre: "Usuaria Prueba" },
};
const CONTEOS = { vehiculos: 3, terminados: 1, con_error: 1, con_deuda: 0, bloqueados: 1, listos_tramite: 0 };
const tarjeta = (nombre: RegExp) => screen.getByRole("button", { name: nombre });
const tablaLote = () => screen.getByRole("table", { name: /semáforo de consultas/i });
const filas = () => within(tablaLote()).getAllByRole("rowheader").map((th) => th.closest("tr")!);

describe("/diagnosticos/{id} — HU #14562 AC23: cabecera con metadatos del lote", () => {
  it("muestra «ID · modo · trámite · fecha de creación · creador» de grilla.lote y las descargas", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla({ lote: LOTE_META })) }, { verDiagnosticos: true, descargarReportes: true });
    expect(screen.getByText(/con análisis ia/i)).toHaveTextContent(
      "ID 00000000 · Con análisis IA · Traspaso · Creado 12/11/2026 09:42 · Por Usuaria Prueba",
    );
    expect(estadoGlobal()).toHaveTextContent("En proceso");
    const descargas = screen.getByRole("region", { name: /descargas del lote/i });
    expect(within(descargas).getByRole("link", { name: "Excel del lote" })).toBeInTheDocument();
    expect(within(descargas).getByRole("button", { name: "PDF de todos (ZIP)" })).toBeInTheDocument();
  });

  it("AC31: sin el nombre del creador ni el trámite, omite lo ausente y no falla", async () => {
    await montar({
      [GRILLA]: () => Response.json(grilla({ lote: { ...LOTE_META, modo: "SIMPLE", tipo_tramite: null, usuario_creador: { id: "x", nombre: null } } })),
    });
    expect(screen.getByText(/· simple ·/i)).toHaveTextContent("ID 00000000 · Simple · Creado 12/11/2026 09:42");
    expect(screen.queryByText(/por usuaria/i)).toBeNull();
  });

  it("AC31: sin grilla.lote la cabecera no pinta metadatos", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(screen.queryByText(/creado \d{2}\/\d{2}\/\d{4}/i)).toBeNull();
  });
});

describe("/diagnosticos/{id} — HU #14562 AC27/AC31: tarjetas-filtro con los conteos del servidor", () => {
  it("pinta las seis tarjetas con los conteos, sin pedir el dictamen por vehículo", async () => {
    const api = await montar({ [GRILLA]: () => Response.json(grilla({ conteos: CONTEOS })) });
    expect(tarjeta(/^Vehículos: 3$/)).toHaveAttribute("aria-pressed", "false");
    expect(tarjeta(/^Terminados: 1$/)).toBeInTheDocument();
    expect(tarjeta(/^Con error: 1$/)).toBeInTheDocument();
    expect(tarjeta(/^Con deuda: 0$/)).toBeInTheDocument();
    expect(tarjeta(/^Bloqueados: 1$/)).toBeInTheDocument();
    expect(tarjeta(/^Listos para trámite: 0$/)).toBeInTheDocument();
    expect(api.mock.calls.filter(([url]) => String(url).includes("/dictamen"))).toHaveLength(0);
  });

  it("activar una tarjeta filtra la grilla en el cliente, es exclusiva y al volver a pulsarla se quita el filtro", async () => {
    const [a, b, c] = grilla().vehiculos;
    const api = await montar({ [GRILLA]: () => Response.json(grilla({ conteos: CONTEOS, vehiculos: [a, { ...b, veredicto: "BLOQUEADO" }, c] })) });
    const pedidas = desdesPedidos(api).length;
    fireEvent.click(tarjeta(/^Con error/));
    expect(filas()).toHaveLength(1);
    fireEvent.click(tarjeta(/^Bloqueados/));
    expect(tarjeta(/^Bloqueados/)).toHaveAttribute("aria-pressed", "true");
    expect(tarjeta(/^Con error/)).toHaveAttribute("aria-pressed", "false");
    expect(filas()).toHaveLength(1);
    expect(within(filas()[0]).getByRole("rowheader", { name: /TST002/ })).toBeInTheDocument();
    fireEvent.click(tarjeta(/^Bloqueados/));
    expect(tarjeta(/^Bloqueados/)).toHaveAttribute("aria-pressed", "false");
    expect(filas()).toHaveLength(3);
    expect(desdesPedidos(api).length).toBe(pedidas);
  });

  it("AC31: sin conteos oculta las tarjetas que dependen de ellos y el Dictamen se lee «Pendiente»", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(tarjeta(/^Vehículos: 3$/)).toBeInTheDocument();
    expect(tarjeta(/^Terminados: 1$/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Bloqueados/ })).toBeNull();
    expect(screen.queryByRole("button", { name: /^Listos para trámite/ })).toBeNull();
    const dictamenes = within(tablaLote()).getAllByRole("button", { name: /^Ver dictamen de / });
    expect(dictamenes).toHaveLength(3);
    dictamenes.forEach((b) => expect(b).toHaveTextContent("Pendiente"));
  });
});

describe("/diagnosticos/{id} — HU #14562 AC28: columna Dictamen, búsqueda y columnas fijas", () => {
  it("cada fila trae su Dictamen (Viable / Bloqueado / Pendiente) en su columna, con la marca del ajuste FLIT", async () => {
    const [a, b, c] = grilla().vehiculos;
    await montar({
      [GRILLA]: () => Response.json(grilla({ vehiculos: [{ ...a, veredicto: "VIABLE" }, { ...b, veredicto: "BLOQUEADO", dictamen_ajustado: true }, c] })),
    });
    expect(within(tablaLote()).getByRole("columnheader", { name: "Dictamen" })).toBeInTheDocument();
    expect(within(tablaLote()).getByRole("button", { name: "Ver dictamen de TST001" })).toHaveTextContent("Viable");
    const bloqueado = within(tablaLote()).getByRole("button", { name: "Ver dictamen de TST002" });
    expect(bloqueado).toHaveTextContent("Bloqueado");
    expect(bloqueado.closest("td")).toHaveTextContent(/ajustado por flit/i);
    expect(within(tablaLote()).getByRole("button", { name: "Ver dictamen de TST003" })).toHaveTextContent("Pendiente");
    expect(within(tablaLote()).getByRole("rowheader", { name: /TST001/ })).toHaveClass("sticky");
  });

  it("la búsqueda por placa o VIN filtra las filas en el cliente", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    fireEvent.change(screen.getByRole("searchbox", { name: /buscar por placa o vin/i }), { target: { value: "tst003" } });
    expect(filas()).toHaveLength(1);
    expect(within(filas()[0]).getByRole("rowheader", { name: /TST003/ })).toBeInTheDocument();
  });
});

describe("/diagnosticos/{id} — HU #14562 AC29: el dictamen ajustado se ve en el sondeo de reposo", () => {
  it("lote terminado: a los 30 s pide ?desde= y la fila cambia su Dictamen y las tarjetas Bloqueados y Listos", async () => {
    const v1 = grilla().vehiculos[0];
    const base = { ...CONTEOS, vehiculos: 1, terminados: 1, con_error: 0 };
    const terminado = grilla({ estado: "COMPLETADO", vehiculos: [{ ...v1, veredicto: "VIABLE" }], conteos: { ...base, bloqueados: 0, listos_tramite: 1 } });
    const ajuste = grilla({
      estado: "COMPLETADO",
      version: 13,
      desde: 12,
      vehiculos: [{ ...v1, veredicto: "BLOQUEADO", dictamen_ajustado: true }],
      conteos: { ...base, bloqueados: 1, listos_tramite: 0 },
    });
    const api = await montar({ [GRILLA]: enOrden(terminado, ajuste) });
    expect(tarjeta(/^Bloqueados: 0$/)).toBeInTheDocument();
    await avanzar(INTERVALO_REPOSO_MS);
    expect(desdesPedidos(api)).toEqual([0, 12]);
    expect(screen.getByRole("button", { name: "Ver dictamen de TST001" })).toHaveTextContent("Bloqueado");
    expect(tarjeta(/^Bloqueados: 1$/)).toBeInTheDocument();
    expect(tarjeta(/^Listos para trámite: 0$/)).toBeInTheDocument();
    expect(navegacion.refresh).not.toHaveBeenCalled();
  });
});

describe("/diagnosticos/{id} — HU #14562 AC26: refresco detenido", () => {
  it("tras fallos repetidos avisa «desactualizados desde hh:mm» con la grilla visible; «Actualizar ahora» lo quita y reanuda", async () => {
    vi.setSystemTime(new Date("2026-11-13T14:05:00Z"));
    let falla = false;
    const api = await montar({ [GRILLA]: () => (falla ? Response.json({ error: "x" }, { status: 503 }) : Response.json(grilla())) });
    falla = true;
    await avanzar(INTERVALO_SONDEO_MS * FALLOS_ANTES_DE_DETENER);
    expect(screen.getByText(/datos desactualizados desde 09:05/i)).toBeInTheDocument();
    expect(tablaLote()).toBeInTheDocument();
    falla = false;
    const antes = desdesPedidos(api).length;
    fireEvent.click(screen.getByRole("button", { name: "Actualizar ahora" }));
    await avanzar();
    expect(screen.queryByText(/datos desactualizados/i)).toBeNull();
    await avanzar(INTERVALO_SONDEO_MS);
    expect(desdesPedidos(api).length).toBe(antes + 2);
  });
});

describe("/diagnosticos/{id} — HU #14562 AC33/AC34: estimado plegable y enmascarado", () => {
  it("AC33: con el estimado del lote terminado aparece la tarjeta plegable «Estimado frente a consumido», cerrada", async () => {
    await montar({
      [GRILLA]: () =>
        Response.json(grilla({ estado: "COMPLETADO", vehiculos: [grilla().vehiculos[0]], estimado_consultas: [{ fuente: "RUNT", estimadas: 1, consumidas: 1 }] })),
    });
    const boton = screen.getByRole("button", { name: /estimado frente a consumido/i });
    expect(boton).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(boton);
    expect(boton).toHaveAttribute("aria-expanded", "true");
  });

  it("AC33: sin estimado (otra configuración) la tarjeta no aparece", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla({ estado: "COMPLETADO", vehiculos: [grilla().vehiculos[0]] })) });
    expect(screen.queryByRole("button", { name: /estimado frente a consumido/i })).toBeNull();
  });

  it("AC34: los documentos del propietario siguen enmascarados y ningún documento sale en claro", async () => {
    const v = { ...grilla().vehiculos[0], propietarios: [{ tipo_documento: "CC", documento_enmascarado: "******7710" }] };
    await montar({ [GRILLA]: () => Response.json(grilla({ vehiculos: [v] })) });
    expect(within(tablaLote()).getByText(/CC .*7710/)).toBeInTheDocument();
    expect(tablaLote().textContent).not.toMatch(/\d{6,}/);
  });
});
