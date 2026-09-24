// HU #12883 AC2 — homologación visual de la lista de prelación: posición a la izquierda del
// agarrador, badge tintado "Generado por FLIT" / "Lo adjunta el gestor" y marca de fila destino
// durante el dragover.
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DocumentPrecedenceList } from "../DocumentPrecedenceList";
import type { OtDocumentPrecedenceItem } from "@/lib/api/types-ot";

const items: OtDocumentPrecedenceItem[] = [
  {
    document_type_id: "doc-fur",
    document_name: "Formulario Único de Registro (FUR)",
    sort_order: 1,
    is_system_generated: true,
  },
  {
    document_type_id: "doc-soat",
    document_name: "SOAT",
    sort_order: 2,
    is_system_generated: false,
  },
];

function renderList(onReorder = vi.fn().mockResolvedValue(undefined)) {
  const utils = render(<DocumentPrecedenceList items={items} onReorder={onReorder} />);
  return { ...utils, onReorder };
}

describe("DocumentPrecedenceList — HU #12883 AC2", () => {
  it("AC1 no usa clases bajo el piso tipográfico (text-[10px]/[11px])", () => {
    const { container } = renderList();
    expect(container.innerHTML).not.toMatch(/text-\[1[01]px\]/);
  });

  it("AC2 la posición va a la izquierda, antes del agarrador", () => {
    renderList();
    const row = screen.getByLabelText(/Reordenar SOAT/i).closest("li");
    expect(row).not.toBeNull();
    const position = row!.querySelector("span.rounded-full");
    const handle = screen.getByLabelText(/Reordenar SOAT/i);
    expect(position).not.toBeNull();
    // El nodo de posición precede al botón agarrador en el DOM.
    expect(
      position!.compareDocumentPosition(handle) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(position).toHaveTextContent("2");
  });

  it("AC2 badge «Generado por FLIT» (tone info) para el documento del sistema", () => {
    renderList();
    const badge = screen.getByText("Generado por FLIT");
    expect(badge).toBeInTheDocument();
    expect(badge.closest('[role="status"]')).toHaveStyle({
      background: "var(--badge-info-bg)",
    });
  });

  it("AC2 badge «Lo adjunta el gestor» (tone neutral) para el documento que sube el gestor", () => {
    renderList();
    const badge = screen.getByText("Lo adjunta el gestor");
    expect(badge).toBeInTheDocument();
    expect(badge.closest('[role="status"]')).toHaveStyle({
      background: "var(--badge-neutral-bg)",
    });
  });

  it("AC2 marca con borde #557EFF la fila destino durante el dragover", () => {
    renderList();
    const rows = screen.getAllByRole("listitem");
    const source = rows[0];
    const target = rows[1];

    fireEvent.dragStart(source);
    fireEvent.dragOver(target);

    expect(target.style.borderColor).toBe("rgb(85, 126, 255)");
  });

  it("conserva el reordenamiento por teclado (ArrowDown + Enter guarda)", async () => {
    const onReorder = vi.fn().mockResolvedValue(undefined);
    renderList(onReorder);

    const handle = screen.getByLabelText(/Reordenar Formulario Único de Registro/i);
    handle.focus();
    fireEvent.keyDown(handle, { key: "ArrowDown" });
    fireEvent.keyDown(handle, { key: "ArrowDown" });
    fireEvent.keyDown(handle, { key: "Enter" });

    expect(onReorder).toHaveBeenCalledWith([
      { ...items[1], sort_order: 1 },
      { ...items[0], sort_order: 2 },
    ]);
  });
});
