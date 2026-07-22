// HU #10844 — CreateButton unificado: el texto visible es el nombre accesible, el icono
// va aria-hidden, y la acción de crear se dispara al hacer clic. Cubre AC1 (fundaciones).
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Building2 } from "lucide-react";
import { CreateButton } from "../CreateButton";

describe("CreateButton", () => {
  it("renderiza el label como nombre accesible del botón", () => {
    render(<CreateButton label="Crear compañía" onClick={vi.fn()} icon={Building2} />);
    const btn = screen.getByRole("button", { name: "Crear compañía" });
    expect(btn).toBeInTheDocument();
  });

  it("dispara onClick al hacer clic", async () => {
    const onClick = vi.fn();
    render(<CreateButton label="Nueva alerta" onClick={onClick} />);
    await userEvent.click(screen.getByRole("button", { name: "Nueva alerta" }));
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("no dispara onClick cuando está deshabilitado", async () => {
    const onClick = vi.fn();
    render(<CreateButton label="Crear documento" onClick={onClick} disabled />);
    await userEvent.click(screen.getByRole("button", { name: "Crear documento" }));
    expect(onClick).not.toHaveBeenCalled();
  });

  it("marca el icono como decorativo (aria-hidden) para no duplicar el nombre accesible", () => {
    const { container } = render(<CreateButton label="Dar de alta OT" icon={Building2} />);
    const svg = container.querySelector("svg");
    expect(svg).toHaveAttribute("aria-hidden", "true");
  });
});
