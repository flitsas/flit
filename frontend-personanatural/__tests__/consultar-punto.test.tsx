import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ConsultarModal } from "@/components/modales/ConsultarModal";
import { crearCliente, type EstadoRadicado } from "@/lib/api/cliente";
import { json, renderConQuery, TIPOS } from "./utilidades";

const ESTADO: EstadoRadicado = {
  radicado: "PN-2026-0001",
  numeroReferencia: null,
  placa: "ABC123",
  tipoTramite: "TIPO_A",
  organismo: "Ciudad Uno",
  macroEstado: { codigo: "en_revision_ot", etiqueta: "En Revisión OT", indice: 1 },
  mensaje: "Tu trámite está en curso.",
  pasos: [
    { clave: "borrador", titulo: "Borrador del trámite", estado: "done", fecha: "2026-10-20T14:15:00Z" },
    { clave: "validacion_identidad", titulo: "Validación de identidad", estado: "done", fecha: "2026-10-20T16:42:00Z" },
    { clave: "enviado_ot", titulo: "Enviado al OT", estado: "done", fecha: "2026-10-21T13:03:00Z" },
    { clave: "en_revision", titulo: "En revisión", estado: "current", fecha: "2026-10-22T19:26:00Z" },
    { clave: "resolucion", titulo: "Aprobado / Rechazado", estado: "pending", fecha: null },
  ],
  subsanacion: null,
  soloLectura: false,
};
// Sin pago ni envío: «Enviado al OT» sigue pendiente.
const PASOS_SIN_ENVIAR = ESTADO.pasos.map((p, i) => ({ ...p, estado: i === 0 ? ("done" as const) : i === 1 ? ("current" as const) : ("pending" as const), fecha: i < 2 ? p.fecha : null }));
const problema = (status: number, code: string) => json({ type: "about:blank", title: "x", status, code }, status);

function montar(manejador: (url: string) => Response, consultaPunto: boolean) {
  const fetchImpl = vi.fn(async (url: string) => manejador(String(url))) as unknown as typeof fetch;
  renderConQuery(<ConsultarModal abierto onCerrar={vi.fn()} cliente={crearCliente({ fetchImpl })} tipos={TIPOS} consultaPunto={consultaPunto} />);
  return fetchImpl as unknown as ReturnType<typeof vi.fn>;
}
const escribir = (placa: string, clave: string) => {
  if (placa) fireEvent.change(screen.getByLabelText("Placa"), { target: { value: placa } });
  fireEvent.change(screen.getByLabelText("Número de Radicado o Cédula"), { target: { value: clave } });
};
const consultar = () => fireEvent.click(screen.getByRole("button", { name: "Consultar" }));
const urls = (f: ReturnType<typeof vi.fn>) => f.mock.calls.map((c) => String(c[0]));

describe("modal Consultar en el punto: fallback sin pago", () => {
  it.each([404, 409])("si el cierre responde %i y hay placa válida, consulta con placa", async (status) => {
    const f = montar((url) => (url.includes("/punto/pago/cierre/") ? problema(status, "pago_pendiente") : json(ESTADO)), true);
    escribir("abc123", "PN-2026-0001");
    consultar();
    await waitFor(() => expect(urls(f)).toContain("/api/v1/persona-natural/radicados/consulta"));
    expect(urls(f)[0]).toContain("/punto/pago/cierre/PN-2026-0001");
  });

  it("si el cierre responde 404 y no hay placa, pide la placa y no consulta radicados", async () => {
    const f = montar(() => problema(404, "solicitud_no_encontrada"), true);
    escribir("", "PN-2026-0001");
    consultar();
    expect(await screen.findByRole("alert")).toHaveTextContent("necesitas ingresar la placa");
    expect(urls(f).some((u) => u.includes("/radicados/consulta"))).toBe(false);
  });
});

describe("modal Consultar web (sin consultaPunto): regresión", () => {
  it("exige la placa, llama /radicados/consulta y nunca /punto/pago/cierre", async () => {
    const f = montar(() => json(ESTADO), false);
    expect(screen.getByLabelText("Placa")).toBeRequired();
    escribir("", "PN-2026-0001");
    expect(screen.getByRole("button", { name: "Consultar" })).toBeDisabled();
    escribir("abc123", "PN-2026-0001");
    consultar();
    await waitFor(() => expect(f).toHaveBeenCalled());
    expect(urls(f)).toEqual(["/api/v1/persona-natural/radicados/consulta"]);
  });
});

describe("modal Consultar en el punto: retomar el pago", () => {
  const SIN_ENVIAR: EstadoRadicado = { ...ESTADO, macroEstado: { codigo: "pendiente_validacion", etiqueta: "Pendiente", indice: 0 }, pasos: PASOS_SIN_ENVIAR };
  const REANUDAR = "/api/v1/persona-natural/sesiones/reanudar";
  const SESION = { solicitudId: "s-9", codigoPublico: "PN-2026-0001", estado: "pendiente_pago", tipoTramiteCodigo: "TIPO_A", expiraEn: "2026-11-13T20:00:00Z" };

  function montarRetomar(reanudar: () => Response, estado: EstadoRadicado = SIN_ENVIAR) {
    const onRetomarPago = vi.fn();
    const onCerrar = vi.fn();
    const fetchImpl = vi.fn(async (url: string) => {
      const u = String(url);
      if (u.includes("/punto/pago/cierre/")) return problema(404, "solicitud_no_encontrada");
      if (u === REANUDAR) return reanudar();
      return json(estado);
    }) as unknown as typeof fetch;
    renderConQuery(
      <ConsultarModal abierto onCerrar={onCerrar} cliente={crearCliente({ fetchImpl })} tipos={TIPOS} consultaPunto onRetomarPago={onRetomarPago} />,
    );
    escribir("abc123", "PN-2026-0001");
    consultar();
    return { onRetomarPago, onCerrar, fetchImpl: fetchImpl as unknown as ReturnType<typeof vi.fn> };
  }
  const pedirRetomar = async (cedula = "1020456789") => {
    fireEvent.click(await screen.findByRole("button", { name: "Retomar el pago" }));
    fireEvent.change(screen.getByLabelText("Cédula de quien radicó"), { target: { value: cedula } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
  };

  it("con el trámite sin enviar ofrece «Retomar el pago»; con pendiente_pago abre el paso Pago con la solicitud reanudada", async () => {
    const { onRetomarPago, fetchImpl } = montarRetomar(() => json(SESION));
    await pedirRetomar();
    await waitFor(() =>
      expect(onRetomarPago).toHaveBeenCalledWith({ solicitudId: "s-9", tipoCodigo: "TIPO_A", placa: "ABC123", documento: "1020456789" }),
    );
    const llamada = fetchImpl.mock.calls.find((c) => String(c[0]) === REANUDAR);
    expect(JSON.parse(String((llamada?.[1] as RequestInit).body))).toEqual({ radicado: "PN-2026-0001", cedula: "1020456789" });
  });

  it("enviar con Enter (clic sintético en 0,0 del botón) no cierra el modal y sí reanuda", async () => {
    const { onRetomarPago, onCerrar, fetchImpl } = montarRetomar(() => json(SESION));
    // El modal ocupa una caja lejos del origen: un clic en 0,0 cae «fuera» de ella (así lo hace el navegador al enviar con Enter).
    const dialogo = document.querySelector("dialog") as HTMLDialogElement;
    dialogo.getBoundingClientRect = () => ({ left: 100, top: 80, right: 700, bottom: 600, width: 600, height: 520, x: 100, y: 80, toJSON: () => ({}) });
    fireEvent.click(await screen.findByRole("button", { name: "Retomar el pago" }), { clientX: 300, clientY: 300 });
    fireEvent.change(screen.getByLabelText("Cédula de quien radicó"), { target: { value: "1020456789" } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }), { clientX: 0, clientY: 0, detail: 0 });
    await waitFor(() =>
      expect(onRetomarPago).toHaveBeenCalledWith({ solicitudId: "s-9", tipoCodigo: "TIPO_A", placa: "ABC123", documento: "1020456789" }),
    );
    expect(fetchImpl.mock.calls.some((c) => String(c[0]) === REANUDAR)).toBe(true);
    expect(onCerrar).not.toHaveBeenCalled();
  });

  it("no ofrece retomar si el trámite ya está en revisión del OT", async () => {
    montarRetomar(() => json(SESION), ESTADO);
    await screen.findByRole("region", { name: "Resultado de la consulta" });
    expect(screen.queryByRole("button", { name: "Retomar el pago" })).toBeNull();
  });

  it("404: «No encontramos un trámite con esos datos.»", async () => {
    const { onRetomarPago } = montarRetomar(() => problema(404, "solicitud_no_encontrada"));
    await pedirRetomar();
    expect(await screen.findByRole("alert")).toHaveTextContent("No encontramos un trámite con esos datos.");
    expect(onRetomarPago).not.toHaveBeenCalled();
  });

  it("429: informa la espera y bloquea el botón", async () => {
    const { onRetomarPago } = montarRetomar(
      () => new Response(JSON.stringify({ type: "about:blank", title: "x", status: 429, code: "rate_limited" }), { status: 429, headers: { "content-type": "application/json", "Retry-After": "30" } }),
    );
    await pedirRetomar();
    expect(await screen.findByText(/Podrás intentarlo de nuevo en \d+ s/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Continuar" })).toBeDisabled();
    expect(onRetomarPago).not.toHaveBeenCalled();
  });

  it("si el estado reanudado no es pendiente_pago solo informa que sigue su curso", async () => {
    const { onRetomarPago } = montarRetomar(() => json({ ...SESION, estado: "enviada" }));
    await pedirRetomar();
    expect(await screen.findByText(/sigue su curso/)).toBeInTheDocument();
    expect(onRetomarPago).not.toHaveBeenCalled();
  });
});

describe("modal Consultar en el punto: resultado por estado (diseño de referencia)", () => {
  const CIERRE = (extra: Record<string, unknown> = {}) => ({
    estadoCierre: "registrado",
    codigoPublico: "PN-2026-0001",
    radicadoFlit2: "TR-2026-000123",
    valorCop: 366000,
    medio: "caja",
    medioCaja: "efectivo",
    pagadoEn: "2026-10-27T15:00:00+00:00",
    recogeEn: { organismo: "Tránsito de Prueba" },
    tipoTramite: "Traspaso",
    placa: "ABC123",
    ...extra,
  });
  const APROBADO: EstadoRadicado = {
    ...ESTADO,
    macroEstado: { codigo: "finalizado", etiqueta: "Finalizado", indice: 3 },
    mensaje: "Tu trámite ha sido aprobado satisfactoriamente por el Organismo de Tránsito.",
    pasos: ESTADO.pasos.map((p) => ({ ...p, estado: "done" as const, fecha: p.fecha ?? "2026-10-24T15:51:00Z" })),
  };
  const estados = (el: HTMLElement) => within(el).getAllByRole("listitem").map((i) => i.getAttribute("data-status"));
  const dato = (k: string) => screen.getByText(k).nextSibling;

  function montarCierre(cierre: Record<string, unknown>, consultarRadicado: () => Response = () => problema(404, "no_simulado")) {
    return montar((url) => (url.includes("/punto/pago/cierre/") ? json(cierre) : consultarRadicado()), true);
  }

  it("en registro: «en proceso» en warning, Enviado al OT actual, Tipo «Traspaso» (no «—») y mensaje de Lovable", async () => {
    montarCierre(CIERRE({ estadoCierre: "en_registro", radicadoFlit2: undefined }));
    escribir("", "PN-2026-0001");
    consultar();

    const titular = await screen.findByRole("heading", { name: "Tu trámite está en proceso" });
    expect(titular).toHaveClass("text-warning");
    expect(estados(screen.getByRole("list", { name: "Tracking del radicado" }))).toEqual(["done", "done", "current", "pending", "pending"]);
    expect(dato("ID del trámite")).toHaveTextContent("PN-2026-0001");
    expect(dato("Placa asociada")).toHaveTextContent("ABC123");
    expect(dato("Tipo de trámite")).toHaveTextContent("Traspaso");
    expect(screen.getByText(/aún no presenta respuesta por parte del Organismo de Tránsito/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Entendido" })).toBeInTheDocument();
  });

  it("registrado con seguimiento real aprobado: título aprobado, pasos completos y comprobante descargable", async () => {
    montarCierre(CIERRE(), () => json(APROBADO));
    escribir("", "PN-2026-0001");
    consultar();

    const titular = await screen.findByRole("heading", { name: "Tu trámite ha sido aprobado" });
    expect(titular).toHaveClass("text-success");
    expect(estados(screen.getByRole("list", { name: "Tracking del radicado" })).every((e) => e === "done")).toBe(true);
    expect(dato("Tipo de trámite")).toHaveTextContent("Traspaso");
    expect(screen.getByRole("button", { name: "Descargar comprobante" })).toBeEnabled();
  });

  it("registrado con seguimiento real en revisión: título «en proceso»", async () => {
    montarCierre(CIERRE(), () => json(ESTADO));
    escribir("", "PN-2026-0001");
    consultar();

    expect(await screen.findByRole("heading", { name: "Tu trámite está en proceso" })).toHaveClass("text-warning");
    expect(estados(screen.getByRole("list", { name: "Tracking del radicado" }))).toEqual(["done", "done", "done", "current", "pending"]);
  });

  it("registrado cuyo seguimiento real no responde: muestra lo del cierre (en proceso) con el Tipo del cierre", async () => {
    montarCierre(CIERRE());
    escribir("", "PN-2026-0001");
    consultar();

    expect(await screen.findByRole("heading", { name: "Tu trámite está en proceso" })).toBeInTheDocument();
    expect(dato("Tipo de trámite")).toHaveTextContent("Traspaso");
  });

  it("rechazado al registrar: título en alert, Enviado al OT rechazado y causal real", async () => {
    montarCierre(CIERRE({ estadoCierre: "rechazado", radicadoFlit2: undefined, motivo: "La placa ya tiene un trámite en curso." }));
    escribir("", "PN-2026-0001");
    consultar();

    const titular = await screen.findByRole("heading", { name: "Tu trámite ha sido rechazado" });
    expect(titular).toHaveClass("text-alert");
    expect(estados(screen.getByRole("list", { name: "Tracking del radicado" }))).toEqual(["done", "done", "rejected", "pending", "pending"]);
    expect(screen.getByRole("note")).toHaveTextContent("Causal de rechazo: La placa ya tiene un trámite en curso.");
  });

  it("«Retomar el pago» usa el estilo del botón principal (flit) en un trámite pendiente de pago", async () => {
    const SIN_ENVIAR: EstadoRadicado = { ...ESTADO, macroEstado: { codigo: "pendiente_validacion", etiqueta: "Pendiente", indice: 0 }, pasos: PASOS_SIN_ENVIAR };
    const fetchImpl = vi.fn(async (url: string) => (String(url).includes("/punto/pago/cierre/") ? problema(404, "solicitud_no_encontrada") : json(SIN_ENVIAR))) as unknown as typeof fetch;
    renderConQuery(<ConsultarModal abierto onCerrar={vi.fn()} cliente={crearCliente({ fetchImpl })} tipos={TIPOS} consultaPunto onRetomarPago={vi.fn()} />);
    escribir("abc123", "PN-2026-0001");
    consultar();

    expect(await screen.findByRole("heading", { name: "Tu trámite está en proceso" })).toHaveClass("text-warning");
    expect(await screen.findByRole("button", { name: "Retomar el pago" })).toHaveClass("bg-primary");
  });
});

describe("modal Consultar en el punto: mensaje, retomar y disposición de las acciones", () => {
  const VALIDANDO = "Estamos validando la información y la identidad de las partes antes de enviar el trámite al Organismo de Tránsito.";
  const EN_CURSO = "Tu trámite está en curso y aún no presenta respuesta por parte del Organismo de Tránsito. Te notificaremos a tu correo tan pronto sea procesado.";
  const MACRO_PENDIENTE = { codigo: "pendiente_validacion", etiqueta: "Pendiente de Validación", indice: 0 };
  // Pagado y radicado, pero sin espejo de FLIT 2: el macro-estado sigue «pendiente_validacion» y «Enviado al OT» está en curso.
  const PAGADO_RADICADO: EstadoRadicado = {
    ...ESTADO,
    macroEstado: MACRO_PENDIENTE,
    mensaje: VALIDANDO,
    pasos: [
      { clave: "borrador", titulo: "Borrador del trámite", estado: "done", fecha: "2026-10-20T14:15:00Z" },
      { clave: "validacion_identidad", titulo: "Validación de identidad", estado: "done", fecha: "2026-10-20T16:42:00Z" },
      { clave: "enviado_ot", titulo: "Enviado al OT", estado: "current", fecha: "2026-10-21T13:03:00Z" },
      { clave: "en_revision", titulo: "En revisión", estado: "pending", fecha: null },
      { clave: "resolucion", titulo: "Aprobado / Rechazado", estado: "pending", fecha: null },
    ],
  };
  const SIN_PAGAR: EstadoRadicado = {
    ...PAGADO_RADICADO,
    pasos: PAGADO_RADICADO.pasos.map((p, i) => ({ ...p, estado: i < 1 ? ("done" as const) : i === 1 ? ("current" as const) : ("pending" as const), fecha: i < 2 ? p.fecha : null })),
  };

  function montarEstado(estado: EstadoRadicado, onRetomarPago: (() => void) | undefined = vi.fn()) {
    const fetchImpl = vi.fn(async (url: string) => (String(url).includes("/punto/pago/cierre/") ? problema(404, "solicitud_no_encontrada") : json(estado))) as unknown as typeof fetch;
    renderConQuery(<ConsultarModal abierto onCerrar={vi.fn()} cliente={crearCliente({ fetchImpl })} tipos={TIPOS} consultaPunto onRetomarPago={onRetomarPago} />);
    escribir("abc123", "PN-2026-0001");
    consultar();
  }

  it("pagado y radicado: no ofrece «Retomar el pago» y usa el texto de «en curso», no «Estamos validando…»", async () => {
    montarEstado(PAGADO_RADICADO);

    await screen.findByRole("heading", { name: "Tu trámite está en proceso" });
    expect(screen.getByText(EN_CURSO)).toBeInTheDocument();
    expect(screen.queryByText(VALIDANDO)).toBeNull();
    expect(screen.queryByRole("button", { name: "Retomar el pago" })).toBeNull();
    // Solo «Entendido»: sola, a la derecha y como principal.
    const fila = screen.getByTestId("acciones-consulta");
    expect(within(fila).getAllByRole("button").map((b) => b.textContent)).toEqual(["Entendido"]);
    expect(fila).toHaveClass("sm:justify-end");
    expect(within(fila).getByRole("button", { name: "Entendido" })).toHaveClass("bg-primary");
  });

  it("sin pagar (identidad en curso): conserva «Estamos validando…» y ofrece retomar; fila con Entendido (back) y Retomar (flit) a la derecha", async () => {
    montarEstado(SIN_PAGAR);

    await screen.findByRole("heading", { name: "Tu trámite está en proceso" });
    expect(screen.getByText(VALIDANDO)).toBeInTheDocument();
    expect(screen.queryByText(EN_CURSO)).toBeNull();
    const fila = screen.getByTestId("acciones-consulta");
    const botones = within(fila).getAllByRole("button");
    expect(botones.map((b) => b.textContent)).toEqual(["Entendido", "Retomar el pago"]);
    expect(botones[0]).toHaveClass("bg-secondary");
    expect(botones[1]).toHaveClass("bg-primary");
    expect(botones[0]).toHaveClass("h-11");
    expect(botones[1]).toHaveClass("h-11");
    expect(fila).toHaveClass("sm:justify-end", "sm:flex-row", "flex-col-reverse");
  });

  it("la nota de «solo lectura» va encima de la fila de acciones", async () => {
    montarEstado({ ...SIN_PAGAR, soloLectura: true });

    const nota = await screen.findByText("Consultas este trámite como parte: solo puedes verlo, no modificarlo.");
    const fila = screen.getByTestId("acciones-consulta");
    expect(nota).toHaveClass("text-xs");
    expect(nota.compareDocumentPosition(fila) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
