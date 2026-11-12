import { describe, it, expect, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";

import { DateRangeFields } from "@/components/admin/transit-offices/_reportes/filters";
import type { DateRange } from "@/components/atom/modules/_reportes/range";

function Host({ onChange }: { onChange: (r: DateRange) => void }) {
  const [range, setRange] = useState<DateRange>({ from: "2026-10-12", to: "2026-11-11" });
  return (
    <DateRangeFields
      range={range}
      onChange={(r) => {
        setRange(r);
        onChange(r);
      }}
    />
  );
}

describe("DateRangeFields (reportes del organismo)", () => {
  it("con el rango lleno se puede cambiar solo el inicio y los límites se cruzan", async () => {
    const onChange = vi.fn();
    render(<Host onChange={onChange} />);

    // El rango no coincide con un preset: abre en «Rango propio» con ambos extremos editables.
    expect(screen.getByLabelText("Desde")).toHaveAttribute("max", "2026-11-11");
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("min", "2026-10-12");

    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2026-10-01" } });
    expect(onChange).toHaveBeenLastCalledWith({ from: "2026-10-01", to: "2026-11-11" });
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("min", "2026-10-01");
  });

  it("un rango incoherente avisa y marca los campos inválidos", async () => {
    const user = userEvent.setup();
    render(<Host onChange={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Hasta"), { target: { value: "2026-09-01" } });
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getByLabelText("Desde")).toHaveAttribute("aria-invalid", "true");
    await user.click(screen.getByLabelText("Desde"));
  });
});
