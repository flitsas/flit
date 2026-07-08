// HU #10198 / RF22 — Lista de documentos del trámite: define qué documentos y su
// obligatoriedad. Tras RF22 ya NO reordena (el orden lo fija solo «Overrides OT»).
//
// Uso de ejemplo:
//   render(<DocumentAssociationList items={items} onToggleObligatorio={fn} onRemove={fn} />);
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

describe("DocumentAssociationList (RF22)", () => {
  it("NO ofrece reordenamiento (el orden lo define Overrides OT)", () => {
    render(
      <DocumentAssociationList
        items={items}
        onToggleObligatorio={vi.fn()}
        onRemove={vi.fn()}
      />,
    );
    // Ya no existen botones de subir/bajar en esta lista.
    expect(screen.queryByRole("button", { name: /subir/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /bajar/i })).toBeNull();
  });

  it("dispara toggle de obligatoriedad y baja", () => {
    const onToggleObligatorio = vi.fn();
    const onRemove = vi.fn();
    render(
      <DocumentAssociationList
        items={items}
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
