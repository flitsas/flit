import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import NotFoundPage from "@/app/not-found";
import AdminMandatosNotFound from "@/app/admin/plataforma/mandatos/not-found";

describe("not-found FLIT", () => {
  it("muestra 404 global con CTA al inicio", () => {
    render(<NotFoundPage />);
    expect(screen.getByRole("heading", { name: /página no encontrada/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /volver al inicio/i })).toHaveAttribute("href", "/");
  });

  it("muestra 404 de Mandatos dentro del segmento Plataforma", () => {
    render(<AdminMandatosNotFound />);
    expect(screen.getByTestId("admin-mandatos-not-found")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /mandatos no disponible/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /volver al inicio/i })).toHaveAttribute("href", "/");
  });
});
