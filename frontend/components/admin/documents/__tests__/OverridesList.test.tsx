// #2 — Lista de overrides reordenable: botones ↑/↓ emiten el nuevo orden vía onReorder
// y el botón eliminar delega en onDelete.
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { OverridesList } from "../panels/OverridesList";
import type { DocumentOrderOverride } from "@/lib/api/types-documents";

const overrides: DocumentOrderOverride[] = [
  {
    id: "ov-1",
    procedureTypeId: "p-1",
    documentTypeId: "doc-a",
    scope: "OT",
    scopeRefId: "ot-1",
    orden: 10,
    documento: { codigo: "DOCA", nombre: "Documento A" },
  },
  {
    id: "ov-2",
    procedureTypeId: "p-1",
    documentTypeId: "doc-b",
    scope: "OT",
    scopeRefId: "ot-1",
    orden: 20,
    documento: { codigo: "DOCB", nombre: "Documento B" },
  },
];

describe("OverridesList (#2 — reordenable)", () => {
  it("renderiza cada override con su badge de orden", () => {
    render(<OverridesList overrides={overrides} onReorder={vi.fn()} onDelete={vi.fn()} />);
    expect(screen.getByText(/Documento A/)).toBeInTheDocument();
    expect(screen.getByText(/Documento B/)).toBeInTheDocument();
    expect(screen.getByLabelText("Orden 10")).toBeInTheDocument();
  });

  it("baja el primero con ↓ y emite la lista reordenada", () => {
    const onReorder = vi.fn();
    render(<OverridesList overrides={overrides} onReorder={onReorder} onDelete={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: /bajar documento a/i }));

    expect(onReorder).toHaveBeenCalledTimes(1);
    expect(onReorder.mock.calls[0][0].map((o: DocumentOrderOverride) => o.id)).toEqual([
      "ov-2",
      "ov-1",
    ]);
  });

  it("deshabilita ↑ en el primero y ↓ en el último", () => {
    render(<OverridesList overrides={overrides} onReorder={vi.fn()} onDelete={vi.fn()} />);
    expect(screen.getByRole("button", { name: /subir documento a/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /bajar documento b/i })).toBeDisabled();
  });

  it("elimina un override vía onDelete", () => {
    const onDelete = vi.fn();
    render(<OverridesList overrides={overrides} onReorder={vi.fn()} onDelete={onDelete} />);

    fireEvent.click(screen.getByRole("button", { name: /eliminar override de documento b/i }));
    expect(onDelete).toHaveBeenCalledWith(overrides[1]);
  });
});
