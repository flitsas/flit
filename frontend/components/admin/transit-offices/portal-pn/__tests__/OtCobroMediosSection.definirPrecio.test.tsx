// Mejora sobre la marcha — «Definir precio» en Medios de pago: sin monto se ofrece de una vez, y recargar relee los montos.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { CobroApi, CobroPortal } from "@/lib/api/admin-ot-portal-cobro";
import { OtCobroMediosSection } from "../OtCobroMediosSection";

function datos(tramite: number): CobroPortal {
  return {
    medios: [{ method: "PASARELA", enabled: true }],
    tipos: [
      {
        tipoTramiteId: "t1",
        nombre: "Traspaso",
        medios: [
          {
            method: "PASARELA",
            ofrecido: false,
            conceptos: [
              { charge: "TRAMITE", enabled: false, montoCop: tramite, editable: true },
              { charge: "GESTION", enabled: false, montoCop: 30000, editable: true },
            ],
          },
        ],
      },
    ],
  };
}

function api(...respuestas: CobroPortal[]): CobroApi {
  const obtener = vi.fn();
  respuestas.forEach((r) => obtener.mockResolvedValueOnce(r));
  obtener.mockResolvedValue(respuestas[respuestas.length - 1]);
  return { obtener, guardarMedio: vi.fn(), guardarSwitch: vi.fn() };
}

function montar(a: CobroApi, onDefinirPrecio?: (id: string) => void, recargar = 0) {
  const ui = (r: number) => (
    <ToastProvider>
      <OtCobroMediosSection api={a} onIrAPrecios={vi.fn()} onDefinirPrecio={onDefinirPrecio} recargar={r} />
    </ToastProvider>
  );
  const v = render(ui(recargar));
  return (r: number) => v.rerender(ui(r));
}

describe("OtCobroMediosSection · Definir precio", () => {
  it("un valor sin monto muestra «Definir precio» y avisa con el trámite", async () => {
    const definir = vi.fn();
    montar(api(datos(0)), definir);
    const boton = await screen.findByRole("button", { name: /Definir precio de valor del trámite, Pasarela de pago, Traspaso/ });
    await userEvent.click(boton);
    expect(definir).toHaveBeenCalledWith("t1");
    // La gestión sí tiene monto: no ofrece definirlo.
    expect(screen.queryByRole("button", { name: /Definir precio de valor de la gestión/ })).not.toBeInTheDocument();
  });

  it("sin `onDefinirPrecio` no aparece el botón", async () => {
    montar(api(datos(0)));
    await screen.findByRole("switch", { name: /Valor del trámite, Pasarela de pago/ });
    expect(screen.queryByRole("button", { name: /Definir precio/ })).not.toBeInTheDocument();
  });

  it("al cambiar `recargar` relee los montos y quita el botón", async () => {
    const a = api(datos(0), datos(245000));
    const rerender = montar(a, vi.fn());
    await screen.findByRole("button", { name: /Definir precio de valor del trámite/ });
    rerender(1);
    await waitFor(() => expect(a.obtener).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(screen.queryByRole("button", { name: /Definir precio de valor del trámite/ })).not.toBeInTheDocument(),
    );
    expect(screen.getByText(/245\.000/)).toBeInTheDocument();
  });
});
