import { fireEvent, screen, waitFor } from "@testing-library/react";
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
  const SIN_ENVIAR: EstadoRadicado = { ...ESTADO, macroEstado: { codigo: "pendiente_validacion", etiqueta: "Pendiente", indice: 0 } };
  const REANUDAR = "/api/v1/persona-natural/sesiones/reanudar";
  const SESION = { solicitudId: "s-9", codigoPublico: "PN-2026-0001", estado: "pendiente_pago", tipoTramiteCodigo: "TIPO_A", expiraEn: "2026-11-13T20:00:00Z" };

  function montarRetomar(reanudar: () => Response, estado: EstadoRadicado = SIN_ENVIAR) {
    const onRetomarPago = vi.fn();
    const fetchImpl = vi.fn(async (url: string) => {
      const u = String(url);
      if (u.includes("/punto/pago/cierre/")) return problema(404, "solicitud_no_encontrada");
      if (u === REANUDAR) return reanudar();
      return json(estado);
    }) as unknown as typeof fetch;
    renderConQuery(
      <ConsultarModal abierto onCerrar={vi.fn()} cliente={crearCliente({ fetchImpl })} tipos={TIPOS} consultaPunto onRetomarPago={onRetomarPago} />,
    );
    escribir("abc123", "PN-2026-0001");
    consultar();
    return { onRetomarPago, fetchImpl: fetchImpl as unknown as ReturnType<typeof vi.fn> };
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
