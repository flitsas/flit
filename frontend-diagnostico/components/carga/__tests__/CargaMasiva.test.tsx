import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import CargaDiagnostico from "@/app/(app)/diagnosticos/[id]/carga/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { FilaResumen } from "@/lib/carga/api";
import { ID, archivo, correccionesDe, fila, formularioDe, llamadas, resumen, ruta, sesion, simularApi } from "./datos";

// Uso de ejemplo: /diagnosticos/{id}/carga sube el archivo, muestra el Resumen de Validación con la tabla de filas
// erróneas y, con «Continuar solo con los válidos», confirma con POST /diagnosticos/{id}/vehiculos (multipart con el MISMO
// archivo y las correcciones de las filas, decisión «Reenviar el archivo») y navega al tablero.

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

const subir = async (f = archivo("flota.csv", "text/csv")) => {
  fireEvent.change(await screen.findByLabelText(/archivo de vehículos/i), { target: { files: [f] } });
  return f;
};
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

const CONFIRMADO = () => Response.json({ diagnostico_id: ID, confirmados: 2, invalidos: 0, duplicados: 0, limite_vehiculos_lote: 500, vehiculos: [] }, { status: 201 });

/** validar-filas simulado: la fila enviada vuelve válida con su número. */
const revalidaValida = (init?: RequestInit) => {
  const [enviada] = (JSON.parse(String(init?.body)) as { filas: { fila: number; identificador: string }[] }).filas;
  return Response.json(resumen({ filas: [fila({ fila: enviada.fila, identificador: enviada.identificador, identificador_norm: enviada.identificador })] }));
};

describe("/diagnosticos/{id}/carga — continuar solo con los válidos (AC 5, reenviando el archivo)", () => {
  it("corrige una fila, reenvía el MISMO archivo con la corrección y navega al tablero del diagnóstico", async () => {
    const api = await montar({
      [`POST ${ruta("/carga")}`]: () => Response.json(resumen()),
      [`POST ${ruta("/carga/validar-filas")}`]: () => Response.json(resumen({ filas: [fila({ fila: 3, identificador: "TST124", identificador_norm: "TST124" })] })),
      [`POST ${ruta("/vehiculos")}`]: CONFIRMADO,
    });
    const subido = await subir();
    const resumenRegion = await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.change(screen.getByRole("textbox", { name: "Identificador de la fila 3" }), { target: { value: "TST124" } });
    await waitFor(() => expect(within(resumenRegion).getByText("Válidos").nextSibling).toHaveTextContent("3"));
    expect(within(resumenRegion).getByText("Erróneos").nextSibling).toHaveTextContent("0");

    fireEvent.click(continuar());

    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    const [post] = llamadas(api, "POST", ruta("/vehiculos"));
    expect(formularioDe(post).get("archivo")).toBe(subido);
    expect(correccionesDe(post)).toEqual([{ fila: 3, identificador: "TST124" }]);
  });

  it("sin corregir, reenvía solo el archivo: sin campo correcciones (el servidor descarta la fila errónea)", async () => {
    const api = await montar({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()), [`POST ${ruta("/vehiculos")}`]: CONFIRMADO });
    const subido = await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(continuar());
    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    const [post] = llamadas(api, "POST", ruta("/vehiculos"));
    expect(formularioDe(post).get("archivo")).toBe(subido);
    expect(correccionesDe(post)).toBeUndefined();
  });

  it("nunca reenvía el documento enmascarado: lo omite si se conserva, lo vacía si se quitó y manda el reescrito completo", async () => {
    const conDoc = (n: number, identificador: string, errores: FilaResumen["errores"]) =>
      fila({ fila: n, identificador, identificador_norm: identificador.replace(/-/g, ""), tipo_identificador: null, tipo_documento_locatario: "CC", documento_locatario: "****7788", valida: false, errores });
    const errDoc: FilaResumen["errores"] = [{ campo: "Documento_Locatario", code: "DOCUMENTO_INVALIDO", mensaje: "Documento inválido." }];
    const errId: FilaResumen["errores"] = [{ campo: "Identificador_Vehiculo", code: "IDENTIFICADOR_INVALIDO", mensaje: "No es una placa ni un VIN válido." }];
    const api = await montar({
      [`POST ${ruta("/carga")}`]: () =>
        Response.json(resumen({ total_filas: 4, validos: 1, erroneos: 3, duplicados: 0, filas: [fila(), conDoc(6, "TST126", errDoc), conDoc(7, "QQ-9", errId), conDoc(8, "TST128", errDoc)] })),
      [`POST ${ruta("/carga/validar-filas")}`]: revalidaValida,
      [`POST ${ruta("/vehiculos")}`]: CONFIRMADO,
    });
    await subir();
    const resumenRegion = await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.change(screen.getByRole("combobox", { name: "Tipo de documento del locatario de la fila 6" }), { target: { value: "" } });
    fireEvent.change(screen.getByRole("textbox", { name: "Identificador de la fila 7" }), { target: { value: "TST127" } });
    fireEvent.change(screen.getByRole("textbox", { name: "Documento del locatario de la fila 8" }), { target: { value: "1020304050" } });
    await waitFor(() => expect(within(resumenRegion).getByText("Válidos").nextSibling).toHaveTextContent("4"));

    fireEvent.click(continuar());

    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    const [post] = llamadas(api, "POST", ruta("/vehiculos"));
    expect(correccionesDe(post)).toEqual([
      { fila: 6, tipo_documento_locatario: null, documento_locatario: null },
      { fila: 7, identificador: "TST127" },
      { fila: 8, tipo_documento_locatario: "CC", documento_locatario: "1020304050" },
    ]);
    expect(String(formularioDe(post).get("correcciones"))).not.toContain("****");
  });

  it("ya no avisa que el documento del archivo no se conserva: el archivo reenviado lo trae completo", async () => {
    await montar({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()) });
    await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    expect(screen.queryByText(/sin el documento del locatario/i)).not.toBeInTheDocument();
  });

  it.each([
    [409, "VEHICULOS_YA_CONFIRMADOS", "El lote ya tiene vehículos confirmados."],
    [413, "ARCHIVO_DEMASIADO_GRANDE", "El archivo supera el tamaño máximo de 5 MB."],
    [415, "ARCHIVO_ILEGIBLE", "No se pudo leer el archivo."],
    [422, "CORRECCIONES_INVALIDAS", "Las correcciones no son válidas."],
    [422, "SIN_VALIDOS", "No hay vehículos válidos para confirmar."],
  ])("un %i %s muestra el motivo de la API, sin Reintentar, y no navega", async (status, code, mensaje) => {
    await montar({
      [`POST ${ruta("/carga")}`]: () => Response.json(resumen()),
      [`POST ${ruta("/vehiculos")}`]: () => Response.json({ code, error: mensaje }, { status }),
    });
    await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(continuar());
    expect(await screen.findByText(mensaje)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
    expect(navegacion.push).not.toHaveBeenCalled();
  });

  it("un 500 al confirmar es un error genérico con Reintentar, que reenvía el mismo archivo", async () => {
    let veces = 0;
    const api = await montar({
      [`POST ${ruta("/carga")}`]: () => Response.json(resumen()),
      [`POST ${ruta("/vehiculos")}`]: () => (++veces === 1 ? new Response("", { status: 500 }) : CONFIRMADO()),
    });
    const subido = await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(continuar());
    expect(await screen.findByText(/diagnóstico no respondió/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    const posts = llamadas(api, "POST", ruta("/vehiculos"));
    expect(posts).toHaveLength(2);
    expect(formularioDe(posts[1]).get("archivo")).toBe(subido);
  });
});
