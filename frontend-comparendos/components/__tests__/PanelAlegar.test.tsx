import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { PanelAlegar } from "@/components/detalle/PanelAlegar";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
import type { DecisionDp } from "@/lib/decisionDp";

const apiFetch = vi.fn();
vi.mock("@/lib/api", () => ({ apiFetch: (...a: unknown[]) => apiFetch(...a) }));

const ctx = {
  detalle: { id: "c1", infraccion: { codigo: "C35", descripcion: "No portar el SOAT." } },
  recargar: () => {},
  sesion: null,
  soloLectura: false,
} as unknown as ContextoDetalle;

const base: DecisionDp = { elegible: true, elegida: "DPR", tipologiaIdElegida: "t1", motivo: null, aplicables: [] };

function responde(d: Partial<DecisionDp>) {
  apiFetch.mockResolvedValue({ ...base, ...d });
}

describe("PanelAlegar (HU #14613)", () => {
  beforeEach(() => apiFetch.mockReset());
  afterEach(cleanup);

  it("lista lo aplicable con su razón, marca la elegida y muestra la infracción con descripción", async () => {
    responde({
      aplicables: [
        { codigo: "DPR", nombre: "Prescripción", tipologiaId: "t1", razon: "Pasaron 3 años o más desde la notificación (2020-01-06).", disponible: true },
        { codigo: "DRP", nombre: "Revocatoria C-321", tipologiaId: null, razon: "C35 impuesta el 2021-05-04, dentro del periodo.", disponible: false },
      ],
    });
    render(<PanelAlegar ctx={ctx} />);
    await screen.findByText("Prescripción");
    expect(apiFetch.mock.calls[0][0]).toBe("/comparendos/c1/dp/decision");
    expect(screen.getByText("La que conviene")).toBeTruthy();
    expect(screen.getByText("No habilitada en tu empresa")).toBeTruthy();
    expect(screen.getByText(/06 de ene de 2020/)).toBeTruthy();
    expect(screen.getByText(/C35 \(No portar el SOAT\) impuesta/)).toBeTruthy();
    expect(screen.getByText("Infracción: C35 — No portar el SOAT.")).toBeTruthy();
    expect(screen.getByText(/Conviene alegar «Prescripción»/)).toBeTruthy();
  });

  it("sin aplicables dice que todavía no aplica ningún argumento", async () => {
    responde({ elegible: false, elegida: null, motivo: "SinTipologiaAplicable" });
    render(<PanelAlegar ctx={ctx} />);
    await waitFor(() => expect(screen.getAllByText(/Todavía no aplica ningún argumento/).length).toBeGreaterThan(0));
    expect(document.body.textContent).not.toContain("SinTipologiaAplicable");
  });

  it("si no es elegible explica el motivo sin el nombre del enum", async () => {
    responde({ elegible: false, elegida: null, motivo: "VentanaDescuento" });
    render(<PanelAlegar ctx={ctx} />);
    await screen.findByText("Esperar: se puede trasladar al conductor con descuento");
    expect(document.body.textContent).not.toContain("VentanaDescuento");
  });
});
