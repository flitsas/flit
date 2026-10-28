import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { TOKEN_MODO_DESARROLLO } from "@/components/Captcha";
import { Landing } from "@/components/landing/Landing";
import { CotizarModal, MENSAJE_COTIZACION_FALLIDA, MENSAJE_SIN_TARIFAS } from "@/components/modales/CotizarModal";
import { crearCliente, type Cotizacion } from "@/lib/api/cliente";
import { formatearPesos } from "@/lib/formato";
import { json, ORGANISMOS, renderConQuery, TIPOS } from "./utilidades";

const COTIZACION: Cotizacion = {
  lineas: [
    { codigo: "transito", nombre: "Valor del Servicio de tránsito", subtitulo: "", valorCop: 185000 },
    { codigo: "flit", nombre: "Valor de Servicio FLIT", subtitulo: "Incluye validación de identidad y radicación digital", valorCop: 181000 },
  ],
  totalCop: 366000,
  versionTarifa: "2026-1",
  adicionalesNoAplicados: [],
};

type Manejador = (init: RequestInit) => Response | Promise<Response>;

/** fetch falso por «MÉTODO ruta-con-consulta»; lo no simulado responde 404. */
function montar(rutas: Record<string, Manejador>, props: { organismoInicial?: (typeof ORGANISMOS)[number] } = {}) {
  const llamadas: { clave: string; init: RequestInit }[] = [];
  const fetchImpl = (async (url: string, init: RequestInit) => {
    const clave = `${init.method} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push({ clave, init });
    return (rutas[clave] ?? (() => json({ code: "no_simulado" }, 404)))(init);
  }) as unknown as typeof fetch;
  const cliente = crearCliente({ fetchImpl });
  const onCerrar = vi.fn();
  renderConQuery(<CotizarModal abierto onCerrar={onCerrar} cliente={cliente} {...props} />);
  return { llamadas, onCerrar };
}

const BASE = {
  "GET /organismos": () => json(ORGANISMOS),
  "GET /tipos-tramite?organismoId=o-1": () => json(TIPOS),
  "POST /cotizaciones": () => json(COTIZACION),
};

async function elegirOrganismoYTipo(organismo = "Ciudad Uno", tipo = "Trámite A") {
  fireEvent.click(await screen.findByRole("button", { name: "Selecciona un organismo" }));
  fireEvent.click(screen.getByRole("button", { name: organismo }));
  const selector = screen.getByRole("button", { name: "Tipo de trámite" });
  await waitFor(() => expect(selector).toBeEnabled());
  fireEvent.click(selector);
  fireEvent.click(within(screen.getByRole("listbox", { name: "Tipo de trámite" })).getByRole("option", { name: tipo }));
}

const marcarCaptcha = () => fireEvent.click(screen.getByLabelText("Verificación de seguridad"));
const calcular = () => fireEvent.click(screen.getByRole("button", { name: "Calcular" }));

describe("modal Cotizar: selección", () => {
  it("abre con título, el tipo de trámite deshabilitado y Calcular sin habilitar hasta elegir organismo y tipo", async () => {
    montar(BASE);

    expect(screen.getByRole("dialog", { name: "Cotizar mi trámite" })).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "Selecciona un organismo" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tipo de trámite" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Tipo de trámite" })).toHaveTextContent("Primero elige un organismo");
    expect(screen.getByRole("button", { name: "Calcular" })).toBeDisabled();
  });

  it("el organismo abre una lista con «Buscar municipio» y el tipo ofrece solo los disponibles de ese organismo", async () => {
    const { llamadas } = montar(BASE);

    fireEvent.click(await screen.findByRole("button", { name: "Selecciona un organismo" }));
    expect(screen.getByPlaceholderText("Buscar municipio")).toBeInTheDocument();
    expect(screen.getByRole("listbox", { name: "Organismos de tránsito" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Ciudad Uno" }));

    const selector = screen.getByRole("button", { name: "Tipo de trámite" });
    await waitFor(() => expect(selector).toBeEnabled());
    fireEvent.click(selector);
    const opciones = within(screen.getByRole("listbox", { name: "Tipo de trámite" })).getAllByRole("option");
    expect(opciones.map((o) => o.textContent)).toEqual(["Trámite A", "Trámite C"]);
    expect(llamadas.map((l) => l.clave)).toContain("GET /tipos-tramite?organismoId=o-1");
  });

  it("Calcular se habilita con organismo, tipo y captcha", async () => {
    montar(BASE);

    await elegirOrganismoYTipo();
    expect(screen.getByRole("button", { name: "Calcular" })).toBeDisabled();
    marcarCaptcha();

    expect(screen.getByRole("button", { name: "Calcular" })).toBeEnabled();
  });

  it("un organismo sin tarifas publicadas lo dice y no deja elegir tipo", async () => {
    montar({ ...BASE, "GET /tipos-tramite?organismoId=o-1": () => json([TIPOS[1]]) });

    fireEvent.click(await screen.findByRole("button", { name: "Selecciona un organismo" }));
    fireEvent.click(screen.getByRole("button", { name: "Ciudad Uno" }));

    expect(await screen.findByText(MENSAJE_SIN_TARIFAS)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tipo de trámite" })).toBeDisabled();
  });

  it("organismos cargando: muestra el cargador; con error: alerta y Reintentar", async () => {
    let falla = true;
    montar({ ...BASE, "GET /organismos": () => (falla ? json({ code: "x" }, 503) : json(ORGANISMOS)) });

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("El servicio no está disponible en este momento");
    falla = false;
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));

    expect(await screen.findByRole("button", { name: "Selecciona un organismo" })).toBeInTheDocument();
  });

  it("mientras carga el catálogo muestra «Cargando...» con role=status", () => {
    montar({ ...BASE, "GET /organismos": () => new Promise<Response>(() => {}) });

    expect(screen.getByText("Cargando...").closest('[role="status"]')).not.toBeNull();
  });

  it("los tipos con error muestran el mensaje y permiten reintentar", async () => {
    let falla = true;
    montar({ ...BASE, "GET /tipos-tramite?organismoId=o-1": () => (falla ? json({ code: "x" }, 503) : json(TIPOS)) });

    fireEvent.click(await screen.findByRole("button", { name: "Selecciona un organismo" }));
    fireEvent.click(screen.getByRole("button", { name: "Ciudad Uno" }));
    const alerta = await screen.findByRole("alert");
    falla = false;
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Tipo de trámite" })).toBeEnabled());
  });

  it("con «Ver tarifas» el modal se abre con el organismo ya elegido", async () => {
    montar(BASE, { organismoInicial: ORGANISMOS[0] });

    expect(await screen.findByRole("button", { name: "Ciudad Uno" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("button", { name: "Tipo de trámite" })).toBeEnabled());
  });
});

describe("modal Cotizar: cálculo", () => {
  it("calcula con el captcha, muestra las dos líneas con su subtítulo, el total en pesos y el encabezado organismo · tipo", async () => {
    const { llamadas } = montar(BASE);
    await elegirOrganismoYTipo();
    marcarCaptcha();

    calcular();

    const tabla = await screen.findByRole("table", { name: "Cotización del trámite" });
    expect(within(tabla).getByText("Valor del Servicio de tránsito")).toBeInTheDocument();
    expect(within(tabla).getByText("Valor de Servicio FLIT")).toBeInTheDocument();
    expect(within(tabla).getByText("Incluye validación de identidad y radicación digital")).toBeInTheDocument();
    expect(within(tabla).getByText("$ 185.000 COP")).toBeInTheDocument();
    expect(within(tabla).getByText("$ 181.000 COP")).toBeInTheDocument();
    expect(within(tabla).getByRole("row", { name: /Total.*\$ 366\.000 COP/ })).toBeInTheDocument();
    expect(screen.getByText("Ciudad Uno · Trámite A")).toBeInTheDocument();
    expect(screen.queryByText(/simulados/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Calcular" })).not.toBeInTheDocument();

    const post = llamadas.find((l) => l.clave === "POST /cotizaciones")!;
    expect(JSON.parse(String(post.init.body))).toEqual({ organismoId: "o-1", tipoCodigo: "TIPO_A" });
    expect((post.init.headers as Record<string, string>)["x-captcha-token"]).toBe(TOKEN_MODO_DESARROLLO);
  });

  it("cambiar el tipo oculta la tabla y vuelve «Calcular»", async () => {
    montar(BASE);
    await elegirOrganismoYTipo();
    marcarCaptcha();
    calcular();
    await screen.findByRole("table");

    fireEvent.click(screen.getByRole("button", { name: "Tipo de trámite" }));
    fireEvent.click(within(screen.getByRole("listbox", { name: "Tipo de trámite" })).getByRole("option", { name: "Trámite C" }));

    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Calcular" })).toBeDisabled(); // el captcha usado ya no sirve
  });

  it("cambiar el organismo oculta la tabla y limpia el tipo", async () => {
    montar({ ...BASE, "GET /tipos-tramite?organismoId=o-2": () => json(TIPOS) });
    await elegirOrganismoYTipo();
    marcarCaptcha();
    calcular();
    await screen.findByRole("table");

    fireEvent.click(screen.getByRole("button", { name: "Ciudad Uno" }));
    fireEvent.click(screen.getByRole("button", { name: "Villa Dos" }));

    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("button", { name: "Tipo de trámite" })).toHaveTextContent("Selecciona"));
    expect(screen.getByRole("button", { name: "Calcular" })).toBeInTheDocument();
  });

  it("cargando: Calcular queda deshabilitado y avisa con role=status", async () => {
    const { llamadas } = montar({ ...BASE, "POST /cotizaciones": () => new Promise<Response>(() => {}) });
    await elegirOrganismoYTipo();
    marcarCaptcha();

    calcular();

    expect(await screen.findByText("Calculando...")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Calcular" })).toBeDisabled();
    expect(llamadas.filter((l) => l.clave === "POST /cotizaciones")).toHaveLength(1);
  });

  it("falla del back: mensaje genérico y se puede reintentar", async () => {
    let falla = true;
    montar({ ...BASE, "POST /cotizaciones": () => (falla ? json({ code: "servicio_no_disponible" }, 503) : json(COTIZACION)) });
    await elegirOrganismoYTipo();
    marcarCaptcha();
    calcular();
    expect(await screen.findByRole("alert")).toHaveTextContent(MENSAJE_COTIZACION_FALLIDA);

    falla = false;
    marcarCaptcha();
    calcular();

    expect(await screen.findByRole("table")).toBeInTheDocument();
  });

  it.each([
    ["tarifa_no_vigente", "Este trámite aún no tiene tarifa en el organismo elegido."],
    ["tipo_no_habilitado", "Este trámite no está habilitado en el organismo elegido."],
  ])("%s: explica por qué no hay cotización", async (code, mensaje) => {
    montar({ ...BASE, "POST /cotizaciones": () => json({ code, title: "x", status: 422 }, 422) });
    await elegirOrganismoYTipo();
    marcarCaptcha();

    calcular();

    expect(await screen.findByRole("alert")).toHaveTextContent(mensaje);
  });

  it("429: deshabilita Calcular durante la espera", async () => {
    montar({ ...BASE, "POST /cotizaciones": () => new Response(JSON.stringify({ code: "rate_limited" }), { status: 429, headers: { "retry-after": "60" } }) });
    await elegirOrganismoYTipo();
    marcarCaptcha();

    calcular();

    expect(await screen.findByText(/Podrás intentarlo de nuevo en \d+ s/)).toBeInTheDocument();
    marcarCaptcha();
    expect(screen.getByRole("button", { name: "Calcular" })).toBeDisabled();
  });
});

describe("modal Cotizar: envío por correo", () => {
  async function conCotizacion(extra: Record<string, Manejador> = {}) {
    const montado = montar({ ...BASE, "POST /cotizaciones/enviar-correo": () => json({ enviado: true, repetido: false }), ...extra });
    await elegirOrganismoYTipo();
    marcarCaptcha();
    calcular();
    await screen.findByRole("table");
    return montado;
  }
  const correo = () => screen.getByLabelText("Correo electrónico");
  const enviar = () => fireEvent.click(screen.getByRole("button", { name: "Enviar al correo" }));

  it("con un correo válido envía con Idempotency-Key y captcha, confirma con role=status y no conserva el correo", async () => {
    const { llamadas } = await conCotizacion();
    fireEvent.change(correo(), { target: { value: "ciudadano@ejemplo.com" } });
    marcarCaptcha();

    enviar();

    expect(await screen.findByText("Te enviamos la cotización a tu correo")).toHaveAttribute("role", "status");
    const envio = llamadas.find((l) => l.clave === "POST /cotizaciones/enviar-correo")!;
    const cabeceras = envio.init.headers as Record<string, string>;
    expect(cabeceras["idempotency-key"]).toMatch(/\S{8,}/);
    expect(cabeceras["x-captcha-token"]).toBe(TOKEN_MODO_DESARROLLO);
    expect(JSON.parse(String(envio.init.body))).toEqual({ organismoId: "o-1", tipoCodigo: "TIPO_A", correo: "ciudadano@ejemplo.com" });
    expect(correo()).toHaveValue("");
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it("un correo inválido no se envía: el botón sigue deshabilitado y el campo es type=email requerido", async () => {
    const { llamadas } = await conCotizacion();

    fireEvent.change(correo(), { target: { value: "no-es-correo" } });
    marcarCaptcha();

    expect(correo()).toHaveAttribute("type", "email");
    expect(correo()).toBeRequired();
    expect(screen.getByRole("button", { name: "Enviar al correo" })).toBeDisabled();
    fireEvent.submit(correo().closest("form")!);
    expect(llamadas.some((l) => l.clave === "POST /cotizaciones/enviar-correo")).toBe(false);
  });

  it("si el envío falla, el reintento con el mismo correo reusa la misma llave de idempotencia", async () => {
    let falla = true;
    const { llamadas } = await conCotizacion({ "POST /cotizaciones/enviar-correo": () => (falla ? json({ code: "envio_fallido" }, 502) : json({ enviado: true, repetido: true })) });
    fireEvent.change(correo(), { target: { value: "ciudadano@ejemplo.com" } });
    marcarCaptcha();
    enviar();
    expect(await screen.findByRole("alert")).toHaveTextContent("No pudimos enviar el correo. Intenta de nuevo.");

    falla = false;
    marcarCaptcha();
    enviar();
    expect(await screen.findByText("Te enviamos la cotización a tu correo")).toBeInTheDocument();

    const llaves = llamadas.filter((l) => l.clave === "POST /cotizaciones/enviar-correo").map((l) => (l.init.headers as Record<string, string>)["idempotency-key"]);
    expect(llaves).toHaveLength(2);
    expect(llaves[0]).toBe(llaves[1]);
  });

  it("límite de correos: muestra el mensaje y no permite insistir", async () => {
    await conCotizacion({ "POST /cotizaciones/enviar-correo": () => new Response(JSON.stringify({ code: "correo_rate_limited" }), { status: 429, headers: { "retry-after": "3600" } }) });
    fireEvent.change(correo(), { target: { value: "ciudadano@ejemplo.com" } });
    marcarCaptcha();

    enviar();

    expect(await screen.findByRole("alert")).toHaveTextContent("Ese correo ya recibió varias cotizaciones. Intenta más tarde.");
    marcarCaptcha();
    expect(screen.getByRole("button", { name: "Enviar al correo" })).toBeDisabled();
  });
});

describe("landing: Cotizar y Ver tarifas", () => {
  const CATALOGOS = { "/organismos": () => json(ORGANISMOS), "/tipos-tramite": () => json(TIPOS), "/tipos-tramite?organismoId=o-2": () => json(TIPOS) };

  function landing() {
    const fetchImpl = (async (url: string) => (CATALOGOS[String(url).replace("/api/v1/persona-natural", "") as keyof typeof CATALOGOS] ?? (() => json({}, 404)))()) as unknown as typeof fetch;
    renderConQuery(<Landing cliente={crearCliente({ fetchImpl })} />);
  }

  it("la fila «Cotizar mi trámite» abre el modal", async () => {
    landing();

    fireEvent.click(screen.getByRole("button", { name: "Cotizar mi trámite" }));

    expect(await screen.findByRole("dialog", { name: "Cotizar mi trámite" })).toBeInTheDocument();
  });

  it("«Ver tarifas» de un organismo abre el modal con ese organismo ya elegido", async () => {
    landing();
    await screen.findByText("Villa Dos");

    fireEvent.click(screen.getByRole("button", { name: "Ver tarifas de Villa Dos" }));

    const dialogo = await screen.findByRole("dialog", { name: "Cotizar mi trámite" });
    expect(await within(dialogo).findByRole("button", { name: "Villa Dos" })).toBeInTheDocument();
  });
});

describe("formato de pesos", () => {
  it("separa los miles con punto, sin Intl", () => {
    expect(formatearPesos(0)).toBe("$ 0 COP");
    expect(formatearPesos(999)).toBe("$ 999 COP");
    expect(formatearPesos(185000)).toBe("$ 185.000 COP");
    expect(formatearPesos(1234567)).toBe("$ 1.234.567 COP");
    expect(formatearPesos(366000.4)).toBe("$ 366.000 COP");
    expect(formatearPesos(-1500)).toBe("-$ 1.500 COP");
    expect(formatearPesos(Number.NaN)).toBe("$ 0 COP");
  });
});
