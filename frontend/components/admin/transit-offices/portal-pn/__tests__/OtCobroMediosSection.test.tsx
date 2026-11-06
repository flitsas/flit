// HU #14394 (Feature #14392) — medios de pago y switches del portal. La API es un doble; el servidor decide `editable`.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import type { CobroApi, CobroPortal } from "@/lib/api/admin-ot-portal-cobro";
import {
  MOTIVO_GESTION_SOLO_SUPERADMIN,
  MOTIVO_SIN_MONTO,
  OtCobroMediosSection,
} from "../OtCobroMediosSection";

const TIPO = "t1";

function datos(over?: {
  tramite?: number;
  gestion?: number;
  tramiteOn?: boolean;
  gestionOn?: boolean;
}): CobroPortal {
  const o = { tramite: 245000, gestion: 30000, tramiteOn: true, gestionOn: true, ...over };
  const medio = (method: "PASARELA" | "PIN") => ({
    method,
    ofrecido: o.tramiteOn || o.gestionOn,
    conceptos: [
      { charge: "TRAMITE" as const, enabled: o.tramiteOn, montoCop: o.tramite, editable: true },
      { charge: "GESTION" as const, enabled: o.gestionOn, montoCop: o.gestion, editable: false },
    ],
  });
  return {
    medios: [
      { method: "PASARELA", enabled: true },
      { method: "PIN", enabled: true },
    ],
    tipos: [{ tipoTramiteId: TIPO, nombre: "Traspaso", medios: [medio("PASARELA"), medio("PIN")] }],
  };
}

function api(inicial: CobroPortal, extra?: Partial<CobroApi>): CobroApi {
  return {
    obtener: vi.fn().mockResolvedValue(inicial),
    guardarMedio: vi.fn().mockResolvedValue({}),
    guardarSwitch: vi.fn().mockResolvedValue({}),
    ...extra,
  };
}

function montar(a: CobroApi, conPin = true) {
  const irAPrecios = vi.fn();
  render(
    <ToastProvider>
      <OtCobroMediosSection api={a} conPin={conPin} onIrAPrecios={irAPrecios} />
    </ToastProvider>,
  );
  return irAPrecios;
}

const sw = (nombre: RegExp) => screen.findByRole("switch", { name: nombre });

describe("OtCobroMediosSection", () => {
  it("AC1: muestra las banderas de pasarela y PIN y los dos switches con su monto por medio", async () => {
    montar(api(datos()));
    expect(await sw(/^Pasarela de pago$/)).toBeChecked();
    expect(
      screen.getByRole("switch", { name: /Valor del trámite, Pasarela de pago, Traspaso/ }),
    ).toBeChecked();
    expect(screen.getAllByText("Se ofrece").length).toBeGreaterThan(0);
    await userEvent.click(screen.getByRole("tab", { name: /PIN en caja/ }));
    expect(await sw(/^PIN en caja$/)).toBeChecked();
    expect(
      screen.getByRole("switch", { name: /Valor de la gestión, PIN en caja, Traspaso/ }),
    ).toBeInTheDocument();
    expect(screen.getAllByText(/245\.000/).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Activado").length).toBeGreaterThan(0);
  });

  it("la web de persona no ofrece PIN", async () => {
    montar(api(datos()), false);
    await sw(/^Pasarela de pago$/);
    expect(screen.queryByRole("switch", { name: /PIN en caja/ })).not.toBeInTheDocument();
  });

  it("AC2: el switch de la gestión queda en solo lectura con su motivo", async () => {
    montar(api(datos()));
    const gestion = await sw(/Valor de la gestión, Pasarela de pago/);
    expect(gestion).toBeDisabled();
    const motivo = document.getElementById(gestion.getAttribute("aria-describedby") ?? "");
    expect(motivo).toHaveTextContent(MOTIVO_GESTION_SOLO_SUPERADMIN);
  });

  it("guarda el switch del trámite y recarga lo guardado", async () => {
    const a = api(datos());
    montar(a);
    await userEvent.click(await sw(/Valor del trámite, Pasarela de pago/));
    await waitFor(() =>
      expect(a.guardarSwitch).toHaveBeenCalledWith({
        tipoTramiteId: TIPO,
        method: "PASARELA",
        charge: "TRAMITE",
        enabled: false,
      }),
    );
    expect(a.obtener).toHaveBeenCalledTimes(2);
  });

  it("guarda la bandera del medio", async () => {
    const a = api(datos());
    montar(a);
    await userEvent.click(await screen.findByRole("tab", { name: /PIN en caja/ }));
    await userEvent.click(await sw(/^PIN en caja$/));
    await waitFor(() =>
      expect(a.guardarMedio).toHaveBeenCalledWith({ method: "PIN", enabled: false }),
    );
  });

  it("AC3: sin monto no enciende, muestra el motivo exacto y lleva a Precios", async () => {
    const a = api(datos({ tramite: 0, tramiteOn: false }));
    const irAPrecios = montar(a);
    await userEvent.click(await sw(/Valor del trámite, Pasarela de pago/));
    expect(await screen.findAllByText(new RegExp(MOTIVO_SIN_MONTO.slice(0, 30)))).not.toHaveLength(
      0,
    );
    expect(a.guardarSwitch).not.toHaveBeenCalled();
    await userEvent.click(screen.getAllByRole("button", { name: /Ir a Precios/ })[0]);
    expect(irAPrecios).toHaveBeenCalled();
  });

  it("AC4: con los dos switches apagados guarda y avisa que el medio no se ofrecerá", async () => {
    const a = api(datos({ gestionOn: false }), {
      obtener: vi
        .fn()
        .mockResolvedValueOnce(datos({ gestionOn: false }))
        .mockResolvedValue(datos({ gestionOn: false, tramiteOn: false })),
    });
    montar(a);
    await userEvent.click(await sw(/Valor del trámite, Pasarela de pago/));
    expect(
      (await screen.findAllByText(/Pasarela de pago no se ofrecerá para Traspaso/)).length,
    ).toBeGreaterThan(0);
    expect(a.guardarSwitch).toHaveBeenCalledTimes(1);
  });

  it("AC5: si falla, muestra el motivo exacto y conserva lo último guardado", async () => {
    const a = api(datos(), {
      guardarSwitch: vi
        .fn()
        .mockRejectedValue(
          new ApiError(422, "x", { code: "sin_monto", message: "El trámite no tiene monto." }),
        ),
    });
    montar(a);
    const s = await sw(/Valor del trámite, Pasarela de pago/);
    await userEvent.click(s);
    expect(await screen.findByText(/El trámite no tiene monto\./)).toBeInTheDocument();
    expect(s).toBeChecked();
    expect(a.obtener).toHaveBeenCalledTimes(1);
  });

  it("error de carga y vacío tienen su estado", async () => {
    const mal = api(datos(), { obtener: vi.fn().mockRejectedValue(new Error("x")) });
    montar(mal);
    expect(await screen.findByText(/No se pudieron cargar los medios de pago/)).toBeInTheDocument();
  });

  it("en Plataforma el acceso y el motivo nombran Tarifas, y el total suma solo lo que se cobra", async () => {
    const a = api(datos({ gestionOn: false }));
    render(
      <ToastProvider>
        <OtCobroMediosSection api={a} conPin={false} onIrAPrecios={vi.fn()} nombrePrecios="Tarifas" />
      </ToastProvider>,
    );
    await sw(/^Pasarela de pago$/);
    expect(screen.getByRole("button", { name: "Ir a Tarifas" })).toBeInTheDocument();
    expect(screen.getByText("No se cobra")).toBeInTheDocument();
    expect(screen.queryByRole("tab")).not.toBeInTheDocument();
  });

  it("sin trámites muestra el vacío", async () => {
    montar(api({ medios: [], tipos: [] }));
    expect(await screen.findByText(/Aún no hay trámites con cobro/)).toBeInTheDocument();
  });
});
