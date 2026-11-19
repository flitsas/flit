import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { FiltrosDashboard } from "@/components/estadisticas/FiltrosDashboard";

// Uso de ejemplo: <FiltrosDashboard valor={{ desde, hasta }} hoy="2026-11-15" onAplicar={(r) => …} errorServidor={null} />.
// Un rango válido se aplica al cambiar (sin botón); uno inválido no se envía y explica el límite junto a «Hasta».

const HOY = "2026-11-15";
const base = { desde: "2026-10-17", hasta: HOY };

describe("FiltrosDashboard — AC filtros de fecha", () => {
  it("tiene «Desde» y «Hasta» con label visible y min/max cruzados (hasta ≤ hoy)", () => {
    render(<FiltrosDashboard valor={base} hoy={HOY} onAplicar={() => {}} />);
    const desde = screen.getByLabelText("Desde");
    const hasta = screen.getByLabelText("Hasta");
    expect(desde).toHaveAttribute("type", "date");
    expect(desde).toHaveAttribute("max", HOY);
    expect(hasta).toHaveAttribute("min", base.desde);
    expect(hasta).toHaveAttribute("max", HOY);
  });

  it("aplica un rango válido al cambiar una fecha", () => {
    const onAplicar = vi.fn();
    render(<FiltrosDashboard valor={base} hoy={HOY} onAplicar={onAplicar} />);
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2026-11-01" } });
    expect(onAplicar).toHaveBeenCalledWith({ desde: "2026-11-01", hasta: HOY });
  });

  it("con más de 366 días no aplica y muestra el límite con aria-invalid en «Hasta»", () => {
    const onAplicar = vi.fn();
    render(<FiltrosDashboard valor={base} hoy={HOY} onAplicar={onAplicar} />);
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2025-11-01" } });
    expect(onAplicar).not.toHaveBeenCalled();
    const hasta = screen.getByLabelText("Hasta");
    expect(hasta).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText("El rango no puede superar un año")).toHaveAttribute("id", hasta.getAttribute("aria-describedby"));
  });

  it("con «Hasta» en el futuro no aplica y lo explica", () => {
    const onAplicar = vi.fn();
    render(<FiltrosDashboard valor={base} hoy={HOY} onAplicar={onAplicar} />);
    fireEvent.change(screen.getByLabelText("Hasta"), { target: { value: "2026-11-20" } });
    expect(onAplicar).not.toHaveBeenCalled();
    expect(screen.getByText("La fecha final no puede ser posterior a hoy")).toBeInTheDocument();
  });

  it("muestra el error del servidor (400 RANGO_INVALIDO) junto a «Hasta»", () => {
    render(<FiltrosDashboard valor={base} hoy={HOY} onAplicar={() => {}} errorServidor="El rango no puede superar un año" />);
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText("El rango no puede superar un año")).toBeInTheDocument();
  });
});
