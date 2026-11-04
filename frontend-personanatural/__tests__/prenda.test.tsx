import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { Adjunto, DefinicionTipo, Solicitud } from "@/lib/api/cliente";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { cuerpo, DIAGNOSTICO_OK, wizardAbierto, type Manejador } from "./montar-wizard";
import { json } from "./utilidades";

// Tarjeta de prenda (HU9): decisión registrar / levantar / sin prenda y el documento que pide cada una.
const RUTA_DEFINICION = "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1";
const flags = (extra: Partial<DefinicionTipo["flags"]> = {}): DefinicionTipo["flags"] => ({
  entryMode: "PLATE",
  requiereComercial: false,
  requierePrenda: false,
  permiteTransformaciones: false,
  permitePrendaComplementaria: true,
  requiereBiometria: false,
  requiereSolicitudPlaca: false,
  ...extra,
});
const DOCS: DefinicionTipo["documentos"] = [
  { docCode: "prenda_registro", nombre: "Contrato de prenda", obligatorio: true, orden: 1, instruccionCargue: "Con la firma del acreedor", condicion: "hay prenda" },
  { docCode: "prenda_levantamiento", nombre: "Paz y salvo del acreedor", obligatorio: true, orden: 2, instruccionCargue: "", condicion: "se levanta la prenda" },
  { docCode: "paz_salvo", nombre: "Paz y salvo de impuestos", obligatorio: false, orden: 3, instruccionCargue: "", condicion: "" },
];
const definicion = (extra: Partial<DefinicionTipo> = {}): DefinicionTipo =>
  definicionDe([["vehicle_query"], ["actor_form"], ["prenda_decision", "document_checklist"]], { flags: flags(), documentos: DOCS, ...extra });
const adjunto = (id: string, tipo: string): Adjunto => ({ id, tipo, nombreArchivo: `${tipo}.pdf`, mimeType: "application/pdf", tamanoBytes: 1000, sha256: "x", creadoEn: "2026-10-28T10:00:00Z" });
const borrador = (extra: Partial<Solicitud> = {}): Solicitud =>
  solicitudDe({
    datos: { diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" } },
    pendientes: [{ codigo: "documento_requerido", referencia: "paz_salvo", paso: "p3" }],
    version: 4,
    ...extra,
  });

async function abrir(def = definicion(), rutas: Record<string, Manejador> = {}, inicial = borrador()) {
  const estado = { actual: inicial };
  const m = await wizardAbierto({
    [RUTA_DEFINICION]: () => json(def),
    "GET /solicitudes/s-1": () => json(estado.actual),
    "PUT /solicitudes/s-1/comercial": () => json((estado.actual = { ...estado.actual, version: estado.actual.version + 1 })),
    "PUT /solicitudes/s-1/requisitos": () => json((estado.actual = { ...estado.actual, version: estado.actual.version + 1 })),
    ...rutas,
  });
  await screen.findByRole("region", { name: "Prenda / limitación a la propiedad" });
  return { ...m, estado };
}
const tarjeta = () => screen.getByRole("region", { name: "Prenda / limitación a la propiedad" });
const siguiente = () => screen.getByRole("button", { name: "Siguiente" });
const pdf = new File(["%PDF-1.4 contenido"], "soporte.pdf", { type: "application/pdf" });

const GRAVAMEN = "GET /solicitudes/s-1/vehiculo/gravamen";

describe("gravamen RUNT en la prenda", () => {
  it("precarga acreedor y NIT del RUNT, editables, y los guarda", async () => {
    const { llamadas } = await abrir(definicion(), { [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860.002.964", aviso: null }) });
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    await waitFor(() => expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("Banco Uno"));
    expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor *")).toHaveValue("860002964");
    expect(llamadas.filter((l) => l.clave === GRAVAMEN)).toHaveLength(1);

    fireEvent.change(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *"), { target: { value: "Otro Banco" } });
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("Otro Banco");
    fireEvent.change(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *"), { target: { value: "" } });
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("");
  });

  it("sin gravamen en el RUNT muestra el aviso y deja los campos vacíos", async () => {
    await abrir(definicion(), { [GRAVAMEN]: () => json({ tienePrenda: false, acreedor: null, nitAcreedor: null, aviso: "sin_gravamen" }) });
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    fireEvent.click(within(tarjeta()).getByLabelText("Levantar la prenda inscrita"));
    expect(await within(tarjeta()).findByText(/RUNT sin gravamen registrado/)).toBeInTheDocument();
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("");
  });

  it("con prenda pero acreedor aún no visible (identidad sin validar) lo indica", async () => {
    await abrir(definicion(), { [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: null, nitAcreedor: null, aviso: null }) });
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    expect(await within(tarjeta()).findByText(/se muestra cuando se valida la identidad/)).toBeInTheDocument();
  });

  it("no consulta el gravamen si no hay prenda, y si falla no estorba", async () => {
    const { llamadas } = await abrir(definicion(), { [GRAVAMEN]: () => json({ code: "vehiculo_no_definido" }, 409) });
    expect(llamadas.some((l) => l.clave === GRAVAMEN)).toBe(false);
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    await waitFor(() => expect(llamadas.some((l) => l.clave === GRAVAMEN)).toBe(true));
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("");
  });

  it("en un tipo de acción única el portal no ofrece elegir (la fija el servidor)", async () => {
    await abrir(definicion({ flags: flags({ requierePrenda: true, permitePrendaComplementaria: false, accionPrenda: "levantar" }) }));
    expect(within(tarjeta()).queryByText("¿Qué se hará con la prenda?")).toBeNull();
    expect(within(tarjeta()).getByText("Paz y salvo del acreedor *")).toBeInTheDocument();
    expect(within(tarjeta()).queryByText(/Contrato de prenda/)).toBeNull();
  });
});

describe("tarjeta de prenda (HU9)", () => {
  it("«No» no pide acción, acreedor ni documento; «Sí» ofrece registrar o levantar con registrar por defecto", async () => {
    await abrir();
    expect(within(tarjeta()).queryByText("¿Qué se hará con la prenda?")).toBeNull();
    expect(within(tarjeta()).queryByLabelText("Acreedor (beneficiario) *")).toBeNull();

    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    expect(within(tarjeta()).getByLabelText("Registrar una prenda nueva")).toBeChecked();
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toBeInTheDocument();
    expect(within(tarjeta()).getByText("Contrato de prenda *")).toBeInTheDocument();
    expect(within(tarjeta()).queryByText(/Paz y salvo del acreedor/)).toBeNull();
  });

  it("levantar cambia el documento, conserva acreedor y NIT (el servidor los pide) y lo explica", async () => {
    await abrir();
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    fireEvent.click(within(tarjeta()).getByLabelText("Levantar la prenda inscrita"));

    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toBeInTheDocument();
    expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor *")).toBeInTheDocument();
    expect(within(tarjeta()).getByRole("note")).toHaveTextContent(/acreedor de la prenda que se levantará/i);
    expect(within(tarjeta()).getByText("Paz y salvo del acreedor *")).toBeInTheDocument();
    expect(within(tarjeta()).queryByText(/Contrato de prenda/)).toBeNull();
  });

  it("los documentos de la prenda no se repiten en «Documentos»", async () => {
    await abrir();
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    const docs = screen.getByRole("region", { name: "Documentos" });
    expect(within(docs).queryByText(/Contrato de prenda|Paz y salvo del acreedor/)).toBeNull();
    expect(within(docs).getByText("Paz y salvo de impuestos")).toBeInTheDocument();
  });

  it("levantar exige acreedor, NIT y el documento; con todo guarda la decisión", async () => {
    const { llamadas, estado } = await abrir(definicion(), { "POST /solicitudes/s-1/adjuntos": () => json(adjunto("a1", "prenda_levantamiento"), 201) });
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    fireEvent.click(within(tarjeta()).getByLabelText("Levantar la prenda inscrita"));
    expect(siguiente()).toBeDisabled();
    fireEvent.change(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *"), { target: { value: "Banco Uno" } });
    fireEvent.change(within(tarjeta()).getByLabelText("NIT / Documento del acreedor *"), { target: { value: "860002964" } });
    expect(siguiente()).toBeDisabled();

    estado.actual = { ...estado.actual, adjuntos: [adjunto("a1", "prenda_levantamiento")] };
    fireEvent.change(within(tarjeta()).getByLabelText(/Paz y salvo del acreedor/), { target: { files: [pdf] } });
    await waitFor(() => expect(siguiente()).toBeEnabled());
    expect((llamadas.find((l) => l.clave === "POST /solicitudes/s-1/adjuntos")?.init.body as FormData).get("tipo")).toBe("prenda_levantamiento");

    fireEvent.click(siguiente());
    await waitFor(() => expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/comercial")).toBe(true));
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/comercial").prenda).toEqual({ tienePrenda: true, decision: "levantar", acreedor: "Banco Uno", nitAcreedor: "860002964" });
  });

  it("un borrador con la decisión de levantar la reabre marcada", async () => {
    await abrir(definicion(), {}, borrador({ datos: { diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" }, prenda: { tiene: true, acreedor: null, nitAcreedor: null, decision: "levantar" } } }));
    expect(within(tarjeta()).getByLabelText("Levantar la prenda inscrita")).toBeChecked();
  });

  it("si el tipo no trae el documento de la decisión no se ofrece un cargador que el servidor rechazaría", async () => {
    await abrir(definicion({ documentos: [DOCS[2]!] }));
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    expect(tarjeta().querySelectorAll('input[type="file"]')).toHaveLength(0);
  });

  it("con prenda obligatoria por definición no se pregunta si hay prenda: se elige registrar o levantar", async () => {
    await abrir(definicion({ flags: flags({ requierePrenda: true, permitePrendaComplementaria: false }) }));
    expect(within(tarjeta()).queryByText("¿Al vehículo se le asociará una prenda?")).toBeNull();
    expect(within(tarjeta()).getByText("¿Qué se hará con la prenda?")).toBeInTheDocument();
    expect(within(tarjeta()).getByLabelText("Registrar una prenda nueva")).toBeChecked();
    fireEvent.click(within(tarjeta()).getByLabelText("Levantar la prenda inscrita"));
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toBeInTheDocument();
  });
});

describe("tipo que exige prenda sin la sección prenda_decision", () => {
  const sinSeccion = () =>
    definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], {
      flags: flags({ requierePrenda: true, permitePrendaComplementaria: false, accionPrenda: "levantar" }),
      documentos: [{ docCode: "paz_salvo_prenda", nombre: "Paz y Salvo de Prenda", obligatorio: true, orden: 1, instruccionCargue: "", condicion: "" }],
    });

  it("muestra la tarjeta con el gravamen precargado, sin selector de acción, y el paz y salvo una sola vez en Documentos", async () => {
    await abrir(sinSeccion(), { [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860.002.964", aviso: null }) });
    await waitFor(() => expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("Banco Uno"));
    expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor *")).toHaveValue("860002964");
    expect(within(tarjeta()).queryByText("¿Qué se hará con la prenda?")).not.toBeInTheDocument();
    expect(screen.getAllByText(/Paz y Salvo de Prenda/)).toHaveLength(1);
  });

  it("con solo prenda complementaria y sin la sección no hay tarjeta", async () => {
    const def = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], { flags: flags({ permitePrendaComplementaria: true }), documentos: DOCS });
    await wizardAbierto({ [RUTA_DEFINICION]: () => json(def), "GET /solicitudes/s-1": () => json(borrador()) });
    await screen.findByRole("button", { name: "Siguiente" });
    expect(screen.queryByRole("region", { name: "Prenda / limitación a la propiedad" })).not.toBeInTheDocument();
  });
});
