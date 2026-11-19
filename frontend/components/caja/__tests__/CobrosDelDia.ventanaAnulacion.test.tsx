// Ventana de anulación configurable: etiqueta «Anulable hasta …» y «Anular» oculto cuando el instante pasa.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import type { CajaCobro, OtCajaApi } from "@/lib/api/ot-caja";
import { formatHoraColombia } from "@/lib/format/date";
import { CobrosDelDia } from "../CobrosDelDia";

const base: CajaCobro = {
  cobroId: "c1",
  codigo: "PN-2026-000001",
  valorCop: 245000,
  medio: "efectivo",
  pagadoEn: "2026-11-10T15:30:00Z",
  pagadoPorNombre: "Ana Caja",
  estado: "pagado",
  anulable: true,
  rowVersion: 1,
};

function api(items: CajaCobro[]): OtCajaApi {
  return {
    tramite: vi.fn(),
    confirmar: vi.fn(),
    cobros: vi.fn().mockResolvedValue({ items, total: items.length, totalDiaCop: 245000 }),
    anular: vi.fn(),
  };
}

const accionesDe = (codigo: string) => screen.queryByRole("button", { name: `Acciones del cobro ${codigo}` });

describe("CobrosDelDia · ventana de anulación", () => {
  it("muestra «Anulable hasta 9:41 p. m.» (hora de Colombia) para un cobro con ventana abierta", async () => {
    const hasta = new Date(Date.now() + 10 * 60_000);
    render(<CobrosDelDia api={api([{ ...base, anulableHasta: hasta.toISOString() }])} puedeAnular />);
    const etiqueta = await screen.findByTestId("anulable-hasta");
    expect(etiqueta).toHaveTextContent(`Anulable hasta ${formatHoraColombia(hasta)}`);
    expect(etiqueta.textContent).toMatch(/Anulable hasta \d{1,2}:\d{2} [ap]\. m\./);
    expect(accionesDe("PN-2026-000001")).toBeInTheDocument();
  });

  it("la persona de caja (no anula) no ve la etiqueta", async () => {
    const hasta = new Date(Date.now() + 10 * 60_000).toISOString();
    render(<CobrosDelDia api={api([{ ...base, anulableHasta: hasta }])} puedeAnular={false} />);
    await screen.findByText("PN-2026-000001");
    expect(screen.queryByTestId("anulable-hasta")).not.toBeInTheDocument();
  });

  it("sin ventana (anulableHasta null) o ya vencida no hay «Anular» ni etiqueta", async () => {
    const vencido = new Date(Date.now() - 60_000).toISOString();
    render(
      <CobrosDelDia
        api={api([
          { ...base, anulableHasta: null },
          { ...base, cobroId: "c2", codigo: "PN-2026-000002", anulableHasta: vencido },
        ])}
        puedeAnular
      />,
    );
    await screen.findByText("PN-2026-000002");
    expect(accionesDe("PN-2026-000001")).not.toBeInTheDocument();
    expect(accionesDe("PN-2026-000002")).not.toBeInTheDocument();
    expect(screen.queryByTestId("anulable-hasta")).not.toBeInTheDocument();
  });

  it("al vencer el instante, «Anular» y la etiqueta desaparecen sin recargar la lista", async () => {
    const a = api([{ ...base, anulableHasta: new Date(Date.now() + 1200).toISOString() }]);
    render(<CobrosDelDia api={a} puedeAnular />);
    expect(await screen.findByTestId("anulable-hasta")).toBeInTheDocument();
    expect(accionesDe("PN-2026-000001")).toBeInTheDocument();
    await waitFor(() => expect(accionesDe("PN-2026-000001")).not.toBeInTheDocument(), { timeout: 4000 });
    expect(screen.queryByTestId("anulable-hasta")).not.toBeInTheDocument();
    expect(a.cobros).toHaveBeenCalledTimes(1);
  });
});
