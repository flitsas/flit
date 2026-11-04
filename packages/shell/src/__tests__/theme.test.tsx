import { afterEach, describe, expect, it } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { ThemeToggle } from "../theme";
import {
  DARK_MODE_ENABLED,
  THEME_COMING_SOON_MESSAGE,
  THEME_INIT_SCRIPT,
} from "../theme-script";

// El tema claro/oscuro es el mismo en todos los productos: clase `dark` en <html> y preferencia en `flit-theme`.
// HU #14159: Dark Mode inhabilitado — forzar claro + aviso "próximamente".

afterEach(() => {
  cleanup();
  document.documentElement.classList.remove("dark");
  localStorage.clear();
});

describe("tema de la suite (HU #14159 — Dark Mode deshabilitado)", () => {
  it("el interruptor está visible pero deshabilitado con aviso de próximamente", () => {
    expect(DARK_MODE_ENABLED).toBe(false);
    render(<ThemeToggle />);
    const toggle = screen.getByRole("button", { name: THEME_COMING_SOON_MESSAGE });
    expect(toggle).toBeDisabled();
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    expect(toggle.closest("span")).toHaveAttribute("title", THEME_COMING_SOON_MESSAGE);
  });

  it("el clic no activa dark mode ni escribe dark en localStorage", async () => {
    render(<ThemeToggle />);
    const toggle = screen.getByRole("button", { name: THEME_COMING_SOON_MESSAGE });

    await act(async () => fireEvent.click(toggle));
    expect(document.documentElement.classList.contains("dark")).toBe(false);
    expect(localStorage.getItem("flit-theme")).not.toBe("dark");
  });

  it("al montarse fuerza claro aunque hubiera preferencia dark guardada", async () => {
    localStorage.setItem("flit-theme", "dark");
    document.documentElement.classList.add("dark");
    await act(async () => {
      render(<ThemeToggle />);
    });
    expect(document.documentElement.classList.contains("dark")).toBe(false);
    expect(localStorage.getItem("flit-theme")).toBe("light");
  });

  it("el script de arranque fuerza claro y limpia preferencia dark", () => {
    localStorage.setItem("flit-theme", "dark");
    document.documentElement.classList.add("dark");
    new Function(THEME_INIT_SCRIPT)();
    expect(document.documentElement.classList.contains("dark")).toBe(false);
    expect(localStorage.getItem("flit-theme")).toBe("light");
  });
});
