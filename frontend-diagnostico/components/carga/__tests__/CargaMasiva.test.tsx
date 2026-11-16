import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import CargaDiagnostico from "@/app/(app)/diagnosticos/[id]/carga/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import { ERRORES_CARGA, type FilaResumen } from "@/lib/carga/api";
import { correccionesAConfirmar } from "@/components/carga/CargaMasiva";
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

const SECRETO = "mensaje del servidor que no debe verse";
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
  fireEvent.click(screen.getByRole("button", { name: "Validar archivo" }));
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

const CONFIRMADO = () => Response.json({ diagnostico_id: ID, confirmados: 2, invalidos: 0, duplicados: 0, limite_vehiculos_lote: 500, vehiculos: [] }, { status: 202 });

/** validar-filas simulado: la fila enviada vuelve válida con su número. */
const revalidaValida = (init?: RequestInit) => {
  const [enviada] = (JSON.parse(String(init?.body)) as { filas: { fila: number; identificador: string }[] }).filas;
  return Response.json(resumen({ filas: [fila({ fila: enviada.fila, identificador: enviada.identificador, identificador_norm: enviada.identificador })] }));
};

describe("/diagnosticos/{id}/carga — continuar solo con los válidos (AC 5, reenviando el archivo)", () => {
  it("con el 202 de la confirmación (lote EN_PROCESO, HU #13727) navega al tablero igual que antes", async () => {
    expect(CONFIRMADO().status).toBe(202);
    await montar({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()), [`POST ${ruta("/vehiculos")}`]: CONFIRMADO });
    await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(continuar());
    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

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
    expect(within(resumenRegion).getByText("Con error").nextSibling).toHaveTextContent("0");

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
      fila({ fila: n, identificador, identificador_norm: identificador.replace(/-/g, ""), tipo_identificador: null, tipo_documento_propietario: "CC", documento_propietario: "****7788", valida: false, errores });
    const errDoc: FilaResumen["errores"] = [{ campo: "Documento_Propietario", code: "DOCUMENTO_INVALIDO", mensaje: "Documento inválido." }];
    const errId: FilaResumen["errores"] = [{ campo: "Identificador_Vehiculo", code: "IDENTIFICADOR_INVALIDO", mensaje: "No es una placa ni un VIN válido." }];
    const api = await montar({
      [`POST ${ruta("/carga")}`]: () =>
        Response.json(resumen({ total_filas: 4, validos: 1, erroneos: 3, duplicados: 0, filas: [fila(), conDoc(6, "TST126", errDoc), conDoc(7, "QQ-9", errId), conDoc(8, "TST128", errDoc)] })),
      [`POST ${ruta("/carga/validar-filas")}`]: revalidaValida,
      [`POST ${ruta("/vehiculos")}`]: CONFIRMADO,
    });
    await subir();
    const resumenRegion = await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.change(screen.getByRole("combobox", { name: "Tipo de documento del propietario actual de la fila 6" }), { target: { value: "" } });
    fireEvent.change(screen.getByRole("textbox", { name: "Identificador de la fila 7" }), { target: { value: "TST127" } });
    fireEvent.change(screen.getByRole("textbox", { name: "Documento del propietario actual de la fila 8" }), { target: { value: "1020304050" } });
    await waitFor(() => expect(within(resumenRegion).getByText("Válidos").nextSibling).toHaveTextContent("4"));

    fireEvent.click(continuar());

    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    const [post] = llamadas(api, "POST", ruta("/vehiculos"));
    expect(correccionesDe(post)).toEqual([
      { fila: 6, tipo_documento_propietario: null, documento_propietario: null },
      { fila: 7, identificador: "TST127" },
      { fila: 8, tipo_documento_propietario: "CC", documento_propietario: "1020304050" },
    ]);
    expect(String(formularioDe(post).get("correcciones"))).not.toContain("****");
  });

  it("ya no avisa que el documento del archivo no se conserva: el archivo reenviado lo trae completo", async () => {
    await montar({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()) });
    await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    expect(screen.queryByText(/sin el documento del locatario/i)).not.toBeInTheDocument();
  });

  // AP-9 (SF-6): reescrito. Antes afirmaba el mensaje de la API; ahora el texto fijo de cada code del contrato.
  it.each([
    [409, "VEHICULOS_YA_CONFIRMADOS"],
    [409, "DIAGNOSTICO_NO_EDITABLE"],
    [413, "ARCHIVO_DEMASIADO_GRANDE"],
    [415, "ARCHIVO_ILEGIBLE"],
    [415, "FORMATO_NO_ADMITIDO"],
    [422, "CORRECCIONES_INVALIDAS"],
    [422, "SIN_VALIDOS"],
    [422, "LIMITE_SUPERADO"],
    [422, "COLUMNA_REQUERIDA"],
  ] as const)("AP-9: un %i %s muestra el texto fijo del code, sin Reintentar, y no navega", async (status, code) => {
    const mensaje = ERRORES_CARGA[code];
    await montar({
      [`POST ${ruta("/carga")}`]: () => Response.json(resumen()),
      [`POST ${ruta("/vehiculos")}`]: () => Response.json({ code, error: SECRETO }, { status }),
    });
    await subir();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(continuar());
    expect(await screen.findByText(mensaje)).toBeInTheDocument();
    expect(screen.queryByText(SECRETO)).not.toBeInTheDocument();
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
    // AP-9 (SF-6): reescrito. Un 5xx cae en el respaldo fijo del sitio, no en el message del ApiError.
    expect(await screen.findByText("No se pudieron confirmar los vehículos.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(navegacion.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    const posts = llamadas(api, "POST", ruta("/vehiculos"));
    expect(posts).toHaveLength(2);
    expect(formularioDe(posts[1]).get("archivo")).toBe(subido);
  });
});

describe("/diagnosticos/{id}/carga — tres pasos sin consultas externas (AC20, AC21)", () => {
  const pasos = () => within(screen.getByRole("list", { name: "Pasos de la carga" })).getAllByRole("listitem");

  it("AC20: con el archivo elegido muestra los pasos, el nombre, el tamaño y Quitar; no analiza hasta «Validar archivo»", async () => {
    const api = await montar({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()) });
    fireEvent.change(await screen.findByLabelText(/archivo de vehículos/i), { target: { files: [archivo("flota.csv", "text/csv")] } });
    expect(pasos().map((p) => p.textContent)).toEqual(["1Subir archivo", "2Revisar", "3Iniciar"]);
    expect(pasos()[0]).toHaveAttribute("aria-current", "step");
    expect(screen.getByText("flota.csv")).toBeInTheDocument();
    expect(screen.getByText(/\d+ bytes/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Quitar archivo" })).toBeInTheDocument();
    expect(screen.getByText(/la validación no dispara consultas externas/i)).toBeInTheDocument();
    expect(llamadas(api, "POST", ruta("/carga"))).toHaveLength(0);

    fireEvent.click(screen.getByRole("button", { name: "Validar archivo" }));
    const region = await screen.findByRole("region", { name: "Resumen de validación" });
    expect(pasos()[1]).toHaveAttribute("aria-current", "step");
    expect(within(region).getByText(/la validación no dispara consultas externas/i)).toBeInTheDocument();
    for (const t of ["Válidos", "Con error", "Duplicados consolidados", "Límite del lote"]) expect(within(region).getByText(t)).toBeInTheDocument();
    expect(screen.getByText(/flota\.csv · 4 filas/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /corregir ahora/i })).toBeInTheDocument();
    expect(continuar()).toBeEnabled();
  });

  it("AC20: «Quitar archivo» vuelve a la zona vacía sin analizar", async () => {
    await montar({});
    fireEvent.change(await screen.findByLabelText(/archivo de vehículos/i), { target: { files: [archivo("flota.csv", "text/csv")] } });
    fireEvent.click(screen.getByRole("button", { name: "Quitar archivo" }));
    expect(screen.queryByText("flota.csv")).toBeNull();
    expect(screen.getByRole("button", { name: "Validar archivo" })).toBeDisabled();
  });
});

// HU #14585 (contrato §1, C1/C2): el documento declarado es el del propietario actual. Las correcciones viajan con
// tipo_documento_propietario / documento_propietario; un error de fila con la cabecera vieja (alias aceptado por la API,
// archivos que ya circulan) sigue contando como error de documento. El texto de la subida habla del propietario actual.
describe("Carga — propietario actual (HU #14585)", () => {
  const original = (campo: string): FilaResumen =>
    fila({ fila: 4, identificador: "TST124", valida: false, tipo_documento_propietario: "CC", documento_propietario: "****4050", errores: [{ campo: campo as FilaResumen["errores"][number]["campo"], code: "DOCUMENTO_INVALIDO", mensaje: "x" }] });

  it.each(["Documento_Propietario", "Tipo_Documento_Propietario", "Documento_Locatario"])(
    "un error de %s sin reescribir el documento lo vacía con los campos nuevos",
    (campo) => {
      const erronea = original(campo);
      const corregida = { ...erronea, valida: true, errores: [] };
      const capturada = { fila: 4, identificador: "TST124", tipo_documento_propietario: "CC", documento_propietario: "****4050" };
      expect(correccionesAConfirmar({ filas: [corregida], capturadas: new Map([[4, capturada]]), erroneas: [erronea] })).toEqual([
        { fila: 4, tipo_documento_propietario: null, documento_propietario: null },
      ]);
    },
  );

  it("ENCABEZADO_AMBIGUO y DEMASIADAS_SOLICITUDES tienen texto fijo propio", () => {
    expect(ERRORES_CARGA.ENCABEZADO_AMBIGUO).toMatch(/propietario/i);
    expect(ERRORES_CARGA.DEMASIADAS_SOLICITUDES).toBeTruthy();
  });

  it("la ayuda de la subida pide el documento del propietario actual (opcional)", async () => {
    await montar({});
    expect(await screen.findByText(/el documento del propietario actual \(opcional\)/i)).toBeInTheDocument();
    expect(screen.queryByText(/documento del locatario/i)).toBeNull();
  });
});
