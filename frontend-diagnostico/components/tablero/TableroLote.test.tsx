import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import TableroDiagnostico from "@/app/(app)/diagnosticos/[id]/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { Grilla } from "@/lib/tablero/api";
import { INTERVALO_SONDEO_MS } from "@/lib/tablero/useGrillaIncremental";
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

async function montar(rutas: Rutas, capacidades: Partial<CapacidadesDiagnostico> = USUARIO, id = ID) {
  const api = simularApi({ "GET /sesion": () => Response.json(sesion(capacidades)), ...rutas });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      {await TableroDiagnostico({ params: Promise.resolve({ id }) })}
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

describe("/diagnosticos/{id} — AC2: se actualiza sola sin recargar", () => {
  it("a los 3 s pide solo los cambios desde su versión y pinta la celda que cambió", async () => {
    const cambio = grilla({ version: 13, vehiculos: [vehiculo(3, [celda(3, "RUNT", "COMPLETADO", { version: 13 })])] });
    const api = await montar({ [GRILLA]: enOrden(grilla(), cambio) });
    await avanzar(INTERVALO_SONDEO_MS);
    expect(desdesPedidos(api)).toEqual([0, 12]);
    const fila = screen.getByRole("rowheader", { name: /TST003/ }).closest("tr")!;
    expect(within(fila).getByText("Completada")).toBeInTheDocument();
    expect(navegacion.push).not.toHaveBeenCalled();
    expect(navegacion.refresh).not.toHaveBeenCalled();
  });
});

describe("/diagnosticos/{id} — AC4/AC5: acciones por perfil", () => {
  it("AC4: el usuario sin capacidad de reprocesar no ve botones en la grilla", async () => {
    await montar({ [GRILLA]: () => Response.json(loteTerminadoConErrores({ estado: "INCOMPLETO" })) });
    // Desde HU #13801 cada semáforo es un botón «Ver detalle de …»; acciones (Reprocesar) no hay ninguna.
    const botones = within(screen.getByRole("table")).queryAllByRole("button");
    expect(botones.filter((b) => !/^Ver detalle de /.test(b.getAttribute("aria-label") ?? ""))).toHaveLength(0);
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
    expect(within(runt).getByText("Pendiente")).toBeInTheDocument();
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

    fireEvent.click(screen.getByRole("button", { name: "Cerrar panel" }));
    await avanzar();
    await avanzar();

    expect(llamadas(api, "POST", `/diagnosticos/${ID}/cerrar-panel`)).toHaveLength(1);
    expect(estadoGlobal()).toHaveTextContent("Incompleto");
    const estimado = screen.getByRole("region", { name: /estimado frente a consumido/i });
    const runt = within(estimado).getByRole("row", { name: /RUNT vehículo/ });
    expect(within(runt).getAllByRole("cell").map((c) => c.textContent)).toEqual(["2", "2"]);
    const simit = within(estimado).getByRole("row", { name: /SIMIT propietario/ });
    expect(within(simit).getAllByRole("cell").map((c) => c.textContent)).toEqual(["2", "1"]);
    expect(screen.queryByRole("button", { name: "Cerrar panel" })).not.toBeInTheDocument();
  });

  it("no ofrece Cerrar panel mientras quedan celdas pendientes o si el lote no tiene errores", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(screen.queryByRole("button", { name: "Cerrar panel" })).not.toBeInTheDocument();
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
    fireEvent.click(screen.getByRole("button", { name: "Cerrar panel" }));
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
    expect(within(barra).getByRole("link", { name: "Descargar Excel" })).toBeInTheDocument();
    expect(within(barra).getByRole("button", { name: "Descargar PDF (.zip)" })).toBeInTheDocument();
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
