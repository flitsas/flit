import { cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { Adjunto, DefinicionTipo, Solicitud } from "@/lib/api/cliente";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { cuerpo, DIAGNOSTICO_OK, nodo, wizardAbierto, type Manejador } from "./montar-wizard";
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
    fireEvent.click(within(tarjeta()).getByLabelText("Registrar una prenda nueva"));
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
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario)")).toHaveValue("");
  });

  it("con prenda del RUNT muestra el aviso y el resumen (fecha DD/MM/YYYY) y precarga acreedor y NIT", async () => {
    await abrir(definicion(), {
      [GRAVAMEN]: () => json({ tienePrenda: true, tieneGravamenes: true, acreedor: "Banco Uno", nitAcreedor: "860002964", fechaInscripcion: "2024-03-07T00:00:00Z", aviso: null }),
    });
    fireEvent.click(within(tarjeta()).getByLabelText("Registrar una prenda nueva"));
    expect(await within(tarjeta()).findByText(/El RUNT reporta gravamen o prenda para este vehículo/)).toBeInTheDocument();
    expect(within(tarjeta()).getByText("Ver información de prenda / gravamen del RUNT")).toBeInTheDocument();
    expect(within(tarjeta()).getByText("TIENE GRAVÁMENES").nextElementSibling).toHaveTextContent("SI");
    expect(within(tarjeta()).getByText("TIENE PRENDAS").nextElementSibling).toHaveTextContent("SI");
    expect(within(tarjeta()).getByText("ACREEDOR").nextElementSibling).toHaveTextContent("Banco Uno");
    expect(within(tarjeta()).getByText("DOCUMENTO ACREEDOR").nextElementSibling).toHaveTextContent("860002964");
    expect(within(tarjeta()).getByText("FECHA DE INSCRIPCIÓN").nextElementSibling).toHaveTextContent("07/03/2024");
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("Banco Uno");
    expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor *")).toHaveValue("860002964");
    expect(screen.queryByText(/se muestra cuando se valida la identidad/)).toBeNull();
  });

  it("si la persona elige levantar (traspaso), acreedor y NIT del RUNT se pueden corregir", async () => {
    await abrir(definicion(), { [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860002964", aviso: null }) });
    fireEvent.click(within(tarjeta()).getByLabelText("Registrar una prenda nueva"));
    await waitFor(() => expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("Banco Uno"));
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).not.toHaveAttribute("readonly");
    fireEvent.click(within(tarjeta()).getByLabelText("Levantar la prenda inscrita"));
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario)")).not.toHaveAttribute("readonly");
    expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor")).not.toHaveAttribute("readonly");
  });

  it("con acción fija de levantar, acreedor y NIT del RUNT son de solo lectura", async () => {
    await abrir(definicion({ flags: flags({ requierePrenda: true, permitePrendaComplementaria: false, accionPrenda: "levantar" }) }), {
      [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860002964", aviso: null }),
    });
    await waitFor(() => expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("Banco Uno"));
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveAttribute("readonly");
    expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor *")).toHaveAttribute("readonly");
  });

  it("reabrir un borrador guardado sin acreedor ni NIT precarga los del RUNT", async () => {
    await abrir(definicion(), { [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860.002.964", aviso: null }) }, borrador({ datos: { diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" }, prenda: { tiene: true, acreedor: "", nitAcreedor: "", decision: "registrar" } } }));
    await waitFor(() => expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("Banco Uno"));
    expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor *")).toHaveValue("860002964");
  });

  it("con prenda pero sin acreedor no inventa nada: avisa y deja escribirlo a mano", async () => {
    await abrir(definicion(), { [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: null, nitAcreedor: null, aviso: null }) });
    fireEvent.click(within(tarjeta()).getByLabelText("Registrar una prenda nueva"));
    expect(await within(tarjeta()).findByText(/No entregó el detalle del acreedor: escríbelo a mano/)).toBeInTheDocument();
    expect(within(tarjeta()).queryByText("ACREEDOR")).toBeNull();
    expect(within(tarjeta()).queryByText("FECHA DE INSCRIPCIÓN")).toBeNull();
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("");
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).not.toHaveAttribute("readonly");
  });

  it("consulta el gravamen con el paso activo para saber si exige decidir; si falla no inventa gravamen ni estorba", async () => {
    const { llamadas } = await abrir(definicion(), { [GRAVAMEN]: () => json({ code: "vehiculo_no_definido" }, 409) });
    await waitFor(() => expect(llamadas.some((l) => l.clave === GRAVAMEN)).toBe(true));
    expect(within(tarjeta()).getByLabelText("No")).toBeChecked();
    expect(within(tarjeta()).queryByText(/Elige qué se hará con la prenda/)).toBeNull();
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("");
    expect(await within(tarjeta()).findByText(/No pudimos consultar el gravamen en el RUNT/)).toBeInTheDocument();
  });

  describe("el RUNT reporta prenda: la decisión es obligatoria y no se preselecciona (como FLIT 2)", () => {
    const CON_PRENDA = { [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860002964", aviso: null }) };

    it("sin decidir muestra la pregunta nueva con tres opciones sin preseleccionar, no avanza y no guarda una decisión por su cuenta", async () => {
      const { llamadas } = await abrir(definicion(), CON_PRENDA);
      await within(tarjeta()).findByText(/El RUNT reporta gravamen o prenda/);
      expect(within(tarjeta()).getByText("Este vehículo tiene una prenda. ¿Qué se hará con ella?")).toBeInTheDocument();
      expect(within(tarjeta()).queryByText("¿Al vehículo se le asociará una prenda?")).toBeNull();
      expect(within(tarjeta()).getAllByRole("radio")).toHaveLength(3);
      for (const e of ["No hacer nada con la prenda", "Registrar una prenda nueva", "Levantar la prenda inscrita"]) expect(within(tarjeta()).getByLabelText(e)).not.toBeChecked();
      expect(siguiente()).toBeDisabled();
      expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/comercial")).toBe(false);
    });

    it("levantar en traspaso: acreedor y NIT quedan precargados, editables y sin asterisco, y no pide la entidad", async () => {
      await abrir(definicion(), CON_PRENDA);
      await within(tarjeta()).findByText(/El RUNT reporta gravamen o prenda/);
      fireEvent.click(within(tarjeta()).getByLabelText("Levantar la prenda inscrita"));
      expect(within(tarjeta()).queryByLabelText(/Entidad ante la que se levantó/)).toBeNull();
      expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario)")).toHaveValue("Banco Uno");
      expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor")).toHaveValue("860002964");
      expect(within(tarjeta()).getByRole("note")).toHaveTextContent(/opcional/);
    });

    it("levantar sin acreedor ni NIT avanza (con el documento) y envía solo lo que tenga valor", async () => {
      const { llamadas, estado } = await abrir(definicion(), {
        [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: null, nitAcreedor: null, aviso: null }),
        "POST /solicitudes/s-1/adjuntos": () => json(adjunto("a1", "prenda_levantamiento"), 201),
      });
      await within(tarjeta()).findByText(/El RUNT reporta gravamen o prenda/);
      fireEvent.click(within(tarjeta()).getByLabelText("Levantar la prenda inscrita"));
      estado.actual = { ...estado.actual, adjuntos: [adjunto("a1", "prenda_levantamiento")] };
      fireEvent.change(within(tarjeta()).getByLabelText(/Paz y salvo del acreedor/), { target: { files: [pdf] } });
      await waitFor(() => expect(siguiente()).toBeEnabled());
      fireEvent.click(siguiente());
      await waitFor(() => expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/comercial")).toBe(true));
      expect(cuerpo(llamadas, "PUT /solicitudes/s-1/comercial").prenda).toEqual({ tienePrenda: true, decision: "levantar" });
    });

    it("registrar sin acreedor ni NIT no avanza (siguen obligatorios, con asterisco)", async () => {
      await abrir(definicion(), { [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: null, nitAcreedor: null, aviso: null }) });
      await within(tarjeta()).findByText(/El RUNT reporta gravamen o prenda/);
      fireEvent.click(within(tarjeta()).getByLabelText("Registrar una prenda nueva"));
      expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("");
      expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor *")).toHaveValue("");
      expect(siguiente()).toBeDisabled();
    });

    it("«No hacer nada con la prenda» es una decisión explícita y habilita el avance una vez cargados los documentos", async () => {
      const { llamadas } = await abrir(definicion(), CON_PRENDA);
      await within(tarjeta()).findByText(/El RUNT reporta gravamen o prenda/);
      fireEvent.click(within(tarjeta()).getByLabelText("No hacer nada con la prenda"));
      expect(within(tarjeta()).queryByText(/Elige qué se hará con la prenda/)).toBeNull();
      await waitFor(() => expect(siguiente()).toBeEnabled());
      fireEvent.click(siguiente());
      await waitFor(() => expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/comercial")).toBe(true));
      expect(cuerpo(llamadas, "PUT /solicitudes/s-1/comercial").prenda).toMatchObject({ tienePrenda: false, decision: "sin_prenda" });
    });
  });

  describe("solo se consulta con el paso de Requisitos activo", () => {
    // Abre en el paso 2 (Actores) con prenda ya declarada: Requisitos está montado pero inactivo.
    const enActores = () =>
      borrador({ pendientes: [{ codigo: "actor_requerido", referencia: "vendedor", paso: "p2" }], datos: { diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" }, prenda: { tiene: true, acreedor: "", nitAcreedor: "", decision: "registrar" } } });
    it("con otro paso activo no llama al gravamen aunque ya haya prenda declarada", async () => {
      const { llamadas } = await wizardAbierto({
        [RUTA_DEFINICION]: () => json(definicion()),
        "GET /solicitudes/s-1": () => json(enActores()),
        [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860002964", aviso: null }),
      });
      await screen.findByRole("button", { name: /Paso 2 ·/ });
      await new Promise((r) => setTimeout(r, 100));
      expect(llamadas.some((l) => l.clave === GRAVAMEN)).toBe(false);
    });

    it("activo consulta y precarga; tras un error, al reactivar el paso vuelve a consultar y el aviso desaparece", async () => {
      let intentos = 0;
      const conPrenda = borrador({ datos: { diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" }, prenda: { tiene: true, acreedor: "", nitAcreedor: "", decision: "registrar" } } });
      const { llamadas } = await abrir(
        definicion(),
        { [GRAVAMEN]: () => (++intentos === 1 ? json({ code: "vehiculo_no_definido" }, 409) : json({ tienePrenda: true, acreedor: "Banco Uno", nitAcreedor: "860002964", aviso: null })) },
        conPrenda,
      );
      expect(await within(tarjeta()).findByText(/No pudimos consultar el gravamen en el RUNT/)).toBeInTheDocument();
      expect(llamadas.filter((l) => l.clave === GRAVAMEN)).toHaveLength(1);
      fireEvent.click(nodo(2));
      fireEvent.click(await screen.findByRole("button", { name: /Paso 3 ·/ }));
      await waitFor(() => expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("Banco Uno"));
      expect(llamadas.filter((l) => l.clave === GRAVAMEN)).toHaveLength(2);
      expect(within(tarjeta()).queryByText(/No pudimos consultar el gravamen en el RUNT/)).toBeNull();
    });
  });

  it("si el RUNT reporta prenda y el acreedor llega vacío u omitido, lo avisa igual", async () => {
    await abrir(definicion(), { [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: "", aviso: null }) });
    fireEvent.click(within(tarjeta()).getByLabelText("Registrar una prenda nueva"));
    expect(await within(tarjeta()).findByText(/No entregó el detalle del acreedor/)).toBeInTheDocument();
  });

  it("acción fija de levantar sin acreedor sigue sin avanzar (obligatorio, con asterisco)", async () => {
    await abrir(definicion({ flags: flags({ requierePrenda: true, permitePrendaComplementaria: false, accionPrenda: "levantar" }) }), {
      [GRAVAMEN]: () => json({ tienePrenda: true, acreedor: null, nitAcreedor: null, aviso: null }),
    });
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *")).toHaveValue("");
    expect(siguiente()).toBeDisabled();
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

    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario)")).toBeInTheDocument();
    expect(within(tarjeta()).getByLabelText("NIT / Documento del acreedor")).toBeInTheDocument();
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

  it("levantar exige el documento (acreedor y NIT son opcionales); con todo guarda la decisión", async () => {
    const { llamadas, estado } = await abrir(definicion(), { "POST /solicitudes/s-1/adjuntos": () => json(adjunto("a1", "prenda_levantamiento"), 201) });
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    fireEvent.click(within(tarjeta()).getByLabelText("Levantar la prenda inscrita"));
    expect(siguiente()).toBeDisabled();
    fireEvent.change(within(tarjeta()).getByLabelText("Acreedor (beneficiario)"), { target: { value: "Banco Uno" } });
    fireEvent.change(within(tarjeta()).getByLabelText("NIT / Documento del acreedor"), { target: { value: "860002964" } });
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
    expect(within(tarjeta()).getByLabelText("Acreedor (beneficiario)")).toBeInTheDocument();
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

  it("con acción fija levantar pide la entidad ante la que se levantó, la guarda y la precarga", async () => {
    const ENTIDAD = "Entidad ante la que se levantó *";
    const { llamadas, estado } = await abrir(definicion({ flags: flags({ requierePrenda: true, permitePrendaComplementaria: false, accionPrenda: "levantar" }) }), {
      "POST /solicitudes/s-1/adjuntos": () => json(adjunto("a1", "prenda_levantamiento"), 201),
    });
    const campo = within(tarjeta()).getByLabelText(ENTIDAD);
    expect(campo).toHaveAttribute("maxlength", "200");
    expect(campo).toHaveAttribute("placeholder", "Ej. Notaría 15 de Medellín");
    expect(siguiente()).toBeDisabled();
    fireEvent.change(within(tarjeta()).getByLabelText("Acreedor (beneficiario) *"), { target: { value: "Banco Uno" } });
    fireEvent.change(within(tarjeta()).getByLabelText("NIT / Documento del acreedor *"), { target: { value: "860002964" } });
    fireEvent.blur(campo);
    expect(await within(tarjeta()).findByText("Campo obligatorio")).toBeInTheDocument();
    fireEvent.change(campo, { target: { value: "Notaría 15 de Medellín" } });
    estado.actual = { ...estado.actual, adjuntos: [adjunto("a1", "prenda_levantamiento")] };
    fireEvent.change(within(tarjeta()).getByLabelText(/Paz y salvo del acreedor/), { target: { files: [pdf] } });
    await waitFor(() => expect(siguiente()).toBeEnabled());
    fireEvent.click(siguiente());
    await waitFor(() => expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/comercial")).toBe(true));
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/comercial").prenda).toMatchObject({ entidadLevantamiento: "Notaría 15 de Medellín" });
  });

  it("precarga la entidad guardada al reanudar", async () => {
    await abrir(sinSeccion(), {}, borrador({ datos: { diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" }, prenda: { tiene: true, acreedor: "B", nitAcreedor: "860002964", entidadLevantamiento: "Notaría 3", decision: "levantar" } } }));
    expect(within(tarjeta()).getByLabelText("Entidad ante la que se levantó *")).toHaveValue("Notaría 3");
  });

  it("registrar y el selector de acción (aunque se elija levantar) no muestran la entidad", async () => {
    await abrir(definicion({ flags: flags({ requierePrenda: true, permitePrendaComplementaria: false, accionPrenda: "registrar" }) }));
    expect(within(tarjeta()).queryByLabelText(/Entidad ante la que se levantó/)).toBeNull();
    cleanup();
    await abrir();
    fireEvent.click(within(tarjeta()).getByLabelText("Sí"));
    fireEvent.click(within(tarjeta()).getByLabelText("Levantar la prenda inscrita"));
    expect(within(tarjeta()).queryByLabelText(/Entidad ante la que se levantó/)).toBeNull();
  });

  it("con solo prenda complementaria y sin la sección la tarjeta sale y pregunta si hay prenda (traspaso)", async () => {
    const def = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], { flags: flags({ permitePrendaComplementaria: true }), documentos: DOCS });
    await wizardAbierto({ [RUTA_DEFINICION]: () => json(def), "GET /solicitudes/s-1": () => json(borrador()) });
    await screen.findByRole("button", { name: "Siguiente" });
    expect(screen.getByRole("region", { name: "Prenda / limitación a la propiedad" })).toBeInTheDocument();
    expect(screen.getByText("¿Al vehículo se le asociará una prenda?")).toBeInTheDocument();
  });
});
