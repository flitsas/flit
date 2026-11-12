import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";

import { RangoFechas } from "@/components/atom/RangoFechas";
import { sinRango, type DateRange } from "@/components/atom/modules/_reportes/range";

// Miércoles 11/11/2026, 8 p. m. en Bogotá (01:00 UTC del 12): con UTC el «hoy» sería el día siguiente.
const HOY = new Date(2026, 10, 11, 20, 0, 0);

function Controlado({
  inicial,
  onChange,
  permiteSinRango,
}: {
  inicial: DateRange;
  onChange: (next: DateRange) => void;
  permiteSinRango?: boolean;
}) {
  const [value, setValue] = useState(inicial);
  return (
    <RangoFechas
      value={value}
      hoy={HOY}
      permiteSinRango={permiteSinRango}
      onChange={(next) => {
        setValue(next);
        onChange(next);
      }}
    />
  );
}

describe("RangoFechas", () => {
  const onChange = vi.fn<(next: DateRange) => void>();
  beforeEach(() => vi.clearAllMocks());

  it("con el rango lleno, cambiar solo el inicio conserva el final y no cierra nada", () => {
    // Bug del Reporte detallado: con 30 días marcados el primer clic movía el FINAL y cerraba.
    render(<Controlado inicial={{ from: "2026-10-12", to: "2026-11-11" }} onChange={onChange} />);

    const desde = screen.getByLabelText("Desde") as HTMLInputElement;
    fireEvent.change(desde, { target: { value: "2026-10-01" } });

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenLastCalledWith({ from: "2026-10-01", to: "2026-11-11" });
    expect(screen.getByLabelText("Desde")).toHaveValue("2026-10-01");
    expect(screen.getByLabelText("Hasta")).toHaveValue("2026-11-11");
  });

  it("cruza min y max: Desde no pasa de Hasta y Hasta no baja de Desde", () => {
    render(<Controlado inicial={{ from: "2026-10-12", to: "2026-11-11" }} onChange={onChange} />);
    expect(screen.getByLabelText("Desde")).toHaveAttribute("max", "2026-11-11");
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("min", "2026-10-12");
  });

  it("un preset resuelve el rango con el día local, no el UTC", async () => {
    const user = userEvent.setup();
    render(<Controlado inicial={{ from: "2026-10-12", to: "2026-11-11" }} onChange={onChange} />);

    await user.selectOptions(screen.getByLabelText("Periodo"), "ultimos_7");
    expect(onChange).toHaveBeenLastCalledWith({ from: "2026-11-05", to: "2026-11-11" });
    expect(screen.queryByLabelText("Desde")).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText("Periodo"), "mes_anterior");
    expect(onChange).toHaveBeenLastCalledWith({ from: "2026-10-01", to: "2026-10-31" });
  });

  it("«Rango propio» muestra Desde y Hasta con el rango actual", async () => {
    const user = userEvent.setup();
    render(<Controlado inicial={{ from: "2026-11-05", to: "2026-11-11" }} onChange={onChange} />);
    expect(screen.getByLabelText("Periodo")).toHaveValue("ultimos_7");

    await user.selectOptions(screen.getByLabelText("Periodo"), "personalizado");
    expect(screen.getByLabelText("Desde")).toHaveValue("2026-11-05");
    expect(screen.getByLabelText("Hasta")).toHaveValue("2026-11-11");
  });

  it("marca el rango incoherente como inválido y avisa", () => {
    render(<Controlado inicial={{ from: "2026-11-20", to: "2026-11-11" }} onChange={onChange} />);
    expect(screen.getByLabelText("Desde")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toHaveTextContent(/inicio.*posterior/i);
  });

  it("sin rango solo se ofrece «Todo el periodo» si el consumidor lo permite", () => {
    const { unmount } = render(<Controlado inicial={sinRango()} onChange={onChange} permiteSinRango />);
    expect(screen.getByLabelText("Periodo")).toHaveValue("todo");
    unmount();
    render(<Controlado inicial={sinRango()} onChange={onChange} />);
    expect(screen.queryByRole("option", { name: "Todo el periodo" })).not.toBeInTheDocument();
  });

  it("DateRangeFilter de Reportes usa RangoFechas: sin calendario emergente", async () => {
    const { DateRangeFilter } = await import("@/components/atom/modules/_reportes/DateRangeFilter");
    render(<DateRangeFilter value={{ from: "2026-09-01", to: "2026-09-15" }} onChange={onChange} />);
    expect(screen.getByTestId("rango-fechas")).toBeInTheDocument();
    expect(screen.queryByTestId("date-range-picker")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Desde")).toHaveValue("2026-09-01");
  });

  it("Escape no descarta lo ya elegido y el foco queda en el campo que se editó", async () => {
    const user = userEvent.setup();
    render(<Controlado inicial={{ from: "2026-10-12", to: "2026-11-11" }} onChange={onChange} />);
    const desde = screen.getByLabelText("Desde");
    await user.click(desde);
    await user.keyboard("{Escape}");
    expect(desde).toHaveFocus();
    expect(screen.getByLabelText("Desde")).toHaveValue("2026-10-12");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("el rango por defecto del Detallado abre como «Últimos 30 días» y se puede editar", async () => {
    const { defaultDetailedFilters } = await import("@/components/atom/modules/_reportesDetallados/filters");
    const { range } = defaultDetailedFilters(HOY);
    expect(range).toEqual({ from: "2026-10-13", to: "2026-11-11" });
    render(<Controlado inicial={range} onChange={onChange} />);
    expect(screen.getByLabelText("Periodo")).toHaveValue("ultimos_30");
  });
});
