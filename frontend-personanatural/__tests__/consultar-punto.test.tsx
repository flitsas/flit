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
