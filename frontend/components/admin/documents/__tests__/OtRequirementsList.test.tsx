// #3 — Obligatoriedad por OT: cada documento asociado muestra un selector de 4 opciones
// (Por defecto / Obligatorio / Opcional / No aplica) y emite la selección vía onChange.
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { OtRequirementsList } from "../panels/OtRequirementsList";
import type { DocumentRequirementSelection, DocumentType } from "@/lib/api/types-documents";

const documents: DocumentType[] = [
  { id: "doc-a", codigo: "DOCA", nombre: "Documento A", estado: "activo", fechaCreacion: "" },
  { id: "doc-b", codigo: "DOCB", nombre: "Documento B", estado: "activo", fechaCreacion: "" },
];

describe("OtRequirementsList (#3 — obligatoriedad por OT)", () => {
  it("muestra «Por defecto» cuando no hay override y refleja el estado cuando lo hay", () => {
    render(
      <OtRequirementsList
        documents={documents}
        selectionByDocId={{ "doc-b": "NOT_APPLICABLE" }}
        onChange={vi.fn()}
      />,
    );

    const selectA = screen.getByLabelText(/Obligatoriedad de Documento A/) as HTMLSelectElement;
    const selectB = screen.getByLabelText(/Obligatoriedad de Documento B/) as HTMLSelectElement;
    expect(selectA.value).toBe("DEFAULT");
    expect(selectB.value).toBe("NOT_APPLICABLE");
  });

  it("emite la nueva selección con el id del documento", () => {
    const onChange = vi.fn<(id: string, s: DocumentRequirementSelection) => void>();
    render(
      <OtRequirementsList documents={documents} selectionByDocId={{}} onChange={onChange} />,
    );

    fireEvent.change(screen.getByLabelText(/Obligatoriedad de Documento A/), {
      target: { value: "REQUIRED" },
    });
    expect(onChange).toHaveBeenCalledWith("doc-a", "REQUIRED");
  });

  it("ofrece las 4 opciones del control", () => {
    render(
      <OtRequirementsList documents={documents} selectionByDocId={{}} onChange={vi.fn()} />,
    );
    const selectA = screen.getByLabelText(/Obligatoriedad de Documento A/);
    expect(selectA.querySelectorAll("option")).toHaveLength(4);
  });

  it("muestra el nombre del catálogo y filtra con el buscador", () => {
    render(
      <OtRequirementsList
        documents={[
          {
            id: "paz",
            codigo: "paz_salvo",
            nombre: "Paz y Salvo de Impuestos",
            estado: "activo",
            fechaCreacion: "",
          },
          documents[0],
        ]}
        selectionByDocId={{}}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getByText(/Paz y Salvo de Impuestos/)).toBeInTheDocument();
    expect(screen.getByText("(paz_salvo)")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Buscar documento"), {
      target: { value: "tradicion" },
    });
    expect(screen.getByText(/Ningún documento coincide/i)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Buscar documento"), {
      target: { value: "Impuestos" },
    });
    expect(screen.getByText(/Paz y Salvo de Impuestos/)).toBeInTheDocument();
    expect(screen.queryByText(/Documento A/)).not.toBeInTheDocument();
  });

  it("muestra una guía cuando el trámite no tiene documentos asociados", () => {
    render(<OtRequirementsList documents={[]} selectionByDocId={{}} onChange={vi.fn()} />);
    expect(screen.getByText(/Agrega documentos en «Orden de los documentos»/i)).toBeInTheDocument();
  });
});
