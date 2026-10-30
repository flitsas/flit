import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { TOKEN_MODO_DESARROLLO } from "@/components/Captcha";
import { ConsultarModal } from "@/components/modales/ConsultarModal";
import { crearCliente, type EstadoRadicado } from "@/lib/api/cliente";
import { formatearFechaHora } from "@/lib/formato";
import { json, renderConQuery, TIPOS } from "./utilidades";

const PASOS_EN_REVISION = [
  { clave: "borrador", titulo: "Borrador del trámite", estado: "done", fecha: "2026-10-20T14:15:00Z" },
  { clave: "validacion_identidad", titulo: "Validación de identidad", estado: "done", fecha: "2026-10-20T16:42:00Z" },
  { clave: "enviado_ot", titulo: "Enviado al OT", estado: "done", fecha: "2026-10-21T13:03:00Z" },
  { clave: "en_revision", titulo: "En revisión", estado: "current", fecha: "2026-10-22T19:26:00Z" },
  { clave: "resolucion", titulo: "Aprobado / Rechazado", estado: "pending", fecha: null },
] as const;

function respuesta(parcial: Partial<EstadoRadicado> = {}): EstadoRadicado {
  return {
    radicado: "PN-2026-0001",
    numeroReferencia: null,
    placa: "ABC123",
    tipoTramite: "TIPO_A",
    organismo: "Ciudad Uno",
    macroEstado: { codigo: "en_revision_ot", etiqueta: "En Revisión OT", indice: 1 },
    mensaje: "Tu trámite está en curso y aún no presenta respuesta.",
    pasos: [...PASOS_EN_REVISION] as EstadoRadicado["pasos"],
    subsanacion: null,
    soloLectura: false,
    ...parcial,
  };
}

const APROBADO = respuesta({
  macroEstado: { codigo: "finalizado", etiqueta: "Finalizado", indice: 3 },
  mensaje: "Tu trámite ha sido aprobado satisfactoriamente.",
  pasos: PASOS_EN_REVISION.map((p) => ({ ...p, estado: "done", fecha: "2026-10-24T15:51:00Z", titulo: p.clave === "resolucion" ? "Aprobado" : p.titulo })) as EstadoRadicado["pasos"],
});

const RECHAZADO = respuesta({
  macroEstado: { codigo: "subsanacion", etiqueta: "Subsanación", indice: 2 },
  mensaje: "El Organismo de Tránsito devolvió la solicitud.",
  pasos: PASOS_EN_REVISION.map((p) => ({ ...p, estado: p.clave === "resolucion" ? "rejected" : "done", fecha: "2026-10-24T15:51:00Z", titulo: p.clave === "resolucion" ? "Rechazado" : p.titulo })) as EstadoRadicado["pasos"],
  subsanacion: { motivo: "Inconsistencia en el paz y salvo de impuestos.", accionCorregir: true },
});

function montar(manejador: (url: string, init: RequestInit) => Response | Promise<Response>) {
  const fetchImpl = vi.fn(async (url: string, init: RequestInit) => manejador(String(url), init)) as unknown as typeof fetch;
  const onCerrar = vi.fn();
  const cliente = crearCliente({ fetchImpl });
  renderConQuery(<ConsultarModal abierto onCerrar={onCerrar} cliente={cliente} tipos={TIPOS} />);
  return { fetchImpl: fetchImpl as unknown as ReturnType<typeof vi.fn>, onCerrar };
}

/** Por defecto consulta con placa + radicado (diseño UI). Pasa `clave` numérica para cédula. */
function llenar({ placa = "abc123", clave = "pn-2026-0001" } = {}) {
  fireEvent.change(screen.getByLabelText("Placa"), { target: { value: placa } });
  fireEvent.change(screen.getByLabelText("Número de Radicado o Cédula"), { target: { value: clave } });
}

const consultar = () => fireEvent.click(screen.getByRole("button", { name: "Consultar" }));

describe("modal Consultar: formulario", () => {
  it("abre como diálogo con el diseño UI y el botón solo se habilita con placa y radicado o cédula", () => {
    montar(() => json(respuesta()));

    const dialogo = screen.getByRole("dialog", { name: "Consultar mi trámite" });
    expect(dialogo).toHaveAccessibleDescription(/placa y el número de radicado o cédula/i);
    const boton = screen.getByRole("button", { name: "Consultar" });
    expect(boton).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "abc123" } });
    expect(boton).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Número de Radicado o Cédula"), { target: { value: "pn-1" } });
    expect(screen.getByLabelText("Número de Radicado o Cédula")).toHaveValue("PN-1");
    expect(boton).toBeEnabled();
  });

  it("la placa va en mayúsculas y se limita a 6 caracteres", () => {
    montar(() => json(respuesta()));

    fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "ab-c12345" } });

    expect(screen.getByLabelText("Placa")).toHaveValue("ABC123");
  });

  it("tiene el botón Cerrar de 44 px y Escape (cancel) cierra el modal", () => {
    const { onCerrar } = montar(() => json(respuesta()));

    expect(screen.getByRole("button", { name: "Cerrar" })).toHaveClass("h-11", "w-11");
    fireEvent(screen.getByRole("dialog"), new Event("cancel", { cancelable: true }));
    expect(onCerrar).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    expect(onCerrar).toHaveBeenCalledTimes(2);
  });
});

describe("modal Consultar: envío y estados", () => {
  it("con radicado manda placa + radicado al BFF (sin cédula) y el captcha en cabecera", async () => {
    const { fetchImpl } = montar(() => json(respuesta()));
    llenar({ placa: "abc123", clave: "pn-2026-0001" });

    consultar();
    await screen.findByRole("heading", { name: "Tu trámite está en proceso" });

    const [url, init] = fetchImpl.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/v1/persona-natural/radicados/consulta");
    expect(init.method).toBe("POST");
    expect(JSON.parse(String(init.body))).toEqual({ placa: "ABC123", radicado: "PN-2026-0001" });
    expect((init.headers as Record<string, string>)["x-captcha-token"]).toBe(TOKEN_MODO_DESARROLLO);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(window.location.href).not.toMatch(/1020456789|PN-2026/);
  });

  it("con cédula (solo dígitos) manda placa + cédula al BFF (sin radicado)", async () => {
    const { fetchImpl } = montar(() => json(respuesta()));
    llenar({ placa: "abc123", clave: "1020456789" });

    consultar();
    await screen.findByRole("heading", { name: "Tu trámite está en proceso" });

    expect(JSON.parse(String((fetchImpl.mock.calls[0] as [string, RequestInit])[1].body))).toEqual({
      placa: "ABC123",
      cedula: "1020456789",
    });
  });

  it("cargando: muestra el cargador con role=status y el botón no permite enviar dos veces", async () => {
    const { fetchImpl } = montar(() => new Promise<Response>(() => {}));
    llenar();

    consultar();

    const cargando = await screen.findByText("Cargando...");
    expect(cargando.closest('[role="status"]')).not.toBeNull();
    expect(screen.getByRole("button", { name: "Consultar" })).toBeDisabled();
    fireEvent.submit(screen.getByRole("button", { name: "Consultar" }).closest("form")!);
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  it("en proceso: titular de advertencia, tracking de 5 pasos con fecha y hora de Colombia, datos y «Entendido»", async () => {
    const { onCerrar } = montar(() => json(respuesta()));
    llenar();

    consultar();

    const titular = await screen.findByRole("heading", { name: "Tu trámite está en proceso" });
    expect(titular).toHaveClass("text-warning");
    const pasos = within(screen.getByRole("list", { name: "Tracking del radicado" })).getAllByRole("listitem");
    expect(pasos).toHaveLength(5);
    expect(pasos[0]).toHaveTextContent("20/10/2026 09:15");
    expect(pasos[3]).toHaveAttribute("aria-current", "step");
    expect(pasos[3]).toHaveTextContent("En revisión");
    expect(pasos[4]).toHaveTextContent("Sin fecha");
    expect(screen.getByText("ID del trámite").nextSibling).toHaveTextContent("PN-2026-0001");
    expect(screen.getByText("Placa asociada").nextSibling).toHaveTextContent("ABC123");
    expect(screen.getByText("Tipo de trámite").nextSibling).toHaveTextContent("Trámite A");

    fireEvent.click(screen.getByRole("button", { name: "Entendido" }));
    expect(onCerrar).toHaveBeenCalled();
  });

  it("si el catálogo no trae el tipo, muestra su código", async () => {
    montar(() => json(respuesta({ tipoTramite: "TIPO_RARO" })));
    llenar();

    consultar();

    await screen.findByRole("heading", { name: "Tu trámite está en proceso" });
    expect(screen.getByText("Tipo de trámite").nextSibling).toHaveTextContent("TIPO_RARO");
  });

  it("aprobado: titular verde, todos los pasos completos y el comprobante marcado como no disponible aún", async () => {
    montar(() => json(APROBADO));
    llenar();

    consultar();

    const titular = await screen.findByRole("heading", { name: "Tu trámite ha sido aprobado" });
    expect(titular).toHaveClass("text-success");
    const pasos = within(screen.getByRole("list", { name: "Tracking del radicado" })).getAllByRole("listitem");
    expect(pasos.every((p) => p.getAttribute("data-status") === "done")).toBe(true);
    const descargar = screen.getByRole("button", { name: "Descargar comprobante" });
    expect(descargar).toBeDisabled();
    expect(descargar).toHaveAccessibleDescription("El comprobante estará disponible próximamente.");
  });

  it("rechazo: muestra la causal y cierra con Entendido, sin ofrecer corregir el trámite", async () => {
    montar(() => json(RECHAZADO));
    llenar();

    consultar();

    const titular = await screen.findByRole("heading", { name: "Tu trámite ha sido rechazado" });
    expect(titular).toHaveClass("text-alert");
    expect(screen.getByText("Rechazado")).toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent("Inconsistencia en el paz y salvo de impuestos.");
    expect(screen.queryByRole("button", { name: /subsanar/i })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Entendido" })).toBeInTheDocument();
  });

  it("rechazo visto por una parte que no es el solicitante: muestra la causal pero no ofrece corregir", async () => {
    montar(() => json({ ...RECHAZADO, soloLectura: true }));
    llenar();

    consultar();

    await screen.findByRole("heading", { name: "Tu trámite ha sido rechazado" });
    expect(screen.queryByRole("button", { name: /Causal de rechazo/ })).not.toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent("Inconsistencia en el paz y salvo de impuestos.");
    expect(screen.getByRole("button", { name: "Entendido" })).toBeInTheDocument();
  });

  it("un paso omitido (tipo sin validación de identidad) se ve como omitido", async () => {
    const pasos = respuesta().pasos.map((p) => (p.clave === "validacion_identidad" ? { ...p, estado: "skipped" as const, fecha: null } : p));
    montar(() => json(respuesta({ pasos })));
    llenar();

    consultar();

    await screen.findByRole("heading", { name: "Tu trámite está en proceso" });
    const omitido = screen.getByText("Validación de identidad").closest("li")!;
    expect(omitido).toHaveAttribute("data-status", "skipped");
    expect(omitido).toHaveTextContent("Omitido");
  });

  it("radicado inexistente y otra clave dan el mismo mensaje genérico, con role=alert", async () => {
    const problema = () => json({ type: "about:blank", title: "No encontrado", status: 404, code: "solicitud_no_encontrada" }, 404);
    montar(problema);
    llenar({ clave: "no-existe" });
    consultar();
    const primera = await screen.findByRole("alert");
    const texto = primera.textContent;

    fireEvent.change(screen.getByLabelText("Número de Radicado o Cédula"), { target: { value: "999999" } });
    consultar();
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("No encontramos un trámite con esos datos."));

    expect(texto).toBe("No encontramos un trámite con esos datos.");
    expect(screen.getByRole("alert").textContent).toBe(texto);
  });

  it("429: muestra el mensaje de espera, deshabilita Consultar y avisa los segundos", async () => {
    montar(() => new Response(JSON.stringify({ code: "reanudar_bloqueado", title: "Demasiados intentos", status: 429 }), { status: 429, headers: { "retry-after": "120", "content-type": "application/problem+json" } }));
    llenar();

    consultar();

    expect(await screen.findByRole("alert")).toHaveTextContent("Demasiados intentos. Vuelve a intentarlo en unos minutos.");
    expect(await screen.findByText(/Podrás consultar de nuevo en \d+ s/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Consultar" })).toBeDisabled();
  });

  it("error de servidor: mensaje claro y el ciudadano puede intentarlo de nuevo", async () => {
    let falla = true;
    const { fetchImpl } = montar(() => (falla ? json({ code: "x" }, 503) : json(respuesta())));
    llenar();
    consultar();
    expect(await screen.findByRole("alert")).toHaveTextContent("El servicio no está disponible en este momento");

    falla = false;
    consultar();

    await screen.findByRole("heading", { name: "Tu trámite está en proceso" });
    expect(fetchImpl).toHaveBeenCalledTimes(2);
  });

  it("cambiar un campo oculta el resultado anterior", async () => {
    montar(() => json(respuesta()));
    llenar();
    consultar();
    await screen.findByRole("heading", { name: "Tu trámite está en proceso" });

    fireEvent.change(screen.getByLabelText("Número de Radicado o Cédula"), { target: { value: "otro" } });

    expect(screen.queryByRole("heading", { name: "Tu trámite está en proceso" })).not.toBeInTheDocument();
  });
});

describe("formato de fechas", () => {
  it("usa la hora de Colombia (UTC-5) y el formato dd/mm/aaaa hh:mm", () => {
    expect(formatearFechaHora("2026-10-20T14:15:00Z")).toBe("20/10/2026 09:15");
    expect(formatearFechaHora("2026-01-01T03:00:00+00:00")).toBe("31/12/2025 22:00");
    expect(formatearFechaHora(null)).toBeNull();
    expect(formatearFechaHora("no es fecha")).toBeNull();
  });
});
