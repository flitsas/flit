import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TOKEN_MODO_DESARROLLO } from "@/components/Captcha";
import { Landing } from "@/components/landing/Landing";

import { crearCliente, type DefinicionTipo, type EstadoRadicado } from "@/lib/api/cliente";
import { CONSENTIMIENTO_VERSION } from "@/lib/consentimiento";
import { datosVehiculo, modoVehiculo, vehiculoValido } from "@/lib/landing/vehiculo";
import { obtenerContexto } from "@/lib/tramite/contexto";
import { routerPush } from "./router";
import { json, ORGANISMOS, renderConQuery, TIPOS } from "./utilidades";

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
const DOCUMENTOS = [doc("cedula", "Cédula del propietario", 2), doc("tarjeta", "Tarjeta de propiedad", 1), doc("extra", "Certificado opcional", 3, false)];

const SESION = { solicitudId: "s-1", codigoPublico: "PN-2026-0042", estado: "borrador", tipoTramiteCodigo: "TIPO_A", expiraEn: "2026-10-28T20:00:00Z", definicionVersion: 1 };

type Manejador = (init: RequestInit) => Response | Promise<Response>;

function montar(rutas: Record<string, Manejador> = {}) {
  const todas: Record<string, Manejador> = {
    "GET /organismos": () => json(ORGANISMOS),
    "GET /tipos-tramite": () => json(TIPOS),
    "GET /tipos-tramite/TIPO_A/definicion": () => json(definicion()),
    "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1": () => json(definicion()),
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

async function llenarTarjeta({ cedula = "1020456789", vehiculo = "abc123", tipo = "Trámite A" } = {}) {
  const selector = screen.getByRole("button", { name: "Tipo de Trámite" });
  await waitFor(() => expect(selector).toBeEnabled());
  fireEvent.click(selector);
  fireEvent.click(within(screen.getByRole("listbox", { name: "Tipo de Trámite" })).getByRole("option", { name: tipo }));
  fireEvent.change(screen.getByLabelText("Número de Cédula"), { target: { value: cedula } });
  fireEvent.change(screen.getByLabelText(/del vehículo/), { target: { value: vehiculo } });
}

const abrirRequisitos = () => fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));

async function elegirOrganismo(nombre = "Ciudad Uno") {
  const dialogo = await screen.findByRole("dialog", { name: "Requisitos para tu trámite" });
  fireEvent.click(await within(dialogo).findByRole("button", { name: "Selecciona un organismo" }));
  fireEvent.click(within(dialogo).getByRole("button", { name: nombre }));
  return dialogo;
}

async function prepararInicio(rutas: Record<string, Manejador> = {}) {
  const montado = montar(rutas);
  await llenarTarjeta();
  abrirRequisitos();
  const dialogo = await elegirOrganismo();
  fireEvent.click(within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales."));
  return { ...montado, dialogo };
}

describe("tarjeta «Iniciar mi trámite»", () => {
  it("el botón solo se habilita con cédula de 6 o más dígitos, placa válida y tipo elegido", async () => {
    montar();
    const boton = screen.getByRole("button", { name: "Iniciar trámite" });
    expect(boton).toBeDisabled();

    const selector = screen.getByRole("button", { name: "Tipo de Trámite" });
    await waitFor(() => expect(selector).toBeEnabled());
    fireEvent.change(screen.getByLabelText("Número de Cédula"), { target: { value: "12345" } });
    fireEvent.change(screen.getByLabelText("Placa del vehículo"), { target: { value: "abc123" } });
    fireEvent.click(selector);
    fireEvent.click(within(screen.getByRole("listbox")).getByRole("option", { name: "Trámite A" }));
    expect(boton).toBeDisabled(); // cédula de 5 dígitos

    fireEvent.change(screen.getByLabelText("Número de Cédula"), { target: { value: "123456" } });
    expect(boton).toBeEnabled();
    fireEvent.change(screen.getByLabelText("Placa del vehículo"), { target: { value: "ab" } });
    expect(boton).toBeDisabled(); // placa corta
  });

  it("si la definición del tipo pide VIN, el campo cambia de etiqueta y de formato", async () => {
    montar({ "GET /tipos-tramite/TIPO_A/definicion": () => json(definicion("VIN")) });

    await llenarTarjeta({ vehiculo: "9bwzzz377vt004251" });

    const campo = await screen.findByLabelText("VIN del vehículo");
    expect(campo).toHaveValue("9BWZZZ377VT004251");
    expect(screen.queryByLabelText("Placa del vehículo")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Iniciar trámite" })).toBeEnabled();
  });

  it("si la definición admite placa o VIN, el campo acepta cualquiera de los dos", async () => {
    montar({ "GET /tipos-tramite/TIPO_A/definicion": () => json(definicion("BOTH")) });

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

describe("modal de requisitos", () => {
  it("lista una tarjeta por documento obligatorio de la definición, ordenados, y pide el organismo", async () => {
    const { llamadas } = montar();
    await llenarTarjeta();

    abrirRequisitos();

    const dialogo = await screen.findByRole("dialog", { name: "Requisitos para tu trámite" });
    expect(dialogo).toHaveAccessibleDescription("Ten a la mano estos documentos para realizar tu trámite digital sin contratiempos.");
    const tarjetas = await within(dialogo).findAllByRole("listitem");
    expect(tarjetas.map((t) => t.textContent)).toEqual(["Tarjeta de propiedad", "Cédula del propietario"]);
    expect(within(dialogo).queryByText("Certificado opcional")).not.toBeInTheDocument();

    await elegirOrganismo();
    await waitFor(() => expect(llamadas.map((l) => l.clave)).toContain("GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1"));
  });

  it("cargando: muestra el cargador mientras llega la definición", async () => {
    montar({ "GET /tipos-tramite/TIPO_A/definicion": () => new Promise<Response>(() => {}) });
    await llenarTarjeta();

    abrirRequisitos();

    const dialogo = await screen.findByRole("dialog", { name: "Requisitos para tu trámite" });
    expect(within(dialogo).getByText("Cargando...").closest('[role="status"]')).not.toBeNull();
  });

  it("error: avisa con role=alert y «Reintentar» vuelve a pedir la definición", async () => {
    let falla = true;
    montar({ "GET /tipos-tramite/TIPO_A/definicion": () => (falla ? json({ code: "x" }, 503) : json(definicion())) });
    await llenarTarjeta();
    abrirRequisitos();

    const dialogo = await screen.findByRole("dialog", { name: "Requisitos para tu trámite" });
    const alerta = await within(dialogo).findByRole("alert");
    expect(alerta).toHaveTextContent("El servicio no está disponible en este momento");
    falla = false;
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));

    expect(await within(dialogo).findByText("Tarjeta de propiedad")).toBeInTheDocument();
  });

  it("vacío: un tipo sin documentos obligatorios lo dice", async () => {
    montar({ "GET /tipos-tramite/TIPO_A/definicion": () => json(definicion("PLATE", [])) });
    await llenarTarjeta();

    abrirRequisitos();

    expect(await screen.findByText("Este trámite no exige documentos obligatorios por ahora.")).toHaveAttribute("role", "status");
  });

  it("«Iniciar más tarde» cierra sin crear nada y la tarjeta conserva lo escrito", async () => {
    const { llamadas } = montar();
    await llenarTarjeta();
    abrirRequisitos();
    await screen.findByRole("dialog", { name: "Requisitos para tu trámite" });

    fireEvent.click(screen.getByRole("button", { name: "Iniciar más tarde" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Requisitos para tu trámite" })).not.toBeInTheDocument());
    expect(posts(llamadas)).toHaveLength(0);
    expect(screen.getByLabelText("Número de Cédula")).toHaveValue("1020456789");
    expect(screen.getByLabelText("Placa del vehículo")).toHaveValue("ABC123");
    expect(screen.getByRole("button", { name: "Tipo de Trámite" })).toHaveTextContent("Trámite A");
  });

  it("Escape (cancel) cierra el modal y el botón Cerrar mide 44 px", async () => {
    montar();
    await llenarTarjeta();
    abrirRequisitos();
    const dialogo = await screen.findByRole("dialog", { name: "Requisitos para tu trámite" });
    expect(within(dialogo).getByRole("button", { name: "Cerrar" })).toHaveClass("h-11", "w-11");

    fireEvent(dialogo, new Event("cancel", { cancelable: true }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Requisitos para tu trámite" })).not.toBeInTheDocument());
  });

  it("el consentimiento es obligatorio: sin marcarlo «Iniciar ahora» no se habilita", async () => {
    montar();
    await llenarTarjeta();
    abrirRequisitos();
    const dialogo = await elegirOrganismo();

    const consentimiento = within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales.");
    expect(consentimiento).toBeRequired();
    expect(consentimiento).not.toBeChecked();
    expect(within(dialogo).getByRole("button", { name: "Iniciar ahora" })).toBeDisabled();

    fireEvent.click(consentimiento);
    expect(within(dialogo).getByRole("button", { name: "Iniciar ahora" })).toBeEnabled();
  });

  it("sin organismo no se puede iniciar", async () => {
    montar();
    await llenarTarjeta();
    abrirRequisitos();
    const dialogo = await screen.findByRole("dialog", { name: "Requisitos para tu trámite" });
    fireEvent.click(within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales."));
    expect(within(dialogo).getByRole("button", { name: "Iniciar ahora" })).toBeDisabled(); // falta el organismo

    fireEvent.click(await within(dialogo).findByRole("button", { name: "Selecciona un organismo" }));
    fireEvent.click(within(dialogo).getByRole("button", { name: "Ciudad Uno" }));
    expect(within(dialogo).getByRole("button", { name: "Iniciar ahora" })).toBeEnabled();
  });

  it("el aviso «Políticas de Privacidad y Términos de Uso» abre el diálogo de documento no disponible", async () => {
    montar();
    await llenarTarjeta();
    abrirRequisitos();
    const dialogo = await screen.findByRole("dialog", { name: "Requisitos para tu trámite" });

    fireEvent.click(within(dialogo).getByRole("button", { name: "Políticas de Privacidad y Términos de Uso" }));

    expect(await screen.findByText("El documento oficial aún no está disponible.")).toBeInTheDocument();
  });
});

describe("Iniciar ahora: sesión efímera", () => {
  it("abre la sesión con consentimiento y su versión, captcha, y entra a /tramite/<tipo> sin datos en la URL", async () => {
    const { llamadas, dialogo } = await prepararInicio();

    fireEvent.click(within(dialogo).getByRole("button", { name: "Iniciar ahora" }));

    await waitFor(() => expect(routerPush).toHaveBeenCalledWith("/tramite/TIPO_A"));
    const [post] = posts(llamadas);
    expect(JSON.parse(String(post.init.body))).toEqual({
      tipoCodigo: "TIPO_A",
      organismoId: "o-1",
      cedula: "1020456789",
      placa: "ABC123",
      consentimiento: true,
      consentimientoVersion: CONSENTIMIENTO_VERSION,
    });
    expect(CONSENTIMIENTO_VERSION).toBe("v1");
    expect((post.init.headers as Record<string, string>)["x-captcha-token"]).toBe(TOKEN_MODO_DESARROLLO);
    expect(String(routerPush.mock.calls[0][0])).not.toMatch(/1020456789|ABC123/);
    expect(obtenerContexto()).toMatchObject({ intencion: "iniciar", tipoCodigo: "TIPO_A", solicitudId: "s-1", radicado: "PN-2026-0042", cedula: "1020456789", placa: "ABC123", organismoId: "o-1" });
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it("con un tipo que pide VIN manda el VIN y no la placa", async () => {
    const { llamadas } = montar({ "GET /tipos-tramite/TIPO_A/definicion": () => json(definicion("VIN")), "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1": () => json(definicion("VIN")) });
    await llenarTarjeta({ vehiculo: "9bwzzz377vt004251" });
    await screen.findByLabelText("VIN del vehículo");
    abrirRequisitos();
    const dialogo = await elegirOrganismo();
    fireEvent.click(within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales."));

    fireEvent.click(within(dialogo).getByRole("button", { name: "Iniciar ahora" }));

    await waitFor(() => expect(routerPush).toHaveBeenCalled());
    expect(JSON.parse(String(posts(llamadas)[0].init.body))).toMatchObject({ vin: "9BWZZZ377VT004251" });
    expect(JSON.parse(String(posts(llamadas)[0].init.body))).not.toHaveProperty("placa");
  });

  it("mientras la solicitud está en curso muestra el cargador a pantalla completa y no crea borradores duplicados", async () => {
    const { llamadas, dialogo } = await prepararInicio({ "POST /sesiones": () => new Promise<Response>(() => {}) });
    const boton = within(dialogo).getByRole("button", { name: "Iniciar ahora" });

    fireEvent.click(boton);
    fireEvent.click(boton);

    await waitFor(() => expect(boton).toBeDisabled());
    expect(posts(llamadas)).toHaveLength(1);
    const cargador = dialogo.querySelector(".fixed.inset-0");
    expect(cargador).not.toBeNull();
    expect(within(cargador as HTMLElement).getByRole("status")).toHaveTextContent("Cargando...");
    expect(within(dialogo).getByRole("button", { name: "Cerrar" })).toBeDisabled();
    expect(within(dialogo).getByRole("button", { name: "Iniciar más tarde" })).toBeDisabled();
    fireEvent(dialogo, new Event("cancel", { cancelable: true }));
    expect(screen.getByRole("dialog", { name: "Requisitos para tu trámite" })).toBeInTheDocument(); // Escape no cierra mientras se abre la sesión
    expect(routerPush).not.toHaveBeenCalled();
  });

  it("captcha vencido o ausente (captcha_required): mensaje claro con role=alert y la persona sigue en la landing con sus datos", async () => {
    const montado = montar({ "POST /sesiones": () => json({ code: "captcha_required", title: "Captcha requerido", status: 400 }, 400) });
    await llenarTarjeta();
    abrirRequisitos();
    const dialogo = await elegirOrganismo();
    fireEvent.click(within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales."));

    fireEvent.click(within(dialogo).getByRole("button", { name: "Iniciar ahora" }));

    expect(await within(dialogo).findByRole("alert")).toHaveTextContent("Resuelve la verificación de seguridad para continuar.");
    expect(routerPush).not.toHaveBeenCalled();
    expect(posts(montado.llamadas)).toHaveLength(1);
    expect(screen.getByLabelText("Número de Cédula")).toHaveValue("1020456789");
  });

  it("429: avisa, bloquea el reintento durante la espera y no navega", async () => {
    montar({ "POST /sesiones": () => new Response(JSON.stringify({ code: "rate_limited" }), { status: 429, headers: { "retry-after": "90" } }) });
    await llenarTarjeta();
    abrirRequisitos();
    const dialogo = await elegirOrganismo();
    fireEvent.click(within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales."));

    fireEvent.click(within(dialogo).getByRole("button", { name: "Iniciar ahora" }));

    expect(await within(dialogo).findByRole("alert")).toHaveTextContent("Demasiados intentos. Vuelve a intentarlo en unos minutos.");
    expect(await within(dialogo).findByText(/Podrás intentarlo de nuevo en \d+ s/)).toBeInTheDocument();
    expect(within(dialogo).getByRole("button", { name: "Iniciar ahora" })).toBeDisabled();
    expect(routerPush).not.toHaveBeenCalled();
  });

  it("el back no responde: mensaje claro y el modal sigue abierto para reintentar", async () => {
    let falla = true;
    montar({ "POST /sesiones": () => (falla ? json({ code: "servicio_no_disponible" }, 503) : json(SESION, 201)) });
    await llenarTarjeta();
    abrirRequisitos();
    const dialogo = await elegirOrganismo();
    fireEvent.click(within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales."));
    fireEvent.click(within(dialogo).getByRole("button", { name: "Iniciar ahora" }));
    expect(await within(dialogo).findByRole("alert")).toHaveTextContent("El servicio no está disponible en este momento");

    falla = false;
    fireEvent.click(within(dialogo).getByRole("button", { name: "Iniciar ahora" }));

    await waitFor(() => expect(routerPush).toHaveBeenCalledWith("/tramite/TIPO_A"));
  });

  it("tipo ya no disponible: lo dice en lugar de un error genérico", async () => {
    montar({ "POST /sesiones": () => json({ code: "tipo_no_disponible", title: "x", status: 409 }, 409) });
    await llenarTarjeta();
    abrirRequisitos();
    const dialogo = await elegirOrganismo();
    fireEvent.click(within(dialogo).getByLabelText("Acepto el tratamiento de mis datos personales."));

    fireEvent.click(within(dialogo).getByRole("button", { name: "Iniciar ahora" }));

    expect(await within(dialogo).findByRole("alert")).toHaveTextContent("Este tipo de trámite ya no está disponible en el portal.");
  });
});

describe("costura con «Consultar mi trámite» (subsanación)", () => {
  const RECHAZADO: EstadoRadicado = {
    radicado: "PN-2026-0007",
    numeroReferencia: null,
    placa: "XYZ987",
    tipoTramite: "TIPO_C",
    organismo: "Ciudad Uno",
    macroEstado: { codigo: "subsanacion", etiqueta: "Subsanación", indice: 2 },
    mensaje: "El Organismo de Tránsito devolvió la solicitud.",
    pasos: [{ clave: "resolucion", titulo: "Rechazado", estado: "rejected", fecha: null }],
    subsanacion: { motivo: "Falta el paz y salvo.", accionCorregir: true },
    soloLectura: false,
  };

  it("al pulsar la alerta cierra el modal, deja el contexto en memoria y entra a la ruta del tipo sin datos en la URL", async () => {
    montar({ "POST /radicados/consulta": () => json(RECHAZADO) });
    fireEvent.click(screen.getByRole("button", { name: "Consultar mi trámite" }));
    const dialogo = await screen.findByRole("dialog", { name: "Consultar mi trámite" });
    fireEvent.change(within(dialogo).getByLabelText("Número de radicado"), { target: { value: "pn-2026-0007" } });
    fireEvent.change(within(dialogo).getByLabelText("Cédula del solicitante"), { target: { value: "1020456789" } });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Consultar" }));

    fireEvent.click(await within(dialogo).findByRole("button", { name: /Causal de rechazo/ }));

    expect(routerPush).toHaveBeenCalledWith("/tramite/TIPO_C");
    expect(obtenerContexto()).toMatchObject({ intencion: "subsanar", tipoCodigo: "TIPO_C", radicado: "PN-2026-0007", cedula: "1020456789" });
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
});
