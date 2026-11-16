import { describe, it, expect, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { QueryConsole } from "@/components/consultas/QueryConsole";
import type { QuerySource } from "@/lib/api/queries";

type Fila = { id: string };

const source: QuerySource<Fila> = {
  testIdPrefix: "t",
  dateFields: [{ value: "creado", label: "Creación" }],
  defaultDateField: "creado",
  columnsStorageKey: "t.cols",
  exportPrefix: "t",
  rowNoun: ["fila", "filas"],
  fetchFields: vi.fn().mockResolvedValue([]),
  fetchSaved: vi.fn().mockResolvedValue([]),
  run: vi.fn().mockResolvedValue({
    total: 0,
    page: 1,
    pageSize: 50,
    desde: "2026-10-13",
    hasta: "2026-11-11",
    totalPeriodoAnterior: 0,
    filas: [],
    cobertura: [],
  }),
  save: vi.fn(),
  remove: vi.fn(),
};

describe("QueryConsole — límites cruzados de Desde/Hasta", () => {
  it("en «Rango propio», Desde no pasa de Hasta y Hasta no baja de Desde", async () => {
    const user = userEvent.setup();
    render(
      <QueryConsole<Fila>
        source={source}
        columns={[]}
        presets={[]}
        defaultColumns={[]}
        rowKey={(r) => r.id}
        sheetName="t"
        ambito="su empresa"
      />,
    );
    await waitFor(() => expect(source.fetchFields).toHaveBeenCalled());

    await user.selectOptions(screen.getByTestId("t-preset"), "personalizado");
    const desde = screen.getByLabelText("Desde");
    const hasta = screen.getByLabelText("Hasta");
    expect(desde).not.toHaveAttribute("max");
    expect(hasta).not.toHaveAttribute("min");

    fireEvent.change(desde, { target: { value: "2026-10-01" } });
    fireEvent.change(hasta, { target: { value: "2026-11-11" } });

    expect(screen.getByLabelText("Desde")).toHaveAttribute("max", "2026-11-11");
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("min", "2026-10-01");
  });
});
