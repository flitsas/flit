import { afterEach, describe, expect, it } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { ThemeToggle } from "../theme";
import { THEME_INIT_SCRIPT } from "../theme-script";

// El tema claro/oscuro es el mismo en todos los productos: clase `dark` en <html> y preferencia en `flit-theme`.

afterEach(() => {
  cleanup();
  document.documentElement.classList.remove("dark");
  localStorage.clear();
});

describe("tema de la suite", () => {
  it("el interruptor cambia la clase de <html> y recuerda la preferencia", async () => {
    render(<ThemeToggle />);
    const toggle = screen.getByRole("button", { name: "Cambiar tema" });
    expect(toggle).toHaveAttribute("aria-pressed", "false");

    await act(async () => fireEvent.click(toggle));
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(localStorage.getItem("flit-theme")).toBe("dark");
    expect(toggle).toHaveAttribute("aria-pressed", "true");

    await act(async () => fireEvent.click(toggle));
    expect(document.documentElement.classList.contains("dark")).toBe(false);
    expect(localStorage.getItem("flit-theme")).toBe("light");
  });

  it("al montarse aplica la preferencia guardada (Trámites no pone el script de arranque)", async () => {
    localStorage.setItem("flit-theme", "dark");
    await act(async () => {
      render(<ThemeToggle />);
    });
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });

  it("el script de arranque aplica el oscuro antes de pintar", () => {
    localStorage.setItem("flit-theme", "dark");
    new Function(THEME_INIT_SCRIPT)();
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });
});
