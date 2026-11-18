// Tiempo para anular un cobro de caja (0..max, 15 por defecto) en la fila «PIN en caja» de Cobro → Medios de pago.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import type { CajaAnulacion, CobroApi, CobroPortal } from "@/lib/api/admin-ot-portal-cobro";
import { OtCobroMediosSection } from "../OtCobroMediosSection";
import { CAJA_ANULACION_AYUDA, CAJA_ANULACION_CERO } from "../CajaAnulacionCampo";

function datos(ca?: CajaAnulacion): CobroPortal {
  const medio = (method: "PASARELA" | "PIN") => ({
    method,
    ofrecido: true,
    conceptos: [
      { charge: "TRAMITE" as const, enabled: true, montoCop: 245000, editable: true },
      { charge: "GESTION" as const, enabled: true, montoCop: 30000, editable: false },
    ],
  });
  return {
    medios: [
      { method: "PASARELA", enabled: true },
      { method: "PIN", enabled: true },
    ],
    tipos: [{ tipoTramiteId: "t1", nombre: "Traspaso", medios: [medio("PASARELA"), medio("PIN")] }],
    cajaAnulacion: ca,
  };
}

const POR_DEFECTO: CajaAnulacion = { minutos: 15, porDefecto: true, maxMinutos: 60 };

function api(inicial: CobroPortal, extra?: Partial<CobroApi>): CobroApi {
  return {
    obtener: vi.fn().mockResolvedValue(inicial),
    guardarMedio: vi.fn().mockResolvedValue({}),
    guardarSwitch: vi.fn().mockResolvedValue({}),
    guardarCajaAnulacion: vi.fn().mockResolvedValue({ minutos: 10, porDefecto: false, maxMinutos: 60 }),
    historialCajaAnulacion: vi.fn().mockResolvedValue([]),
    ...extra,
  };
}

function montar(a: CobroApi) {
  render(
    <ToastProvider>
      <OtCobroMediosSection api={a} onIrAPrecios={vi.fn()} />
    </ToastProvider>,
  );
}

const campo = () => screen.findByLabelText(/Tiempo para anular un cobro/);

describe("Tiempo para anular un cobro de caja", () => {
  it("muestra el valor actual, la ayuda y «Valor por defecto»", async () => {
    montar(api(datos(POR_DEFECTO)));
    expect(await campo()).toHaveValue(15);
    expect(screen.getByText(CAJA_ANULACION_AYUDA, { exact: false })).toBeInTheDocument();
    expect(screen.getByText(/Valor por defecto/)).toBeInTheDocument();
  });

  it("no muestra «Valor por defecto» cuando el organismo ya lo cambió", async () => {
    montar(api(datos({ minutos: 20, porDefecto: false, maxMinutos: 60 })));
    expect(await campo()).toHaveValue(20);
    expect(screen.queryByText(/Valor por defecto/)).not.toBeInTheDocument();
  });

  it("guarda el valor nuevo y lo refleja", async () => {
    const a = api(datos(POR_DEFECTO));
    montar(a);
    const input = await campo();
    await userEvent.clear(input);
    await userEvent.type(input, "10");
    await userEvent.click(screen.getByRole("button", { name: "Guardar tiempo para anular" }));
    await waitFor(() => expect(a.guardarCajaAnulacion).toHaveBeenCalledWith(10));
    await waitFor(() => expect(screen.queryByText(/Valor por defecto/)).not.toBeInTheDocument());
    expect(input).toHaveValue(10);
  });

  it.each(["61", "-1", "2.5", ""])("rechaza %j sin llamar al servidor", async (valor) => {
    const a = api(datos(POR_DEFECTO));
    montar(a);
    const input = await campo();
    await userEvent.clear(input);
    if (valor) await userEvent.type(input, valor);
    await userEvent.click(screen.getByRole("button", { name: "Guardar tiempo para anular" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("entre 0 y 60");
    expect(a.guardarCajaAnulacion).not.toHaveBeenCalled();
  });

  it("con 0 avisa que el trámite se envía al cobrar y el cobro no se anula", async () => {
    montar(api(datos(POR_DEFECTO)));
    const input = await campo();
    expect(screen.queryByText(CAJA_ANULACION_CERO)).not.toBeInTheDocument();
    await userEvent.clear(input);
    await userEvent.type(input, "0");
    expect(screen.getByText(CAJA_ANULACION_CERO)).toBeInTheDocument();
  });

  it("muestra el mensaje del servidor ante el 400 ventana_anulacion_invalida", async () => {
    const a = api(datos(POR_DEFECTO), {
      guardarCajaAnulacion: vi.fn().mockRejectedValue(
        new ApiError(400, "x", { code: "ventana_anulacion_invalida", message: "El máximo permitido es 45 minutos." }),
      ),
    });
    montar(a);
    await campo();
    await userEvent.click(screen.getByRole("button", { name: "Guardar tiempo para anular" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("El máximo permitido es 45 minutos.");
  });

  it("«Ver historial» abre un modal con la tabla de cambios", async () => {
    const a = api(datos(POR_DEFECTO), {
      historialCajaAnulacion: vi.fn().mockResolvedValue([
        { minutosAntes: 15, minutosDespues: 10, actorNombre: "Luis Admin", cambiadoEn: "2026-11-10T15:30:00Z" },
      ]),
    });
    montar(a);
    await campo();
    await userEvent.click(screen.getByRole("button", { name: /Ver historial/ }));
    const dlg = await screen.findByRole("dialog");
    expect(await within(dlg).findByText("Luis Admin")).toBeInTheDocument();
    expect(within(dlg).getByText("10/11/2026 10:30")).toBeInTheDocument();
    expect(within(dlg).getByLabelText(/Filas por página/i)).toBeInTheDocument();
  });

  it("historial vacío y con error", async () => {
    const a = api(datos(POR_DEFECTO), { historialCajaAnulacion: vi.fn().mockRejectedValue(new Error("x")) });
    montar(a);
    await campo();
    await userEvent.click(screen.getByRole("button", { name: /Ver historial/ }));
    expect(await screen.findByText("No se pudo cargar el historial.")).toBeInTheDocument();
  });

  it("sin los métodos de anulación (cajero, web de persona) el campo no se ofrece", async () => {
    const a = api(datos(POR_DEFECTO), { guardarCajaAnulacion: undefined, historialCajaAnulacion: undefined });
    montar(a);
    await screen.findByRole("switch", { name: /^PIN en caja$/ });
    expect(screen.queryByLabelText(/Tiempo para anular un cobro/)).not.toBeInTheDocument();
  });

  it("si el servidor no manda cajaAnulacion tampoco se ofrece", async () => {
    montar(api(datos(undefined)));
    await screen.findByRole("switch", { name: /^PIN en caja$/ });
    expect(screen.queryByLabelText(/Tiempo para anular un cobro/)).not.toBeInTheDocument();
  });
});
