import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { Adjunto, DeclaracionTipo, DefinicionTipo, Solicitud } from "@/lib/api/cliente";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { cuerpo, DIAGNOSTICO_OK, wizardAbierto, type Manejador } from "./montar-wizard";
import { json } from "./utilidades";

// Renderer único de declaraciones dentro del paso Requisitos: color con catálogo y soporte, causal con soportes por opción,
// trámite simultáneo y los estados del catálogo. El API es un doble según el contrato de la definición.
const RUTA_DEFINICION = "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1";
const flags = (extra: Partial<DefinicionTipo["flags"]> = {}): DefinicionTipo["flags"] => ({
  entryMode: "PLATE",
  requiereComercial: false,
  requierePrenda: false,
  permiteTransformaciones: false,
  permitePrendaComplementaria: false,
  requiereBiometria: false,
  requiereSolicitudPlaca: false,
  ...extra,
});
const COLOR: DeclaracionTipo = {
  clave: "color",
  control: "seleccion_catalogo",
  etiqueta: "Nuevo color",
  obligatoria: true,
  opciones: [],
  fieldKey: "vehicle_color",
  fieldKeysExtra: ["cambio_color"],
  docCodeSoporte: "soporte_cambio_color",
  soporteObligatorio: true,
  catalogo: "colores",
};
const CAUSAL: DeclaracionTipo = {
  clave: "causal",
  control: "seleccion",
  etiqueta: "Causal de cancelación",
  obligatoria: true,
  opciones: [
    { codigo: "DECISION_JUDICIAL", etiqueta: "Decisión judicial", docCodes: ["oficio_judicial"], exigeSoporte: true },
    { codigo: "PERDIDA_TOTAL", etiqueta: "Pérdida total", docCodes: ["certificado_dijin", "certificado_aseguradora"], exigeSoporte: true },
    { codigo: "VOLUNTARIA", etiqueta: "Decisión voluntaria", docCodes: [], exigeSoporte: false },
  ],
  fieldKey: "cancelacion_causal",
  fieldKeysExtra: [],
  docCodeSoporte: "",
  soporteObligatorio: false,
  catalogo: "",
};
const COLORES = [
  { codigo: "ROJO", etiqueta: "Rojo" },
  { codigo: "AZUL_CLARO", etiqueta: "Azul claro" },
  { codigo: "VERDE", etiqueta: "Verde" },
];

function definicion(declaraciones: DeclaracionTipo[], extra: Partial<DefinicionTipo> = {}): DefinicionTipo {
  return definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], { flags: flags(), declaraciones, ...extra });
}
const adjunto = (id: string, tipo: string): Adjunto => ({ id, tipo, nombreArchivo: `${tipo}.pdf`, mimeType: "application/pdf", tamanoBytes: 1000, sha256: "x", creadoEn: "2026-10-28T10:00:00Z" });
const borrador = (extra: Partial<Solicitud> = {}): Solicitud =>
  solicitudDe({
    datos: { diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" } },
    pendientes: [{ codigo: "declaracion_requerida", referencia: "color", paso: "p3" }],
    version: 4,
    ...extra,
  });
const SOLO_COMERCIAL = [{ codigo: "comercial_requerido", referencia: "valorComercial", paso: "p3" }];
const pdf = (nombre = "soporte.pdf") => new File(["%PDF-1.4 contenido"], nombre, { type: "application/pdf" });
const problema = (status: number, code: string, extra: Record<string, unknown> = {}) =>
  new Response(JSON.stringify({ type: "about:blank", title: "t", status, code, ...extra }), { status, headers: { "content-type": "application/problem+json" } });

async function abrir(def: DefinicionTipo, rutas: Record<string, Manejador> = {}, inicial: Solicitud = borrador(), esperarTarjeta = true) {
  const estado = { actual: inicial };
  const m = await wizardAbierto({
    [RUTA_DEFINICION]: () => json(def),
    "GET /solicitudes/s-1": () => json(estado.actual),
    "GET /catalogos/colores": () => json(COLORES),
    "PUT /solicitudes/s-1/campos": () => json((estado.actual = { ...estado.actual, version: estado.actual.version + 1 })),
    "PUT /solicitudes/s-1/requisitos": () => json((estado.actual = { ...estado.actual, version: estado.actual.version + 1 })),
    ...rutas,
  });
  if (esperarTarjeta) await screen.findByRole("region", { name: "Declaraciones del trámite" });
  else await screen.findByRole("heading", { level: 3, name: "Observaciones del trámite" });
  return { ...m, estado };
}
const tarjeta = () => screen.getByRole("region", { name: "Declaraciones del trámite" });
const siguiente = () => screen.getByRole("button", { name: "Siguiente" });

async function elegirColor(nombre: RegExp) {
  const combo = await within(tarjeta()).findByRole("combobox", { name: "Nuevo color" });
  await waitFor(() => expect(combo).toBeEnabled());
  fireEvent.focus(combo);
  fireEvent.click(await screen.findByRole("option", { name: nombre }));
}

describe("declaraciones del tipo en el paso Requisitos", () => {
  it("pinta el valor nuevo con catálogo con búsqueda, marca el obligatorio y pide el soporte solo al elegir un valor", async () => {
    await abrir(definicion([COLOR]), {
      "GET /catalogos/colores?texto=azul": () => json([COLORES[1]]),
      "GET /catalogos/colores?texto=zzz": () => json([]),
    });
    expect(within(tarjeta()).getByText("Nuevo color *")).toBeInTheDocument();
    expect(within(tarjeta()).queryByText(/Soporte cambio color/)).toBeNull();

    const combo = await within(tarjeta()).findByRole("combobox", { name: "Nuevo color" });
    await waitFor(() => expect(combo).toBeEnabled());
    fireEvent.focus(combo);
    expect(await screen.findAllByRole("option")).toHaveLength(3);
    fireEvent.change(combo, { target: { value: "zzz" } });
    expect(await screen.findByText("Sin coincidencias.")).toBeInTheDocument();
    fireEvent.change(combo, { target: { value: "azul" } });
    await waitFor(() => expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Azul claro"]));
    fireEvent.click(screen.getByRole("option", { name: "Azul claro" }));

    expect(combo).toHaveValue("Azul claro");
    expect(within(tarjeta()).getByText("Soporte cambio color *")).toBeInTheDocument();
  });

  it("el catálogo se busca en el servidor con el texto escrito y las carrocerías van con el borrador (el servidor filtra por clase)", async () => {
    const carroceria: DeclaracionTipo = { ...COLOR, clave: "transformacion_carroceria", etiqueta: "Nueva carrocería", fieldKey: "vehicle_body_type", fieldKeysExtra: ["cambio_carroceria"], docCodeSoporte: "factura_carroceria", soporteObligatorio: false, catalogo: "carrocerias" };
    const { llamadas } = await abrir(
      definicion([carroceria]),
      { "GET /catalogos/carrocerias?solicitudId=s-1": () => json([{ codigo: "ESTACAS", etiqueta: "ESTACAS" }, { codigo: "FURGON", etiqueta: "FURGON" }]), "GET /catalogos/carrocerias?solicitudId=s-1&texto=fur": () => json([{ codigo: "FURGON", etiqueta: "FURGON" }]) },
    );
    const combo = await within(tarjeta()).findByRole("combobox", { name: "Nueva carrocería" });
    await waitFor(() => expect(combo).toBeEnabled());
    fireEvent.focus(combo);
    expect(await screen.findAllByRole("option")).toHaveLength(2);
    fireEvent.change(combo, { target: { value: "fur" } });
    await waitFor(() => expect(screen.getAllByRole("option")).toHaveLength(1));
    expect(llamadas.map((l) => l.clave)).toContain("GET /catalogos/carrocerias?solicitudId=s-1&texto=fur");
    expect(llamadas.some((l) => l.clave.includes("clase="))).toBe(false);
    fireEvent.click(screen.getByRole("option", { name: "FURGON" }));
    expect(within(tarjeta()).getByText("Factura carroceria")).toBeInTheDocument();
  });

  it("carrocerías sin opciones para la clase del vehículo: mensaje claro y el campo queda deshabilitado", async () => {
    const carroceria: DeclaracionTipo = { ...COLOR, clave: "transformacion_carroceria", etiqueta: "Nueva carrocería", fieldKey: "vehicle_body_type", catalogo: "carrocerias" };
    await abrir(definicion([carroceria]), { "GET /catalogos/carrocerias?solicitudId=s-1": () => json([]) });
    expect(await within(tarjeta()).findByText("No hay carrocerías disponibles para la clase de este vehículo.")).toBeInTheDocument();
    expect(within(tarjeta()).getByRole("combobox", { name: "Nueva carrocería" })).toBeDisabled();
  });

  it("con opciones en línea (combustibles) no consulta el catálogo y el soporte es opcional", async () => {
    const combustible: DeclaracionTipo = {
      ...COLOR,
      clave: "transformacion_combustible",
      etiqueta: "Nuevo combustible",
      fieldKey: "vehicle_fuel",
      fieldKeysExtra: ["cambio_combustible"],
      docCodeSoporte: "soporte_conversion_combustible",
      soporteObligatorio: false,
      catalogo: "combustibles",
      opciones: [{ codigo: "GAS", etiqueta: "Gas natural", docCodes: [], exigeSoporte: false }],
    };
    const { llamadas } = await abrir(definicion([combustible]));
    fireEvent.change(within(tarjeta()).getByLabelText("Nuevo combustible *"), { target: { value: "GAS" } });
    expect(within(tarjeta()).getByText("Soporte conversion combustible")).toBeInTheDocument();
    expect(within(tarjeta()).getAllByText("Opcional")).toHaveLength(1);
    expect(llamadas.some((l) => l.clave.startsWith("GET /catalogos"))).toBe(false);
    await waitFor(() => expect(siguiente()).toBeEnabled());
  });

  it("sin valor ni soporte el paso no avanza y el error se anuncia; con ambos guarda el valor y la bandera", async () => {
    const { llamadas, estado } = await abrir(definicion([COLOR]), { "POST /solicitudes/s-1/adjuntos": () => json(adjunto("a1", "soporte_cambio_color"), 201) });
    expect(siguiente()).toBeDisabled();

    await elegirColor(/Rojo/);
    const input = within(tarjeta()).getByLabelText(/Soporte cambio color/) as HTMLInputElement;
    expect(siguiente()).toBeDisabled();
    estado.actual = borrador({ version: 5, adjuntos: [adjunto("a1", "soporte_cambio_color")], pendientes: SOLO_COMERCIAL });
    fireEvent.change(input, { target: { files: [pdf()] } });

    const envio = llamadas.find((l) => l.clave === "POST /solicitudes/s-1/adjuntos");
    await waitFor(() => expect(envio ?? llamadas.find((l) => l.clave === "POST /solicitudes/s-1/adjuntos")).toBeTruthy());
    await waitFor(() => expect(siguiente()).toBeEnabled());
    const subido = llamadas.find((l) => l.clave === "POST /solicitudes/s-1/adjuntos");
    expect((subido?.init.body as FormData).get("tipo")).toBe("soporte_cambio_color");

    fireEvent.click(siguiente());
    await waitFor(() => expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/campos")).toBe(true));
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/campos").valores).toEqual({ vehicle_color: "ROJO" });
  });

  it("la causal pide los documentos de su opción y los cambia al elegir otra", async () => {
    await abrir(definicion([CAUSAL]));
    const causal = within(tarjeta()).getByLabelText("Causal de cancelación *");
    fireEvent.blur(causal);
    expect(within(tarjeta()).getByRole("alert")).toHaveTextContent("Este campo es obligatorio");
    expect(causal).toHaveAttribute("aria-invalid", "true");

    fireEvent.change(causal, { target: { value: "PERDIDA_TOTAL" } });
    expect(within(tarjeta()).getByText("Certificado dijin *")).toBeInTheDocument();
    expect(within(tarjeta()).getByText("Certificado aseguradora *")).toBeInTheDocument();
    expect(within(tarjeta()).getAllByText(/Respalda: Causal de cancelación \(Pérdida total\)/)).toHaveLength(2);

    fireEvent.change(causal, { target: { value: "DECISION_JUDICIAL" } });
    expect(within(tarjeta()).getByText("Oficio judicial *")).toBeInTheDocument();
    expect(within(tarjeta()).queryByText("Certificado dijin *")).toBeNull();

    fireEvent.change(causal, { target: { value: "VOLUNTARIA" } });
    expect(tarjeta().querySelectorAll('input[type="file"]')).toHaveLength(0);
  });

  it("el trámite simultáneo activa la declaración opcional atada y al apagarlo se limpia lo guardado", async () => {
    const simultaneo: DeclaracionTipo = { ...COLOR, clave: "color_simultaneo", obligatoria: false, soporteObligatorio: false };
    const def = definicion([simultaneo], { flags: flags({ permiteTransformaciones: true }) });
    const { llamadas } = await abrir(def, {}, borrador({ pendientes: SOLO_COMERCIAL, fieldValues: { vehicle_color: "ROJO" } }), false);
    expect(screen.queryByRole("region", { name: "Declaraciones del trámite" })).toBeNull();

    const interruptor = await screen.findByRole("switch", { name: /Cambio de color/ });
    fireEvent.click(interruptor);
    // La declaración atada sale en la tarjeta de su transformación, no en «Declaraciones del trámite».
    const suTarjeta = screen.getByRole("region", { name: /Cambio de color/ });
    expect(screen.queryByRole("region", { name: "Declaraciones del trámite" })).toBeNull();
    const combo = await within(suTarjeta).findByRole("combobox", { name: "Nuevo color" });
    await waitFor(() => expect(combo).toBeEnabled());
    fireEvent.focus(combo);
    fireEvent.click(await screen.findByRole("option", { name: /Verde/ }));
    expect(siguiente()).toBeEnabled();
    fireEvent.click(interruptor);
    await waitFor(() => expect(within(suTarjeta).queryByRole("combobox", { name: "Nuevo color" })).toBeNull());

    fireEvent.click(siguiente());
    await waitFor(() => expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/campos")).toBe(true));
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/campos").valores).toEqual({ vehicle_color: null });
  });

  it("estado de carga, error con reintento y catálogo vacío", async () => {
    let intentos = 0;
    await abrir(definicion([COLOR]), {
      "GET /catalogos/colores": () => (++intentos === 1 ? problema(503, "x") : json([])),
    });
    const combo = within(tarjeta()).getByRole("combobox", { name: "Nuevo color" });
    expect(combo).toBeDisabled();
    fireEvent.click(await within(tarjeta()).findByRole("button", { name: "Reintentar" }));
    expect(await within(tarjeta()).findByText("No hay opciones disponibles por ahora.")).toBeInTheDocument();
    expect(within(tarjeta()).getByRole("combobox", { name: "Nuevo color" })).toBeDisabled();
  });

  it("un 422 del servidor sobre el campo de la declaración se muestra en ella", async () => {
    const { llamadas } = await abrir(
      definicion([CAUSAL]),
      { "PUT /solicitudes/s-1/campos": () => problema(422, "datos_invalidos", { errors: { "valores.cancelacion_causal": ["La causal no es válida."] } }) },
    );
    fireEvent.change(within(tarjeta()).getByLabelText("Causal de cancelación *"), { target: { value: "VOLUNTARIA" } });
    await waitFor(() => expect(siguiente()).toBeEnabled());
    fireEvent.click(siguiente());
    expect(await within(tarjeta()).findByText("La causal no es válida.")).toBeInTheDocument();
    expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/campos")).toBe(true);
  });

  it("un soporte que la matriz del tipo también lista se pide solo en la tarjeta de declaraciones", async () => {
    const doc = { docCode: "soporte_cambio_color", nombre: "Factura del cambio", obligatorio: true, orden: 1, instruccionCargue: "", condicion: "" };
    await abrir(definicion([COLOR], { documentos: [doc, { ...doc, docCode: "otro", nombre: "Otro documento" }] }), {}, borrador({ fieldValues: { vehicle_color: "ROJO" } }));
    expect(screen.getAllByText(/Factura del cambio/)).toHaveLength(1);
    expect(within(screen.getByRole("region", { name: "Documentos" })).getByText("Otro documento *")).toBeInTheDocument();
  });
});
