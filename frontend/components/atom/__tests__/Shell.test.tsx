// Dock del Shell (artefacto inferior): el FAB de inicio debe quedar SIEMPRE centrado y el
// botón "Ayuda" (soporte universal) debe verse aunque RBAC filtre los demás módulos.
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { Shell } from "../Shell";

// Shell usa usePathname para resaltar la ruta admin activa.
vi.mock("next/navigation", () => ({ usePathname: () => "/" }));

function renderShell(visibleModuleCodes?: string[]) {
  return render(
    <Shell active="dashboard" onNav={vi.fn()} visibleModuleCodes={visibleModuleCodes}>
      <div>contenido</div>
    </Shell>,
  );
}

describe("Shell — dock", () => {
  it("muestra 'Ayuda' aunque RBAC no la incluya en los módulos visibles", () => {
    renderShell(["dashboard", "reportes"]);
    expect(screen.getByRole("button", { name: "Ayuda" })).toBeInTheDocument();
  });

  it("mantiene el FAB de inicio centrado: mismo nº de elementos a cada lado", () => {
    // Número impar de entradas para ejercitar el relleno con espaciador.
    renderShell(["dashboard", "reportes", "validaciones"]);
    const fab = screen.getByRole("button", { name: "Inicio FLIT" });
    const dock = fab.parentElement;
    expect(dock).not.toBeNull();
    const children = Array.from(dock!.children);
    const fabIndex = children.indexOf(fab);
    const before = fabIndex;
    const after = children.length - fabIndex - 1;
    expect(before).toBe(after);
  });
});
