import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";

/**
 * HU #10493 — Encabezado de módulo unificado (PageHeader / ModuleTitle).
 *
 * Uso de ejemplo:
 *   render(<ModuleTitle title="X" action={<button>Crear</button>} />)
 *   -> el botón queda FUERA de la caja del título (regla R7 del feedback).
 */

const FE_ROOT = path.resolve(__dirname, "..");
const read = (rel: string) => readFileSync(path.join(FE_ROOT, rel), "utf8");

describe("HU #10493 / HU #14474 — AC2: cabecera page-header de la propuesta (acciones dentro, a la derecha)", () => {
  // La propuesta «Diseño Inteligente» (rama `propuesta`) pone las acciones DENTRO de la cabecera, a la derecha del
  // texto; reemplaza la regla R7 de HU #10493, que las sacaba fuera de la caja.
  it("happy path: el botón de `action` va dentro de la cabecera, después del título", () => {
    render(<ModuleTitle title="Administración" action={<button>Crear</button>} />);
    const heading = screen.getByRole("heading", { name: "Administración" });
    const cabecera = heading.closest("header");
    const btn = screen.getByRole("button", { name: "Crear" });
    expect(cabecera).not.toBeNull();
    expect(cabecera!.contains(btn)).toBe(true);
    expect(heading.compareDocumentPosition(btn) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("contrato: `right` también vive dentro de la cabecera (indicador de estado)", () => {
    render(<ModuleTitle title="Validaciones" right={<span>En vivo</span>} />);
    const cabecera = screen.getByRole("heading", { name: "Validaciones" }).closest("header");
    expect(cabecera).not.toBeNull();
    expect(cabecera!.textContent).toContain("En vivo");
  });

  it("muestra el ícono del módulo (decorativo) elegido por el título", () => {
    // UI Kit 2026 (propuesta-2): el ícono del kit en su disco (`.kit-disco`), no la ilustración WebP.
    const { container } = render(<ModuleTitle title="Validaciones de identidad" />);
    const disco = container.querySelector("header .kit-disco");
    expect(disco).not.toBeNull();
    expect(disco?.getAttribute("aria-hidden")).toBe("true");
  });

  it("edge: sin action ni right renderiza el título sin romper", () => {
    expect(() => render(<ModuleTitle title="Solo título" />)).not.toThrow();
    expect(screen.getByRole("heading", { name: "Solo título" })).toBeInTheDocument();
  });
});

describe("HU #10493 — AC1/unificación: módulos migrados al encabezado unificado", () => {
  const usuarios = read("components/atom/modules/Usuarios.tsx");
  const rbac = read("components/atom/modules/RbacAdmin.tsx");
  const usersTable = read("components/atom/modules/users/UsersTable.tsx");

  it("Usuarios usa `action=` para el botón primario (fuera de la caja), no `right=`", () => {
    expect(usuarios).toMatch(/action=\{\s*\n?\s*tab === "usuarios"/);
    expect(usuarios).not.toMatch(/right=\{\s*\n?\s*tab === "usuarios"/);
  });

  it("Usuarios no tiene una fila de búsqueda separada (max-w-md) por encima de la tabla", () => {
    // La intención original de la HU sigue vigente: nada de una banda de búsqueda propia
    // ocupando el ancho por encima del listado. Lo que cambió con la unificación de tablas
    // (context/usuarios-contex.md) es DÓNDE vive: la búsqueda ya no está suelta en la fila de
    // tabs, sino junto a los filtros de perfil/rol/estado dentro de UsersTable, que es la
    // misma barra que usan la ficha de compañía y el hub OT.
    expect(usuarios).not.toMatch(/p-2\.5 rounded-xl border bg-white dark:bg-\[#0B0F14\] max-w-md/);
    expect(usuarios).not.toMatch(/placeholder="Buscar/);
    expect(usersTable).toMatch(/ml-auto|flex-1[\s\S]*?rounded-xl border bg-white/);
    expect(usersTable).toMatch(/aria-label="Buscar usuarios"/);
  });

  it("RBAC usa el componente ModuleTitle unificado con `action` (antes era un h1 plano)", () => {
    expect(rbac).toMatch(/import \{ ModuleTitle \}/);
    expect(rbac).toMatch(/<ModuleTitle[\s\S]*?title="RBAC — Administración"[\s\S]*?action=/);
  });
});
