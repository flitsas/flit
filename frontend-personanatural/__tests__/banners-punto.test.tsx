// Banners principales del OT: solo en la marca del organismo, validados, cada uno con enlace opcional; el banner único anterior
// (`bannerUrl`) sigue valiendo si no hay lista.
import { describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { CarruselBanners } from "@/components/landing/CarruselBanners";
import { marcaAplicable } from "@/lib/marca/marca";

const RUTA = (id: string) => `/api/v1/persona-natural/marca/imagenes/${id}`;
const BASE = { nombre: "Tránsito X", colores: { primario: "#001682", secundario: "#CC4E0A", sobrePrimario: "#FFFFFF" }, logoUrl: null, bannerUrl: RUTA("legacy"), version: 3 };

describe("marcaAplicable · banners", () => {
  it("el organismo trae sus banners validados y en orden", () => {
    const m = marcaAplicable({
      ...BASE,
      origen: "organismo",
      banners: [
        { imagenUrl: RUTA("a1"), alt: "Movilidad", enlace: "https://example.org/movilidad" },
        { imagenUrl: RUTA("b2"), alt: "SOAT", enlace: null },
      ],
    });
    expect(m?.banners).toEqual([
      { imagenUrl: RUTA("a1"), alt: "Movilidad", enlace: "https://example.org/movilidad" },
      { imagenUrl: RUTA("b2"), alt: "SOAT", enlace: null },
    ]);
    // El banner único queda disponible como respaldo.
    expect(m?.bannerUrl).toBe(RUTA("legacy"));
  });

  it("descarta lo inválido: imagen externa, sin texto alternativo, enlaces que no son http/https y ítems de más", () => {
    const m = marcaAplicable({
      ...BASE,
      origen: "organismo",
      banners: [
        { imagenUrl: "https://evil.example/x.png", alt: "Externa", enlace: null },
        { imagenUrl: RUTA("c3"), alt: "   ", enlace: null },
        { imagenUrl: RUTA("d4"), alt: "Con script", enlace: "javascript:alert(1)" },
        ...Array.from({ length: 10 }, (_, i) => ({ imagenUrl: RUTA(`f${i}`), alt: `Extra ${i}`, enlace: null })),
      ],
    });
    expect(m?.banners).toHaveLength(6);
    expect(m?.banners?.[0]).toEqual({ imagenUrl: RUTA("d4"), alt: "Con script", enlace: null });
  });

  it("la web de persona no usa banners de marca aunque lleguen", () => {
    const m = marcaAplicable({ ...BASE, origen: "flit", banners: [{ imagenUrl: RUTA("a1"), alt: "X", enlace: null }] }, ["organismo", "flit"]);
    expect(m?.banners).toBeUndefined();
  });

  it("sin banners la marca queda como siempre", () => {
    expect(marcaAplicable({ ...BASE, origen: "organismo" })?.banners).toBeUndefined();
    expect(marcaAplicable({ ...BASE, origen: "organismo", banners: [] })?.banners).toBeUndefined();
  });
});

describe("CarruselBanners con banners del organismo", () => {
  const banners = [
    { id: "marca-0", src: RUTA("a1"), alt: "Movilidad", enlace: "https://example.org/movilidad" },
    { id: "marca-1", src: RUTA("b2"), alt: "SOAT", enlace: null },
    { id: "marca-2", src: RUTA("c3"), alt: "Cursos", enlace: null },
  ];

  it("pinta cada banner con su alt, enlaza solo el que tiene enlace (pestaña nueva) y navega con los puntos", () => {
    render(<CarruselBanners banners={banners} />);
    expect(screen.getByAltText("Movilidad")).toBeInTheDocument();
    expect(screen.getByAltText("SOAT")).toBeInTheDocument();
    expect(screen.getByAltText("Cursos")).toBeInTheDocument();
    const enlace = screen.getByRole("link", { name: /Movilidad \(se abre en una pestaña nueva\)/ });
    expect(enlace).toHaveAttribute("href", "https://example.org/movilidad");
    expect(enlace).toHaveAttribute("target", "_blank");
    expect(enlace).toHaveAttribute("rel", expect.stringContaining("noopener"));
    expect(screen.getAllByRole("link")).toHaveLength(1);
    const puntos = screen.getAllByRole("button", { name: /Ir al banner/ });
    expect(puntos).toHaveLength(3);
    fireEvent.click(puntos[2]);
    expect(puntos[2]).toHaveAttribute("aria-current", "true");
  });

  it("con un solo banner no hay puntos", () => {
    render(<CarruselBanners banners={[banners[1]]} />);
    expect(screen.queryByRole("button", { name: /Ir al banner/ })).not.toBeInTheDocument();
  });
});
