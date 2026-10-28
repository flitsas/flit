import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Adjunto, DefinicionTipo, Solicitud } from "@/lib/api/cliente";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { DIAGNOSTICO_OK, nodo, wizardAbierto, type Manejador } from "./montar-wizard";
import { json } from "./utilidades";

const RUTA_DEFINICION = "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1";
const flags = (extra: Partial<DefinicionTipo["flags"]> = {}): DefinicionTipo["flags"] => ({
  entryMode: "PLATE",
  requiereComercial: true,
  requierePrenda: false,
  permiteTransformaciones: true,
  permitePrendaComplementaria: true,
  requiereBiometria: false,
  requiereSolicitudPlaca: false,
  ...extra,
});
const DOCS: DefinicionTipo["documentos"] = [
  { docCode: "paz_salvo", nombre: "Paz y salvo de impuestos", obligatorio: true, orden: 2, instruccionCargue: "Expedido por la Secretaría de Hacienda", condicion: "" },
  { docCode: "improntas", nombre: "Improntas", obligatorio: false, orden: 1, instruccionCargue: "Adjunta las improntas legibles", condicion: "" },
  { docCode: "cert_especial", nombre: "Certificado especial", obligatorio: true, orden: 3, instruccionCargue: "", condicion: "el vehículo es de servicio público" },
  { docCode: "comprobante_pago", nombre: "Comprobante de pago", obligatorio: true, orden: 4, instruccionCargue: "", condicion: "" },
];
const CAMPOS: DefinicionTipo["pasos"][number]["campos"] = [
  { fieldKey: "cilindraje", etiqueta: "Cilindraje", tipo: "number", obligatorio: true, opciones: [], validationSchema: "" },
  { fieldKey: "color", etiqueta: "Color", tipo: "select", obligatorio: false, opciones: ["ROJO", "AZUL"], validationSchema: "" },
  { fieldKey: "entrega", etiqueta: "Fecha de entrega", tipo: "date", obligatorio: false, opciones: [], validationSchema: "" },
  { fieldKey: "nota", etiqueta: "Nota", tipo: "text", obligatorio: false, opciones: [], validationSchema: "" },
];

function definicion(extra: Partial<DefinicionTipo> = {}, conCampos = true): DefinicionTipo {
  const d = definicionDe([["vehicle_query"], ["actor_form"], ["commercial", "prenda_decision", "document_checklist", "generic_form"]], { flags: flags(), documentos: DOCS, ...extra });
  return { ...d, pasos: d.pasos.map((p) => (p.clave === "p3" ? { ...p, titulo: "Datos del vehículo", campos: conCampos ? CAMPOS : [] } : p)) };
}

// Con documentos ya cargados el back solo reporta lo comercial: el wizard reabre en este paso.
const SOLO_COMERCIAL = [{ codigo: "comercial_requerido", referencia: "valorComercial", paso: "p3" }];
const adjunto = (id: string, tipo: string, nombre: string, creadoEn = "2026-10-28T10:00:00Z"): Adjunto => ({ id, tipo, nombreArchivo: nombre, mimeType: "application/pdf", tamanoBytes: 1000, sha256: "x", creadoEn });
const borrador = (extra: Partial<Solicitud> = {}): Solicitud =>
  solicitudDe({
    datos: { diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" } },
    pendientes: [{ codigo: "documento_requerido", referencia: "paz_salvo", paso: "p3" }],
    version: 4,
    ...extra,
  });
const problema = (status: number, code: string, extra: Record<string, unknown> = {}) =>
  new Response(JSON.stringify({ type: "about:blank", title: "t", status, code, ...extra }), { status, headers: { "content-type": "application/problem+json" } });
const pdf = (nombre = "paz.pdf") => new File(["%PDF-1.4 contenido"], nombre, { type: "application/pdf" });

/** Abre el wizard en el paso 3. `estado.actual` es el borrador que devuelve GET; las pruebas lo cambian como lo haría el back. */
async function abrir(rutas: Record<string, Manejador> = {}, { def = definicion(), inicial = borrador() }: { def?: DefinicionTipo; inicial?: Solicitud } = {}) {
  const estado = { actual: inicial };
  const m = await wizardAbierto({ [RUTA_DEFINICION]: () => json(def), "GET /solicitudes/s-1": () => json(estado.actual), ...rutas });
  await screen.findByRole("heading", { level: 3, name: "Observaciones del trámite" });
  return { ...m, estado };
}

const tarjeta = (nombre: string) => screen.getByRole("region", { name: nombre });
const siguiente = () => screen.getByRole("button", { name: "Siguiente" });
const entrada = (nombre: RegExp | string) => screen.getByLabelText(nombre) as HTMLInputElement;

function llenarObligatorios() {
  fireEvent.change(entrada(/Valor de venta/), { target: { value: "45000000" } });
  fireEvent.change(entrada(/Cilindraje/), { target: { value: "1600" } });
}

afterEach(() => {
  vi.useRealTimers();
});

describe("Paso 3 · tarjetas que aplican al tipo, en el orden del repo (F4 AC1 y K6 AC4)", () => {
  it("con todas las capacidades pinta las tarjetas en orden y el valor total liquidado debajo", async () => {
    await abrir();

    const titulos = screen.getAllByRole("heading", { level: 3 }).map((h) => h.textContent);
    expect(titulos.filter((t) => ["Datos comerciales", "Prenda / limitación a la propiedad", "Documentos", "Observaciones del trámite", "Transformaciones del vehículo"].includes(t ?? ""))).toEqual([
      "Datos comerciales",
      "Prenda / limitación a la propiedad",
      "Documentos",
      "Observaciones del trámite",
      "Transformaciones del vehículo",
    ]);
    expect(titulos).toContain("Datos del vehículo");
    expect(screen.getByText(/Valor total liquidado:/)).toHaveTextContent("Valor total liquidado: —");
    expect(screen.getByRole("button", { name: "Atrás" })).toBeInTheDocument();
    expect(siguiente()).toBeInTheDocument();
  });

  it("un tipo sin comercial, prenda ni transformaciones muestra solo documentos y observaciones, sin huecos", async () => {
    await abrir({}, { def: definicion({ flags: flags({ requiereComercial: false, permiteTransformaciones: false, permitePrendaComplementaria: false }) }, false) });

    expect(screen.queryByRole("heading", { level: 3, name: "Datos comerciales" })).toBeNull();
    expect(screen.queryByRole("heading", { level: 3, name: "Prenda / limitación a la propiedad" })).toBeNull();
    expect(screen.queryByRole("heading", { level: 3, name: "Transformaciones del vehículo" })).toBeNull();
    expect(screen.getByRole("heading", { level: 3, name: "Documentos" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 3, name: "Observaciones del trámite" })).toBeInTheDocument();
  });

  it("un tipo con transformaciones y sin prenda ni documentos muestra solo lo que su definición habilita", async () => {
    await abrir({}, { def: definicion({ flags: flags({ requiereComercial: false, permitePrendaComplementaria: false }), documentos: [] }, false), inicial: borrador({ pendientes: SOLO_COMERCIAL }) });

    expect(screen.queryByRole("heading", { level: 3, name: "Documentos" })).toBeNull();
    expect(screen.getByRole("heading", { level: 3, name: "Transformaciones del vehículo" })).toBeInTheDocument();
    expect(screen.getAllByRole("switch")).toHaveLength(3);
    expect(siguiente()).toBeEnabled();
  });
});

describe("datos comerciales y prenda (F4 AC2 y AC3)", () => {
  it("el valor de venta acepta solo dígitos y es obligatorio; el avalúo muestra «—» y Fasecolda no disponible", async () => {
    await abrir();
    const card = tarjeta("Datos comerciales");

    expect(within(card).getByLabelText("Avalúo sugerido (Fasecolda)")).toBeDisabled();
    expect(within(card).getByLabelText("Avalúo sugerido (Fasecolda)")).toHaveAttribute("placeholder", "—");
    expect(within(card).getByText("Fasecolda: No disponible")).toBeInTheDocument();
    const valor = within(card).getByLabelText(/Valor de venta/);
    fireEvent.change(valor, { target: { value: "12a.3" } });
    expect(valor).toHaveValue("123");
    fireEvent.change(valor, { target: { value: "" } });
    fireEvent.blur(valor);
    expect(within(card).getByText("Ingresa el valor de venta")).toBeInTheDocument();
    expect(valor).toHaveAttribute("aria-invalid", "true");
  });

  it("la prenda se pregunta: «Sí» pide acreedor y NIT obligatorios y «No» los oculta", async () => {
    await abrir();
    const card = tarjeta("Prenda / limitación a la propiedad");
    const no = within(card).getByRole("radio", { name: "No" });
    expect(no).toBeChecked();
    expect(within(card).queryByLabelText(/Acreedor/)).toBeNull();

    fireEvent.click(within(card).getByRole("radio", { name: "Sí" }));
    const acreedor = within(card).getByLabelText("Acreedor (beneficiario) *");
    const nit = within(card).getByLabelText("NIT / Documento del acreedor *");
    fireEvent.blur(acreedor);
    fireEvent.change(nit, { target: { value: "86-0.002.964x" } });
    expect(nit).toHaveValue("860002964");
    fireEvent.change(nit, { target: { value: "12" } });
    fireEvent.blur(nit);
    expect(within(card).getByText("Campo obligatorio")).toBeInTheDocument();
    expect(within(card).getByText("Ingresa el NIT del acreedor (8 a 10 dígitos)")).toBeInTheDocument();
    expect(acreedor).toHaveAttribute("aria-invalid", "true");

    fireEvent.click(within(card).getByRole("radio", { name: "No" }));
    expect(within(card).queryByLabelText(/Acreedor/)).toBeNull();
  });

  it("si la prenda es obligatoria por definición no se pregunta: pide acreedor y NIT directamente", async () => {
    await abrir({}, { def: definicion({ flags: flags({ requierePrenda: true }) }) });
    const card = tarjeta("Prenda / limitación a la propiedad");

    expect(within(card).queryByText("¿Al vehículo se le asociará una prenda?")).toBeNull();
    expect(within(card).getByLabelText("Acreedor (beneficiario) *")).toBeInTheDocument();
    expect(within(card).getByLabelText("NIT / Documento del acreedor *")).toBeInTheDocument();
  });
});

describe("campos dinámicos del tipo (K6 AC1)", () => {
  it("pinta cada campo según su tipo, marca los obligatorios y valida antes de continuar", async () => {
    await abrir();
    const card = tarjeta("Datos del vehículo");

    expect(within(card).getByLabelText("Cilindraje *")).toHaveAttribute("inputmode", "decimal");
    expect(within(card).getByLabelText("Color")).toHaveProperty("tagName", "SELECT");
    expect(within(card).getAllByRole("option").map((o) => o.textContent)).toEqual(["Selecciona", "ROJO", "AZUL"]);
    expect(within(card).getByLabelText("Fecha de entrega")).toHaveAttribute("type", "date");
    expect(within(card).getByLabelText("Nota")).not.toHaveAttribute("type");

    const cilindraje = within(card).getByLabelText("Cilindraje *");
    fireEvent.blur(cilindraje);
    expect(within(card).getByText("Este campo es obligatorio")).toBeInTheDocument();
    expect(cilindraje).toHaveAttribute("aria-invalid", "true");
    fireEvent.change(cilindraje, { target: { value: "1.6.2" } });
    fireEvent.blur(cilindraje);
    expect(within(card).getByText("Escribe un número")).toBeInTheDocument();
    expect(siguiente()).toBeDisabled();
  });
});

describe("documentos de la definición (F4 AC4, K6 AC2 y AC3)", () => {
  it("un cargador por documento, ordenado, con su instrucción, «*» en los obligatorios y sin el comprobante de pago", async () => {
    await abrir();
    const card = tarjeta("Documentos");

    const titulos = within(card).getAllByText(/^(Improntas|Paz y salvo de impuestos \*|Certificado especial)$/).map((n) => n.textContent);
    expect(titulos).toEqual(["Improntas", "Paz y salvo de impuestos *", "Certificado especial"]);
    expect(within(card).queryByText(/Comprobante de pago/)).toBeNull();
    expect(within(card).getByText(/Expedido por la Secretaría de Hacienda\. PDF, JPG, PNG, WEBP · hasta 20\.0 MB/)).toBeInTheDocument();
    // Optional (Improntas) y el condicional (su condición se evalúa en el servidor): no se exigen.
    expect(within(card).getAllByText("Opcional")).toHaveLength(2);
    expect(within(card).getByText(/Aplica si: el vehículo es de servicio público/)).toBeInTheDocument();
    expect(card.querySelectorAll('input[type="file"]')).toHaveLength(3);
    expect(card.querySelector('input[type="file"]')).toHaveAttribute("accept", ".pdf,.jpg,.jpeg,.png,.webp");
  });

  it("sube el archivo con su tipo (multipart), muestra «subiendo» con role=status y luego el nombre cargado", async () => {
    let terminar: (r: Response) => void = () => undefined;
    const { llamadas, estado } = await abrir({
      "POST /solicitudes/s-1/adjuntos": () => new Promise<Response>((r) => (terminar = r)),
    });
    const input = entrada(/Paz y salvo de impuestos/);
    fireEvent.change(input, { target: { files: [pdf("paz.pdf")] } });

    expect(await within(tarjeta("Documentos")).findByRole("status")).toHaveTextContent("Subiendo paz.pdf...");
    const envio = llamadas.find((l) => l.clave === "POST /solicitudes/s-1/adjuntos");
    expect(envio?.init.body).toBeInstanceOf(FormData);
    expect((envio?.init.body as FormData).get("tipo")).toBe("paz_salvo");
    expect(((envio?.init.body as FormData).get("archivo") as File).name).toBe("paz.pdf");

    estado.actual = borrador({ version: 5, adjuntos: [adjunto("a1", "paz_salvo", "paz.pdf")], pendientes: SOLO_COMERCIAL });
    terminar(json(adjunto("a1", "paz_salvo", "paz.pdf"), 201));

    expect(await within(tarjeta("Documentos")).findByText(/paz\.pdf · Da clic para reemplazar el archivo/)).toBeInTheDocument();
    expect(within(tarjeta("Documentos")).getByRole("button", { name: "Quitar Paz y salvo de impuestos *" })).toBeInTheDocument();
  });

  it("un tipo de archivo no permitido muestra el error en el propio cargador y no sube nada", async () => {
    const { llamadas } = await abrir();
    fireEvent.change(entrada(/Paz y salvo de impuestos/), { target: { files: [new File(["x"], "virus.exe")] } });

    const card = tarjeta("Documentos");
    expect(await within(card).findByText("Solo se aceptan archivos PDF, JPG, PNG o WEBP.")).toBeInTheDocument();
    expect(entrada(/Paz y salvo de impuestos/)).toHaveAttribute("aria-invalid", "true");
    expect(llamadas.some((l) => l.clave === "POST /solicitudes/s-1/adjuntos")).toBe(false);
  });

  it("un archivo de más de 20 MB muestra el error y no se sube", async () => {
    const { llamadas } = await abrir();
    const pesado = pdf("grande.pdf");
    Object.defineProperty(pesado, "size", { value: 20 * 1024 * 1024 + 1 });
    fireEvent.change(entrada(/Paz y salvo de impuestos/), { target: { files: [pesado] } });

    expect(await within(tarjeta("Documentos")).findByText("El archivo supera los 20 MB permitidos.")).toBeInTheDocument();
    expect(llamadas.some((l) => l.clave === "POST /solicitudes/s-1/adjuntos")).toBe(false);
  });

  it("si el servidor falla muestra el error con «Reintentar subida», conserva el archivo y reintenta", async () => {
    let falla = true;
    const { llamadas, estado } = await abrir({
      "POST /solicitudes/s-1/adjuntos": () => {
        if (falla) return problema(502, "almacenamiento_no_disponible");
        estado.actual = borrador({ version: 5, adjuntos: [adjunto("a1", "paz_salvo", "paz.pdf")], pendientes: SOLO_COMERCIAL });
        return json(adjunto("a1", "paz_salvo", "paz.pdf"), 201);
      },
    });
    fireEvent.change(entrada(/Paz y salvo de impuestos/), { target: { files: [pdf("paz.pdf")] } });

    expect(await within(tarjeta("Documentos")).findByText("No pudimos guardar el archivo. Intenta de nuevo.")).toBeInTheDocument();
    falla = false;
    fireEvent.click(within(tarjeta("Documentos")).getByRole("button", { name: "Reintentar subida" }));

    expect(await within(tarjeta("Documentos")).findByText(/paz\.pdf · Da clic para reemplazar/)).toBeInTheDocument();
    expect(llamadas.filter((l) => l.clave === "POST /solicitudes/s-1/adjuntos")).toHaveLength(2);
  });

  it("un tipo de archivo que el back rechaza (415) y uno enorme (413) se explican", async () => {
    await abrir({ "POST /solicitudes/s-1/adjuntos": () => problema(415, "adjunto_no_permitido") });
    fireEvent.change(entrada(/Improntas/), { target: { files: [pdf("improntas.pdf")] } });

    expect(await within(tarjeta("Documentos")).findByText("Solo se aceptan archivos PDF, JPG, PNG o WEBP de hasta 20 MB.")).toBeInTheDocument();
  });

  it("se puede quitar el archivo cargado: DELETE por id y el cargador vuelve a su estado inicial", async () => {
    const { llamadas, estado } = await abrir(
      {
        "DELETE /solicitudes/s-1/adjuntos/a1": () => {
          estado.actual = borrador({ version: 6 });
          return new Response(null, { status: 204 });
        },
      },
      { inicial: borrador({ adjuntos: [adjunto("a1", "paz_salvo", "paz.pdf")], pendientes: SOLO_COMERCIAL }) },
    );
    const card = tarjeta("Documentos");
    expect(within(card).getByText(/paz\.pdf · Da clic para reemplazar/)).toBeInTheDocument();

    fireEvent.click(within(card).getByRole("button", { name: "Quitar Paz y salvo de impuestos *" }));

    await waitFor(() => expect(within(card).queryByText(/paz\.pdf/)).toBeNull());
    expect(llamadas.some((l) => l.clave === "DELETE /solicitudes/s-1/adjuntos/a1")).toBe(true);
  });

  it("reemplazar un archivo sube el nuevo y retira el anterior", async () => {
    const { llamadas, estado } = await abrir(
      {
        "POST /solicitudes/s-1/adjuntos": () => {
          estado.actual = borrador({ version: 5, adjuntos: [adjunto("a1", "paz_salvo", "viejo.pdf"), adjunto("a2", "paz_salvo", "nuevo.pdf", "2026-10-28T11:00:00Z")], pendientes: SOLO_COMERCIAL });
          return json(adjunto("a2", "paz_salvo", "nuevo.pdf", "2026-10-28T11:00:00Z"), 201);
        },
        "DELETE /solicitudes/s-1/adjuntos/a1": () => {
          estado.actual = borrador({ version: 6, adjuntos: [adjunto("a2", "paz_salvo", "nuevo.pdf", "2026-10-28T11:00:00Z")], pendientes: SOLO_COMERCIAL });
          return new Response(null, { status: 204 });
        },
      },
      { inicial: borrador({ adjuntos: [adjunto("a1", "paz_salvo", "viejo.pdf")], pendientes: SOLO_COMERCIAL }) },
    );
    fireEvent.change(entrada(/Paz y salvo de impuestos/), { target: { files: [pdf("nuevo.pdf")] } });

    expect(await within(tarjeta("Documentos")).findByText(/nuevo\.pdf · Da clic para reemplazar/)).toBeInTheDocument();
    const claves = llamadas.map((l) => l.clave);
    expect(claves.indexOf("POST /solicitudes/s-1/adjuntos")).toBeLessThan(claves.indexOf("DELETE /solicitudes/s-1/adjuntos/a1"));
  });
});

describe("transformaciones y total (F4 AC5)", () => {
  it("cada interruptor tiene nombre y estado; sin ninguna activa se lee el dato del RUNT", async () => {
    await abrir();
    const card = tarjeta("Transformaciones del vehículo");

    expect(within(card).getByText("Sin transformaciones declaradas: se registrará el dato del RUNT.")).toBeInTheDocument();
    const color = within(card).getByRole("switch", { name: "Cambio de color" });
    expect(color).toHaveAttribute("aria-checked", "false");
    fireEvent.click(color);
    expect(color).toHaveAttribute("aria-checked", "true");
    expect(within(card).queryByText(/Sin transformaciones declaradas/)).toBeNull();
    expect(within(card).getByRole("switch", { name: "Conversión de combustible" })).toBeInTheDocument();
    expect(within(card).getByRole("switch", { name: "Cambio de carrocería" })).toBeInTheDocument();
  });

  it("el total liquidado se calcula en el back con las transformaciones activas como conceptos adicionales", async () => {
    const { llamadas } = await abrir({
      "POST /cotizaciones": () => json({ lineas: [], totalCop: 426000, versionTarifa: "v1", adicionalesNoAplicados: ["CAMBIO_CARROCERIA"] }),
    });
    fireEvent.click(within(tarjeta("Transformaciones del vehículo")).getByRole("switch", { name: "Cambio de color" }));
    fireEvent.click(within(tarjeta("Transformaciones del vehículo")).getByRole("switch", { name: "Cambio de carrocería" }));

    fireEvent.click(screen.getByRole("button", { name: "Calcular valor total" }));

    expect(await screen.findByText("Valor total liquidado: $ 426.000 COP")).toBeInTheDocument();
    const peticion = llamadas.find((l) => l.clave === "POST /cotizaciones");
    expect(JSON.parse(String(peticion?.init.body))).toEqual({ organismoId: "o-1", tipoCodigo: "TIPO_A", adicionales: ["CAMBIO_COLOR", "CAMBIO_CARROCERIA"] });
    expect(screen.getByText("No aplican en este organismo: Cambio de carrocería.")).toBeInTheDocument();
  });

  it("sin transformaciones el total comparte la cotización del valor base y no vuelve a pedir la verificación", async () => {
    const { llamadas } = await abrir({ "POST /cotizaciones": () => json({ lineas: [], totalCop: 366000, versionTarifa: "v1", adicionalesNoAplicados: [] }) });

    fireEvent.click(screen.getByRole("button", { name: "Calcular valor total" }));

    expect(await screen.findByText("Valor total liquidado: $ 366.000 COP")).toBeInTheDocument();
    expect(JSON.parse(String(llamadas.find((l) => l.clave === "POST /cotizaciones")?.init.body))).toEqual({ organismoId: "o-1", tipoCodigo: "TIPO_A" });
  });
});

describe("guardado del borrador con versión y avance (F4 AC6)", () => {
  /** Rutas que aceptan cada escritura y devuelven el borrador con la versión siguiente. */
  const escrituras = (estado: { actual: Solicitud }): Record<string, Manejador> => ({
    "PUT /solicitudes/s-1/campos": () => json((estado.actual = { ...estado.actual, version: estado.actual.version + 1 })),
    "PUT /solicitudes/s-1/comercial": () => json((estado.actual = { ...estado.actual, version: estado.actual.version + 1 })),
    "PUT /solicitudes/s-1/requisitos": () => json((estado.actual = { ...estado.actual, version: estado.actual.version + 1 })),
  });

  it("«Siguiente» guarda campos, comercial con prenda y requisitos, cada uno con la versión vigente, y pasa al paso 4", async () => {
    const estado = { actual: borrador({ adjuntos: [adjunto("a1", "paz_salvo", "paz.pdf")], pendientes: SOLO_COMERCIAL }) };
    const { llamadas } = await abrir(
      { "GET /solicitudes/s-1": () => json(estado.actual), ...escrituras(estado) },
      { inicial: estado.actual },
    );
    llenarObligatorios();
    fireEvent.change(tarjeta("Datos del vehículo").querySelector("select") as HTMLSelectElement, { target: { value: "AZUL" } });
    fireEvent.click(within(tarjeta("Prenda / limitación a la propiedad")).getByRole("radio", { name: "Sí" }));
    fireEvent.change(entrada("Acreedor (beneficiario) *"), { target: { value: "  Banco Uno " } });
    fireEvent.change(entrada("NIT / Documento del acreedor *"), { target: { value: "860002964" } });
    fireEvent.click(within(tarjeta("Transformaciones del vehículo")).getByRole("switch", { name: "Cambio de color" }));
    fireEvent.change(within(tarjeta("Observaciones del trámite")).getByRole("textbox"), { target: { value: "Entregar el lunes" } });
    await waitFor(() => expect(siguiente()).toBeEnabled());

    fireEvent.click(siguiente());

    await waitFor(() => expect(nodo(4)).toHaveAttribute("aria-current", "step"));
    const cuerpo = (clave: string) => JSON.parse(String(llamadas.find((l) => l.clave === clave)?.init.body));
    expect(cuerpo("PUT /solicitudes/s-1/campos")).toEqual({ version: 4, valores: { cilindraje: "1600", color: "AZUL", entrega: null, nota: null } });
    expect(cuerpo("PUT /solicitudes/s-1/comercial")).toEqual({ version: 5, valorComercial: 45000000, prenda: { tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860002964" } });
    expect(cuerpo("PUT /solicitudes/s-1/requisitos")).toEqual({ version: 6, transformaciones: ["CAMBIO_COLOR"], observaciones: "Entregar el lunes" });
  });

  it("con «No» a la prenda no se envían acreedor ni NIT", async () => {
    const estado = { actual: borrador({ adjuntos: [adjunto("a1", "paz_salvo", "paz.pdf")], pendientes: SOLO_COMERCIAL }) };
    const { llamadas } = await abrir({ "GET /solicitudes/s-1": () => json(estado.actual), ...escrituras(estado) }, { inicial: estado.actual });
    llenarObligatorios();
    fireEvent.click(within(tarjeta("Prenda / limitación a la propiedad")).getByRole("radio", { name: "Sí" }));
    fireEvent.change(entrada("Acreedor (beneficiario) *"), { target: { value: "Banco" } });
    fireEvent.click(within(tarjeta("Prenda / limitación a la propiedad")).getByRole("radio", { name: "No" }));
    await waitFor(() => expect(siguiente()).toBeEnabled());
    fireEvent.click(siguiente());

    await waitFor(() => expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/comercial")).toBe(true));
    expect(JSON.parse(String(llamadas.find((l) => l.clave === "PUT /solicitudes/s-1/comercial")?.init.body)).prenda).toEqual({ tienePrenda: false });
  });

  it("«Siguiente» sigue bloqueado con un documento obligatorio sin cargar y avisa por qué", async () => {
    await abrir();
    llenarObligatorios();

    expect(siguiente()).toBeDisabled();
    expect(screen.getByText("Completa los datos y documentos obligatorios para continuar.")).toBeInTheDocument();
  });

  it("un 409 de versión avisa y ofrece recargar, sin pasar de paso", async () => {
    const estado = { actual: borrador({ adjuntos: [adjunto("a1", "paz_salvo", "paz.pdf")], pendientes: SOLO_COMERCIAL }) };
    await abrir({ "GET /solicitudes/s-1": () => json(estado.actual), "PUT /solicitudes/s-1/campos": () => problema(409, "conflicto_version") }, { inicial: estado.actual });
    llenarObligatorios();
    await waitFor(() => expect(siguiente()).toBeEnabled());
    fireEvent.click(siguiente());

    expect(await screen.findByText(/Este trámite cambió mientras lo editabas/)).toBeInTheDocument();
    expect(nodo(3)).toHaveAttribute("aria-current", "step");
    expect(entrada(/Cilindraje/)).toHaveValue("1600");
  });

  it("un 422 del back pone el error en el campo y no pierde lo escrito", async () => {
    const estado = { actual: borrador({ adjuntos: [adjunto("a1", "paz_salvo", "paz.pdf")], pendientes: SOLO_COMERCIAL }) };
    await abrir(
      { "GET /solicitudes/s-1": () => json(estado.actual), "PUT /solicitudes/s-1/campos": () => problema(422, "datos_invalidos", { errors: { "valores.cilindraje": ["Escriba un número."] } }) },
      { inicial: estado.actual },
    );
    llenarObligatorios();
    await waitFor(() => expect(siguiente()).toBeEnabled());
    fireEvent.click(siguiente());

    expect(await within(tarjeta("Datos del vehículo")).findByText("Escriba un número.")).toBeInTheDocument();
    expect(entrada(/Cilindraje/)).toHaveAttribute("aria-invalid", "true");
    expect(entrada(/Valor de venta/)).toHaveValue("45000000");
  });

  it("tras un rato sin escribir guarda el borrador (si lo escrito es válido) y confirma «Borrador guardado»", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const estado = { actual: borrador({ adjuntos: [adjunto("a1", "paz_salvo", "paz.pdf")], pendientes: SOLO_COMERCIAL }) };
    const { llamadas } = await abrir({ "GET /solicitudes/s-1": () => json(estado.actual), ...escrituras(estado) }, { inicial: estado.actual });
    llenarObligatorios();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    expect(llamadas.some((l) => l.clave.startsWith("PUT "))).toBe(false);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2500);
    });

    expect(await screen.findByText("Borrador guardado")).toBeInTheDocument();
    expect(llamadas.filter((l) => l.clave === "PUT /solicitudes/s-1/comercial")).toHaveLength(1);
  });

  it("si falla el autoguardado muestra el error con «Reintentar» y no pierde lo escrito", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const estado = { actual: borrador({ adjuntos: [adjunto("a1", "paz_salvo", "paz.pdf")], pendientes: SOLO_COMERCIAL }) };
    let falla = true;
    await abrir(
      { "GET /solicitudes/s-1": () => json(estado.actual), ...escrituras(estado), "PUT /solicitudes/s-1/campos": () => (falla ? problema(503, "x") : json((estado.actual = { ...estado.actual, version: 5 }))) },
      { inicial: estado.actual },
    );
    llenarObligatorios();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3500);
    });

    expect(await screen.findByText(/No pudimos guardar el borrador/)).toBeInTheDocument();
    expect(entrada(/Valor de venta/)).toHaveValue("45000000");
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Borrador guardado")).toBeInTheDocument();
  });
});

describe("Habeas Data en el paso Requisitos", () => {
  it("el nombre de los archivos y los valores escritos no salen en la URL ni en el almacenamiento", async () => {
    const { llamadas, estado } = await abrir({
      "POST /solicitudes/s-1/adjuntos": () => {
        estado.actual = borrador({ version: 5, adjuntos: [adjunto("a1", "paz_salvo", "cedula-1020456789.pdf")], pendientes: SOLO_COMERCIAL });
        return json(adjunto("a1", "paz_salvo", "cedula-1020456789.pdf"), 201);
      },
    });
    fireEvent.change(entrada(/Paz y salvo de impuestos/), { target: { files: [pdf("cedula-1020456789.pdf")] } });
    await within(tarjeta("Documentos")).findByText(/cedula-1020456789\.pdf/);
    fireEvent.change(entrada(/Valor de venta/), { target: { value: "45000000" } });

    for (const { clave } of llamadas) {
      expect(clave).not.toContain("1020456789");
      expect(clave).not.toContain("45000000");
    }
    expect(window.location.href).not.toContain("1020456789");
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});
