import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { RequisitosPunto } from "@/components/punto/RequisitosPunto";
import { crearCliente, type Adjunto, type CompletitudRequisitosPunto, type DefinicionTipo, type MatrizRequisitosPunto, type Solicitud } from "@/lib/api/cliente";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { json, renderConQuery } from "./utilidades";

type Manejador = (init: RequestInit) => Response | Promise<Response>;

const RUTA_MATRIZ = "GET /punto/requisitos?tipoCodigo=TRASPASO";
const RUTA_SUGERIDO = "GET /punto/requisitos/valor-sugerido?placa=ABC123";
const RUTA_DEFINICION = "GET /tipos-tramite/TRASPASO/definicion?organismoId=o-1";
const RUTA_SOLICITUD = "GET /solicitudes/s-1";

const item = (docCode: string, nombre: string, clase: string, orden: number, condicion = "") => ({ docCode, nombre, clase, orden, instruccionCargue: `Instrucción de ${nombre}`, condicion });

// El servidor repite la lista por actor: en pantalla debe verse una sola tarjeta con cada documento una vez.
const DOCS_PARTE = [
  item("cedula", "Cédula del vendedor", "obligatorio", 1),
  item("compraventa", "Contrato de compraventa", "obligatorio", 2),
  item("levantamiento_prenda", "Levantamiento de prenda", "obligatorio", 3, "Si se debe levantar prenda"),
  item("improntas", "Improntas", "opcional", 4),
  item("nota_informativa", "Nota informativa", "informativo", 5),
];
const MATRIZ: MatrizRequisitosPunto = {
  tipoCodigo: "TRASPASO",
  organismoId: "o-1",
  huella: "h",
  version: 1,
  grupos: [
    { parte: "vendedor", etiqueta: "Vendedor", ordinal: 1, items: DOCS_PARTE },
    { parte: "comprador", etiqueta: "Comprador", ordinal: 1, items: DOCS_PARTE },
  ],
};

const DEFINICION: DefinicionTipo = definicionDe([["vehicle_query"], ["actor_form"], ["commercial", "prenda_decision", "document_checklist"]], {
  code: "TRASPASO",
  flags: { entryMode: "PLATE", requiereComercial: true, requierePrenda: false, permiteTransformaciones: true, permitePrendaComplementaria: true, requiereBiometria: false, requiereSolicitudPlaca: false },
});

const adjunto = (id: string, tipo: string, nombre: string, creadoEn = "2026-10-28T10:00:00Z"): Adjunto => ({ id, tipo, nombreArchivo: nombre, mimeType: "application/pdf", tamanoBytes: 1000, sha256: "x", creadoEn });
const pdf = (nombre = "cedula.pdf") => new File(["%PDF-1.4 contenido"], nombre, { type: "application/pdf" });
const GATE_NO: CompletitudRequisitosPunto = {
  puedeContinuarPago: false,
  requiereValorVenta: true,
  pendientes: [
    { codigo: "compraventa", nombre: "Contrato de compraventa" },
    { codigo: "valor_venta", nombre: "Valor de venta" },
  ],
};

function montar(rutas: Record<string, Manejador> = {}, { inicial = solicitudDe({ version: 4 }), sugerido = { valorSugerido: 38500000, fuente: "fasecolda" } as unknown, onContinuarPago = vi.fn() }: { inicial?: Solicitud; sugerido?: unknown; onContinuarPago?: () => void } = {}) {
  const estado = { actual: inicial };
  const todas: Record<string, Manejador> = {
    [RUTA_MATRIZ]: () => json(MATRIZ),
    [RUTA_SOLICITUD]: () => json(estado.actual),
    [RUTA_DEFINICION]: () => json(DEFINICION),
    [RUTA_SUGERIDO]: () => json(sugerido),
    ...rutas,
  };
  const llamadas: { clave: string; init: RequestInit }[] = [];
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push({ clave, init });
    return (todas[clave] ?? (() => json({ code: "no_simulado", message: clave }, 404)))(init);
  }) as unknown as typeof fetch;
  const onAviso = vi.fn();
  renderConQuery(<RequisitosPunto cliente={crearCliente({ fetchImpl })} solicitudId="s-1" tipoCodigo="TRASPASO" placa="ABC123" onAtras={() => undefined} onContinuarPago={onContinuarPago} onAviso={onAviso} />);
  return { estado, llamadas, onContinuarPago, onAviso };
}

const cuerpo = (llamadas: { clave: string; init: RequestInit }[], clave: string) => JSON.parse(String([...llamadas].reverse().find((l) => l.clave === clave)?.init.body));
const tarjeta = (nombre: string) => screen.getByRole("region", { name: nombre });
const esperar = () => screen.findByRole("heading", { level: 3, name: "Documentos" });
const entrada = (nombre: RegExp | string) => screen.getByLabelText(nombre) as HTMLInputElement;
/** Responde una escritura con el borrador y la versión siguiente. */
const escritura = (estado: { actual: Solicitud }, cambios: (s: Solicitud) => Partial<Solicitud> = () => ({})): Manejador => () => {
  estado.actual = { ...estado.actual, ...cambios(estado.actual), version: estado.actual.version + 1 };
  return json(estado.actual);
};

describe("Requisitos del punto · mismas tarjetas del portal", () => {
  it("muestra una sola tarjeta Documentos, sin duplicar por actor ni listar informativos, dentro de las tarjetas del portal", async () => {
    montar();
    await esperar();

    expect(screen.getAllByRole("heading", { level: 3, name: "Documentos" })).toHaveLength(1);
    const docs = tarjeta("Documentos");
    expect(within(docs).getAllByText(/^(Cédula del vendedor \*|Contrato de compraventa \*|Improntas)$/).map((n) => n.textContent)).toEqual(["Cédula del vendedor *", "Contrato de compraventa *", "Improntas"]);
    expect(docs.querySelectorAll('input[type="file"]')).toHaveLength(3);
    expect(screen.queryByText(/Nota informativa/)).toBeNull();
    expect(screen.queryByText("Vendedor")).toBeNull();
    expect(screen.getByRole("heading", { level: 3, name: "Datos comerciales" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 3, name: "Prenda / limitación a la propiedad" })).toBeInTheDocument();
    // Nada de colores sueltos ni de cotización en el punto.
    expect(document.body.innerHTML).not.toMatch(/#[0-9a-fA-F]{6}/);
    expect(screen.queryByText(/Valor total liquidado/)).toBeNull();
    expect(screen.getByRole("button", { name: "Atrás" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Continuar" })).toBeInTheDocument();
  }, 20000);

  it("muestra las tarjetas de transformaciones con su opción y la de prenda pregunta si hay prenda (traspaso)", async () => {
    montar();
    await esperar();

    expect(screen.getByText("Cambio de color")).toBeInTheDocument();
    expect(screen.getByText("Conversión de combustible")).toBeInTheDocument();
    expect(screen.getByText("Cambio de carrocería")).toBeInTheDocument();
    expect(screen.getByText("¿Al vehículo se le asociará una prenda?")).toBeInTheDocument();
  });

  it("mientras carga muestra el estado de carga y si falla ofrece reintentar", async () => {
    let falla = true;
    montar({ [RUTA_MATRIZ]: () => (falla ? json({ code: "servicio_no_disponible" }, 503) : json(MATRIZ)) });

    expect(screen.getByText("Cargando requisitos…")).toBeInTheDocument();
    expect(await screen.findByRole("alert")).toHaveTextContent("El servicio no está disponible por ahora");
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await esperar()).toBeInTheDocument();
  });
});

describe("Requisitos del punto · carga de documentos", () => {
  it("un archivo válido se sube con su tipo, muestra «Subiendo» y queda cargado", async () => {
    let terminar: (r: Response) => void = () => undefined;
    const { llamadas, estado } = montar({ "POST /solicitudes/s-1/adjuntos": () => new Promise<Response>((r) => (terminar = r)) });
    await esperar();

    fireEvent.change(entrada(/Cédula del vendedor/), { target: { files: [pdf("cedula.pdf")] } });

    expect(await within(tarjeta("Documentos")).findByRole("status")).toHaveTextContent("Subiendo cedula.pdf...");
    const envio = llamadas.find((l) => l.clave === "POST /solicitudes/s-1/adjuntos");
    expect((envio?.init.body as FormData).get("tipo")).toBe("cedula");
    estado.actual = solicitudDe({ version: 5, adjuntos: [adjunto("a1", "cedula", "cedula.pdf")] });
    terminar(json(adjunto("a1", "cedula", "cedula.pdf"), 201));

    expect(await within(tarjeta("Documentos")).findByText(/cedula\.pdf · Da clic para reemplazar el archivo/)).toBeInTheDocument();
    expect(within(tarjeta("Documentos")).getByRole("button", { name: "Quitar archivo" })).toBeInTheDocument();
  });

  it("un formato inválido muestra el motivo y no queda cargado ni se sube", async () => {
    const { llamadas } = montar();
    await esperar();

    fireEvent.change(entrada(/Cédula del vendedor/), { target: { files: [new File(["x"], "virus.exe")] } });

    expect(await within(tarjeta("Documentos")).findByText("Solo se aceptan archivos PDF, JPG, PNG o WEBP.")).toBeInTheDocument();
    expect(entrada(/Cédula del vendedor/)).toHaveAttribute("aria-invalid", "true");
    expect(within(tarjeta("Documentos")).queryByText(/Da clic para reemplazar/)).toBeNull();
    expect(llamadas.some((l) => l.clave === "POST /solicitudes/s-1/adjuntos")).toBe(false);
  });

  it("un archivo de más de 20 MB muestra el motivo y no se sube", async () => {
    const { llamadas } = montar();
    await esperar();
    const pesado = pdf("grande.pdf");
    Object.defineProperty(pesado, "size", { value: 20 * 1024 * 1024 + 1 });

    fireEvent.change(entrada(/Cédula del vendedor/), { target: { files: [pesado] } });

    expect(await within(tarjeta("Documentos")).findByText("El archivo supera los 20 MB permitidos.")).toBeInTheDocument();
    expect(llamadas.some((l) => l.clave === "POST /solicitudes/s-1/adjuntos")).toBe(false);
  });

  it("reemplazar sube el nuevo y retira el anterior; quitar elimina el archivo", async () => {
    const { llamadas, estado } = montar(
      {
        "POST /solicitudes/s-1/adjuntos": () => {
          estado.actual = solicitudDe({ version: 5, adjuntos: [adjunto("a1", "cedula", "viejo.pdf"), adjunto("a2", "cedula", "nuevo.pdf", "2026-10-28T11:00:00Z")] });
          return json(adjunto("a2", "cedula", "nuevo.pdf", "2026-10-28T11:00:00Z"), 201);
        },
        "DELETE /solicitudes/s-1/adjuntos/a1": () => {
          estado.actual = solicitudDe({ version: 6, adjuntos: [adjunto("a2", "cedula", "nuevo.pdf", "2026-10-28T11:00:00Z")] });
          return new Response(null, { status: 204 });
        },
        "DELETE /solicitudes/s-1/adjuntos/a2": () => {
          estado.actual = solicitudDe({ version: 7 });
          return new Response(null, { status: 204 });
        },
      },
      { inicial: solicitudDe({ version: 4, adjuntos: [adjunto("a1", "cedula", "viejo.pdf")] }) },
    );
    await esperar();
    expect(within(tarjeta("Documentos")).getByText(/viejo\.pdf · Da clic para reemplazar/)).toBeInTheDocument();

    fireEvent.change(entrada(/Cédula del vendedor/), { target: { files: [pdf("nuevo.pdf")] } });

    expect(await within(tarjeta("Documentos")).findByText(/nuevo\.pdf · Da clic para reemplazar/)).toBeInTheDocument();
    const claves = llamadas.map((l) => l.clave);
    expect(claves.indexOf("POST /solicitudes/s-1/adjuntos")).toBeLessThan(claves.indexOf("DELETE /solicitudes/s-1/adjuntos/a1"));

    fireEvent.click(within(tarjeta("Documentos")).getByRole("button", { name: "Quitar archivo" }));
    await waitFor(() => expect(within(tarjeta("Documentos")).queryByText(/nuevo\.pdf/)).toBeNull());
  });

  it("si el servidor falla ofrece «Reintentar subida» y la repite con el mismo archivo", async () => {
    let falla = true;
    const { llamadas, estado } = montar({
      "POST /solicitudes/s-1/adjuntos": () => {
        if (falla) return json({ code: "almacenamiento_no_disponible" }, 502);
        estado.actual = solicitudDe({ version: 5, adjuntos: [adjunto("a1", "cedula", "cedula.pdf")] });
        return json(adjunto("a1", "cedula", "cedula.pdf"), 201);
      },
    });
    await esperar();
    fireEvent.change(entrada(/Cédula del vendedor/), { target: { files: [pdf("cedula.pdf")] } });

    expect(await within(tarjeta("Documentos")).findByText("No pudimos guardar el archivo. Intenta de nuevo.")).toBeInTheDocument();
    falla = false;
    fireEvent.click(within(tarjeta("Documentos")).getByRole("button", { name: "Reintentar subida" }));

    expect(await within(tarjeta("Documentos")).findByText(/cedula\.pdf · Da clic para reemplazar/)).toBeInTheDocument();
    expect(llamadas.filter((l) => l.clave === "POST /solicitudes/s-1/adjuntos")).toHaveLength(2);
  });
});

describe("Requisitos del punto · datos comerciales y documentos condicionales", () => {
  it("precarga el avalúo de Fasecolda en el avalúo sugerido y en el valor de venta, y el valor sigue editable", async () => {
    montar();
    await esperar();
    const comercial = tarjeta("Datos comerciales");

    expect(within(comercial).getByLabelText("Avalúo sugerido (Fasecolda)")).toHaveValue("38.500.000,00");
    expect(within(comercial).getByLabelText("Avalúo sugerido (Fasecolda)")).toBeDisabled();
    expect(within(comercial).getByText("Fasecolda: Disponible")).toBeInTheDocument();
    const valor = within(comercial).getByLabelText(/Valor de venta/);
    expect(valor).toHaveValue("38.500.000,00");
    fireEvent.change(valor, { target: { value: "40000000" } });
    expect(valor).toHaveValue("40.000.000");
  });

  it("si el borrador ya tiene valor no lo pisa con el avalúo", async () => {
    montar({}, { inicial: solicitudDe({ version: 4, datos: { comercial: { valor: 30000000 } } }) });
    await esperar();

    expect(within(tarjeta("Datos comerciales")).getByLabelText(/Valor de venta/)).toHaveValue("30.000.000,00");
  });

  it.each([
    ["sin avalúo", { valorSugerido: null, fuente: "sin_datos" }, undefined],
    ["Fasecolda caído", undefined, () => json({ code: "proveedor_no_disponible" }, 503)],
  ])("%s: el valor de venta queda vacío para escribirlo y exige mayor a cero", async (_nombre, sugerido, ruta) => {
    montar(ruta ? { [RUTA_SUGERIDO]: ruta } : {}, { sugerido });
    await esperar();
    const comercial = tarjeta("Datos comerciales");

    expect(within(comercial).getByLabelText("Avalúo sugerido (Fasecolda)")).toHaveValue("");
    expect(within(comercial).getByText("Fasecolda: No disponible")).toBeInTheDocument();
    const valor = within(comercial).getByLabelText(/Valor de venta/);
    expect(valor).toHaveValue("");
    fireEvent.blur(valor);
    expect(within(comercial).getByText("Ingresa el valor de venta")).toBeInTheDocument();
    fireEvent.change(valor, { target: { value: "25000000" } });
    expect(valor).toHaveValue("25.000.000");
  });

  it("el contrato de compraventa es obligatorio (*) y el levantamiento de prenda solo aparece, y se exige, con prenda", async () => {
    montar();
    await esperar();
    const docs = tarjeta("Documentos");

    expect(within(docs).getByText("Contrato de compraventa *")).toBeInTheDocument();
    expect(within(docs).queryByText(/Levantamiento de prenda/)).toBeNull();

    fireEvent.click(within(tarjeta("Prenda / limitación a la propiedad")).getByRole("radio", { name: "Sí" }));

    expect(within(docs).getByText("Levantamiento de prenda *")).toBeInTheDocument();
    expect(within(docs).getByText(/Aplica si: Si se debe levantar prenda/)).toBeInTheDocument();
  });
});

describe("Requisitos del punto · Continuar a Pago", () => {
  it("con el gate en falso no avanza, muestra los pendientes por nombre (no por código) y marca lo que falta bajo cada cargador", async () => {
    const { llamadas, onContinuarPago, estado } = montar({
      "PUT /solicitudes/s-1/comercial": () => escritura(estado, () => ({ datos: { comercial: { valor: 38500000 } } }))({}),
      "PUT /solicitudes/s-1/requisitos": () => escritura(estado)({}),
      "POST /punto/requisitos/completitud": () => json(GATE_NO),
    });
    await esperar();

    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    const pendientes = await waitFor(() => {
      const el = document.getElementById("punto-requisitos-pendientes");
      expect(el).not.toBeNull();
      return el as HTMLElement;
    });
    expect(within(pendientes).getByText("Contrato de compraventa")).toBeInTheDocument();
    expect(within(pendientes).getByText("Valor de venta")).toBeInTheDocument();
    expect(pendientes).not.toHaveTextContent("valor_venta");
    expect(pendientes).toHaveFocus();
    expect(within(tarjeta("Documentos")).getAllByText("Adjunta el archivo requerido").length).toBeGreaterThan(0);
    expect(screen.getByText("Completa los datos y documentos obligatorios para continuar.")).toBeInTheDocument();
    expect(onContinuarPago).not.toHaveBeenCalled();
    // El front ya no manda documentos ni valor: solo el tipo.
    expect(cuerpo(llamadas, "POST /punto/requisitos/completitud")).toEqual({ tipoCodigo: "TRASPASO" });
  });

  it("guarda el valor precargado con la versión vigente y, con el gate en verdadero, avanza a Pago", async () => {
    const gate: CompletitudRequisitosPunto = { puedeContinuarPago: true, requiereValorVenta: true, pendientes: [] };
    const { llamadas, onContinuarPago, estado } = montar({
      "PUT /solicitudes/s-1/comercial": () => escritura(estado, () => ({ datos: { comercial: { valor: 40000000 } } }))({}),
      "PUT /solicitudes/s-1/requisitos": () => escritura(estado)({}),
      "POST /punto/requisitos/completitud": () => json(gate),
    });
    await esperar();
    fireEvent.change(within(tarjeta("Datos comerciales")).getByLabelText(/Valor de venta/), { target: { value: "40000000" } });

    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    await waitFor(() => expect(onContinuarPago).toHaveBeenCalledTimes(1));
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/comercial")).toMatchObject({ version: 4, valorComercial: 40000000, prenda: { tienePrenda: false } });
    const claves = llamadas.map((l) => l.clave);
    expect(claves.indexOf("PUT /solicitudes/s-1/comercial")).toBeLessThan(claves.indexOf("POST /punto/requisitos/completitud"));
    expect(document.getElementById("punto-requisitos-pendientes")).toBeNull();
  });

  it("el valor precargado con Fasecolda se guarda aunque no se edite", async () => {
    const { llamadas, onContinuarPago, estado } = montar({
      "PUT /solicitudes/s-1/comercial": () => escritura(estado)({}),
      "PUT /solicitudes/s-1/requisitos": () => escritura(estado)({}),
      "POST /punto/requisitos/completitud": () => json({ puedeContinuarPago: true, requiereValorVenta: true, pendientes: [] }),
    });
    await esperar();

    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    await waitFor(() => expect(onContinuarPago).toHaveBeenCalled());
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/comercial").valorComercial).toBe(38500000);
  });

  it("con prenda en el RUNT no avanza a Pago sin decidir, y con la decisión sí avanza", async () => {
    const { onContinuarPago, onAviso, llamadas, estado } = montar({
      "GET /solicitudes/s-1/vehiculo/gravamen": () => json({ tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860002964", aviso: null }),
      "PUT /solicitudes/s-1/comercial": () => escritura(estado)({}),
      "PUT /solicitudes/s-1/requisitos": () => escritura(estado)({}),
      "POST /punto/requisitos/completitud": () => json({ puedeContinuarPago: true, requiereValorVenta: true, pendientes: [] }),
    });
    await esperar();
    await screen.findByText(/El RUNT reporta gravamen o prenda/);

    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
    await waitFor(() => expect(onAviso).toHaveBeenCalledWith("Falta la decisión de prenda", expect.any(String), "alerta"));
    expect(onContinuarPago).not.toHaveBeenCalled();
    expect(llamadas.some((l) => l.clave === "POST /punto/requisitos/completitud")).toBe(false);
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/comercial").prenda).toBeUndefined();

    fireEvent.click(within(tarjeta("Prenda / limitación a la propiedad")).getByLabelText("No"));
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
    await waitFor(() => expect(onContinuarPago).toHaveBeenCalled());
  });

  it("si el gate falla por red avisa y no avanza", async () => {
    const { onContinuarPago, onAviso, estado } = montar({
      "PUT /solicitudes/s-1/comercial": () => escritura(estado)({}),
      "PUT /solicitudes/s-1/requisitos": () => escritura(estado)({}),
      "POST /punto/requisitos/completitud": () => json({ code: "servicio_no_disponible" }, 503),
    });
    await esperar();

    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    await waitFor(() => expect(onAviso).toHaveBeenCalledWith("No se pudo validar", expect.any(String), "alerta"));
    expect(onContinuarPago).not.toHaveBeenCalled();
  });
});
