import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import TableroDiagnostico from "@/app/(app)/diagnosticos/[id]/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { FALLOS_ANTES_DE_DETENER, INTERVALO_REPOSO_MS, INTERVALO_SONDEO_MS } from "@/lib/tablero/useGrillaIncremental";
import { ID, V1, celda, desdesPedidos, grilla, llamadas, loteTerminadoConErrores, sesion, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";
import { deudaSinPendientes, saneamientoEnGestion } from "@/components/saneamiento/__tests__/datosDeuda";
import { accionFila, accionesDeFila, accionesEnTodas, disparadorDe, quizaAccionFila } from "@/components/tablero/__tests__/menuFila";

// Uso de ejemplo: /diagnosticos/{id} (a donde navegan la confirmación de la carga y el Express) abre el tablero del lote:
// tarjeta título con el nombre, estado global y avance, la grilla de semáforos con sondeo cada 3 s y «Cerrar panel»
// cuando el lote terminó con errores. Cuatro estados: cargando, error con Reintentar, vacío (sin vehículos) y lleno.

const navegacion = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn(), notFound: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => navegacion, notFound: navegacion.notFound }));

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

/**
 * Chip del estado del lote. HU #14562 (auditoría m-5): EN_PROCESO usa la EtiquetaEnProceso del kit; N2 (reauditoría): con
 * `nombreAccesible`, todos los estados se nombran igual, «Estado del lote: …».
 */
const estadoGlobal = () => screen.getByRole("status", { name: /^Estado del lote: / });

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
  navegacion.push.mockReset();
  navegacion.replace.mockReset();
  navegacion.notFound.mockReset();
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

  it("HU #14562 AC49/AC57 error 404 (no existe o es de otro usuario con visibilidad PROPIOS): la página 404 de la app", async () => {
    await montar({ [GRILLA]: () => Response.json({ error: "No existe" }, { status: 404 }) });
    expect(navegacion.notFound).toHaveBeenCalled();
    expect(navegacion.replace).not.toHaveBeenCalled();
  });

  it("HU #14562 AC49: un 5xx no es la 404: error de módulo con Reintentar y sin notFound", async () => {
    await montar({ [GRILLA]: () => Response.json({ error: "x" }, { status: 502 }) });
    expect(screen.getByRole("button", { name: "Reintentar" })).toBeInTheDocument();
    expect(navegacion.notFound).not.toHaveBeenCalled();
  });

  it("HU #14562 AC49: un 403 de sesión de la grilla (SIN_EMPRESA) lleva a la página 403 con su código", async () => {
    await montar({ [GRILLA]: () => Response.json({ code: "SIN_EMPRESA", error: "x" }, { status: 403 }) });
    expect(navegacion.replace).toHaveBeenCalledWith("/403?code=SIN_EMPRESA");
    expect(navegacion.notFound).not.toHaveBeenCalled();
  });

  it("vacío: un lote sin vehículos lo dice y no muestra la grilla", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla({ estado: "CREADO", vehiculos: [], avance: { vehiculos: 0, terminados: 0 } })) });
    expect(screen.getByRole("heading", { name: /todavía no tiene vehículos/i })).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("HU #14562 AC57: el lote sin vehículos ofrece «Cargar vehículos» solo con crearDiagnosticos", async () => {
    const vacio = () => Response.json(grilla({ estado: "CREADO", vehiculos: [], avance: { vehiculos: 0, terminados: 0 } }));
    await montar({ [GRILLA]: vacio }, { verDiagnosticos: true, crearDiagnosticos: true });
    expect(screen.getByRole("link", { name: "Cargar vehículos" })).toHaveAttribute("href", `/diagnosticos/${ID}/carga`);
  });

  it("HU #14562 AC57 (negativo): sin crearDiagnosticos el lote sin vehículos no ofrece «Cargar vehículos»", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla({ estado: "CREADO", vehiculos: [], avance: { vehiculos: 0, terminados: 0 } })) });
    expect(screen.getByRole("heading", { name: /todavía no tiene vehículos/i })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Cargar vehículos" })).toBeNull();
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
  it("AC4: el usuario sin capacidad de reprocesar no ve «Reprocesar»; sí ve el Dictamen de cada fila", async () => {
    await montar({ [GRILLA]: () => Response.json(loteTerminadoConErrores({ estado: "INCOMPLETO" })) });
    const tabla = screen.getByRole("table");
    expect(within(tabla).queryAllByRole("button", { name: /reprocesar/i })).toHaveLength(0);
    expect(tabla.querySelectorAll('[data-columna="dictamen"]')).toHaveLength(2);
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
    // FE-2 (spec-ui §2.3): la columna se titula siempre «Acciones» y el PDF es un ítem del menú de la fila.
    expect(within(tabla).getByRole("columnheader", { name: "Acciones" })).toBeInTheDocument();
    expect(within(tabla).queryByRole("columnheader", { name: "Reporte" })).toBeNull();
    const pdf1 = accionFila("TST001", "Descargar PDF de TST001");
    expect(pdf1.tagName).toBe("A");
    expect(pdf1).toHaveAttribute("href", `/api/v1/diagnostico/diagnosticos/${ID}/vehiculos/vehiculo-1/pdf`);
    expect(pdf1).toHaveAttribute("download");
    const pdf2 = accionFila("TST002", "Descargar PDF de TST002");
    expect(pdf2).toHaveAttribute("aria-disabled", "true");
    expect(pdf2).toHaveAccessibleDescription("Disponible cuando terminen las consultas de este vehículo");
    const barra = screen.getByRole("region", { name: "Descargas del lote" });
    expect(within(barra).getByRole("link", { name: "Excel del lote" })).toBeInTheDocument();
    expect(within(barra).getByRole("button", { name: "PDF de todos (ZIP)" })).toBeInTheDocument();
  });

  it("AC6: lote INCOMPLETO, el PDF del vehículo con celdas en error está habilitado con la ayuda de «no disponible»", async () => {
    await montar({ [GRILLA]: () => Response.json(loteTerminadoConErrores({ estado: "INCOMPLETO" })) }, DESCARGA);
    const pdf4 = accionFila("TST004", "Descargar PDF de TST004");
    expect(pdf4).toHaveAccessibleDescription("Las consultas no disponibles aparecerán así en el PDF");
  });

  it("AC4: sin la capacidad descargarReportes no hay ningún control de descarga ni la columna Reporte", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) }, USUARIO);
    expect(screen.getByRole("table", { name: /semáforo de consultas/i })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Descargas del lote" })).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Reporte" })).not.toBeInTheDocument();
    // HU #14584 §6.4: la columna «Acciones» existe siempre por «Ver dictamen»; sin la capacidad no hay descarga en el menú.
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
    expect(within(migas).getByRole("link", { name: "Diagnósticos" })).toHaveAttribute("href", "/diagnosticos");
    expect(within(items[1]).getByText(grilla().nombre)).toHaveAttribute("aria-current", "page");
  });

  it("sin ?desde= vuelve a /diagnosticos", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(volver()).toHaveAttribute("href", "/diagnosticos");
  });

  it("con ?desde= vuelve al listado con sus filtros como search params de /", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) }, USUARIO, ID, "desde=2026-10-01&estado=EN_PROCESO&nombre=Flota+prueba&pagina=2");
    const href = volver().getAttribute("href")!;
    expect(href.startsWith("/diagnosticos?")).toBe(true);
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
    expect(href === "/diagnosticos" || href.startsWith("/diagnosticos?")).toBe(true);
    expect(new URL(href, "http://localhost").origin).toBe("http://localhost");
    expect(href).not.toMatch(/malicioso|javascript/);
  });

  it("también en los estados de error: el botón de volver está siempre", async () => {
    await montar({ [GRILLA]: () => Response.json({ error: "x" }, { status: 500 }) });
    expect(volver()).toHaveAttribute("href", "/diagnosticos");
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
/** HU #14584 §6.4: la columna Dictamen es un dato (chip), no un botón; el panel se abre con «Ver dictamen» del menú. */
const dictamenDe = (tr: HTMLElement) => tr.querySelector<HTMLElement>('[data-columna="dictamen"]')!;
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
    const dictamenes = filas().map(dictamenDe);
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
    const [f1, f2, f3] = filas();
    expect(dictamenDe(f1)).toHaveTextContent("Viable");
    expect(dictamenDe(f2)).toHaveTextContent("Bloqueado");
    expect(dictamenDe(f2)).toHaveTextContent(/ajustado por flit/i);
    expect(dictamenDe(f3)).toHaveTextContent("Pendiente");
    // HU #14584 §6.4: el veredicto es dato; ningún botón en la columna.
    for (const tr of [f1, f2, f3]) expect(within(dictamenDe(tr)).queryByRole("button")).toBeNull();
    // Fija desde 1024 px; por debajo, la fila es tarjeta (auditoría B2, AC56).
    expect(within(tablaLote()).getByRole("rowheader", { name: /TST001/ })).toHaveClass("lg:sticky");
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
    expect(dictamenDe(filas()[0])).toHaveTextContent("Bloqueado");
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

  it("AC34 / HU #14585 (C5, SP-19): la grilla no pinta documentos, ni en claro ni enmascarados, aunque llegaran", async () => {
    const v = { ...grilla().vehiculos[0], propietarios: [{ tipo_documento: "CC", documento_enmascarado: "******7710" }] };
    await montar({ [GRILLA]: () => Response.json(grilla({ vehiculos: [v] })) });
    expect(tablaLote().innerHTML).not.toContain("7710");
    expect(tablaLote().textContent).not.toMatch(/\d{6,}/);
  });
});

// Uso de ejemplo (HU #14577 AC5–AC11, spec-ui §2): en el tablero, con la capacidad `sanear`, cada fila lleva la bandera
// BotonSanear antes de «Crear trámite»; la celda Dictamen suma la etiqueta del saneamiento; los chips «Con / Sin
// saneamiento» filtran (y quedan en ?saneamiento=); la barra de selección ofrece «Crear trámites (N)» y «Sanear (N)».
describe("/diagnosticos/{id} — HU #14577 PS-1: saneamiento en el tablero", () => {
  type EstadoSan = "SOLICITADO" | "EN_GESTION" | "SANEADO" | "NO_PROCEDE" | null;
  const SANEA: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, sanear: true };
  const SANEA_DERIVA: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, sanear: true, derivar: true };
  const RUTA_MASIVO = "/saneamientos/solicitudes";
  const terminadas = (n: number) => V1.celdas.map((c) => ({ ...c, id: `${c.id}-${n}` }));
  const san = (estado: EstadoSan, motivo: "NO_TERMINADO" | "YA_SOLICITADO" | null = estado ? "YA_SOLICITADO" : null) => ({
    estado,
    elegible: estado === null && motivo === null,
    motivo_no_elegible: motivo,
  });
  const deriva = (elegible: boolean) => ({ derivacion: { elegible, motivo_no_elegible: elegible ? null : "NO_VIABLE", estado: null, tramite: null } });

  /**
   * TST001 saneable y derivable; TST002 SOLICITADO; TST003 EN_GESTION; TST004 no terminado; TST005 saneable y no derivable.
   * Lote COMPLETADO (sin sondeo cada 3 s).
   */
  function loteSan(cambios: Record<string, Partial<FilaVehiculo>> = {}): Grilla {
    const filas: FilaVehiculo[] = [
      { ...V1, ...deriva(true), saneamiento: san(null) },
      { ...vehiculo(2, terminadas(2)), ...deriva(false), saneamiento: san("SOLICITADO") },
      { ...vehiculo(3, terminadas(3)), ...deriva(false), saneamiento: san("EN_GESTION") },
      { ...vehiculo(4, [celda(4, "RUNT", "EN_CURSO")]), ...deriva(false), saneamiento: san(null, "NO_TERMINADO") },
      { ...vehiculo(5, terminadas(5)), ...deriva(false), saneamiento: san(null) },
    ].map((v) => ({ ...v, ...cambios[v.id] }));
    return grilla({ estado: "COMPLETADO", vehiculos: filas });
  }

  async function montarSan(rutas: Rutas, capacidades: Partial<CapacidadesDiagnostico> = SANEA, saneamiento?: string) {
    const api = simularApi({ "GET /sesion": () => Response.json(sesion(capacidades)), ...rutas });
    render(
      <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
        {await TableroDiagnostico({ params: Promise.resolve({ id: ID }), searchParams: Promise.resolve(saneamiento ? { saneamiento } : {}) })}
      </DiagnosticoShell>,
    );
    await avanzar();
    await avanzar();
    return api;
  }

  const fila = (placa: string) => within(tablaLote()).getByRole("rowheader", { name: new RegExp(placa) }).closest("tr")!;
  const placasVisibles = () => filas().map((tr) => within(tr).getByRole("rowheader").textContent!.match(/TST\d{3}/)![0]);
  const chip = (nombre: string) => within(screen.getByRole("group", { name: "Filtrar por saneamiento" })).getByRole("button", { name: nombre });
  const consulta = () => new URLSearchParams(window.location.search).get("saneamiento");
  // FE-2: la bandera es el ítem «Solicitar / Quitar solicitud de saneamiento» del menú «Acciones» de la fila.
  const bandera = (placa: string) => accionFila(placa, `Solicitar saneamiento de la placa ${placa}`);
  const quitar = (placa: string) => quizaAccionFila(placa, `Quitar solicitud de saneamiento de la placa ${placa}`);
  const celdaDictamen = (placa: string) => dictamenDe(fila(placa));

  beforeEach(() => window.history.replaceState(null, "", "/diagnosticos/x"));

  it("AC5: la bandera va antes de «Crear trámite», apagada; un clic la enciende, llama PUT y la fila muestra la etiqueta", async () => {
    const ruta = `/diagnosticos/${ID}/vehiculos/vehiculo-1/saneamiento`;
    const api = await montarSan(
      { [GRILLA]: () => Response.json(loteSan()), [`PUT ${ruta}`]: () => Response.json({ id: "s1", estado: "SOLICITADO" }, { status: 201 }) },
      SANEA_DERIVA,
    );
    // HU #14584 §6.4: «Ver dictamen» abre el menú (grupo propio); luego el orden D-103.
    expect(accionesDeFila("TST001").slice(0, 3)).toEqual(["Ver dictamen de TST001", "Solicitar saneamiento de la placa TST001", "Crear trámite para la placa TST001"]);
    expect(within(fila("TST001")).queryByText("Saneamiento solicitado")).toBeNull();
    fireEvent.click(bandera("TST001"));
    expect(within(celdaDictamen("TST001")).getByText("Saneamiento solicitado")).toBeInTheDocument();
    expect(quitar("TST001")).not.toBeNull();
    await avanzar();
    expect(llamadas(api, "PUT", ruta)).toHaveLength(1);
    expect(quitar("TST001")).not.toBeNull();
    // Región viva de la pantalla (spec §1.6) y, desde la ronda 2 de E2E, también el status visible bajo «Acciones» (spec §2.5).
    const avisos = screen.getAllByText("Saneamiento solicitado para TST001.").map((n) => n.closest("[role='status']"));
    expect(avisos).toHaveLength(2);
    expect(avisos.filter((a) => a?.classList.contains("sr-only"))).toHaveLength(1);
    expect(within(fila("TST001")).getByRole("status")).toHaveTextContent("Saneamiento solicitado para TST001.");
  });

  it("AC6: si el PUT falla, la fila vuelve a «sin saneamiento» y la etiqueta desaparece", async () => {
    const ruta = `/diagnosticos/${ID}/vehiculos/vehiculo-1/saneamiento`;
    await montarSan({ [GRILLA]: () => Response.json(loteSan()), [`PUT ${ruta}`]: () => Response.json({ code: "VEHICULO_NO_TERMINADO", error: "x" }, { status: 409 }) });
    fireEvent.click(bandera("TST001"));
    await avanzar();
    expect(bandera("TST001")).not.toHaveAttribute("aria-disabled");
    expect(quitar("TST001")).toBeNull();
    expect(within(fila("TST001")).queryByText("Saneamiento solicitado")).toBeNull();
    expect(within(fila("TST001")).getByRole("alert")).toHaveTextContent("Este vehículo aún tiene consultas por terminar.");
  });

  it("AC7: EN_GESTION es «Ver saneamiento» con el estado en el nombre y el clic abre el panel del vehículo", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()) });
    const ver = accionFila("TST003", "Ver saneamiento de la placa TST003: En saneamiento");
    expect(ver).not.toHaveAttribute("aria-pressed");
    fireEvent.click(ver);
    await avanzar();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("AC8: no terminado: bandera aria-disabled con «Faltan consultas por terminar»", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()) });
    expect(bandera("TST004")).toHaveAttribute("aria-disabled", "true");
    expect(bandera("TST004")).toHaveAccessibleDescription("Faltan consultas por terminar");
  });

  it("AC8 (negativo): sin la capacidad sanear no hay bandera en las filas sin saneamiento; las que tienen, solo «Ver saneamiento»", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()) }, { verDiagnosticos: true });
    expect(accionesEnTodas(/^(Solicitar|Quitar solicitud de) saneamiento/)).toHaveLength(0);
    expect(quizaAccionFila("TST002", "Ver saneamiento de la placa TST002: Saneamiento solicitado")).not.toBeNull();
    expect(within(tablaLote()).getByRole("columnheader", { name: "Acciones" })).toBeInTheDocument();
  });

  it("AC9: la etiqueta va en la celda Dictamen, debajo del veredicto; sin saneamiento no hay etiqueta", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()) });
    const dictamen = dictamenDe(fila("TST002"));
    expect(within(dictamen).getByText("Saneamiento solicitado")).toBeInTheDocument();
    // UA-1 (auditoría UI, supera spec-ui §2.4): el estado solo lo informa la celda Dictamen; la celda Acciones no lo repite.
    const acciones = disparadorDe("TST002")!.closest("td")!;
    expect(acciones.querySelector("[data-parte='chip-saneamiento']")).toBeNull();
    expect(within(acciones).queryByText(/Saneamiento:/)).toBeNull();
    expect(dictamenDe(fila("TST003"))).toHaveTextContent("En saneamiento");
    const sin = dictamenDe(fila("TST001"));
    expect(sin).not.toHaveTextContent(/saneamiento/i);
  });

  it("AC9: «Con» y «Sin saneamiento» filtran en el cliente, son excluyentes, se apagan con otro clic y quedan en la URL", async () => {
    const api = await montarSan({ [GRILLA]: () => Response.json(loteSan()) });
    const pedidas = desdesPedidos(api).length;
    expect(chip("Con saneamiento")).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(chip("Con saneamiento"));
    expect(placasVisibles()).toEqual(["TST002", "TST003"]);
    expect(consulta()).toBe("con");
    fireEvent.click(chip("Sin saneamiento"));
    expect(chip("Sin saneamiento")).toHaveAttribute("aria-pressed", "true");
    expect(chip("Con saneamiento")).toHaveAttribute("aria-pressed", "false");
    expect(placasVisibles()).toEqual(["TST001", "TST004", "TST005"]);
    expect(consulta()).toBe("sin");
    fireEvent.click(chip("Sin saneamiento"));
    expect(placasVisibles()).toHaveLength(5);
    expect(consulta()).toBeNull();
    expect(window.location.pathname).toBe("/diagnosticos/x");
    expect(desdesPedidos(api).length).toBe(pedidas);
  });

  it("AC9: con ?saneamiento=con el chip nace activo y la grilla ya viene filtrada; se combina con la búsqueda", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()) }, SANEA, "con");
    expect(chip("Con saneamiento")).toHaveAttribute("aria-pressed", "true");
    expect(placasVisibles()).toEqual(["TST002", "TST003"]);
    fireEvent.change(screen.getByRole("searchbox", { name: /buscar por placa o vin/i }), { target: { value: "tst003" } });
    expect(placasVisibles()).toEqual(["TST003"]);
  });

  it("AC9: vacío por el filtro: «Ningún vehículo coincide» y «Limpiar filtros» quita chip y búsqueda", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()) }, SANEA, "con");
    fireEvent.change(screen.getByRole("searchbox", { name: /buscar por placa o vin/i }), { target: { value: "tst001" } });
    // EstadoVacio con `anunciar` nace vacío y recibe el contenido pasado REGION_VIVA_RETARDO_MS (HU #14562 · M11).
    await avanzar(100);
    expect(screen.getByRole("heading", { name: "Ningún vehículo coincide" })).toBeInTheDocument();
    expect(screen.getByText("Quita el filtro de saneamiento o prueba con otra búsqueda.")).toBeInTheDocument();
    // HU #14584 §6.6: «Limpiar filtros» está en la barra y en el vacío; se usa el del vacío.
    fireEvent.click(screen.getAllByRole("button", { name: "Limpiar filtros" }).at(-1)!);
    expect(placasVisibles()).toHaveLength(5);
    expect(chip("Con saneamiento")).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByRole("searchbox", { name: /buscar por placa o vin/i })).toHaveValue("");
    expect(consulta()).toBeNull();
  });

  it("§2.6: filas sin la clave saneamiento (backend previo): sin chips, sin bandera y sin etiqueta", async () => {
    await montarSan({ [GRILLA]: () => Response.json(grilla({ estado: "COMPLETADO" })) });
    expect(screen.queryByRole("group", { name: "Filtrar por saneamiento" })).toBeNull();
    expect(screen.queryAllByRole("button", { name: /saneamiento/i })).toHaveLength(0);
    expect(accionesEnTodas(/saneamiento/i)).toHaveLength(0);
  });

  it("AC10: casilla habilitada si es elegible para alguna acción; «Crear trámites (N)» y «Sanear (N)» cuentan cada una lo suyo", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()) }, SANEA_DERIVA);
    const casilla = (placa: string) => within(fila(placa)).getByRole("checkbox", { name: `Seleccionar la placa ${placa}` });
    expect(casilla("TST001")).toBeEnabled();
    expect(casilla("TST005")).toBeEnabled();
    expect(casilla("TST002")).toBeDisabled();
    expect(casilla("TST004")).toBeDisabled();
    expect(casilla("TST004")).toHaveAccessibleDescription(/ · Faltan consultas por terminar$/);
    fireEvent.click(casilla("TST005"));
    const region = screen.getByRole("region", { name: "Selección" });
    expect(within(region).getByRole("button", { name: "Crear trámites (0)" })).toHaveAttribute("aria-disabled", "true");
    expect(within(region).getByRole("button", { name: "Sanear (1)" })).not.toHaveAttribute("aria-disabled");
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }));
    expect(within(region).getByRole("button", { name: "Crear trámites (1)" })).toBeInTheDocument();
    expect(within(region).getByRole("button", { name: "Sanear (2)" })).toBeInTheDocument();
  });

  it("AC10: con solo la capacidad sanear la barra no ofrece «Crear trámites»", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()) }, SANEA);
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }));
    const region = screen.getByRole("region", { name: "Selección" });
    expect(within(region).queryByRole("button", { name: /crear trámites/i })).toBeNull();
    expect(within(region).getByRole("button", { name: "Sanear (2)" })).toBeInTheDocument();
  });

  it("AC11: «Sanear (N)» con omitidos: mensaje con los motivos, las solicitadas con su etiqueta sin recargar y las omitidas siguen marcadas", async () => {
    let solicitado = false;
    const api = await montarSan(
      {
        [GRILLA]: () => Response.json(solicitado ? loteSan({ "vehiculo-1": { saneamiento: san("SOLICITADO") } }) : loteSan()),
        [`POST ${RUTA_MASIVO}`]: () => {
          solicitado = true;
          return Response.json({ solicitados: 1, omitidos: [{ vehiculo_id: "vehiculo-5", motivo: "NO_TERMINADO" }] });
        },
      },
      SANEA_DERIVA,
    );
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }));
    const sanear = screen.getByRole("button", { name: "Sanear (2)" });
    sanear.focus();
    fireEvent.click(sanear);
    await avanzar();
    await avanzar();
    const [, init] = llamadas(api, "POST", RUTA_MASIVO)[0] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ vehiculo_ids: ["vehiculo-1", "vehiculo-5"] });
    const texto = "Se solicitó el saneamiento de 1 vehículo. 1 se omitió: 1 con consultas por terminar.";
    const aviso = screen.getAllByText(texto).map((n) => n.closest("[tabindex='-1']")).find(Boolean) as HTMLElement;
    expect(aviso).toHaveTextContent("Saneamiento solicitado");
    // UA-4: el aviso visible no es región viva; el anuncio va solo por la región de la pantalla (el foco no se movió).
    expect(aviso.querySelector("[role='alert'], [role='status'], [aria-live]")).toBeNull();
    expect(dictamenDe(fila("TST001"))).toHaveTextContent("Saneamiento solicitado");
    expect(within(fila("TST005")).getByRole("checkbox")).toBeChecked();
    expect(within(fila("TST001")).getByRole("checkbox")).not.toBeChecked();
    expect(screen.getByRole("region", { name: "Selección" })).toHaveTextContent("1 vehículo seleccionado");
    expect(screen.getByRole("button", { name: /^Sanear \(/ })).toHaveFocus();
    // El mismo texto en la región viva de la pantalla.
    expect(screen.getAllByText(texto).some((n) => n.closest(".sr-only"))).toBe(true);
    expect(desdesPedidos(api).length).toBeGreaterThan(1);
  });

  it("UA-4: todas solicitadas (selección vacía): el foco va al aviso y la región viva no repite el texto", async () => {
    let solicitado = false;
    await montarSan(
      {
        [GRILLA]: () =>
          Response.json(solicitado ? loteSan({ "vehiculo-1": { saneamiento: san("SOLICITADO") }, "vehiculo-5": { saneamiento: san("SOLICITADO") } }) : loteSan()),
        [`POST ${RUTA_MASIVO}`]: () => {
          solicitado = true;
          return Response.json({ solicitados: 2, omitidos: [] });
        },
      },
      SANEA,
    );
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }));
    fireEvent.click(screen.getByRole("button", { name: "Sanear (2)" }));
    await avanzar();
    await avanzar();
    const texto = "Se solicitó el saneamiento de 2 vehículos.";
    const nodos = screen.getAllByText(texto);
    expect(nodos.some((n) => n.closest(".sr-only"))).toBe(false);
    const aviso = nodos.map((n) => n.closest("[tabindex='-1']")).find(Boolean) as HTMLElement;
    expect(aviso).toHaveFocus();
    expect(aviso.querySelector("[role='alert'], [role='status'], [aria-live]")).toBeNull();
  });

  it("AC11: 400 LISTA_INVALIDA: mensaje de error y la selección se conserva", async () => {
    await montarSan(
      { [GRILLA]: () => Response.json(loteSan()), [`POST ${RUTA_MASIVO}`]: () => Response.json({ code: "LISTA_INVALIDA", error: "x" }, { status: 400 }) },
      SANEA,
    );
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }));
    fireEvent.click(screen.getByRole("button", { name: "Sanear (2)" }));
    await avanzar();
    expect(screen.getByRole("alert")).toHaveTextContent("No pudimos solicitar el saneamiento. Intenta de nuevo.");
    expect(screen.getByRole("region", { name: "Selección" })).toHaveTextContent("2 vehículos seleccionados");
    expect(within(fila("TST001")).getByRole("checkbox")).toBeChecked();
  });

  it("AC11: error de red: mensaje de error y la selección se conserva", async () => {
    await montarSan(
      {
        [GRILLA]: () => Response.json(loteSan()),
        [`POST ${RUTA_MASIVO}`]: () => {
          throw new TypeError("Failed to fetch");
        },
      },
      SANEA,
    );
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }));
    fireEvent.click(screen.getByRole("button", { name: "Sanear (2)" }));
    await avanzar();
    expect(screen.getByRole("alert")).toHaveTextContent("No pudimos solicitar el saneamiento. Intenta de nuevo.");
    expect(within(fila("TST005")).getByRole("checkbox")).toBeChecked();
  });
  // ---------- HU #14577 FE-C: PS-2 desde el tablero (cableado-fe-c.md, D-29) ----------
  // Uso de ejemplo: «Ver saneamiento» de la fila, «Ver deuda» de la fila y «Ver deuda y sanear» del dictamen bloqueado abren
  // PanelDeudaSaneamiento (PS-2), no el panel del dictamen; al cerrarlo el foco vuelve al control que lo abrió.
  const rutaVeh = (n: number, final: string) => `/diagnosticos/${ID}/vehiculos/vehiculo-${n}/${final}`;
  const sinSan = () => Response.json({ code: "SIN_SANEAMIENTO", error: "x" }, { status: 404 });
  const rutasPanel = (n: number, saneamiento: () => Response = sinSan) => ({
    [`GET ${rutaVeh(n, "deuda")}`]: () =>
      Response.json(deudaSinPendientes({ vehiculo: { id: `vehiculo-${n}`, placa: `TST00${n}` } })),
    [`GET ${rutaVeh(n, "saneamiento")}`]: saneamiento,
  });
  const panelDeuda = () => screen.getByRole("dialog", { name: "Deuda y saneamiento" });

  it("AC13/D-29: «Ver saneamiento» abre PS-2 «Deuda y saneamiento» del vehículo (no el dictamen) y al cerrar el foco vuelve a la bandera", async () => {
    const api = await montarSan({ [GRILLA]: () => Response.json(loteSan()), ...rutasPanel(3, () => Response.json(saneamientoEnGestion())) });
    fireEvent.click(accionFila("TST003", "Ver saneamiento de la placa TST003: En saneamiento"));
    await avanzar();
    await avanzar();
    expect(panelDeuda()).toBeInTheDocument();
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    expect(llamadas(api, "GET", rutaVeh(3, "deuda"))).toHaveLength(1);
    expect(llamadas(api, "GET", rutaVeh(3, "dictamen"))).toHaveLength(0);
    fireEvent.click(within(panelDeuda()).getAllByRole("button", { name: /cerrar/i })[0]);
    await avanzar();
    expect(screen.queryByRole("dialog")).toBeNull();
    // FE-2 (spec-ui §2.7.4): el foco vuelve al disparador «Acciones» de la fila.
    expect(disparadorDe("TST003")).toHaveFocus();
  });

  it("AC13: «Ver deuda» de la fila solo en vehículos terminados; abre PS-2 y devuelve el foco al cerrar", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()), ...rutasPanel(1) });
    expect(quizaAccionFila("TST004", /^Ver deuda/)).toBeNull();
    const ver = accionFila("TST001", "Ver deuda de la placa TST001");
    expect(ver).toHaveTextContent("Ver deuda");
    fireEvent.click(ver);
    await avanzar();
    await avanzar();
    expect(within(panelDeuda()).getByRole("heading", { name: "Deuda por fuente" })).toBeInTheDocument();
    fireEvent.keyDown(document.activeElement ?? panelDeuda(), { key: "Escape" });
    await avanzar();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(disparadorDe("TST001")).toHaveFocus();
  });

  it("AC13: «Ver deuda» también sin la capacidad sanear (solo lectura) y sin la bandera en la cabecera", async () => {
    await montarSan({ [GRILLA]: () => Response.json(loteSan()), ...rutasPanel(1) }, { verDiagnosticos: true });
    fireEvent.click(accionFila("TST001", "Ver deuda de la placa TST001"));
    await avanzar();
    await avanzar();
    expect(within(panelDeuda()).getByRole("heading", { name: "Deuda por fuente" })).toBeInTheDocument();
    expect(within(panelDeuda()).queryByRole("button", { name: /saneamiento de la placa/ })).toBeNull();
  });

  it("AC13: «Ver deuda y sanear» del dictamen bloqueado reemplaza el panel del dictamen por PS-2 y al cerrar el foco vuelve al disparador «Acciones»", async () => {
    const dictamen = { version: 1, veredicto: "BLOQUEADO", estado_redaccion: "PENDIENTE", alertamientos: [], catalogo_version: 1, creado_en: "2026-11-03T15:00:00+00:00" };
    await montarSan({
      [GRILLA]: () => Response.json(loteSan({ "vehiculo-1": { veredicto: "BLOQUEADO" } })),
      [`GET ${rutaVeh(1, "dictamen")}`]: () => Response.json(dictamen),
      ...rutasPanel(1),
    });
    // HU #14584 §6.4: «Ver dictamen» vive en el menú «Acciones» de la fila.
    disparadorDe("TST001")!.focus();
    fireEvent.click(accionFila("TST001", "Ver dictamen de TST001"));
    await avanzar();
    await avanzar();
    fireEvent.click(screen.getByRole("button", { name: "Ver deuda y sanear" }));
    await avanzar();
    await avanzar();
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    expect(panelDeuda()).toBeInTheDocument();
    fireEvent.click(within(panelDeuda()).getAllByRole("button", { name: /cerrar/i })[0]);
    await avanzar();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(disparadorDe("TST001")).toHaveFocus();
  });

  it("cableado §1: la bandera de la cabecera de PS-2 marca con PUT, la fila del tablero toma la etiqueta y el detalle se relee solo tras la confirmación", async () => {
    let soltar: (r: Response) => void = () => {};
    let solicitado = false;
    const api = await montarSan({
      [GRILLA]: () => Response.json(loteSan()),
      ...rutasPanel(5, () =>
        solicitado ? Response.json(saneamientoEnGestion({ estado: "SOLICITADO", tomado_en: null, renglones: [], historial: [], observacion_flit: null })) : sinSan(),
      ),
      [`PUT ${rutaVeh(5, "saneamiento")}`]: () => new Promise<Response>((r) => (soltar = r)),
    });
    fireEvent.click(accionFila("TST005", "Ver deuda de la placa TST005"));
    await avanzar();
    await avanzar();
    const cabecera = within(panelDeuda()).getByRole("button", { name: "Solicitar saneamiento de la placa TST005" });
    expect(cabecera.className).toContain("var(--kit-tinta)");
    expect(cabecera).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(cabecera);
    expect(cabecera).toHaveAttribute("aria-pressed", "true");
    expect(llamadas(api, "GET", rutaVeh(5, "saneamiento"))).toHaveLength(1);
    await act(async () => {
      solicitado = true;
      soltar(Response.json({ id: "s5", estado: "SOLICITADO" }, { status: 201 }));
      await vi.advanceTimersByTimeAsync(0);
    });
    await avanzar();
    expect(llamadas(api, "PUT", rutaVeh(5, "saneamiento"))).toHaveLength(1);
    expect(llamadas(api, "GET", rutaVeh(5, "saneamiento"))).toHaveLength(2);
    expect(within(panelDeuda()).getByText("Saneamiento solicitado para TST005.")).toBeInTheDocument();
    fireEvent.click(within(panelDeuda()).getAllByRole("button", { name: /cerrar/i })[0]);
    await avanzar();
    expect(dictamenDe(fila("TST005"))).toHaveTextContent("Saneamiento solicitado");
    expect(quizaAccionFila("TST005", "Quitar solicitud de saneamiento de la placa TST005")).not.toBeNull();
  });

  it("§2.6: filas sin la clave saneamiento (backend previo): sin «Ver deuda»", async () => {
    await montarSan({ [GRILLA]: () => Response.json(grilla({ estado: "COMPLETADO" })) });
    expect(accionesEnTodas(/^Ver deuda/)).toHaveLength(0);
  });
});

// HU #14584 CR-4/CR-9 (spec-ui §6.4): «Auditoría del dictamen» del menú solo con verAuditoria (con la compañía de la sesión)
// y «Ver dictamen» de una fila Pendiente abre el panel con un vacío claro, no un error.
// Uso de ejemplo: «Acciones de la placa TST001» → «Auditoría del dictamen de TST001» → modal de versiones; Esc devuelve el foco.
describe("/diagnosticos/{id} — HU #14584: auditoría y dictamen desde el menú «Acciones»", () => {
  const RUTA_VERSIONES = `/diagnosticos/${ID}/vehiculos/vehiculo-1/dictamen/versiones`;

  it("CR-4: sin verAuditoria no hay «Auditoría del dictamen» en el menú", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(quizaAccionFila("TST001", "Auditoría del dictamen de TST001")).toBeNull();
    expect(quizaAccionFila("TST001", "Ver dictamen de TST001")).not.toBeNull();
  });

  it("CR-4: con verAuditoria abre el modal de versiones con ?empresa= de la sesión y al cerrarlo el foco vuelve a «Acciones»", async () => {
    const api = await montar(
      {
        [GRILLA]: () => Response.json(grilla()),
        [`GET /backoffice${RUTA_VERSIONES}`]: () => Response.json({ versiones: [] }),
      },
      { ...USUARIO, verAuditoria: true },
    );
    disparadorDe("TST001")!.focus();
    fireEvent.click(accionFila("TST001", "Auditoría del dictamen de TST001"));
    await avanzar();
    await avanzar();
    const panel = screen.getByRole("dialog", { name: "Auditoría del dictamen · TST001" });
    expect(panel).toBeInTheDocument();
    const [[url]] = llamadas(api, "GET", `/backoffice${RUTA_VERSIONES}`) as [[string]];
    expect(new URL(url, "http://localhost").searchParams.get("empresa")).toBe(sesion({}).empresa.id);
    fireEvent.keyDown(document.activeElement ?? panel, { key: "Escape" });
    await avanzar();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(disparadorDe("TST001")).toHaveFocus();
  });

  it("CR-9: «Ver dictamen» de una fila Pendiente (SIN_DICTAMEN) muestra el vacío claro, sin aviso de error", async () => {
    await montar({
      [GRILLA]: () => Response.json(grilla()),
      [`GET /diagnosticos/${ID}/vehiculos/vehiculo-1/dictamen`]: () => Response.json({ code: "SIN_DICTAMEN", error: "x" }, { status: 404 }),
    });
    fireEvent.click(accionFila("TST001", "Ver dictamen de TST001"));
    await avanzar();
    await avanzar();
    const panel = screen.getByRole("dialog", { name: "Dictamen · TST001" });
    expect(within(panel).getByText("Este vehículo no tiene un dictamen vigente.")).toBeInTheDocument();
    expect(within(panel).queryByRole("alert")).toBeNull();
    expect(within(panel).queryByRole("button", { name: "Reintentar" })).toBeNull();
  });
});
