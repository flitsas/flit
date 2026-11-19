// Bug #14570 — casillas «bolsa» (tipos múltiples): se listan TODOS los archivos, se eliminan por id
// y la acción es «Agregar otro documento», nunca «Reemplazar archivo».
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DocumentSlot } from "../DocumentChecklist";
import { esTipoDocumentoMultiple } from "@/lib/tramites/documentos-multiples";
import type { ChecklistItemView, ProcedureAttachment } from "@/lib/api/types/procedure-runtime";

function att(id: string, filename: string, uploadedAt: string, tipo = "otro"): ProcedureAttachment {
  return { id, tipo, filename, sizeBytes: 2048, uploadedAt } as ProcedureAttachment;
}

const item = (tipo: string): ChecklistItemView => ({
  key: tipo,
  label: tipo === "otro" ? "Otro" : "Factura",
  obligatorio: false,
  docTipo: tipo,
  satisfied: false,
});

function renderSlot(
  tipo: string,
  bolsa: ProcedureAttachment[] | undefined,
  attachment?: ProcedureAttachment,
) {
  const onRemove = vi.fn();
  render(
    <DocumentSlot
      item={item(tipo)}
      attachment={attachment}
      bolsa={bolsa}
      uploading={false}
      analyzing={false}
      deleting={false}
      ocr={undefined}
      onUpload={vi.fn()}
      onRemove={onRemove}
      onPreview={vi.fn()}
    />,
  );
  return { onRemove };
}

describe("Bug #14570 — bolsa de documentos", () => {
  const a = att("a1", "viejo.pdf", "2026-11-01T10:00:00Z");
  const b = att("b2", "corregido.pdf", "2026-11-02T10:00:00Z");

  it("lista todos los archivos del tipo múltiple", () => {
    renderSlot("otro", [a, b], b);
    expect(screen.getByText("viejo.pdf")).toBeInTheDocument();
    expect(screen.getByText("corregido.pdf")).toBeInTheDocument();
  });

  it("eliminar llama onRemove con el id de SU archivo", () => {
    const { onRemove } = renderSlot("otro", [a, b], b);
    fireEvent.click(screen.getByRole("button", { name: "Eliminar viejo.pdf" }));
    expect(onRemove).toHaveBeenCalledWith("a1");
  });

  it("la acción dice «Agregar otro documento» y no «Reemplazar archivo»", () => {
    renderSlot("otro", [a, b], b);
    expect(screen.getByRole("button", { name: "Agregar otro documento" })).toBeInTheDocument();
    expect(screen.queryByText("Reemplazar archivo")).not.toBeInTheDocument();
  });

  it("bolsa vacía ofrece «Adjuntar archivo»", () => {
    renderSlot("otro", []);
    expect(screen.getByRole("button", { name: "Adjuntar archivo" })).toBeInTheDocument();
  });

  it("tipo normal sin cambios: «Reemplazar archivo» y «Borrar»", () => {
    const f = att("f1", "factura.pdf", "2026-11-01T10:00:00Z", "factura");
    renderSlot("factura", undefined, f);
    expect(screen.getByRole("button", { name: "Reemplazar archivo" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Borrar/ })).toBeInTheDocument();
  });

  it("constante de tipos múltiples", () => {
    expect(esTipoDocumentoMultiple("OTRO")).toBe(true);
    expect(esTipoDocumentoMultiple("anexos_generales")).toBe(true);
    expect(esTipoDocumentoMultiple("factura")).toBe(false);
  });
});
