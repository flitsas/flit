import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { ManualArticle } from "@/lib/manual/catalog";
import { ManualArticleView } from "@/components/manual/ManualArticleView";

/**
 * HU #13012 (Épica #12755) — AC1: un artículo con bloques de imagen y diagrama los muestra con su
 * caption. El tema claro/oscuro lo resuelven los tokens del manual (la figura no trae colores
 * propios), así que aquí se fija el contrato de render: img con alt, caption visible y ruta del
 * diagrama compilado.
 */

const articulo: ManualArticle = {
  slug: "0-introduccion/fixture-media",
  title: "Artículo de prueba con medios",
  audience: "Todos",
  sectionId: "introduccion",
  keywords: ["fixture"],
  summary: "Fixture de la HU #13012.",
  blocks: [
    {
      id: "con-medios",
      title: "1. Sección con captura y diagrama",
      paragraphs: ["Texto de la sección."],
      media: [
        {
          kind: "image",
          src: "/manual/fixture/pantalla.png",
          alt: "Pantalla de ejemplo del wizard",
          caption: "Paso 1 del wizard",
        },
        {
          kind: "diagram",
          id: "flujo-ejemplo",
          alt: "Diagrama del flujo de ejemplo",
        },
      ],
    },
  ],
};

describe("ManualArticleView · media", () => {
  it("AC1 — muestra la imagen con alt y caption, y el diagrama con su SVG compilado", () => {
    render(<ManualArticleView article={articulo} />);

    const imagen = screen.getByAltText("Pantalla de ejemplo del wizard");
    expect(imagen).toHaveAttribute("src", "/manual/fixture/pantalla.png");
    expect(screen.getByText("Paso 1 del wizard")).toBeInTheDocument();

    const diagrama = screen.getByAltText("Diagrama del flujo de ejemplo");
    expect(diagrama).toHaveAttribute("src", "/manual/diagrams/flujo-ejemplo.svg");
  });

  it("una sección sin media renderiza igual que siempre", () => {
    const sinMedia: ManualArticle = {
      ...articulo,
      blocks: [{ id: "solo-texto", title: "1. Solo texto", paragraphs: ["Nada visual."] }],
    };
    render(<ManualArticleView article={sinMedia} />);
    expect(screen.getByText("Nada visual.")).toBeInTheDocument();
    expect(screen.queryByRole("figure")).not.toBeInTheDocument();
  });
});
