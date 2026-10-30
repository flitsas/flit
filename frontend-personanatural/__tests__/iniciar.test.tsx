import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TOKEN_MODO_DESARROLLO } from "@/components/Captcha";
import { Landing } from "@/components/landing/Landing";

import { crearCliente, type DefinicionTipo } from "@/lib/api/cliente";
import { CONSENTIMIENTO_VERSION } from "@/lib/consentimiento";
import { mensajeDeOrganismo, mensajeOrganismoNoDisponible, organismoHabilitadoDelRunt } from "@/lib/landing/puerta-iniciar";
import { datosVehiculo, modoVehiculo, vehiculoValido } from "@/lib/landing/vehiculo";
import { obtenerContexto } from "@/lib/tramite/contexto";
import { routerPush } from "./router";
import { json, ORGANISMOS, renderConQuery } from "./utilidades";

function definicion(entryMode = "PLATE", documentos: DefinicionTipo["documentos"] = DOCUMENTOS): DefinicionTipo {
  return {
    code: "TIPO_A",
    name: "Trámite A",
    family: "fam-a",
    version: 1,
    huella: "h1",
    disponible: true,
    motivoNoDisponible: "",
    pasos: [],
    actores: [],
    documentos,
    flags: { entryMode, requiereComercial: false, requierePrenda: false, permiteTransformaciones: false, permitePrendaComplementaria: false, requiereBiometria: false, requiereSolicitudPlaca: false },
  };
}

const doc = (docCode: string, nombre: string, orden: number, obligatorio = true) => ({ docCode, nombre, obligatorio, orden, instruccionCargue: "", condicion: "" });
const DOCUMENTOS = [doc("cedula", "Cédula del propietario", 2), doc("tarjeta", "Tarjeta de propiedad", 1), doc("extra", "Certificado opcional", 3, false), { ...doc("leasing", "Contrato de leasing", 4), condicion: "si_leasing" }];

const SESION = { solicitudId: "s-1", codigoPublico: "PN-2026-0042", estado: "borrador", tipoTramiteCodigo: "CAMBIO_COLOR", expiraEn: "2026-10-28T20:00:00Z", definicionVersion: 1 };
const VEHICULO = { existe: true, placa: "ABC123", clase: "AUTOMOVIL", marca: "M", linea: "L", modeloAno: "2020", tipoServicio: "PARTICULAR", estado: "ACTIVO", tienePrenda: false, tieneLimitacion: false, tieneRestricciones: false, organismo: "Ciudad Uno" };
const TIPO_PUERTA = { code: "CAMBIO_COLOR", name: "Cambio de color", family: "OTROS", version: 1, disponible: true };

type Manejador = (init: RequestInit) => Response | Promise<Response>;

function montar(rutas: Record<string, Manejador> = {}) {
  const todas: Record<string, Manejador> = {
    "GET /organismos": () => json(ORGANISMOS),
    "GET /tipos-tramite": () => json([TIPO_PUERTA]),
    "GET /tipos-tramite/CAMBIO_COLOR/definicion": () => json(definicion()),
    "GET /tipos-tramite/CAMBIO_COLOR/definicion?organismoId=o-1": () => json(definicion()),
    "POST /consultas/vehiculo": () => json(VEHICULO),
    "POST /sesiones": () => json(SESION, 201),
    ...rutas,
  };
  const llamadas: { clave: string; init: RequestInit }[] = [];
  const fetchImpl = (async (url: string, init: RequestInit) => {
    const clave = `${init.method} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push({ clave, init });
    return (todas[clave] ?? (() => json({ code: "no_simulado" }, 404)))(init);
  }) as unknown as typeof fetch;
  renderConQuery(<Landing cliente={crearCliente({ fetchImpl })} />);
  return { llamadas };
}

const posts = (llamadas: { clave: string; init: RequestInit }[]) => llamadas.filter((l) => l.clave === "POST /sesiones");

const tarjeta = () => screen.getAllByRole("region", { name: "Acciones principales" }).at(-1)!;

async function llenarTarjeta({ cedula = "1020456789", vehiculo = "abc123", tipo = "Cambio de color" } = {}) {
  const selector = within(tarjeta()).getByRole("button", { name: "Tipo de Trámite" });
  await waitFor(() => expect(selector).toBeEnabled());
  fireEvent.click(selector);
  fireEvent.click(within(screen.getByRole("listbox", { name: "Tipo de Trámite" })).getByRole("option", { name: tipo }));
  fireEvent.change(within(tarjeta()).getByLabelText("Número de Cédula"), { target: { value: cedula } });
  fireEvent.change(within(tarjeta()).getByLabelText(/del vehículo/), { target: { value: vehiculo } });
}

const abrirConsulta = () => fireEvent.click(within(tarjeta()).getByRole("button", { name: "Iniciar trámite" }));

async function dialogoDeConsulta() {
  return screen.findByRole("dialog", { name: "Requisitos para tu trámite" });
}

async function iniciarAhora(dialogo: HTMLElement) {
  fireEvent.click(within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales."));
  const ahora = within(dialogo).getByRole("button", { name: "Iniciar ahora" });
  await waitFor(() => expect(ahora).toBeEnabled());
  fireEvent.click(ahora);
}

async function prepararInicio(rutas: Record<string, Manejador> = {}) {
  const montado = montar(rutas);
  await llenarTarjeta();
  abrirConsulta();
  const dialogo = await dialogoDeConsulta();
  await iniciarAhora(dialogo);
  return { ...montado, dialogo };
}

describe("tarjeta «Iniciar mi trámite»", () => {
  it("el botón solo se habilita con documento de 6 o más dígitos, placa válida y tipo de esta puerta", async () => {
    montar();
    const boton = within(tarjeta()).getByRole("button", { name: "Iniciar trámite" });
    expect(boton).toBeDisabled();

    const selector = within(tarjeta()).getByRole("button", { name: "Tipo de Trámite" });
    await waitFor(() => expect(selector).toBeEnabled());
    fireEvent.change(within(tarjeta()).getByLabelText("Número de Cédula"), { target: { value: "12345" } });
    fireEvent.change(within(tarjeta()).getByLabelText("Placa del vehículo"), { target: { value: "abc123" } });
    fireEvent.click(selector);
    fireEvent.click(within(screen.getByRole("listbox")).getByRole("option", { name: "Cambio de color" }));
    expect(boton).toBeDisabled();

    fireEvent.change(within(tarjeta()).getByLabelText("Número de Cédula"), { target: { value: "123456" } });
    expect(boton).toBeEnabled();
    fireEvent.change(within(tarjeta()).getByLabelText("Placa del vehículo"), { target: { value: "ab" } });
    expect(boton).toBeDisabled();
  });

  it("si la definición del tipo pide VIN, el campo cambia de etiqueta y de formato", async () => {
    montar({ "GET /tipos-tramite/CAMBIO_COLOR/definicion": () => json(definicion("VIN")) });

    await llenarTarjeta({ vehiculo: "9bwzzz377vt004251" });

    const campo = await screen.findByLabelText("VIN del vehículo");
    expect(campo).toHaveValue("9BWZZZ377VT004251");
    expect(screen.queryByLabelText("Placa del vehículo")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Iniciar trámite" })).toBeEnabled();
  });

  it("si la definición admite placa o VIN, el campo acepta cualquiera de los dos", async () => {
    montar({ "GET /tipos-tramite/CAMBIO_COLOR/definicion": () => json(definicion("BOTH")) });

    await llenarTarjeta({ vehiculo: "abc123" });

    expect(await screen.findByLabelText("Placa o VIN del vehículo")).toHaveValue("ABC123");
    expect(screen.getByRole("button", { name: "Iniciar trámite" })).toBeEnabled();
  });

  it("no guarda cédula ni placa en el navegador ni en la URL", async () => {
    montar();
    await llenarTarjeta();

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(window.location.href).not.toMatch(/1020456789|ABC123/);
  });
});

describe("consulta al pulsar Iniciar", () => {
  it("muestra los documentos del tipo y no consulta hasta pulsar Iniciar ahora", async () => {
    const { llamadas } = montar();
    await llenarTarjeta();
    abrirConsulta();
    const dialogo = await dialogoDeConsulta();

    expect(await within(dialogo).findByText("Tarjeta de propiedad")).toBeInTheDocument();
    expect(within(dialogo).getByText("Cédula del propietario")).toBeInTheDocument();
    expect(within(dialogo).queryByText("Certificado opcional")).not.toBeInTheDocument();
    expect(within(dialogo).queryByText("Contrato de leasing")).not.toBeInTheDocument();
    fireEvent.click(within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales."));
    expect(llamadas.filter((l) => l.clave === "POST /consultas/vehiculo")).toHaveLength(0);

    const ahora = within(dialogo).getByRole("button", { name: "Iniciar ahora" });
    await waitFor(() => expect(ahora).toBeEnabled());
    fireEvent.click(ahora);
    await waitFor(() => expect(routerPush).toHaveBeenCalledWith("/tramite/CAMBIO_COLOR"));
  });

  it("mientras consulta muestra el loader con la frase de espera", async () => {
    montar({ "POST /consultas/vehiculo": () => new Promise<Response>(() => {}) });
    await llenarTarjeta();
    abrirConsulta();
    const dialogo = await dialogoDeConsulta();
    await iniciarAhora(dialogo);

    const frase = await screen.findByText("Consultando el vehículo…");
    expect(frase.closest('[role="status"]')).not.toBeNull();
  });

  it("si el organismo del RUNT no está habilitado, avisa en el modal y no abre el borrador", async () => {
    const { llamadas, dialogo } = await prepararInicio({
      "POST /consultas/vehiculo": () => json({ ...VEHICULO, organismo: "Tránsito de Envigado" }),
    });

    expect(await within(dialogo).findByRole("alert")).toHaveTextContent(mensajeOrganismoNoDisponible("Tránsito de Envigado"));
    expect(posts(llamadas)).toHaveLength(0);
    expect(routerPush).not.toHaveBeenCalled();
    expect(screen.queryByRole("heading", { name: "Diagnóstico de requisitos previos" })).not.toBeInTheDocument();
  });

  it("si la placa no existe, avisa y no abre el borrador", async () => {
    const { llamadas, dialogo } = await prepararInicio({ "POST /consultas/vehiculo": () => json({ ...VEHICULO, existe: false }) });

    expect(await within(dialogo).findByRole("alert")).toHaveTextContent("No encontramos un vehículo con esa placa.");
    expect(posts(llamadas)).toHaveLength(0);
    expect(routerPush).not.toHaveBeenCalled();
  });

  it("si el RUNT no responde, avisa con el texto de la puerta y permite reintentar", async () => {
    let falla = true;
    const { llamadas, dialogo } = await prepararInicio({
      "POST /consultas/vehiculo": () => (falla ? json({ code: "proveedor_no_disponible" }, 503) : json(VEHICULO)),
    });
    expect(await within(dialogo).findByRole("alert")).toHaveTextContent("No pudimos consultar el RUNT. Inténtalo de nuevo.");

    falla = false;
    const ahora = within(dialogo).getByRole("button", { name: "Iniciar ahora" });
    await waitFor(() => expect(ahora).toBeEnabled());
    fireEvent.click(ahora);
    await waitFor(() => expect(routerPush).toHaveBeenCalledWith("/tramite/CAMBIO_COLOR"));
    expect(posts(llamadas)).toHaveLength(1);
  });

  it("abre la sesión con el documento del propietario y entra al trámite sin datos en la URL", async () => {
    const { llamadas } = await prepararInicio();

    await waitFor(() => expect(routerPush).toHaveBeenCalledWith("/tramite/CAMBIO_COLOR"));
    const consulta = llamadas.find((l) => l.clave === "POST /consultas/vehiculo");
    expect(JSON.parse(String(consulta?.init.body))).toMatchObject({
      placa: "ABC123",
      propietario: { tipo: "CC", numero: "1020456789" },
      consentimiento: true,
      consentimientoVersion: CONSENTIMIENTO_VERSION,
    });
    const [post] = posts(llamadas);
    expect(JSON.parse(String(post.init.body))).toEqual({
      tipoCodigo: "CAMBIO_COLOR",
      organismoId: "o-1",
      cedula: "1020456789",
      placa: "ABC123",
      consentimiento: true,
      consentimientoVersion: CONSENTIMIENTO_VERSION,
    });
    expect((post.init.headers as Record<string, string>)["x-captcha-token"]).toBe(TOKEN_MODO_DESARROLLO);
    expect(String(routerPush.mock.calls[0][0])).not.toMatch(/1020456789|ABC123/);
    expect(obtenerContexto()).toMatchObject({
      intencion: "iniciar",
      tipoCodigo: "CAMBIO_COLOR",
      cedula: "1020456789",
      placa: "ABC123",
      propietario: { tipo: "CC", numero: "1020456789" },
    });
  });

  it("«Iniciar más tarde» cierra sin consultar y la tarjeta conserva lo escrito", async () => {
    const { llamadas } = montar();
    await llenarTarjeta();
    abrirConsulta();
    await dialogoDeConsulta();
    fireEvent.click(screen.getByRole("button", { name: "Iniciar más tarde" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Requisitos para tu trámite" })).not.toBeInTheDocument());
    expect(llamadas.filter((l) => l.clave.startsWith("POST"))).toHaveLength(0);
    expect(screen.getByLabelText("Número de Cédula")).toHaveValue("1020456789");
    expect(screen.getByLabelText("Placa del vehículo")).toHaveValue("ABC123");
  });
});

describe("identificación del vehículo según el modo", () => {
  it("el modo sale de la definición y por omisión es la placa", () => {
    expect(modoVehiculo(undefined)).toBe("PLATE");
    expect(modoVehiculo("plate")).toBe("PLATE");
    expect(modoVehiculo("VIN")).toBe("VIN");
    expect(modoVehiculo("both")).toBe("BOTH");
    expect(modoVehiculo("otra cosa")).toBe("PLATE");
  });

  it("valida y arma los datos que se mandan al BFF", () => {
    expect(vehiculoValido("PLATE", "ABC123")).toBe(true);
    expect(vehiculoValido("PLATE", "AB")).toBe(false);
    expect(vehiculoValido("VIN", "9BWZZZ377VT004251")).toBe(true);
    expect(vehiculoValido("VIN", "ABC123")).toBe(false);
    expect(vehiculoValido("BOTH", "ABC123")).toBe(true);
    expect(vehiculoValido("BOTH", "ABCDEFGH")).toBe(false);
    expect(datosVehiculo("PLATE", "ABC123")).toEqual({ placa: "ABC123" });
    expect(datosVehiculo("VIN", "9BWZZZ377VT004251")).toEqual({ vin: "9BWZZZ377VT004251" });
    expect(datosVehiculo("BOTH", "ABC123")).toEqual({ placa: "ABC123" });
    expect(datosVehiculo("BOTH", "9BWZZZ377VT004251")).toEqual({ vin: "9BWZZZ377VT004251" });
  });

  it("el organismo del RUNT se compara con los habilitados, sin exigir el mismo texto", () => {
    const habilitados = [{ nombre: "STRIA TTOyTTE MCPAL SABANETA", municipio: "Sabaneta" }];
    expect(mensajeDeOrganismo("SABANETA", habilitados)).toBeNull();
    expect(organismoHabilitadoDelRunt("SABANETA", [{ id: "ot-sabaneta", ...habilitados[0] }])?.id).toBe("ot-sabaneta");
    expect(mensajeDeOrganismo("Secretaría de otro municipio", habilitados)).toBe(mensajeOrganismoNoDisponible("Secretaría de otro municipio"));
    expect(mensajeDeOrganismo("  ", habilitados)).toMatch(/no trajo el organismo/);
  });
});
