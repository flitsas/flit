import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import TableroDiagnostico from "@/app/(app)/diagnosticos/[id]/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { Grilla } from "@/lib/tablero/api";
import { ID, V1, V2, V3, desdesPedidos, grilla, llamadas, sesion, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: el tablero /diagnosticos/{id} muestra «Reintentar placa» en cada placa con `pegada` y, encima de la
// grilla, «Reintentar pegadas (N)» cuando `total_pegadas` > 0 (Punto 3 de la ola 2026-11-11, AC 3, 4, 5 y 9). Ambas acciones
// exigen la capacidad `reprocesarConsultas`; al usarlas, POST al endpoint, aviso «N consultas reenviadas» y la grilla se
// vuelve a leer.

const navegacion = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => navegacion }));

const USUARIO: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, descargarReportes: true };
const ADMIN: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, reprocesarConsultas: true };
const GRILLA = `GET /diagnosticos/${ID}/grilla`;
const POR_PLACA = `/diagnosticos/${ID}/vehiculos/vehiculo-2/reintentar`;
const POR_LOTE = `/diagnosticos/${ID}/reintentar-pegadas`;

const avanzar = (ms = 0) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

type Rutas = Parameters<typeof simularApi>[0];

async function montar(rutas: Rutas, capacidades: Partial<CapacidadesDiagnostico> = ADMIN) {
  const api = simularApi({ "GET /sesion": () => Response.json(sesion(capacidades)), ...rutas });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      {await TableroDiagnostico({ params: Promise.resolve({ id: ID }), searchParams: Promise.resolve({}) })}
    </DiagnosticoShell>,
  );
  await avanzar();
  await avanzar();
  return api;
}

/** TST002 y TST003 pegadas; TST001 no. */
const conPegadas = (cambios: Partial<Grilla> = {}): Grilla =>
  grilla({
    vehiculos: [{ ...V1, pegada: false }, { ...V2, pegada: true }, { ...V3, pegada: true }],
    total_pegadas: 2,
    ...cambios,
  });

const enOrden = (...grillas: Grilla[]) => {
  let n = 0;
  return () => Response.json(grillas[Math.min(n++, grillas.length - 1)]);
};

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
  navegacion.push.mockReset();
  navegacion.replace.mockReset();
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Punto 3 AC3 — «Reintentar placa» solo en las placas pegadas", () => {
  it("aparece en cada placa con pegada=true, con la placa en el nombre accesible, y no en la que no está pegada", async () => {
    await montar({ [GRILLA]: () => Response.json(conPegadas()) });
    const tabla = screen.getByRole("table", { name: /semáforo de consultas/i });
    expect(within(tabla).getByRole("button", { name: "Reintentar consultas de la placa TST002" })).toBeEnabled();
    expect(within(tabla).getByRole("button", { name: "Reintentar consultas de la placa TST003" })).toBeInTheDocument();
    expect(within(tabla).queryByRole("button", { name: "Reintentar consultas de la placa TST001" })).not.toBeInTheDocument();
  });

  it("sin `pegada` ni `total_pegadas` en la respuesta (backend previo) no hay ninguna acción de reintento", async () => {
    await montar({ [GRILLA]: () => Response.json(grilla()) });
    expect(screen.queryByRole("button", { name: /reintentar consultas de la placa/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reintentar pegadas/i })).not.toBeInTheDocument();
  });

  it("al pulsarla: POST al endpoint de la placa, deshabilitada y aria-busy mientras responde, aviso «N consultas reenviadas» y vuelve a leer la grilla", async () => {
    let responder: (r: Response) => void = () => {};
    const api = await montar({
      [GRILLA]: enOrden(conPegadas(), grilla({ version: 13, vehiculos: [{ ...V2, pegada: false }], total_pegadas: 1 })),
      [`POST ${POR_PLACA}`]: () => new Promise<Response>((r) => (responder = r)),
    });
    const boton = screen.getByRole("button", { name: "Reintentar consultas de la placa TST002" });
    fireEvent.click(boton);
    await avanzar();
    expect(boton).toBeDisabled();
    expect(boton).toHaveAttribute("aria-busy", "true");
    expect(llamadas(api, "POST", POR_PLACA)).toHaveLength(1);

    responder(Response.json({ reencoladas: 2 }));
    await avanzar();
    await avanzar();

    const aviso = screen.getByRole("status", { name: /resultado del reintento/i });
    expect(aviso).toHaveTextContent("2 consultas reenviadas");
    expect(desdesPedidos(api)).toEqual([0, 12]);
    expect(screen.queryByRole("button", { name: "Reintentar consultas de la placa TST002" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reintentar consultas de la placa TST003" })).toBeInTheDocument();
  });

  it("singular: «1 consulta reenviada»", async () => {
    await montar({
      [GRILLA]: () => Response.json(conPegadas()),
      [`POST ${POR_PLACA}`]: () => Response.json({ reencoladas: 1 }),
    });
    fireEvent.click(screen.getByRole("button", { name: "Reintentar consultas de la placa TST002" }));
    await avanzar();
    expect(screen.getByRole("status", { name: /resultado del reintento/i })).toHaveTextContent("1 consulta reenviada");
  });

  it("error: se muestra en el aviso de error y el botón vuelve a quedar disponible", async () => {
    await montar({
      [GRILLA]: () => Response.json(conPegadas()),
      [`POST ${POR_PLACA}`]: () => Response.json({ error: "Falla interna" }, { status: 500 }),
    });
    fireEvent.click(screen.getByRole("button", { name: "Reintentar consultas de la placa TST002" }));
    await avanzar();
    expect(screen.getByRole("alert")).toHaveTextContent(/no se completó la acción/i);
    expect(screen.queryByRole("status", { name: /resultado del reintento/i })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reintentar consultas de la placa TST002" })).toBeEnabled();
  });

  it("un 403 se explica como falta de permiso", async () => {
    await montar({
      [GRILLA]: () => Response.json(conPegadas()),
      [`POST ${POR_PLACA}`]: () => Response.json({ code: "SIN_PERMISO", error: "x" }, { status: 403 }),
    });
    fireEvent.click(screen.getByRole("button", { name: "Reintentar consultas de la placa TST002" }));
    await avanzar();
    expect(screen.getByRole("alert")).toHaveTextContent(/no tienes permiso para reintentar consultas/i);
  });
});

describe("Punto 3 AC4/AC5 — «Reintentar pegadas (N)» del lote", () => {
  it("con total_pegadas > 0 se muestra encima de la grilla con N", async () => {
    await montar({ [GRILLA]: () => Response.json(conPegadas()) });
    const seccion = screen.getByRole("region", { name: "Grilla del lote" });
    const boton = within(seccion).getByRole("button", { name: "Reintentar las 2 placas pegadas del lote" });
    expect(boton).toHaveTextContent("Reintentar pegadas (2)");
    const tabla = within(seccion).getByRole("table");
    expect(boton.compareDocumentPosition(tabla) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("con total_pegadas = 0 queda oculto", async () => {
    await montar({ [GRILLA]: () => Response.json(conPegadas({ total_pegadas: 0 })) });
    expect(screen.queryByRole("button", { name: /reintentar las .* placas pegadas/i })).not.toBeInTheDocument();
  });

  it("al pulsarlo: POST al endpoint del lote, aviso con las reenviadas, vuelve a leer la grilla y se oculta con N = 0", async () => {
    const api = await montar({
      [GRILLA]: enOrden(conPegadas(), grilla({ version: 14, vehiculos: [], avance: conPegadas().avance, total_pegadas: 0 })),
      [`POST ${POR_LOTE}`]: () => Response.json({ reencoladas: 5 }),
    });
    fireEvent.click(screen.getByRole("button", { name: "Reintentar las 2 placas pegadas del lote" }));
    await avanzar();
    await avanzar();
    expect(llamadas(api, "POST", POR_LOTE)).toHaveLength(1);
    expect(screen.getByRole("status", { name: /resultado del reintento/i })).toHaveTextContent("5 consultas reenviadas");
    expect(desdesPedidos(api)).toEqual([0, 12]);
    expect(screen.queryByRole("button", { name: /reintentar las .* placas pegadas/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reintentar consultas de la placa/i })).not.toBeInTheDocument();
  });

  it("AC5: mientras responde queda deshabilitado (no se puede pulsar dos veces)", async () => {
    const api = await montar({
      [GRILLA]: () => Response.json(conPegadas()),
      [`POST ${POR_LOTE}`]: () => new Promise<Response>(() => {}),
    });
    const boton = screen.getByRole("button", { name: "Reintentar las 2 placas pegadas del lote" });
    fireEvent.click(boton);
    await avanzar();
    expect(boton).toBeDisabled();
    expect(boton).toHaveAttribute("aria-busy", "true");
    fireEvent.click(boton);
    await avanzar();
    expect(llamadas(api, "POST", POR_LOTE)).toHaveLength(1);
  });

  it("error del masivo: aviso de error visible", async () => {
    await montar({
      [GRILLA]: () => Response.json(conPegadas()),
      [`POST ${POR_LOTE}`]: () => Response.json({ error: "Servicio no disponible" }, { status: 503 }),
    });
    fireEvent.click(screen.getByRole("button", { name: "Reintentar las 2 placas pegadas del lote" }));
    await avanzar();
    expect(screen.getByRole("alert")).toHaveTextContent(/no se completó la acción/i);
  });
});

describe("Punto 3 AC9 — sin la capacidad reprocesarConsultas no hay reintentos", () => {
  it("con placas pegadas y total_pegadas > 0, el usuario sin permiso no ve ninguna de las dos acciones", async () => {
    await montar({ [GRILLA]: () => Response.json(conPegadas()) }, USUARIO);
    expect(screen.getByRole("table", { name: /semáforo de consultas/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reintentar consultas de la placa/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reintentar las .* placas pegadas/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/reintentar pegadas/i)).not.toBeInTheDocument();
  });
});
