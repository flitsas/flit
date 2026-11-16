// Carrusel de imágenes del OT: solo en la marca del organismo, validado, y reemplaza «Servicios Adicionales» en el punto.
import { describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { CarruselServiciosPunto } from "@/components/punto/CarruselServiciosPunto";
import { marcaAplicable } from "@/lib/marca/marca";

const RUTA = (id: string) => `/api/v1/persona-natural/marca/imagenes/${id}`;
const BASE = { nombre: "Tránsito X", colores: { primario: "#001682", secundario: "#CC4E0A", sobrePrimario: "#FFFFFF" }, logoUrl: null, bannerUrl: null, version: 3 };

describe("marcaAplicable · carrusel", () => {
  it("el organismo trae sus imágenes validadas y en orden", () => {
    const m = marcaAplicable({
      ...BASE,
      origen: "organismo",
      carrusel: [
        { imagenUrl: RUTA("a1"), alt: "Curso", enlace: "https://example.org/curso" },
        { imagenUrl: RUTA("b2"), alt: "SOAT", enlace: null },
      ],
    });
    expect(m?.carrusel).toEqual([
      { imagenUrl: RUTA("a1"), alt: "Curso", enlace: "https://example.org/curso" },
      { imagenUrl: RUTA("b2"), alt: "SOAT", enlace: null },
    ]);
  });

  it("descarta lo inválido: imagen externa, sin texto alternativo, ítems de más y enlaces que no son http/https", () => {
    const m = marcaAplicable({
      ...BASE,
      origen: "organismo",
      carrusel: [
        { imagenUrl: "https://evil.example/x.png", alt: "Externa", enlace: null },
        { imagenUrl: RUTA("c3"), alt: "   ", enlace: null },
        { imagenUrl: RUTA("d4"), alt: "Con script", enlace: "javascript:alert(1)" },
        { imagenUrl: RUTA("e5"), alt: "Con credenciales", enlace: "https://u:p@example.org" },
        ...Array.from({ length: 10 }, (_, i) => ({ imagenUrl: RUTA(`f${i}`), alt: `Extra ${i}`, enlace: null })),
      ],
    });
    expect(m?.carrusel).toHaveLength(6);
    expect(m?.carrusel?.[0]).toEqual({ imagenUrl: RUTA("d4"), alt: "Con script", enlace: null });
    expect(m?.carrusel?.[1]).toEqual({ imagenUrl: RUTA("e5"), alt: "Con credenciales", enlace: null });
  });

  it("la web de persona no usa carrusel aunque llegue", () => {
    const m = marcaAplicable({ ...BASE, origen: "web", carrusel: [{ imagenUrl: RUTA("a1"), alt: "X", enlace: null }] }, ["organismo", "web"]);
    expect(m).not.toBeNull();
    expect(m?.carrusel).toBeUndefined();
  });

  it("sin carrusel la marca queda como siempre", () => {
    expect(marcaAplicable({ ...BASE, origen: "organismo", carrusel: [] })?.carrusel).toBeUndefined();
  });
});

describe("CarruselServiciosPunto", () => {
  const items = [
    { imagenUrl: RUTA("a1"), alt: "Curso de infractores", enlace: "https://example.org/curso" },
    { imagenUrl: RUTA("b2"), alt: "SOAT", enlace: null },
  ];

  it("pinta las imágenes con su alt, enlaza en pestaña nueva y navega con los puntos", () => {
    render(<CarruselServiciosPunto items={items} />);
    expect(screen.getByRole("region", { name: "Imágenes destacadas del organismo" })).toBeInTheDocument();
    expect(screen.getAllByRole("img", { hidden: true }).map((i) => i.getAttribute("src"))).toEqual([RUTA("a1"), RUTA("b2")]);
    const enlace = screen.getByRole("link", { name: "Curso de infractores (se abre en una pestaña nueva)" });
    expect(enlace).toHaveAttribute("target", "_blank");
    expect(enlace).toHaveAttribute("rel", expect.stringContaining("noopener"));
    expect(screen.getByRole("button", { name: "Ir a la imagen 1" })).toHaveAttribute("aria-current", "true");
    fireEvent.click(screen.getByRole("button", { name: "Ir a la imagen 2" }));
    expect(screen.getByRole("button", { name: "Ir a la imagen 2" })).toHaveAttribute("aria-current", "true");
  });

  it("con una sola imagen no hay puntos", () => {
    render(<CarruselServiciosPunto items={[items[1]]} />);
    expect(screen.queryByRole("button", { name: /Ir a la imagen/ })).not.toBeInTheDocument();
  });
});
