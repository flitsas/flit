import type { AnchorHTMLAttributes } from "react";
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { PaginaExcepcion } from "@flit/ui/PaginaExcepcion";

// Uso de ejemplo: <PaginaExcepcion codigo={404} titulo="Esta página no existe" mensaje="Revisa la dirección."
//   accion={{ etiqueta: "Volver al inicio", href: "/" }} enlace={Link} />

describe("PaginaExcepcion (HU #14562 · UI-1)", () => {
  it("404: un único h1, el código visible y la acción como enlace", () => {
    render(
      <PaginaExcepcion
        codigo={404}
        titulo="Esta página no existe"
        mensaje="Revisa la dirección."
        accion={{ etiqueta: "Volver al inicio", href: "/" }}
      />,
    );
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    expect(screen.getByRole("heading", { level: 1, name: "Esta página no existe" })).toBeInTheDocument();
    expect(screen.getByText("404")).toBeInTheDocument();
    expect(screen.getByText("Revisa la dirección.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Volver al inicio" })).toHaveAttribute("href", "/");
  });

  it("403: el código y el texto propios, con data-codigo", () => {
    const { container } = render(
      <PaginaExcepcion
        codigo={403}
        titulo="No tienes acceso"
        mensaje="Pídelo al administrador."
        accion={{ etiqueta: "Ir a mis productos", href: "/productos" }}
      />,
    );
    expect(container.querySelector("[data-codigo='403']")).not.toBeNull();
    expect(screen.getByText("403")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ir a mis productos" })).toHaveAttribute("href", "/productos");
  });

  it("usa el componente de enlace que le pasan (p. ej. Link de next)", () => {
    const Propio = (props: AnchorHTMLAttributes<HTMLAnchorElement>) => <a data-propio="si" {...props} />;
    render(
      <PaginaExcepcion codigo={404} titulo="No existe" mensaje="—" accion={{ etiqueta: "Volver", href: "/" }} enlace={Propio} />,
    );
    expect(screen.getByRole("link", { name: "Volver" })).toHaveAttribute("data-propio", "si");
  });
});
