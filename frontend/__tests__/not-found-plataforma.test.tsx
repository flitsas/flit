import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import NotFoundPage from "@/app/not-found";
import AdminMandatosNotFound from "@/app/admin/plataforma/mandatos/not-found";
import AdminFurNotFound from "@/app/admin/plataforma/fur/not-found";
import AdminNotificacionesNotFound from "@/app/admin/plataforma/notificaciones/not-found";

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

  // Uso de ejemplo: 404 placeholder de /admin/plataforma/notificaciones
  // (HU #11369) mientras la pantalla real (HU #11370) no existe.
  it("muestra 404 de FUR dentro del segmento Plataforma", () => {
    render(<AdminFurNotFound />);
    expect(screen.getByTestId("admin-fur-not-found")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /fur no disponible/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /volver al inicio/i })).toHaveAttribute("href", "/");
  });

  it("muestra 404 de Notificaciones dentro del segmento Plataforma (HU #11369)", () => {
    render(<AdminNotificacionesNotFound />);
    expect(screen.getByTestId("admin-notificaciones-not-found")).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: /notificaciones no disponible/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /volver al inicio/i })).toHaveAttribute("href", "/");
  });

  it("el placeholder de Notificaciones no reutiliza el testid de Mandatos (edge case: segmentos no se confunden)", () => {
    render(<AdminNotificacionesNotFound />);
    expect(screen.queryByTestId("admin-mandatos-not-found")).not.toBeInTheDocument();
  });
});
