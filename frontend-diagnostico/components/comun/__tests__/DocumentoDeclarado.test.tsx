import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { DocumentoDeclarado } from "@/components/comun/DocumentoDeclarado";

// Uso de ejemplo: <DocumentoDeclarado tipo="CC" documento="****4050" /> → «CC ****4050 · Declarado por el cliente».

describe("DocumentoDeclarado — enmascarado y rótulo (AC 8)", () => {
  it("muestra el documento enmascarado con el rótulo «Declarado por el cliente»", () => {
    render(<DocumentoDeclarado tipo="CC" documento="****4050" />);
    expect(screen.getByText("CC ****4050")).toBeInTheDocument();
    expect(screen.getByText(/declarado por el cliente/i)).toBeInTheDocument();
  });

  it("si llegara un documento completo lo enmascara en pantalla («****» y los 4 últimos)", () => {
    const { container } = render(<DocumentoDeclarado tipo="NIT" documento="9001234567" />);
    expect(screen.getByText("NIT ****4567")).toBeInTheDocument();
    expect(container).not.toHaveTextContent("9001234567");
  });

  it("sin documento no pinta nada", () => {
    const { container } = render(<DocumentoDeclarado tipo={null} documento={null} />);
    expect(container).toBeEmptyDOMElement();
  });
});
