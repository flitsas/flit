// AC2 — Lista reordenable: alternativa accesible (↑/↓) al drag-and-drop, toggle de
// obligatoriedad y baja. El reordenamiento emite la lista completa renumerada.
//
// Uso de ejemplo:
//   render(<DocumentAssociationList items={items} onReorder={fn} ... />);
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DocumentAssociationList } from "../panels/DocumentAssociationList";
import type { ProcedureDocumentRequirement } from "@/lib/api/types-documents";

function req(id: string, codigo: string, nombre: string, orden: number): ProcedureDocumentRequirement {
  return {
    id,
    procedureTypeId: "33333333-3333-3333-3333-333333333333",
    documentTypeId: `doc-${id}`,
    ordenDefault: orden,
    obligatorio: false,
    documento: { codigo, nombre, estado: "activo" },
  };
}

const items = [req("a", "DOCA", "Documento A", 10), req("b", "DOCB", "Documento B", 20)];

describe("DocumentAssociationList (AC2)", () => {
  it("reordena con el botón bajar y emite el nuevo orden", () => {
    const onReorder = vi.fn();
    render(
      <DocumentAssociationList
        items={items}
        onReorder={onReorder}
        onToggleObligatorio={vi.fn()}
        onRemove={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /bajar documento a/i }));
    const reordered = onReorder.mock.calls[0][0] as ProcedureDocumentRequirement[];
    expect(reordered.map((r) => r.id)).toEqual(["b", "a"]);
  });

  it("deshabilita subir en el primer ítem y bajar en el último", () => {
    render(
      <DocumentAssociationList
        items={items}
        onReorder={vi.fn()}
        onToggleObligatorio={vi.fn()}
        onRemove={vi.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: /subir documento a/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /bajar documento b/i })).toBeDisabled();
  });

  it("dispara toggle de obligatoriedad y baja", () => {
    const onToggleObligatorio = vi.fn();
    const onRemove = vi.fn();
    render(
      <DocumentAssociationList
        items={items}
        onReorder={vi.fn()}
        onToggleObligatorio={onToggleObligatorio}
        onRemove={onRemove}
      />,
    );

    fireEvent.click(screen.getByRole("checkbox", { name: /documento obligatorio: documento a/i }));
    expect(onToggleObligatorio).toHaveBeenCalledWith(items[0]);

    fireEvent.click(screen.getByRole("button", { name: /remover documento b/i }));
    expect(onRemove).toHaveBeenCalledWith(items[1]);
  });
});
