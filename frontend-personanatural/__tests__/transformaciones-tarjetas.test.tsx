import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { Adjunto, DeclaracionTipo, DefinicionTipo, Solicitud } from "@/lib/api/cliente";
import { filasDeDeclaraciones } from "@/lib/tramite/resumen";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { cuerpo, DIAGNOSTICO_OK, wizardAbierto, type Manejador } from "./montar-wizard";
import { json } from "./utilidades";

// Transformaciones del vehículo como tarjetas (paridad con «Trámites Simultáneos» de FLIT 2): al activar una, su opción, el valor
// actual del RUNT y la carga de su documento salen DENTRO de su tarjeta, y la tarjeta «Declaraciones del trámite» no las repite.
const RUTA_DEFINICION = "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1";
const flags: DefinicionTipo["flags"] = { entryMode: "PLATE", requiereComercial: false, requierePrenda: false, permiteTransformaciones: true, permitePrendaComplementaria: false, requiereBiometria: false, requiereSolicitudPlaca: false };
const base = { control: "seleccion_catalogo", obligatoria: false, soporteObligatorio: false };
const COLOR: DeclaracionTipo = { ...base, clave: "simultaneo_color", etiqueta: "Nuevo color", opciones: [], fieldKey: "vehicle_color", fieldKeysExtra: ["cambio_color"], docCodeSoporte: "soporte_cambio_color", catalogo: "colores", activadaPor: "CAMBIO_COLOR" };
const COMBUSTIBLE: DeclaracionTipo = {
  ...base,
  clave: "simultaneo_combustible",
  etiqueta: "Nuevo combustible",
  opciones: [
    { codigo: "GASOLINA", etiqueta: "GASOLINA", docCodes: [], exigeSoporte: false },
    { codigo: "DIESEL", etiqueta: "DIESEL", docCodes: [], exigeSoporte: false },
  ],
  fieldKey: "vehicle_fuel",
  fieldKeysExtra: ["cambio_combustible"],
  docCodeSoporte: "soporte_conversion_combustible",
  catalogo: "combustibles",
  activadaPor: "CONVERSION_COMBUSTIBLE",
};
const CARROCERIA: DeclaracionTipo = { ...base, clave: "simultaneo_carroceria", etiqueta: "Nueva carrocería", opciones: [], fieldKey: "vehicle_body_type", fieldKeysExtra: ["cambio_carroceria"], docCodeSoporte: "factura_carroceria", catalogo: "carrocerias", activadaPor: "CAMBIO_CARROCERIA" };
const PROPIA: DeclaracionTipo = { ...base, clave: "causal", control: "seleccion", obligatoria: true, etiqueta: "Causal propia", opciones: [{ codigo: "A", etiqueta: "Opción A", docCodes: [], exigeSoporte: false }], fieldKey: "causal_propia", fieldKeysExtra: [], docCodeSoporte: "", catalogo: "", activadaPor: "" };
const DOC_CARROCERIA = { docCode: "factura_carroceria", nombre: "Factura de carrocería", obligatorio: true, orden: 1, instruccionCargue: "", condicion: "Se pide si el trámite cambia la carrocería." };
const RUNT = { color: "BLANCO", combustible: "GASOLINA", carroceria: "ESTACAS" };
const DATOS = { diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" } };

const definicion = (declaraciones: DeclaracionTipo[], extra: Partial<DefinicionTipo> = {}) => definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], { flags, declaraciones, documentos: [DOC_CARROCERIA], ...extra });
const adjunto = (id: string, tipo: string): Adjunto => ({ id, tipo, nombreArchivo: `${tipo}.pdf`, mimeType: "application/pdf", tamanoBytes: 1000, sha256: "x", creadoEn: "2026-10-28T10:00:00Z" });
const borrador = (extra: Partial<Solicitud> = {}): Solicitud => solicitudDe({ datos: DATOS, pendientes: [{ codigo: "comercial_requerido", referencia: "valorComercial", paso: "p3" }], version: 4, ...extra });
const pdf = () => new File(["%PDF-1.4 contenido"], "soporte.pdf", { type: "application/pdf" });

async function abrir(def: DefinicionTipo, rutas: Record<string, Manejador> = {}, inicial: Solicitud = borrador()) {
  const estado = { actual: inicial };
  const m = await wizardAbierto({
    [RUTA_DEFINICION]: () => json(def),
    "GET /solicitudes/s-1": () => json(estado.actual),
    "GET /solicitudes/s-1/vehiculo/valores-actuales": () => json(RUNT),
    "GET /catalogos/colores": () => json([{ codigo: "ROJO", etiqueta: "Rojo" }, { codigo: "BLANCO", etiqueta: "Blanco" }]),
    "PUT /solicitudes/s-1/campos": () => json((estado.actual = { ...estado.actual, version: estado.actual.version + 1 })),
    "PUT /solicitudes/s-1/requisitos": () => json((estado.actual = { ...estado.actual, version: estado.actual.version + 1 })),
    ...rutas,
  });
  await screen.findByRole("heading", { level: 3, name: "Transformaciones del vehículo" });
  return { ...m, estado };
}
const tarjeta = (nombre: RegExp) => screen.getByRole("region", { name: nombre });
const interruptor = (nombre: RegExp) => screen.getByRole("switch", { name: nombre });
const siguiente = () => screen.getByRole("button", { name: "Siguiente" });
const archivos = (t: HTMLElement) => t.querySelectorAll('input[type="file"]');

describe("Transformaciones como tarjetas (paridad con FLIT 2)", () => {
  it("una tarjeta por transformación con su interruptor; apagadas no muestran opción ni documento", async () => {
    await abrir(definicion([COLOR, COMBUSTIBLE, CARROCERIA]));
    for (const nombre of [/Cambio de color/, /Conversión de combustible/, /Cambio de carrocería/]) {
      const t = tarjeta(nombre);
      expect(within(t).getByRole("switch")).toHaveAttribute("aria-checked", "false");
      expect(within(t).queryByRole("combobox")).toBeNull();
      expect(archivos(t)).toHaveLength(0);
    }
    expect(screen.queryByRole("region", { name: "Declaraciones del trámite" })).toBeNull();
    // La rejilla pasa a una columna en móvil.
    expect(tarjeta(/Cambio de color/).parentElement).toHaveClass("grid-cols-1", "md:grid-cols-3");
  });

  it("al activarla, su opción, el valor actual del RUNT y la carga del documento salen dentro de la misma tarjeta, y no en «Declaraciones del trámite»", async () => {
    await abrir(definicion([PROPIA, COLOR, COMBUSTIBLE, CARROCERIA]));
    // La tarjeta de arriba conserva solo la propia del tipo.
    const arriba = screen.getByRole("region", { name: "Declaraciones del trámite" });
    expect(within(arriba).getByLabelText("Causal propia *")).toBeInTheDocument();

    fireEvent.click(interruptor(/Conversión de combustible/));
    const t = tarjeta(/Conversión de combustible/);
    expect(within(t).getByLabelText("Nuevo combustible *")).toBeInTheDocument();
    expect(await within(t).findByText("Combustible actual en el RUNT: GASOLINA")).toBeInTheDocument();
    expect(archivos(t)).toHaveLength(1);
    expect(within(t).getByText("Opcional")).toBeInTheDocument();

    // Nada de la transformación aparece en la tarjeta de arriba ni fuera de la suya.
    expect(within(arriba).queryByText(/combustible/i)).toBeNull();
    expect(archivos(arriba)).toHaveLength(0);
    expect(screen.getAllByLabelText("Nuevo combustible *")).toHaveLength(1);
    expect(screen.queryByRole("region", { name: "Documentos" })).toBeNull();
  });

  it("sin la opción el paso no avanza; el valor igual al RUNT da error; con uno distinto avanza (el soporte de combustible es opcional)", async () => {
    await abrir(definicion([COMBUSTIBLE]));
    fireEvent.click(interruptor(/Conversión de combustible/));
    await waitFor(() => expect(siguiente()).toBeDisabled());
    const combo = within(tarjeta(/Conversión de combustible/)).getByLabelText("Nuevo combustible *");

    fireEvent.change(combo, { target: { value: "GASOLINA" } });
    fireEvent.blur(combo);
    expect(await within(tarjeta(/Conversión de combustible/)).findByText("El nuevo combustible debe ser distinto al registrado en el RUNT.")).toBeInTheDocument();
    expect(siguiente()).toBeDisabled();

    fireEvent.change(combo, { target: { value: "DIESEL" } });
    await waitFor(() => expect(siguiente()).toBeEnabled());
  });

  it("la factura de carrocería que la matriz marca obligatoria se exige en su tarjeta; con el archivo cargado avanza", async () => {
    const { llamadas, estado } = await abrir(definicion([CARROCERIA]), {
      "GET /catalogos/carrocerias?solicitudId=s-1": () => json([{ codigo: "FURGON", etiqueta: "FURGON" }]),
      "POST /solicitudes/s-1/adjuntos": () => json(adjunto("a1", "factura_carroceria"), 201),
    });
    fireEvent.click(interruptor(/Cambio de carrocería/));
    const t = tarjeta(/Cambio de carrocería/);
    expect(within(t).getByText("Factura de carrocería *")).toBeInTheDocument();
    const combo = await within(t).findByRole("combobox", { name: "Nueva carrocería" });
    await waitFor(() => expect(combo).toBeEnabled());
    fireEvent.focus(combo);
    fireEvent.click(await screen.findByRole("option", { name: "FURGON" }));
    await waitFor(() => expect(siguiente()).toBeDisabled());

    estado.actual = borrador({ version: 5, adjuntos: [adjunto("a1", "factura_carroceria")] });
    fireEvent.change(archivos(t)[0] as HTMLInputElement, { target: { files: [pdf()] } });
    await waitFor(() => expect(siguiente()).toBeEnabled());
    expect((llamadas.find((l) => l.clave === "POST /solicitudes/s-1/adjuntos")?.init.body as FormData).get("tipo")).toBe("factura_carroceria");
  });

  it("al apagarla se oculta, deja de exigirse y no se envía", async () => {
    const { llamadas } = await abrir(definicion([COMBUSTIBLE]), {}, borrador({ fieldValues: { vehicle_fuel: "DIESEL" }, datos: { ...DATOS, requisitos: { transformaciones: ["CONVERSION_COMBUSTIBLE"], observaciones: "" } } }));
    const t = tarjeta(/Conversión de combustible/);
    expect(within(t).getByLabelText("Nuevo combustible *")).toHaveValue("DIESEL");

    fireEvent.click(interruptor(/Conversión de combustible/));
    expect(within(t).queryByLabelText("Nuevo combustible *")).toBeNull();
    expect(archivos(t)).toHaveLength(0);
    await waitFor(() => expect(siguiente()).toBeEnabled());

    fireEvent.click(siguiente());
    await waitFor(() => expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/requisitos")).toBe(true));
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/requisitos").transformaciones).toEqual([]);
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/campos").valores).toEqual({ vehicle_fuel: null });
  });

  it("reactivarla no trae el valor anterior: hay que escoger de nuevo", async () => {
    await abrir(definicion([COMBUSTIBLE]));
    fireEvent.click(interruptor(/Conversión de combustible/));
    fireEvent.change(within(tarjeta(/Conversión de combustible/)).getByLabelText("Nuevo combustible *"), { target: { value: "DIESEL" } });
    fireEvent.click(interruptor(/Conversión de combustible/));
    fireEvent.click(interruptor(/Conversión de combustible/));
    expect(within(tarjeta(/Conversión de combustible/)).getByLabelText("Nuevo combustible *")).toHaveValue("");
  });

  it("un tipo sin declaraciones de transformación conserva solo los interruptores", async () => {
    await abrir(definicion([]));
    fireEvent.click(interruptor(/Cambio de color/));
    const t = tarjeta(/Cambio de color/);
    expect(within(t).getByRole("switch")).toHaveAttribute("aria-checked", "true");
    expect(within(t).queryByRole("combobox")).toBeNull();
    expect(archivos(t)).toHaveLength(0);
  });

  it("el Resumen muestra la transformación con su valor nuevo y su soporte cargado", () => {
    const filas = filasDeDeclaraciones(
      definicion([COMBUSTIBLE]),
      borrador({ fieldValues: { vehicle_fuel: "DIESEL" }, adjuntos: [adjunto("a1", "soporte_conversion_combustible")], datos: { ...DATOS, requisitos: { transformaciones: ["CONVERSION_COMBUSTIBLE"], observaciones: "" } } }),
    );
    expect(filas).toEqual([
      { etiqueta: "Nuevo combustible", valor: "DIESEL" },
      { etiqueta: "Soporte conversion combustible", valor: "Cargado" },
    ]);
  });
});
