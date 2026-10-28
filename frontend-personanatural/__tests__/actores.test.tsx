import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ActorEstado, DefinicionTipo, EstadoIdentidad, ParteIdentidad } from "@/lib/api/cliente";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { cuerpo, DIAGNOSTICO_OK, guardado, nodo, wizardAbierto, type Manejador } from "./montar-wizard";
import { json } from "./utilidades";

const RUTA_DETALLE = "POST /solicitudes/s-1/consultas/persona-detalle";
const RUTA_DEFINICION = "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1";
const CEDULA_SOLICITANTE = "1020456789"; // la del contexto del wizard (CONTEXTO)
const comun = { firma: false, personaNaturalPermitida: true };
const ROLES: DefinicionTipo["actores"] = [
  { rol: "vendedor", etiqueta: "Propietario (vendedor)", min: 1, max: 1, biometria: true, ...comun },
  { rol: "comprador", etiqueta: "Comprador", min: 1, max: 1, biometria: true, ...comun },
  { rol: "copropietario", etiqueta: "Copropietario", min: 0, max: 2, biometria: false, ...comun },
];
const DEFINICION = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], { actores: ROLES });

const parte = (rol: string, estado: string, extra: Partial<ParteIdentidad> = {}): ParteIdentidad => ({ parte: rol, ordinal: 1, estado, puedeReenviar: true, reenviosRestantes: 5, esperaSegundos: 0, ...extra });
const identidad = (partes: ParteIdentidad[]): EstadoIdentidad => ({ solicitudId: "s-1", completa: partes.every((p) => p.estado === "aprobada"), partes });
const sinSolicitar = () => identidad([parte("vendedor", "sin_solicitar"), parte("comprador", "sin_solicitar")]);
const problema = (status: number, code: string, headers: Record<string, string> = {}, extra: Record<string, unknown> = {}) =>
  new Response(JSON.stringify({ type: "about:blank", title: "t", status, code, ...extra }), { status, headers: { "content-type": "application/problem+json", ...headers } });

const guardadoActor = (rol: string, doc: string, nombre: string, estado = "sin_solicitar"): ActorEstado => ({ rol, ordinal: 1, validacionEstado: estado, tipoDocumento: "CC", numeroDocumento: doc, nombre, correo: "ana@correo.co", celular: "3001234567", direccion: null });

/** Abre el wizard en el paso 2 (el borrador trae el diagnóstico hecho y falta un actor). */
async function abrir(rutas: Record<string, Manejador> = {}, solicitud = guardado(DIAGNOSTICO_OK), definicion = DEFINICION) {
  const m = await wizardAbierto({
    [RUTA_DEFINICION]: () => json(definicion),
    "GET /solicitudes/s-1": () => json(solicitud),
    "GET /solicitudes/s-1/identidad": () => json(sinSolicitar()),
    ...rutas,
  });
  await screen.findByRole("region", { name: "Comprador" });
  return m;
}

const tarjeta = (nombre: string) => screen.getByRole("region", { name: nombre });
const continuar = () => screen.getByRole("button", { name: "Continuar" });

function llenarContacto(card: HTMLElement, correo = "ana@correo.co") {
  fireEvent.change(within(card).getByLabelText(/Nombre completo/), { target: { value: "ANA RUIZ" } });
  fireEvent.change(within(card).getByLabelText(/Teléfono/), { target: { value: "3001234567" } });
  fireEvent.change(within(card).getByLabelText(/Correo electrónico/), { target: { value: correo } });
}

/** El vendedor es el solicitante (su cédula ya se identificó al abrir): no hay captcha ni consulta. */
function consultarSolicitante(card: HTMLElement) {
  fireEvent.change(within(card).getByLabelText(/Número de documento/), { target: { value: CEDULA_SOLICITANTE } });
  fireEvent.click(within(card).getByRole("button", { name: "Consultar" }));
}

async function consultarTercero(card: HTMLElement, documento: string) {
  fireEvent.change(within(card).getByLabelText(/Número de documento/), { target: { value: documento } });
  fireEvent.click(within(card).getByRole("button", { name: "Consultar" }));
  await waitFor(() => expect(within(card).getByLabelText(/Teléfono/)).toBeEnabled());
}

const detalle = (extra: Record<string, unknown> = {}) => json({ existe: true, nombres: "ANA MARIA", apellidos: "RUIZ PEREZ", nombreCompleto: "ANA MARIA RUIZ PEREZ", ciudad: null, ...extra });
const consultar = (card: HTMLElement, documento = "52123456") => {
  fireEvent.change(within(card).getByLabelText(/Número de documento/), { target: { value: documento } });
  fireEvent.click(within(card).getByRole("button", { name: "Consultar" }));
};

afterEach(() => {
  vi.useRealTimers();
});

describe("Paso 2 · una tarjeta por rol de la definición (F3 AC1, K5 AC1 y AC2)", () => {
  it("pinta una tarjeta por rol con su etiqueta y los campos del diseño, deshabilitados hasta consultar", async () => {
    await abrir();

    for (const nombre of ["Propietario (vendedor)", "Comprador", "Copropietario"]) expect(tarjeta(nombre)).toBeInTheDocument();
    const comprador = tarjeta("Comprador");
    expect(within(comprador).getByLabelText("Tipo de documento")).toHaveValue("CC");
    expect(within(comprador).getByLabelText(/Número de documento/)).toBeEnabled();
    for (const campo of [/Nombre completo/, /Teléfono/, /Correo electrónico/, /Dirección/]) expect(within(comprador).getByLabelText(campo)).toBeDisabled();
    expect(within(comprador).getByRole("button", { name: "Consultar" })).toBeInTheDocument();
    expect(continuar()).toBeDisabled();
    expect(screen.getByText("Completa los datos de Propietario (vendedor).")).toBeInTheDocument();
  });

  it("un tipo de un solo titular muestra solo esa tarjeta, con el rol que define el tipo", async () => {
    const definicion = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], { actores: [{ rol: "titular", etiqueta: "Titular del vehículo", min: 1, max: 1, biometria: false, ...comun }] });
    await wizardAbierto({ [RUTA_DEFINICION]: () => json(definicion), "GET /solicitudes/s-1": () => json(guardado(DIAGNOSTICO_OK)) });
    await screen.findByRole("region", { name: "Titular del vehículo" });

    expect(screen.queryByRole("region", { name: "Comprador" })).toBeNull();
    expect(screen.queryByRole("region", { name: "Propietario (vendedor)" })).toBeNull();
  });

  it("un rol con máximo mayor que uno agrega hasta el máximo y quita sin bajar del mínimo", async () => {
    await abrir();
    const copro = tarjeta("Copropietario");
    expect(within(copro).getByText("Este rol es opcional: agrega una persona solo si aplica.")).toBeInTheDocument();

    fireEvent.click(within(copro).getByRole("button", { name: "Agregar Copropietario" }));
    fireEvent.click(within(copro).getByRole("button", { name: "Agregar Copropietario" }));
    expect(within(copro).getAllByRole("group")).toHaveLength(2);
    expect(within(copro).getByRole("group", { name: "Copropietario 2" })).toBeInTheDocument();
    expect(within(copro).queryByRole("button", { name: "Agregar Copropietario" })).toBeNull();

    fireEvent.click(within(copro).getByRole("button", { name: "Quitar Copropietario 1" }));
    expect(within(copro).getAllByRole("group")).toHaveLength(1);
    expect(within(copro).getByRole("button", { name: "Agregar Copropietario" })).toBeInTheDocument();
    // Los roles con mínimo 1 y máximo 1 no se agregan ni se quitan.
    expect(within(tarjeta("Comprador")).queryByRole("button", { name: /Agregar|Quitar/ })).toBeNull();
  });
});

describe("validación de formato y consulta del documento (F3 AC1 y AC2, K5 AC5)", () => {
  it("el número de documento y el teléfono solo aceptan dígitos; el correo se valida con formato", async () => {
    await abrir();
    const card = tarjeta("Propietario (vendedor)");
    consultarSolicitante(card);
    await waitFor(() => expect(within(card).getByLabelText(/Teléfono/)).toBeEnabled());

    const tel = within(card).getByLabelText(/Teléfono/);
    fireEvent.change(tel, { target: { value: "30a0-12" } });
    expect(tel).toHaveValue("30012");
    fireEvent.blur(tel);
    expect(within(card).getByText("Ingresa un teléfono válido")).toBeInTheDocument();
    expect(tel).toHaveAttribute("aria-invalid", "true");

    const correo = within(card).getByLabelText(/Correo electrónico/);
    fireEvent.change(correo, { target: { value: "no-es-correo" } });
    fireEvent.blur(correo);
    expect(within(card).getByText("Ingresa un correo electrónico válido")).toBeInTheDocument();
    expect(correo).toHaveAttribute("aria-invalid", "true");
    expect(continuar()).toBeDisabled();
  });

  it("consulta a un tercero con la sesión de la solicitud, sin captcha: rellena el nombre y lo deja de solo lectura", async () => {
    const { llamadas } = await abrir({ [RUTA_DETALLE]: () => detalle() });
    const card = tarjeta("Comprador");
    await consultarTercero(card, "52123456");

    const llamada = llamadas.find((l) => l.clave === RUTA_DETALLE);
    expect(JSON.parse(String(llamada?.init.body))).toEqual({ tipoDocumento: "CC", documento: "52123456", rol: "comprador", consentimiento: true, consentimientoVersion: "v1" });
    expect((llamada?.init.headers as Record<string, string>)["x-captcha-token"]).toBeUndefined();
    expect(within(card).queryByLabelText("Verificación de seguridad")).toBeNull();
    expect(llamadas.some((l) => l.clave === "POST /consultas/persona")).toBe(false);
    expect(within(card).getByLabelText(/Número de documento/)).toBeDisabled();
    const nombre = within(card).getByLabelText(/Nombre completo/);
    expect(nombre).toHaveValue("ANA MARIA RUIZ PEREZ");
    expect(nombre).toHaveAttribute("readonly");
    expect(within(card).getByLabelText(/Teléfono/)).toBeEnabled();
  });

  it("con la identidad pendiente el nombre consultado se muestra enmascarado", async () => {
    await abrir({
      [RUTA_DETALLE]: () => detalle(),
      "GET /solicitudes/s-1/identidad": () => json(identidad([parte("vendedor", "sin_solicitar"), parte("comprador", "pendiente")])),
    });
    const card = tarjeta("Comprador");
    await consultarTercero(card, "52123456");
    await waitFor(() => expect(within(card).getByLabelText(/Nombre completo/)).toHaveValue("AN••• MA••• RU••• PE•••"));
  });

  it("un documento sin coincidencia avisa bajo los campos y deja escribir el nombre a mano", async () => {
    await abrir({ [RUTA_DETALLE]: () => json({ existe: false, nombres: null, apellidos: null, nombreCompleto: null, ciudad: null }) });
    const card = tarjeta("Comprador");
    await consultarTercero(card, "52123456");

    expect(within(card).getByText(/No encontramos los datos de esta persona; escribe su nombre manualmente/)).toBeInTheDocument();
    const nombre = within(card).getByLabelText(/Nombre completo/);
    expect(nombre).toBeEnabled();
    expect(nombre).not.toHaveAttribute("readonly");
    fireEvent.change(nombre, { target: { value: "ANA RUIZ" } });
    expect(nombre).toHaveValue("ANA RUIZ");
    expect(within(card).getByLabelText(/Número de documento/)).toBeDisabled();
  });

  it("si solo vienen nombres y apellidos arma el nombre completo", async () => {
    await abrir({ [RUTA_DETALLE]: () => detalle({ nombreCompleto: null }) });
    const card = tarjeta("Comprador");
    await consultarTercero(card, "52123456");
    expect(within(card).getByLabelText(/Nombre completo/)).toHaveValue("ANA MARIA RUIZ PEREZ");
  });

  it("«Cambiar documento» borra el nombre consultado y vuelve a pedir la consulta", async () => {
    await abrir({ [RUTA_DETALLE]: () => detalle() });
    const card = tarjeta("Comprador");
    await consultarTercero(card, "52123456");
    fireEvent.click(within(card).getByRole("button", { name: "Cambiar documento" }));

    expect(within(card).getByLabelText(/Número de documento/)).toBeEnabled();
    expect(within(card).getByLabelText(/Nombre completo/)).toHaveValue("");
    expect(within(card).getByRole("button", { name: "Consultar" })).toBeInTheDocument();
  });

  it("429 detalle_agotado degrada a captura manual con el aviso, sin bloquear la consulta", async () => {
    await abrir({ [RUTA_DETALLE]: () => problema(429, "detalle_agotado") });
    const card = tarjeta("Comprador");
    await consultarTercero(card, "52123456");

    expect(within(card).getByText("Alcanzaste el máximo de consultas para este trámite; escribe los datos manualmente.")).toBeInTheDocument();
    expect(within(card).getByLabelText(/Nombre completo/)).toBeEnabled();
    expect(within(card).queryByText(/Podrás intentarlo de nuevo/)).toBeNull();
  });

  it("429 cedula_rate_limited con Retry-After muestra la espera y no habilita los campos", async () => {
    await abrir({ [RUTA_DETALLE]: () => problema(429, "cedula_rate_limited", { "retry-after": "3600" }) });
    const card = tarjeta("Comprador");
    consultar(card);

    expect(await within(card).findByText(/máximo de consultas por hora para este documento/)).toBeInTheDocument();
    expect(within(card).getByText(/Podrás intentarlo de nuevo en/)).toBeInTheDocument();
    expect(within(card).getByRole("button", { name: "Consultar" })).toBeDisabled();
    expect(within(card).getByLabelText(/Teléfono/)).toBeDisabled();
  });

  it("un 503 proveedor_no_disponible se explica, muestra la espera y no habilita los campos", async () => {
    await abrir({ [RUTA_DETALLE]: () => problema(503, "proveedor_no_disponible", { "retry-after": "20" }) });
    const card = tarjeta("Comprador");
    consultar(card);

    expect(await within(card).findByText(/El servicio no está disponible en este momento/)).toBeInTheDocument();
    expect(within(card).getByLabelText(/Teléfono/)).toBeDisabled();
    expect(within(card).getByText(/Podrás intentarlo de nuevo en/)).toBeInTheDocument();
  });

  it("un 503 consultas_no_disponibles se explica sin habilitar los campos", async () => {
    await abrir({ [RUTA_DETALLE]: () => problema(503, "consultas_no_disponibles") });
    const card = tarjeta("Comprador");
    consultar(card);

    expect(await within(card).findByText("Las consultas están pausadas por hoy. Intenta de nuevo mañana.")).toBeInTheDocument();
    expect(within(card).getByLabelText(/Teléfono/)).toBeDisabled();
  });

  it("un 422 rol_invalido se explica bajo el documento", async () => {
    await abrir({ [RUTA_DETALLE]: () => problema(422, "rol_invalido") });
    const card = tarjeta("Comprador");
    consultar(card);

    expect(await within(card).findByText("Este trámite no admite a esta persona en ese rol.")).toBeInTheDocument();
    expect(within(card).getByLabelText(/Número de documento/)).toHaveAttribute("aria-invalid", "true");
    expect(within(card).getByLabelText(/Teléfono/)).toBeDisabled();
  });

  it("la persona que abrió el trámite no se vuelve a consultar: no hay llamada de consulta", async () => {
    const { llamadas } = await abrir();
    const card = tarjeta("Propietario (vendedor)");
    consultarSolicitante(card);

    await waitFor(() => expect(within(card).getByLabelText(/Teléfono/)).toBeEnabled());
    expect(llamadas.some((l) => l.clave === RUTA_DETALLE || l.clave === "POST /consultas/persona")).toBe(false);
    expect(within(card).queryByLabelText("Verificación de seguridad")).toBeNull();
  });

  it("un NIT es persona jurídica: aviso con aria-invalid, sin consulta y sin poder continuar", async () => {
    const { llamadas } = await abrir();
    const card = tarjeta("Comprador");
    fireEvent.change(within(card).getByLabelText("Tipo de documento"), { target: { value: "NIT" } });
    fireEvent.change(within(card).getByLabelText(/Número de documento/), { target: { value: "900123456" } });
    fireEvent.click(within(card).getByRole("button", { name: "Consultar" }));

    expect(within(card).getByText("Este trámite no admite personas jurídicas en este rol")).toBeInTheDocument();
    expect(within(card).getByLabelText(/Número de documento/)).toHaveAttribute("aria-invalid", "true");
    expect(within(card).queryByLabelText("Verificación de seguridad")).toBeNull();
    expect(llamadas.some((l) => l.clave === RUTA_DETALLE)).toBe(false);
    expect(continuar()).toBeDisabled();
  });

  it("un rol que no admite persona natural avisa en su campo y bloquea el paso", async () => {
    const definicion = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], {
      actores: [{ rol: "empresa", etiqueta: "Empresa propietaria", min: 1, max: 1, biometria: false, firma: false, personaNaturalPermitida: false }],
    });
    await wizardAbierto({ [RUTA_DEFINICION]: () => json(definicion), "GET /solicitudes/s-1": () => json(guardado(DIAGNOSTICO_OK)) });
    const card = await screen.findByRole("region", { name: "Empresa propietaria" });

    expect(within(card).getByText("Este trámite no admite personas jurídicas en este rol")).toBeInTheDocument();
    expect(screen.getByText(/no se puede continuar con este trámite en línea/)).toBeInTheDocument();
    expect(continuar()).toBeDisabled();
  });
});

describe("dirección con ayuda de entrega (F3 AC4)", () => {
  it("el campo de dirección es editable tras consultar y, sin marca de entrega en la definición, no muestra el ícono de ayuda", async () => {
    await abrir();
    const card = tarjeta("Propietario (vendedor)");
    consultarSolicitante(card);
    await waitFor(() => expect(within(card).getByLabelText("Dirección")).toBeEnabled());

    expect(within(card).queryByRole("button", { name: "Información sobre la dirección de entrega" })).toBeNull();
  });
});

describe("validación de identidad por actor (F3 AC5 y AC6, K5 AC3)", () => {
  /** Estado de las partes que el sondeo va devolviendo; las pruebas lo cambian entre una lectura y otra. */
  function conIdentidad(inicial: ParteIdentidad[]) {
    const estado = { partes: inicial };
    return { estado, ruta: (() => json(identidad(estado.partes))) as Manejador };
  }

  async function vendedorListo(rutas: Record<string, Manejador>, ident = conIdentidad([parte("vendedor", "sin_solicitar"), parte("comprador", "sin_solicitar")])) {
    const m = await abrir({ "GET /solicitudes/s-1/identidad": ident.ruta, ...rutas });
    const card = tarjeta("Propietario (vendedor)");
    consultarSolicitante(card);
    await waitFor(() => expect(within(card).getByLabelText(/Teléfono/)).toBeEnabled());
    llenarContacto(card);
    return { ...m, card, ident };
  }

  it("guarda a la persona con la versión y solicita su validación: queda pendiente y avisa el correo enmascarado", async () => {
    const ident = conIdentidad([parte("vendedor", "sin_solicitar"), parte("comprador", "sin_solicitar")]);
    const { llamadas, card } = await vendedorListo(
      {
        "PATCH /solicitudes/s-1/actores": () => json(solicitudDe({ version: 5 })),
        "POST /solicitudes/s-1/identidad/vendedor": () => {
          ident.estado.partes = [parte("vendedor", "pendiente", { esperaSegundos: 60 }), parte("comprador", "sin_solicitar")];
          return json({ parte: "vendedor", ordinal: 1, estado: "pendiente" }, 202);
        },
      },
      ident,
    );
    expect(within(card).getByText("Validación: sin enviar")).toBeInTheDocument();

    fireEvent.click(within(card).getByRole("button", { name: "Validar identidad" }));

    expect(await within(card).findByText("Enlace enviado a an•••@correo.co")).toBeInTheDocument();
    expect(within(card).getByText("Validación: pendiente")).toBeInTheDocument();
    const claves = llamadas.map((l) => l.clave);
    expect(claves.indexOf("PATCH /solicitudes/s-1/actores")).toBeLessThan(claves.indexOf("POST /solicitudes/s-1/identidad/vendedor"));
    expect(cuerpo(llamadas, "PATCH /solicitudes/s-1/actores")).toEqual({
      version: 4,
      actores: [{ rol: "vendedor", ordinal: 1, tipoDocumento: "CC", numeroDocumento: CEDULA_SOLICITANTE, nombre: "ANA RUIZ", correo: "ana@correo.co", celular: "3001234567" }],
    });
    expect(cuerpo(llamadas, "POST /solicitudes/s-1/identidad/vendedor")).toEqual({ ordinal: 1 });
    // Con la validación en curso el documento y el nombre quedan protegidos (el nombre de un tercero se muestra enmascarado).
    expect(within(card).getByLabelText(/Número de documento/)).toBeDisabled();
    expect(within(card).getByLabelText(/Nombre completo/)).toHaveValue("AN••• RU•••");
    expect(within(card).getByRole("button", { name: "Reenviar Validación" })).toBeDisabled();
    expect(within(card).getByText(/Podrás reenviar en \d+ s/)).toBeInTheDocument();
  });

  it("un reenvío a un actor no afecta a otro: el comprador sigue sin enviar", async () => {
    const ident = conIdentidad([parte("vendedor", "pendiente"), parte("comprador", "sin_solicitar")]);
    const { llamadas } = await vendedorListo(
      {
        "PATCH /solicitudes/s-1/actores": () => json(solicitudDe({ version: 5 })),
        "POST /solicitudes/s-1/identidad/vendedor/reenviar": () => json({ parte: "vendedor", ordinal: 1, estado: "pendiente" }),
      },
      ident,
    );
    const vendedor = tarjeta("Propietario (vendedor)");
    fireEvent.click(await within(vendedor).findByRole("button", { name: "Reenviar Validación" }));

    expect(await within(vendedor).findByText("Enlace enviado a an•••@correo.co")).toBeInTheDocument();
    expect(llamadas.some((l) => l.clave === "POST /solicitudes/s-1/identidad/comprador/reenviar")).toBe(false);
    expect(within(tarjeta("Comprador")).queryByRole("button", { name: "Reenviar Validación" })).toBeNull();
  });

  it("respeta el tope del back: un 429 con Retry-After muestra el tiempo y deshabilita el botón", async () => {
    const ident = conIdentidad([parte("vendedor", "pendiente"), parte("comprador", "sin_solicitar")]);
    await vendedorListo(
      {
        "PATCH /solicitudes/s-1/actores": () => json(solicitudDe({ version: 5 })),
        "POST /solicitudes/s-1/identidad/vendedor/reenviar": () => problema(429, "reenvio_muy_pronto", { "retry-after": "42" }, { retryAfterSeconds: 42 }),
      },
      ident,
    );
    const card = tarjeta("Propietario (vendedor)");
    fireEvent.click(await within(card).findByRole("button", { name: "Reenviar Validación" }));

    expect(await within(card).findByText("Espera 42 s antes de reenviar el enlace de nuevo.")).toBeInTheDocument();
    expect(within(card).getByText(/Podrás reenviar en 4\d s/)).toBeInTheDocument();
    expect(within(card).getByRole("button", { name: "Reenviar Validación" })).toBeDisabled();
  });

  it("con los reenvíos agotados el botón se deshabilita y se avisa", async () => {
    const ident = conIdentidad([parte("vendedor", "pendiente", { reenviosRestantes: 0, puedeReenviar: false }), parte("comprador", "sin_solicitar")]);
    await vendedorListo({}, ident);
    const card = tarjeta("Propietario (vendedor)");

    expect(await within(card).findByRole("button", { name: "Reenviar Validación" })).toBeDisabled();
    expect(within(card).queryByText(/Te quedan/)).toBeNull();
  });

  it("un actor cuyo rol no exige validación no muestra insignia ni botón de validación", async () => {
    await abrir();
    const copro = tarjeta("Copropietario");
    fireEvent.click(within(copro).getByRole("button", { name: "Agregar Copropietario" }));
    const grupo = within(copro).getByRole("group", { name: "Copropietario 1" });
    fireEvent.change(within(grupo).getByLabelText(/Número de documento/), { target: { value: CEDULA_SOLICITANTE } });
    fireEvent.click(within(grupo).getByRole("button", { name: "Consultar" }));
    await waitFor(() => expect(within(grupo).getByLabelText(/Teléfono/)).toBeEnabled());

    expect(within(grupo).queryByText(/Validación:/)).toBeNull();
    expect(within(grupo).queryByRole("button", { name: /Validar identidad|Reenviar/ })).toBeNull();
  });

  it("un NIT que el back rechaza (persona jurídica no soportada) se explica en la tarjeta", async () => {
    const { card } = await vendedorListo({
      "PATCH /solicitudes/s-1/actores": () => json(solicitudDe({ version: 5 })),
      "POST /solicitudes/s-1/identidad/vendedor": () => problema(422, "persona_juridica_no_soportada"),
    });
    fireEvent.click(within(card).getByRole("button", { name: "Validar identidad" }));

    expect(await within(card).findByText(/solo para personas naturales/)).toBeInTheDocument();
  });

  it("si FLIT 2 no responde (202 pendiente_envio) lo dice y deja reintentar", async () => {
    const ident = conIdentidad([parte("vendedor", "sin_solicitar"), parte("comprador", "sin_solicitar")]);
    const { card } = await vendedorListo(
      {
        "PATCH /solicitudes/s-1/actores": () => json(solicitudDe({ version: 5 })),
        "POST /solicitudes/s-1/identidad/vendedor": () => {
          ident.estado.partes = [parte("vendedor", "pendiente_envio"), parte("comprador", "sin_solicitar")];
          return json({ parte: "vendedor", ordinal: 1, estado: "pendiente_envio" }, 202);
        },
      },
      ident,
    );
    fireEvent.click(within(card).getByRole("button", { name: "Validar identidad" }));

    expect(await within(card).findByText(/Aún no pudimos enviar el enlace/)).toBeInTheDocument();
    expect(await within(card).findByText("Validación: enviando enlace")).toBeInTheDocument();
  });

  it("un rechazo muestra el estado y permite pedir un enlace nuevo", async () => {
    const ident = conIdentidad([parte("vendedor", "rechazada"), parte("comprador", "sin_solicitar")]);
    await vendedorListo({}, ident);
    const card = tarjeta("Propietario (vendedor)");

    expect(await within(card).findByText("Validación: rechazada")).toBeInTheDocument();
    expect(within(card).getByText(/fue rechazada/)).toBeInTheDocument();
    expect(within(card).getByRole("button", { name: "Validar identidad" })).toBeEnabled();
  });
});

describe("estado en vivo y compuerta de «Continuar» (F3 AC6)", () => {
  const con = (vendedor: string, comprador: string): ActorEstado[] => [guardadoActor("vendedor", CEDULA_SOLICITANTE, "JUAN PEREZ", vendedor), guardadoActor("comprador", "52123456", "MARIA RODRIGUEZ", comprador)];
  const rehidratado = (vendedor: string, comprador: string) => solicitudDe({ ...guardado(DIAGNOSTICO_OK), actores: con(vendedor, comprador) });

  it("el sondeo cambia la insignia a aprobada sin recargar y se habilita «Continuar» cuando todos aprobaron", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let partes = [parte("vendedor", "aprobada"), parte("comprador", "pendiente")];
    let lecturas = 0;
    await abrir(
      { "GET /solicitudes/s-1/identidad": () => (lecturas++, json(identidad(partes))) },
      rehidratado("aprobada", "pendiente"),
    );
    const comprador = tarjeta("Comprador");
    await within(comprador).findByText("Validación: pendiente");
    expect(continuar()).toBeDisabled();
    expect(screen.getByText("Falta validar la identidad de: Comprador.")).toBeInTheDocument();
    expect(within(comprador).getByLabelText(/Nombre completo/)).toHaveValue("MA••• RO•••");

    partes = [parte("vendedor", "aprobada"), parte("comprador", "aprobada")];
    await act(async () => {
      await vi.advanceTimersByTimeAsync(8500);
    });

    expect(await within(comprador).findByText("Validación: aprobada")).toBeInTheDocument();
    expect(within(comprador).getByLabelText(/Nombre completo/)).toHaveValue("MARIA RODRIGUEZ");
    await waitFor(() => expect(continuar()).toBeEnabled());
    // Al aprobarse todo el sondeo se detiene.
    const antes = lecturas;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(40000);
    });
    expect(lecturas).toBe(antes);
  });

  it("el sondeo es prudente: una lectura cada 8 s mientras algo esté pendiente, sin ráfagas", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let lecturas = 0;
    await abrir(
      { "GET /solicitudes/s-1/identidad": () => (lecturas++, json(identidad([parte("vendedor", "aprobada"), parte("comprador", "pendiente")]))) },
      rehidratado("aprobada", "pendiente"),
    );
    await screen.findByText("Validación: pendiente");
    expect(lecturas).toBe(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(7000);
    });
    expect(lecturas).toBe(1);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(lecturas).toBe(2);
  });

  it("con todas las identidades aprobadas guarda con la versión y pasa al paso 3", async () => {
    const { llamadas } = await abrir(
      {
        "GET /solicitudes/s-1/identidad": () => json(identidad([parte("vendedor", "aprobada"), parte("comprador", "aprobada")])),
        "PATCH /solicitudes/s-1/actores": () => json(solicitudDe({ ...guardado(DIAGNOSTICO_OK), version: 5, pendientes: [], actores: con("aprobada", "aprobada") })),
      },
      rehidratado("aprobada", "aprobada"),
    );
    await waitFor(() => expect(continuar()).toBeEnabled());

    fireEvent.click(continuar());

    await waitFor(() => expect(nodo(3)).toHaveAttribute("aria-current", "step"));
    const cuerpoGuardado = cuerpo(llamadas, "PATCH /solicitudes/s-1/actores");
    expect(cuerpoGuardado.version).toBe(4);
    expect(cuerpoGuardado.actores).toHaveLength(2);
    expect(cuerpoGuardado.actores[1]).toMatchObject({ rol: "comprador", ordinal: 1, numeroDocumento: "52123456", nombre: "MARIA RODRIGUEZ", celular: "3001234567" });
  });

  it("un 409 de versión al guardar los actores avisa y ofrece recargar, sin pasar de paso", async () => {
    await abrir(
      {
        "GET /solicitudes/s-1/identidad": () => json(identidad([parte("vendedor", "aprobada"), parte("comprador", "aprobada")])),
        "PATCH /solicitudes/s-1/actores": () => problema(409, "conflicto_version"),
      },
      rehidratado("aprobada", "aprobada"),
    );
    await waitFor(() => expect(continuar()).toBeEnabled());
    fireEvent.click(continuar());

    expect(await screen.findByText(/Este trámite cambió mientras lo editabas/)).toBeInTheDocument();
    expect(nodo(2)).toHaveAttribute("aria-current", "step");
  });

  it("un 422 con errores por campo los pone en la persona y el campo correspondiente", async () => {
    await abrir(
      {
        "GET /solicitudes/s-1/identidad": () => json(identidad([parte("vendedor", "aprobada"), parte("comprador", "aprobada")])),
        "PATCH /solicitudes/s-1/actores": () => problema(422, "datos_invalidos", {}, { errors: { "actores[1].correo": ["El correo no es válido."] } }),
      },
      rehidratado("aprobada", "aprobada"),
    );
    await waitFor(() => expect(continuar()).toBeEnabled());
    fireEvent.click(continuar());

    const correo = within(tarjeta("Comprador")).getByLabelText(/Correo electrónico/);
    await waitFor(() => expect(correo).toHaveAttribute("aria-invalid", "true"));
    expect(within(tarjeta("Comprador")).getByText("El correo no es válido.")).toBeInTheDocument();
  });
});

describe("estados de la pantalla (F3 y K5 AC6)", () => {
  it("mientras llega el estado de la identidad se ve «cargando»", async () => {
    let resolver: (r: Response) => void = () => undefined;
    const pendiente = new Promise<Response>((r) => (resolver = r));
    await abrir({ "GET /solicitudes/s-1/identidad": () => pendiente });

    expect(await screen.findByText("Consultando el estado de las validaciones...")).toBeInTheDocument();
    resolver(json(sinSolicitar()));
    await waitFor(() => expect(screen.queryByText("Consultando el estado de las validaciones...")).toBeNull());
  });

  it("si falla la lectura del estado muestra el error con «Reintentar» y lo recupera", async () => {
    let falla = true;
    await abrir({ "GET /solicitudes/s-1/identidad": () => (falla ? problema(503, "servicio_no_disponible") : json(sinSolicitar())) });

    const alerta = await screen.findByText(/No pudimos consultar el estado de las validaciones de identidad/);
    expect(alerta).toBeInTheDocument();
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(screen.queryByText(/No pudimos consultar el estado/)).toBeNull());
  });

  it("un tipo sin actores muestra un aviso claro y deja continuar", async () => {
    const definicion = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], { actores: [] });
    await wizardAbierto({ [RUTA_DEFINICION]: () => json(definicion), "GET /solicitudes/s-1": () => json(solicitudDe({ ...guardado(DIAGNOSTICO_OK), pendientes: [{ codigo: "documento_requerido", referencia: "doc", paso: "p3" }] })) });
    // El borrador reabre en el paso 3 (le falta un documento): se vuelve al paso 2 desde el navegador de pasos.
    fireEvent.click(nodo(2));

    expect(await screen.findByText("Este trámite no pide registrar personas en este paso.")).toBeInTheDocument();
    await waitFor(() => expect(continuar()).toBeEnabled());
  });
});

describe("Habeas Data en el paso Actores", () => {
  it("el documento y el contacto no salen en ninguna URL ni en el almacenamiento del navegador", async () => {
    const { llamadas } = await abrir({
      "PATCH /solicitudes/s-1/actores": () => json(solicitudDe({ version: 5 })),
      "POST /solicitudes/s-1/identidad/vendedor": () => json({ parte: "vendedor", ordinal: 1, estado: "pendiente" }, 202),
    });
    const card = tarjeta("Propietario (vendedor)");
    consultarSolicitante(card);
    await waitFor(() => expect(within(card).getByLabelText(/Teléfono/)).toBeEnabled());
    llenarContacto(card);
    fireEvent.click(within(card).getByRole("button", { name: "Validar identidad" }));
    await within(card).findByText(/Enlace enviado/);

    const sensibles = [CEDULA_SOLICITANTE, "3001234567", "ana@correo.co", "ANA", "ana%40correo"];
    for (const { clave } of llamadas) for (const s of sensibles) expect(clave).not.toContain(s);
    expect(window.location.href).not.toContain(CEDULA_SOLICITANTE);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it("el documento y el nombre de un tercero consultado no salen en la URL, el almacenamiento ni la consola", async () => {
    const consola = [vi.spyOn(console, "log"), vi.spyOn(console, "info"), vi.spyOn(console, "warn"), vi.spyOn(console, "error")];
    const { llamadas } = await abrir({ [RUTA_DETALLE]: () => detalle() });
    await consultarTercero(tarjeta("Comprador"), "52123456");

    for (const { clave } of llamadas) for (const s of ["52123456", "ANA", "RUIZ"]) expect(clave).not.toContain(s);
    expect(window.location.href).not.toMatch(/52123456|ANA|RUIZ/);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    for (const espia of consola) expect(JSON.stringify(espia.mock.calls)).not.toMatch(/52123456|RUIZ/);
    vi.restoreAllMocks();
  });
});
