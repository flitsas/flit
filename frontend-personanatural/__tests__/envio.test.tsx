import { act, cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { EstadoSolicitud } from "@/lib/api/cliente";
import { obtenerContexto } from "@/lib/tramite/contexto";
import { ACEPTADA, abrirResumen, definicionCompleta, irAlEnvio, problema, RUTA_DEFINICION, solicitudCompleta } from "./fixtures-envio";
import { nodo, wizardAbierto, type Manejador } from "./montar-wizard";
import { routerPush } from "./router";
import { json } from "./utilidades";

// El sondeo real es cada 12 s: en las pruebas se acorta para verlo correr y detenerse sin esperar.
vi.mock("@/lib/tramite/envio", async (original) => ({ ...(await original<typeof import("@/lib/tramite/envio")>()), INTERVALO_SONDEO_MS: 30 }));

const RADICAR = "POST /solicitudes/s-1/radicar";
const ESTADO = "GET /solicitudes/s-1/estado";
const MARCA = "2026-10-28T15:30:00Z"; // 10:30 en Colombia

const estadoDe = (estado: string, extra: Partial<EstadoSolicitud> = {}): EstadoSolicitud => ({
  solicitudId: "s-1",
  codigoPublico: "PN-2026-0042",
  estado,
  tipoTramiteCodigo: "TIPO_A",
  actualizadoEn: MARCA,
  ...extra,
});
/** El borrador tras radicar: sin datos personales, como lo devuelve el back a la sesión de solo estado. */
const enCola = () => solicitudCompleta({ estado: "en_cola", version: 8, actores: solicitudCompleta().actores.map((a) => ({ ...a, tipoDocumento: null, numeroDocumento: null, nombre: null, correo: null, celular: null, direccion: null })) });

const radicaciones = <T extends { clave: string }>(llamadas: T[]) => llamadas.filter((l) => l.clave === RADICAR);
const llaveDe = (l: { init: RequestInit }) => (l.init.headers as Record<string, string>)["idempotency-key"];
const enviar = () => screen.getByRole("button", { name: "Enviar trámite" });
const esperar = (ms: number) => act(() => new Promise<void>((r) => setTimeout(r, ms)));

/** Abre el wizard en el paso Envío; `radicar` responde la radicación y, si es 202, el back deja el borrador en cola. */
async function abrirEnvio({ radicar, estado, rutas = {} }: { radicar?: Manejador; estado?: Manejador; rutas?: Record<string, Manejador> } = {}) {
  const fase = { enviada: false };
  const m = await abrirResumen({
    "GET /solicitudes/s-1": () => json(fase.enviada ? enCola() : solicitudCompleta()),
    [RADICAR]:
      radicar ??
      (() => {
        fase.enviada = true;
        return json(ACEPTADA, 202);
      }),
    [ESTADO]: estado ?? (() => json(estadoDe("en_cola"))),
    ...rutas,
  });
  await irAlEnvio();
  return { ...m, fase };
}

describe("Paso 5 · Envío (F6)", () => {
  it("antes de enviar muestra el resumen del trámite y el botón que radica", async () => {
    await abrirEnvio();

    const resumen = screen.getByRole("region", { name: "Resumen del trámite" });
    expect(within(resumen).getByText("Valor del trámite")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Elección de facturación" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Datos bancarios FLIT" })).not.toBeInTheDocument();
    expect(screen.queryByText(/Comprobante de pago/)).not.toBeInTheDocument();
    expect(within(resumen).getByRole("button", { name: "Atrás" })).toBeEnabled();
    expect(enviar()).toBeEnabled();
    expect(nodo(4)).toHaveAttribute("aria-current", "step");
  });

  it("al recibir el 202 muestra la confirmación: radicado del portal, tipo, organismo, mensaje de borrador y el seguimiento", async () => {
    const { llamadas } = await abrirEnvio();

    fireEvent.click(enviar());

    expect(await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" })).toHaveFocus();
    expect(llamadas.filter((l) => l.clave === RADICAR)).toHaveLength(1);
    expect(llaveDe(radicaciones(llamadas)[0])).toBeTruthy();
    expect(radicaciones(llamadas)[0].init.body).toBeUndefined();

    const tabla = screen.getByRole("table");
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["ID del trámite", "Tipo", "Nº radicado", "Organismo"]);
    expect(within(tabla).getAllByRole("cell").map((c) => c.textContent)).toEqual(["PN-2026-0042", "Trámite A", "Pendiente", "Ciudad Uno"]);
    const confirmacion = screen.getByRole("region", { name: "Confirmación del trámite" });
    expect(confirmacion).toHaveTextContent(/Para consultar el estado de tu trámite o la respuesta del Organismo de Tránsito/);
    expect(within(confirmacion).getByRole("region", { name: "Vehículo" })).toHaveTextContent("ABC123");
    expect(within(confirmacion).getByRole("region", { name: "Actores" })).toHaveTextContent("Propietario (vendedor): ANA RUIZ");
    expect(within(confirmacion).getByRole("region", { name: "Actores" })).toHaveTextContent("Comprador: LUIS PEREZ");
    const documentos = within(confirmacion).getByRole("region", { name: "Documentos cargados" });
    expect(documentos).toHaveTextContent("Paz y salvo de impuestos");
    expect(documentos).not.toHaveTextContent("Comprobante de pago");
    expect(documentos).not.toHaveTextContent(".pdf");
    expect(screen.queryByRole("button", { name: "Enviar trámite" })).toBeNull();

    const seguimiento = await screen.findByRole("list", { name: "Tracking del radicado" });
    const pasos = within(seguimiento).getAllByRole("listitem");
    expect(pasos.map((p) => p.getAttribute("data-status"))).toEqual(["done", "done", "current", "pending", "pending"]);
    expect(pasos[2]).toHaveTextContent("Enviado al OT");
    expect(pasos[2]).toHaveTextContent("28/10/2026 10:30");
    expect(pasos[3]).toHaveTextContent("En revisión");
  });

  it("no hay rastro de pago en la confirmación", async () => {
    await abrirEnvio();
    fireEvent.click(enviar());
    await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });

    const confirmacion = screen.getByRole("region", { name: "Confirmación del trámite" });
    expect(confirmacion.textContent).not.toMatch(/PSE\b|transferencia|bancari|bancolombia|factura|convenio|cuenta corriente/i);
    expect(within(confirmacion).getByRole("region", { name: "Documentos cargados" })).not.toHaveTextContent("Comprobante de pago");
  });

  it("un doble clic radica una sola vez, con el cargador a pantalla completa y el botón deshabilitado", async () => {
    let liberar!: () => void;
    const puerta = new Promise<void>((r) => (liberar = r));
    const { llamadas, fase } = await abrirEnvio({
      radicar: async () => {
        await puerta;
        return json(ACEPTADA, 202);
      },
    });

    fireEvent.click(enviar());
    fireEvent.click(enviar());

    expect(screen.getByText("Enviando tu trámite")).toBeInTheDocument();
    expect(enviar()).toBeDisabled();
    expect(document.body.querySelector("[aria-busy='true']")).toBeInTheDocument();
    fase.enviada = true;
    liberar();
    await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });

    expect(radicaciones(llamadas)).toHaveLength(1);
    expect(screen.queryByText("Enviando tu trámite")).toBeNull();
  });

  it("si el intento falló sin saber qué pasó (sin red), el reintento usa la misma Idempotency-Key", async () => {
    let intentos = 0;
    const { llamadas, fase } = await abrirEnvio({
      radicar: () => {
        if (++intentos === 1) return Promise.reject(new TypeError("fetch failed"));
        fase.enviada = true;
        return json(ACEPTADA, 202);
      },
    });

    fireEvent.click(enviar());
    expect(await screen.findByText("No pudimos completar la acción. Intenta de nuevo en unos minutos.")).toBeInTheDocument();
    expect(enviar()).toBeEnabled();
    fireEvent.click(enviar());
    await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });

    const [primera, segunda] = radicaciones(llamadas);
    expect(llaveDe(primera)).toBe(llaveDe(segunda));
  });

  it("tras un rechazo definitivo (409 idempotency_conflicto) el siguiente intento usa una llave nueva", async () => {
    let intentos = 0;
    const { llamadas, fase } = await abrirEnvio({
      radicar: () => {
        if (++intentos === 1) return problema(409, "idempotency_conflicto");
        fase.enviada = true;
        return json(ACEPTADA, 202);
      },
    });

    fireEvent.click(enviar());
    expect(await screen.findByText("No pudimos procesar el envío. Vuelve a intentarlo.")).toBeInTheDocument();
    fireEvent.click(enviar());
    await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });

    const [primera, segunda] = radicaciones(llamadas);
    expect(llaveDe(primera)).not.toBe(llaveDe(segunda));
  });

  it("422 requisitos_incompletos: lista lo que falta con enlace al paso, y lo que el portal no puede completar sin enlace", async () => {
    const pendientes = [
      { codigo: "documento_requerido", referencia: "paz_salvo", paso: "p3" },
      { codigo: "actor_requerido", referencia: "comprador", paso: "p2" },
      { codigo: "comprobante_pago_requerido", referencia: null, paso: null },
    ];
    const { llamadas } = await abrirEnvio({ radicar: () => problema(422, "requisitos_incompletos", { pendientes }) });

    fireEvent.click(enviar());

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("Aún falta información para enviar tu trámite");
    const items = within(alerta).getAllByRole("listitem");
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent("Falta cargar el documento: Paz y salvo de impuestos.");
    expect(items[1]).toHaveTextContent("Falta registrar a: Comprador.");
    expect(items[2]).toHaveTextContent("El servicio pide un requisito que este portal todavía no puede completar.");
    expect(within(items[2]).queryByRole("button")).toBeNull();
    expect(screen.queryByText(/Enviando tu trámite/)).toBeNull();
    expect(enviar()).toBeEnabled();

    fireEvent.click(within(items[1]).getByRole("button", { name: "Ir a Actores" }));
    expect(nodo(2)).toHaveAttribute("aria-current", "step");
    // El rechazo vuelve a leer el borrador para que lo mostrado sea lo vigente.
    await waitFor(() => expect(llamadas.filter((l) => l.clave === "GET /solicitudes/s-1").length).toBeGreaterThan(1));
  });

  it("409 identity_pending: impide el envío y dice qué parte falta", async () => {
    await abrirEnvio({ radicar: () => problema(409, "identity_pending", { motivo: "identidad_pendiente", partes: [{ parte: "comprador", ordinal: 1, estado: "pendiente" }] }) });

    fireEvent.click(enviar());

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("Falta validar la identidad");
    expect(alerta).toHaveTextContent("Comprador: validación pendiente");
    expect(within(alerta).getByRole("button", { name: "Ir a Actores" })).toBeInTheDocument();
    expect(enviar()).toBeEnabled();
  });

  it("409 estado_no_permite_cambio (ya había salido): vuelve a leer el borrador y muestra la confirmación", async () => {
    const { fase } = await abrirEnvio({
      radicar: () => {
        fase.enviada = true;
        return problema(409, "estado_no_permite_cambio");
      },
    });

    fireEvent.click(enviar());

    expect(await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" })).toBeInTheDocument();
  });

  it.each([
    [429, "Demasiados intentos. Vuelve a intentarlo en unos minutos."],
    [503, "El servicio no está disponible en este momento. Intenta de nuevo en unos minutos."],
  ])("%i con Retry-After: avisa, bloquea el botón con la cuenta regresiva y no reintenta solo", async (status, mensaje) => {
    const { llamadas } = await abrirEnvio({ radicar: () => problema(status, status === 429 ? "rate_limited" : "no_disponible", {}, { "retry-after": "30" }) });

    fireEvent.click(enviar());

    expect(await screen.findByText(mensaje)).toBeInTheDocument();
    expect(await screen.findByText("Podrás intentarlo de nuevo en 30 s.")).toBeInTheDocument();
    expect(enviar()).toBeDisabled();
    fireEvent.click(enviar());
    await esperar(50);
    expect(radicaciones(llamadas)).toHaveLength(1);
  });

  it("un 500 muestra un mensaje claro y deja reintentar", async () => {
    await abrirEnvio({ radicar: () => json({ code: "boom" }, 500) });

    fireEvent.click(enviar());

    expect(await screen.findByRole("alert")).toHaveTextContent("No pudimos completar la acción");
    expect(enviar()).toBeEnabled();
  });

  it("si la sesión venció al enviar, avisa «Tu sesión venció» y deja reanudar", async () => {
    await abrirEnvio({ radicar: () => problema(401, "session_expired") });

    fireEvent.click(enviar());

    expect(await screen.findByRole("dialog", { name: "Tu sesión venció" })).toBeInTheDocument();
  });

  it("«Atrás» en el resumen regresa a Requisitos", async () => {
    await abrirEnvio();

    fireEvent.click(screen.getByRole("button", { name: "Atrás" }));

    expect(nodo(3)).toHaveAttribute("aria-current", "step");
  });
});

describe("Seguimiento tras enviar (sondeo de GET /estado)", () => {
  it("sondea hasta que el trámite queda creado, muestra la referencia en la tabla y deja de preguntar", async () => {
    let n = 0;
    const { llamadas } = await abrirEnvio({ estado: () => json(++n < 3 ? estadoDe("en_cola") : estadoDe("creada", { numeroReferencia: "TR-2026-9001", actualizadoEn: "2026-10-28T16:00:00Z" })) });
    fireEvent.click(enviar());

    expect(await screen.findByText("TR-2026-9001", undefined, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.queryByText(/ya fue creado en FLIT 2/)).toBeNull();
    expect(screen.queryByText(/pendiente de continuar con el equipo/)).toBeNull();
    const tabla = screen.getByRole("table");
    expect(within(tabla).getAllByRole("cell")[2]).toHaveTextContent("TR-2026-9001");
    const pasos = within(screen.getByRole("list", { name: "Tracking del radicado" })).getAllByRole("listitem");
    expect(pasos.map((p) => p.getAttribute("data-status"))).toEqual(["done", "done", "done", "current", "pending"]);
    expect(pasos[3]).toHaveTextContent("En revisión");
    expect(pasos[3]).toHaveTextContent("28/10/2026 11:00");

    const sondeos = llamadas.filter((l) => l.clave === ESTADO).length;
    expect(sondeos).toBe(3);
    await esperar(150);
    expect(llamadas.filter((l) => l.clave === ESTADO)).toHaveLength(sondeos);
  });

  it("creado sin referencia deja el radicado en pendiente y no anuncia la creación", async () => {
    await abrirEnvio({ estado: () => json(estadoDe("creada")) });
    fireEvent.click(enviar());

    expect(await screen.findByText("En revisión", undefined, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.queryByText(/Tu trámite ya fue creado en FLIT 2/)).toBeNull();
    expect(within(screen.getByRole("table")).getAllByRole("cell")[2]).toHaveTextContent("Pendiente");
  });

  it("mientras siga en cola sigue sondeando de forma periódica", async () => {
    const { llamadas } = await abrirEnvio();
    fireEvent.click(enviar());
    await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });

    await waitFor(() => expect(llamadas.filter((l) => l.clave === ESTADO).length).toBeGreaterThanOrEqual(3), { timeout: 3000 });
  });

  it("al salir de la pantalla el sondeo se corta", async () => {
    const { llamadas } = await abrirEnvio();
    fireEvent.click(enviar());
    await waitFor(() => expect(llamadas.filter((l) => l.clave === ESTADO).length).toBeGreaterThanOrEqual(2), { timeout: 3000 });

    cleanup();
    await esperar(60);
    const al_salir = llamadas.filter((l) => l.clave === ESTADO).length;
    await esperar(150);

    expect(llamadas.filter((l) => l.clave === ESTADO)).toHaveLength(al_salir);
  });

  it("si FLIT 2 rechaza crear el trámite lo dice sin éxito falso, marca el paso rechazado y deja de preguntar", async () => {
    const { llamadas } = await abrirEnvio({ estado: () => json(estadoDe("rechazada_creacion")) });
    fireEvent.click(enviar());

    expect(await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" }, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.queryByText(/No pudimos crear tu trámite/)).toBeNull();
    const pasos = within(screen.getByRole("list", { name: "Tracking del radicado" })).getAllByRole("listitem");
    expect(pasos[2]).toHaveAttribute("data-status", "current");
    expect(pasos[4]).toHaveAttribute("data-status", "pending");
    await esperar(150);
    expect(llamadas.filter((l) => l.clave === ESTADO)).toHaveLength(1);
  });

  it("un fallo del sondeo avisa y se recupera en el siguiente intento", async () => {
    let n = 0;
    await abrirEnvio({ estado: () => (++n === 1 ? json({ code: "no_disponible" }, 503) : json(estadoDe("creada"))) });
    fireEvent.click(enviar());

    expect(await screen.findByText("No pudimos actualizar el estado ahora. Lo intentaremos de nuevo.")).toBeInTheDocument();
    expect(await screen.findByText("En revisión", undefined, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.queryByText(/Tu trámite ya fue creado en FLIT 2/)).toBeNull();
  });

  it("si la sesión de solo estado ya no sirve (401) deja de sondear", async () => {
    const { llamadas } = await abrirEnvio({ estado: () => problema(401, "session_expired") });
    fireEvent.click(enviar());

    await screen.findByRole("dialog", { name: "Tu sesión venció" });
    await esperar(150);
    expect(llamadas.filter((l) => l.clave === ESTADO)).toHaveLength(1);
  });
});

describe("Reanudar un trámite que ya salió", () => {
  const abrirEnviado = async (estado: string) => {
    const m = await wizardAbierto({
      [RUTA_DEFINICION]: () => json(definicionCompleta()),
      "GET /solicitudes/s-1": () => json({ ...enCola(), estado }),
      [ESTADO]: () => json(estadoDe(estado)),
    });
    return m;
  };

  it("un trámite en cola reabre directamente en la confirmación, sin botón de enviar ni nueva radicación", async () => {
    const { llamadas } = await abrirEnviado("en_cola");

    expect(await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" })).toBeInTheDocument();
    expect(nodo(5)).toHaveAttribute("aria-current", "step");
    expect(screen.queryByRole("button", { name: "Enviar trámite" })).toBeNull();
    expect(radicaciones(llamadas)).toHaveLength(0);
  });

  it("un trámite cancelado lo dice y no muestra el seguimiento", async () => {
    await abrirEnviado("cancelada");

    expect(await screen.findByRole("heading", { level: 2, name: "Este trámite fue cancelado" })).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Tracking del radicado" })).toBeNull();
  });
});

describe("Salir o consultar más tarde", () => {
  it("«Consultar mi trámite» abre la consulta con el radicado escrito y la cédula vacía", async () => {
    await abrirEnvio();
    fireEvent.click(enviar());
    await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });

    fireEvent.click(screen.getByRole("button", { name: "Consultar mi trámite" }));

    const dialogo = await screen.findByRole("dialog", { name: "Consultar mi trámite" });
    expect(within(dialogo).getByLabelText("Número de radicado")).toHaveValue("PN-2026-0042");
    expect(within(dialogo).getByLabelText("Cédula del solicitante")).toHaveValue("");
  });

  it("«Salir» vuelve al inicio y suelta el contexto en memoria (la cédula no se conserva)", async () => {
    await abrirEnvio();
    fireEvent.click(enviar());
    await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });
    expect(obtenerContexto()?.cedula).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Ir al Dashboard" }));

    expect(routerPush).toHaveBeenCalledWith("/");
    expect(obtenerContexto()).toBeNull();
  });
});

describe("Habeas Data en el envío", () => {
  it("la cédula y el radicado no van a la URL, al almacenamiento ni a la consola, ni al enviar ni al sondear", async () => {
    const espias = (["log", "info", "warn", "error", "debug"] as const).map((n) => vi.spyOn(console, n).mockImplementation(() => undefined));
    const { llamadas } = await abrirEnvio();
    fireEvent.click(enviar());
    await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });
    await waitFor(() => expect(llamadas.some((l) => l.clave === ESTADO)).toBe(true));

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(window.location.href).not.toMatch(/1020456789|PN-2026-0042|ABC123/);
    for (const l of llamadas) expect(String(l.init.body ?? "")).not.toMatch(/1020456789/);
    for (const e of espias) expect(e).not.toHaveBeenCalled();
    vi.restoreAllMocks();
  });
});
