import { afterEach, describe, expect, it } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { ThemeToggle } from "../theme";
import { DARK_MODE_ENABLED, THEME_INIT_SCRIPT } from "../theme-script";

// El tema claro/oscuro es el mismo en todos los productos: clase `dark` en <html> y preferencia en `flit-theme`.
// HU #14478: el rediseño del UI kit enciende el modo oscuro (estaba apagado desde la HU #14159).

afterEach(() => {
  cleanup();
  document.documentElement.classList.remove("dark");
  localStorage.clear();
});

describe("tema de la suite (HU #14478 — modo oscuro encendido)", () => {
  it("el interruptor está habilitado y arranca en claro sin preferencia guardada", async () => {
    expect(DARK_MODE_ENABLED).toBe(true);
    await act(async () => {
      render(<ThemeToggle />);
    });
    const toggle = screen.getByRole("button", { name: "Cambiar tema" });
    expect(toggle).toBeEnabled();
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    expect(document.documentElement.classList.contains("dark")).toBe(false);
  });

  it("el clic pasa a oscuro, guarda la preferencia y otro clic vuelve a claro", async () => {
    await act(async () => {
      render(<ThemeToggle />);
    });
    const toggle = screen.getByRole("button", { name: "Cambiar tema" });

    await act(async () => fireEvent.click(toggle));
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(localStorage.getItem("flit-theme")).toBe("dark");
    expect(toggle).toHaveAttribute("aria-pressed", "true");

    await act(async () => fireEvent.click(toggle));
    expect(document.documentElement.classList.contains("dark")).toBe(false);
    expect(localStorage.getItem("flit-theme")).toBe("light");
  });

  it("al montarse respeta la preferencia oscura guardada", async () => {
    localStorage.setItem("flit-theme", "dark");
    await act(async () => {
      render(<ThemeToggle />);
    });
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });

  it("el script de arranque aplica la preferencia oscura antes de pintar", () => {
    localStorage.setItem("flit-theme", "dark");
    new Function(THEME_INIT_SCRIPT)();
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });

  it("el script de arranque no fuerza oscuro sin preferencia", () => {
    new Function(THEME_INIT_SCRIPT)();
    expect(document.documentElement.classList.contains("dark")).toBe(false);
  });
});
