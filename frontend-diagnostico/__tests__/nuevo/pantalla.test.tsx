import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { DISPONIBLES_CON_IMPUESTO } from "@/components/parametrizacion/__tests__/datos";
import { ID, archivo, configuracion, diagnostico, llamadas, montarNuevo, resumen, type Navegacion } from "./datos";

// Uso de ejemplo: /diagnosticos/nuevo?modo=masivo abre la pantalla única de creación (HU #14085) con el selector
// Express | Masivo, la zona Vehículos a la izquierda y la zona Parámetros (sticky) a la derecha desde 1024 px.

const nav = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn(), params: new URLSearchParams() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/diagnosticos/nuevo",
  useSearchParams: () => nav.params,
  useRouter: () => nav,
}));

beforeEach(() => {
  nav.push.mockReset();
  nav.replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

const navegacion = nav as unknown as Navegacion;
const selector = () => screen.findByRole("radiogroup", { name: "Tipo de diagnóstico" });
const radioModo = (nombre: "Express" | "Masivo") => within(screen.getByRole("radiogroup", { name: "Tipo de diagnóstico" })).getByRole("radio", { name: nombre });

describe("Pantalla única — selector y zonas (AC1)", () => {
  it("muestra la tarjeta título, el selector Express|Masivo y las zonas Vehículos y Parámetros; Express por defecto", async () => {
    montarNuevo(navegacion);
    expect(await screen.findByRole("heading", { level: 1, name: "Nuevo diagnóstico" })).toBeInTheDocument();
    await selector();
    expect(radioModo("Express")).toHaveAttribute("aria-checked", "true");
    expect(radioModo("Masivo")).toHaveAttribute("aria-checked", "false");
    // Con la configuración cargada (mientras llega, Vehículos se ve deshabilitado y Parámetros muestra el loader).
    const nombre = await screen.findByRole("textbox", { name: "Nombre del diagnóstico" });
    expect(screen.getByRole("region", { name: "Parámetros" })).toContainElement(nombre);
    expect(screen.getByRole("region", { name: "Vehículos" })).toContainElement(screen.getByRole("textbox", { name: "Placa o VIN" }));
  });

  it("en ≥1024 px las zonas van en dos columnas con Parámetros sticky; en móvil se apilan sin desbordar", async () => {
    montarNuevo(navegacion);
    await screen.findByRole("textbox", { name: "Nombre del diagnóstico" });
    const vehiculos = screen.getByRole("region", { name: "Vehículos" });
    const parametros = screen.getByRole("region", { name: "Parámetros" });
    const rejilla = vehiculos.parentElement as HTMLElement;
    expect(rejilla).toBe(parametros.parentElement);
    expect(rejilla.className).toMatch(/(^|\s)grid(\s|$)/);
    expect(rejilla.className).toMatch(/lg:grid-cols-/);
    expect(parametros.className).toMatch(/lg:sticky/);
    // Sin ancho mínimo propio en las columnas: en móvil se apilan y no generan scroll horizontal.
    expect(vehiculos.className).toContain("min-w-0");
    expect(parametros.className).toContain("min-w-0");
  });

  it("?modo=masivo abre en Masivo con la plantilla, la zona de archivo y «Validar archivo»", async () => {
    montarNuevo(navegacion, { query: "modo=masivo" });
    await selector();
    expect(radioModo("Masivo")).toHaveAttribute("aria-checked", "true");
    // Con la configuración ya cargada (mientras llega, la zona de archivo se ve deshabilitada).
    expect(await screen.findByRole("button", { name: "Validar archivo" })).toBeInTheDocument();
    expect(screen.getByLabelText(/archivo de vehículos/i)).toBeEnabled();
    expect(screen.getByRole("link", { name: /descargar plantilla/i })).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: "Placa o VIN" })).not.toBeInTheDocument();
  });

  it("vacío: sin crearDiagnosticos explica que no tiene permiso y no pide la configuración", async () => {
    const api = montarNuevo(navegacion, { capacidades: { verDiagnosticos: true } });
    expect(await screen.findByText(/no tienes permiso para crear diagnósticos/i)).toBeInTheDocument();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(llamadas(api, "GET", "/configuracion")).toHaveLength(0);
  });

  it("cargando y error de la configuración en Parámetros: loader y error con Reintentar, que la vuelve a pedir", async () => {
    let falla = true;
    montarNuevo(navegacion, {
      rutas: { "GET /configuracion": () => (falla ? Response.json({}, { status: 500 }) : Response.json(configuracion())) },
    });
    expect(await screen.findByRole("status", { name: "Cargando la parametrización…" })).toBeInTheDocument();
    expect(await screen.findByText("No se pudo cargar la parametrización")).toBeInTheDocument();
    expect(screen.getByText(/diagnóstico no respondió/i)).toBeInTheDocument();
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("textbox", { name: "Nombre del diagnóstico" })).toBeInTheDocument();
  });

  it("un 403 de sesión al leer la configuración lleva a /403 con su código", async () => {
    montarNuevo(navegacion, { rutas: { "GET /configuracion": () => Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "No" }, { status: 403 }) } });
    await waitFor(() => expect(nav.replace).toHaveBeenCalledWith("/403?code=PRODUCTO_NO_HABILITADO"));
  });

  it("con el impuesto habilitado y disponible lo ofrece en el alcance; sin él en fuentes_disponibles no", async () => {
    montarNuevo(navegacion, {
      config: configuracion({ contrato: { fuentes_habilitadas: ["RUNT", "IMPUESTO_DEPARTAMENTAL"] }, fuentes_disponibles: DISPONIBLES_CON_IMPUESTO }),
    });
    const grupo = await screen.findByRole("group", { name: "Por Vehículo" });
    const impuesto = within(grupo).getByRole("checkbox", { name: "Impuesto departamental" });
    fireEvent.click(impuesto);
    expect(impuesto).toBeChecked();
  });

  it("sin el impuesto en fuentes_disponibles no lo ofrece aunque esté contratado", async () => {
    montarNuevo(navegacion, { config: configuracion({ contrato: { fuentes_habilitadas: ["RUNT", "IMPUESTO_DEPARTAMENTAL"] } }) });
    await screen.findByRole("group", { name: "Por Vehículo" });
    expect(screen.queryByRole("checkbox", { name: "Impuesto departamental" })).toBeNull();
  });
});

describe("Pantalla única — cambio de modo (AC5)", () => {
  it("sin cabecera creada, cambiar de modo conserva las fuentes, el modo de análisis y lo digitado en cada modo", async () => {
    montarNuevo(navegacion);
    fireEvent.change(await screen.findByRole("textbox", { name: "Placa o VIN" }), { target: { value: "tst123" } });
    fireEvent.click(await screen.findByRole("checkbox", { name: "SIMIT vehículo" }));
    fireEvent.click(screen.getByRole("radio", { name: "Con Análisis IA" }));
    fireEvent.change(await screen.findByRole("combobox", { name: /tipo de trámite/i }), { target: { value: "TRASPASO_STANDARD" } });

    fireEvent.click(radioModo("Masivo"));
    expect(radioModo("Masivo")).toHaveAttribute("aria-checked", "true");
    expect(await screen.findByLabelText(/archivo de vehículos/i)).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "SIMIT vehículo" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Con Análisis IA" })).toBeChecked();
    expect(screen.getByRole("combobox", { name: /tipo de trámite/i })).toHaveValue("TRASPASO_STANDARD");
    expect(window.location.search).toContain("modo=masivo");

    fireEvent.click(radioModo("Express"));
    expect(screen.getByRole("textbox", { name: "Placa o VIN" })).toHaveValue("TST123");
    expect(screen.getByRole("checkbox", { name: "SIMIT vehículo" })).toBeChecked();
    expect(window.location.search).toContain("modo=express");
  });

  it("con la cabecera de Masivo ya creada el selector queda deshabilitado con una explicación", async () => {
    montarNuevo(navegacion, {
      query: "modo=masivo",
      rutas: {
        "POST /diagnosticos": () => Response.json(diagnostico(), { status: 201 }),
        [`POST /diagnosticos/${ID}/carga`]: () => Response.json(resumen()),
      },
    });
    fireEvent.change(await screen.findByRole("textbox", { name: "Nombre del diagnóstico" }), { target: { value: "Flota norte" } });
    fireEvent.change(screen.getByLabelText(/archivo de vehículos/i), { target: { files: [archivo("flota.csv", "text/csv")] } });
    fireEvent.click(screen.getByRole("button", { name: "Validar archivo" }));
    await screen.findByRole("region", { name: "Resumen de validación" });

    const grupo = screen.getByRole("radiogroup", { name: "Tipo de diagnóstico" });
    expect(grupo).toHaveAttribute("aria-disabled", "true");
    expect(grupo).toHaveAccessibleDescription(/ya se creó la cabecera del lote/i);
    fireEvent.click(radioModo("Express"));
    expect(radioModo("Masivo")).toHaveAttribute("aria-checked", "true");
    expect(screen.queryByRole("textbox", { name: "Placa o VIN" })).not.toBeInTheDocument();
  });
});

describe("Pantalla única — accesibilidad y estilos (AC8)", () => {
  it("el selector es un radiogroup con tabindex itinerante, operable con flechas", async () => {
    montarNuevo(navegacion);
    await selector();
    const express = radioModo("Express");
    expect(express).toHaveAttribute("tabindex", "0");
    expect(radioModo("Masivo")).toHaveAttribute("tabindex", "-1");
    express.focus();
    fireEvent.keyDown(express, { key: "ArrowRight" });
    expect(radioModo("Masivo")).toHaveAttribute("aria-checked", "true");
    expect(radioModo("Masivo")).toHaveFocus();
    expect(radioModo("Masivo")).toHaveAttribute("tabindex", "0");
    fireEvent.keyDown(radioModo("Masivo"), { key: "ArrowLeft" });
    expect(radioModo("Express")).toHaveAttribute("aria-checked", "true");
    expect(radioModo("Express").className).toContain("focus-visible:outline");
  });

  it("vehículos en Masivo: vacío (zona de archivo), cargando (analizando), error (rechazo) y lleno (resumen)", async () => {
    let responder: (r: Response) => void = () => {};
    montarNuevo(navegacion, {
      query: "modo=masivo",
      rutas: {
        "POST /diagnosticos": () => Response.json(diagnostico(), { status: 201 }),
        [`POST /diagnosticos/${ID}/carga`]: () => new Promise<Response>((r) => (responder = r)),
      },
    });
    fireEvent.change(await screen.findByRole("textbox", { name: "Nombre del diagnóstico" }), { target: { value: "Flota norte" } });
    expect(screen.getByLabelText(/archivo de vehículos/i)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/archivo de vehículos/i), { target: { files: [archivo("flota.csv", "text/csv")] } });
    fireEvent.click(screen.getByRole("button", { name: "Validar archivo" }));
    expect(await screen.findByText(/analizando el archivo/i)).toBeInTheDocument();
    responder(Response.json(resumen()));
    expect(await screen.findByRole("region", { name: "Resumen de validación" })).toBeInTheDocument();
  });

  it("los archivos de la pantalla no declaran colores HEX ni RGB sueltos y reutilizan las clases de configuracion/estilos", () => {
    const dir = resolve(__dirname, "..", "..", "components", "nuevo");
    const archivos = readdirSync(dir).filter((n) => /\.tsx?$/.test(n));
    expect(archivos.length).toBeGreaterThan(0);
    const codigo = archivos.map((n) => readFileSync(join(dir, n), "utf8")).join("\n");
    // Un HEX dentro de un valor o clase (comillas, corchete de Tailwind o paréntesis); «HU #14085» en un comentario no cuenta.
    expect(codigo).not.toMatch(/["'`[(]#[0-9a-fA-F]{3,8}\b/);
    expect(codigo).not.toMatch(/rgba?\(/);
    expect(codigo).toContain("TARJETA_CLS");
    expect(codigo).toContain("BOTON_PRIMARIO_CLS");
  });
});

describe("Pantalla única — sin la ruta /carga intermedia (AC3)", () => {
  it("Masivo no ofrece «Cargar vehículos» hacia /diagnosticos/{id}/carga: todo ocurre en la misma pantalla", async () => {
    montarNuevo(navegacion, {
      query: "modo=masivo",
      rutas: {
        "POST /diagnosticos": () => Response.json(diagnostico(), { status: 201 }),
        [`POST /diagnosticos/${ID}/carga`]: () => Response.json(resumen()),
      },
    });
    fireEvent.change(await screen.findByRole("textbox", { name: "Nombre del diagnóstico" }), { target: { value: "Flota norte" } });
    fireEvent.change(screen.getByLabelText(/archivo de vehículos/i), { target: { files: [archivo("flota.csv", "text/csv")] } });
    fireEvent.click(screen.getByRole("button", { name: "Validar archivo" }));
    await screen.findByRole("region", { name: "Resumen de validación" });
    expect(screen.queryByRole("link", { name: /cargar vehículos/i })).not.toBeInTheDocument();
    expect(nav.push).not.toHaveBeenCalled();
  });
});
