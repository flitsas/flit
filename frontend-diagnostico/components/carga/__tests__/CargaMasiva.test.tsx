import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import CargaDiagnostico from "@/app/(app)/diagnosticos/[id]/carga/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import { FILA_CON_LOCATARIO, ID, archivo, cuerpoDe, fila, llamadas, resumen, ruta, sesion, simularApi } from "./datos";

// Uso de ejemplo: /diagnosticos/{id}/carga sube el archivo, muestra el Resumen de Validación con la tabla de filas
// erróneas y, con «Continuar solo con los válidos», confirma con POST /diagnosticos/{id}/vehiculos y navega al tablero.

const navegacion = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({
  usePathname: () => `/diagnosticos/${"00000000-0000-7000-8000-00000000c001"}/carga`,
  useRouter: () => navegacion,
}));

beforeEach(() => {
  navegacion.push.mockReset();
  navegacion.replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

const CREADOR: Partial<CapacidadesDiagnostico> = { crearDiagnosticos: true, verDiagnosticos: true };
type Rutas = Parameters<typeof simularApi>[0];

async function montar(rutas: Rutas, capacidades: Partial<CapacidadesDiagnostico> = CREADOR) {
  const api = simularApi({ "GET /sesion": () => Response.json(sesion(capacidades)), ...rutas });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      {await CargaDiagnostico({ params: Promise.resolve({ id: ID }) })}
    </DiagnosticoShell>,
  );
  return api;
}

const subir = async (f = archivo("flota.csv", "text/csv")) =>
  fireEvent.change(await screen.findByLabelText(/archivo de vehículos/i), { target: { files: [f] } });
const continuar = () => screen.getByRole("button", { name: /continuar solo con los válidos/i });

describe("/diagnosticos/{id}/carga — los cuatro estados", () => {
  it("vacío: tarjeta título y la subida, sin resumen", async () => {
    await montar({});
    expect(await screen.findByRole("heading", { level: 1, name: "Cargar vehículos" })).toBeInTheDocument();
    expect(await screen.findByLabelText(/archivo de vehículos/i)).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Resumen de validación" })).not.toBeInTheDocument();
  });

  it("cargando: mientras se analiza el archivo muestra el loader", async () => {
    await montar({ [`POST ${ruta("/carga")}`]: () => new Promise<Response>(() => {}) });
    await subir();
    expect(await screen.findByText(/analizando el archivo/i)).toBeInTheDocument();
  });

  it("lleno: el resumen y la tabla de filas erróneas", async () => {
    await montar({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()) });
    await subir();
    expect(await screen.findByRole("region", { name: "Resumen de validación" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Identificador de la fila 3" })).toBeInTheDocument();
  });

  it("sin permiso de crear diagnósticos no ofrece la subida", async () => {
    await montar({}, { crearDiagnosticos: false, verDiagnosticos: true });
    expect(await screen.findByText(/no tienes permiso para cargar vehículos/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/archivo de vehículos/i)).not.toBeInTheDocument();
  });

  it("un 403 de sesión de la carga lleva a la página 403 con su código", async () => {
    await montar({ [`POST ${ruta("/carga")}`]: () => Response.json({ code: "SIN_EMPRESA", error: "Sin empresa" }, { status: 403 }) });
    await subir();
    await waitFor(() => expect(navegacion.replace).toHaveBeenCalledWith("/403?code=SIN_EMPRESA"));
  });
});

describe("/diagnosticos/{id}/carga — continuar solo con los válidos (AC 5)", () => {
  it("corrige una fila, confirma solo los válidos y navega al tablero del diagnóstico", async () => {
    const api = await montar({
      [`POST ${ruta("/carga")}`]: () => Response.json(resumen()),
      [`POST ${ruta("/carga/validar-filas")}`]: () => Response.json(resumen({ filas: [fila({ fila: 3, identificador: "TST124", identificador_norm: "TST124" })] })),
      [`POST ${ruta("/vehiculos")}`]: () => Response.json({ diagnostico_id: ID, confirmados: 3, invalidos: 0, duplicados: 0, limite_vehiculos_lote: 500, vehiculos: [] }, { status: 201 }),
    });
    await subir();
    const resumenRegion = await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.change(screen.getByRole("textbox", { name: "Identificador de la fila 3" }), { target: { value: "TST124" } });
    await waitFor(() => expect(within(resumenRegion).getByText("Válidos").nextSibling).toHaveTextContent("3"));
    expect(within(resumenRegion).getByText("Erróneos").nextSibling).toHaveTextContent("0");

    fireEvent.click(continuar());

    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    const [post] = llamadas(api, "POST", ruta("/vehiculos"));
    expect(cuerpoDe(post)).toEqual({
      vehiculos: [
        { identificador: "TST123", tipo_documento_locatario: null, documento_locatario: null },
        { identificador: "TST124", tipo_documento_locatario: null, documento_locatario: null },
        { identificador: FILA_CON_LOCATARIO.identificador, tipo_documento_locatario: null, documento_locatario: null },
      ],
    });
  });

  it("sin corregir, envía solo los válidos y deja fuera la fila errónea", async () => {
    const api = await montar({
      [`POST ${ruta("/carga")}`]: () => Response.json(resumen()),
      [`POST ${ruta("/vehiculos")}`]: () => Response.json({ diagnostico_id: ID, confirmados: 2, invalidos: 0, duplicados: 0, limite_vehiculos_lote: 500, vehiculos: [] }, { status: 201 }),
    });
    await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(continuar());
    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    const enviados = (cuerpoDe(llamadas(api, "POST", ruta("/vehiculos"))[0]) as { vehiculos: { identificador: string }[] }).vehiculos;
    expect(enviados.map((v) => v.identificador)).toEqual(["TST123", "PRB45A"]);
  });

  it("avisa que el documento enmascarado del archivo no viaja en la confirmación", async () => {
    await montar({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()) });
    await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    expect(screen.getByText(/1 vehículo válido.*sin el documento del locatario/i)).toBeInTheDocument();
  });

  it("un 409 VEHICULOS_YA_CONFIRMADOS muestra el motivo y no navega", async () => {
    await montar({
      [`POST ${ruta("/carga")}`]: () => Response.json(resumen()),
      [`POST ${ruta("/vehiculos")}`]: () => Response.json({ code: "VEHICULOS_YA_CONFIRMADOS", error: "El lote ya tiene vehículos confirmados." }, { status: 409 }),
    });
    await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(continuar());
    expect(await screen.findByText("El lote ya tiene vehículos confirmados.")).toBeInTheDocument();
    expect(navegacion.push).not.toHaveBeenCalled();
  });

  it("un 500 al confirmar es un error genérico con Reintentar", async () => {
    let veces = 0;
    const api = await montar({
      [`POST ${ruta("/carga")}`]: () => Response.json(resumen()),
      [`POST ${ruta("/vehiculos")}`]: () =>
        ++veces === 1 ? new Response("", { status: 500 }) : Response.json({ diagnostico_id: ID, confirmados: 2, invalidos: 0, duplicados: 0, limite_vehiculos_lote: 500, vehiculos: [] }, { status: 201 }),
    });
    await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(continuar());
    expect(await screen.findByText(/diagnóstico no respondió/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    expect(llamadas(api, "POST", ruta("/vehiculos"))).toHaveLength(2);
  });
});
