// AC4 — Matriz OT con buscador en tiempo real (client-side, insensible a tildes) y
// checkboxes que reflejan los grants; al alternar dispara POST/DELETE.
//
// Uso de ejemplo:
//   render(<OTMatrix offices={offices} grantedIds={["o1"]} onToggle={spy} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OTMatrix } from "../OTMatrix";
import type { TransitOffice } from "@/lib/api/types";

const offices: TransitOffice[] = [
  { id: "o1", code: "11001", name: "Secretaría de Movilidad Bogotá", departmentCode: "11", cityCode: "11001" },
  { id: "o2", code: "05001", name: "Medellín — Secretaría de Movilidad", departmentCode: "05", cityCode: "05001" },
  { id: "o3", code: "76001", name: "Cali — STTMP", departmentCode: "76", cityCode: "76001" },
];

describe("OTMatrix (AC4)", () => {
  it("filtra en tiempo real ignorando mayúsculas y tildes", async () => {
    const user = userEvent.setup();
    render(<OTMatrix offices={offices} grantedIds={[]} onToggle={vi.fn()} />);

    expect(within(screen.getByTestId("ot-list")).getAllByRole("listitem")).toHaveLength(3);

    await user.type(screen.getByLabelText(/buscar organismo/i), "bogota");

    const list = screen.getByTestId("ot-list");
    expect(within(list).getAllByRole("listitem")).toHaveLength(1);
    expect(within(list).getByText(/Secretaría de Movilidad Bogotá/i)).toBeInTheDocument();
  });

  it("filtra por código", async () => {
    const user = userEvent.setup();
    render(<OTMatrix offices={offices} grantedIds={[]} onToggle={vi.fn()} />);

    await user.type(screen.getByLabelText(/buscar organismo/i), "05001");
    const list = screen.getByTestId("ot-list");
    expect(within(list).getAllByRole("listitem")).toHaveLength(1);
    expect(within(list).getByText(/Medellín/i)).toBeInTheDocument();
  });

  it("refleja los grants existentes en los checkboxes", () => {
    render(<OTMatrix offices={offices} grantedIds={["o1"]} onToggle={vi.fn()} />);
    expect(screen.getByLabelText(/Secretaría de Movilidad Bogotá/i)).toBeChecked();
    expect(screen.getByLabelText(/Cali/i)).not.toBeChecked();
  });

  it("dispara onToggle al habilitar (POST) y al deshabilitar (DELETE)", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn().mockResolvedValue(undefined);
    render(<OTMatrix offices={offices} grantedIds={["o1"]} onToggle={onToggle} />);

    await user.click(screen.getByLabelText(/Cali/i));
    await waitFor(() => expect(onToggle).toHaveBeenCalledWith("o3", true));

    await user.click(screen.getByLabelText(/Secretaría de Movilidad Bogotá/i));
    await waitFor(() => expect(onToggle).toHaveBeenCalledWith("o1", false));
  });

  it("revierte el checkbox (rollback) si la persistencia falla", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn().mockRejectedValue(new Error("boom"));
    const onError = vi.fn();
    render(<OTMatrix offices={offices} grantedIds={[]} onToggle={onToggle} onError={onError} />);

    const cali = screen.getByLabelText(/Cali/i);
    await user.click(cali);

    await waitFor(() => expect(cali).not.toBeChecked());
    expect(onError).toHaveBeenCalled();
  });

  it("muestra mensaje cuando ningún organismo coincide", async () => {
    const user = userEvent.setup();
    render(<OTMatrix offices={offices} grantedIds={[]} onToggle={vi.fn()} />);
    await user.type(screen.getByLabelText(/buscar organismo/i), "zzz");
    expect(screen.getByText(/ningún organismo coincide/i)).toBeInTheDocument();
  });
});
