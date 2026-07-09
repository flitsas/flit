import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusBadge } from "@/components/atom/StatusBadge";

/**
 * HU #10494 — StatusBadge tintado + unificación de chips de estado.
 *
 * Uso de ejemplo:
 *   render(<StatusBadge label="Aprobado" bg="rgba(34,197,94,0.12)" color="#15803d" border="rgba(34,197,94,0.3)" />)
 */

const FE_ROOT = path.resolve(__dirname, "..");
const read = (rel: string) => readFileSync(path.join(FE_ROOT, rel), "utf8");

describe("HU #10494 — StatusBadge (componente tintado)", () => {
  it("happy path: renderiza el label con rol status, aria-label y forma tintada", () => {
    render(
      <StatusBadge label="Aprobado" bg="rgba(34,197,94,0.12)" color="#15803d" border="rgba(34,197,94,0.3)" />,
    );
    const chip = screen.getByRole("status", { name: "Estado: Aprobado" });
    expect(chip).toHaveTextContent("Aprobado");
    expect(chip.className).toContain("rounded-full");
    expect(chip.className).toContain("border");
    expect(chip.style.color).toBeTruthy();
    expect(chip.style.background).toBeTruthy();
  });

  it("contrato: aplica bg, color y border vía estilo inline (no clases hardcodeadas)", () => {
    render(<StatusBadge label="Rechazado" bg="rgba(255,78,0,0.10)" color="#c2410c" border="rgba(255,78,0,0.3)" />);
    const chip = screen.getByRole("status");
    expect(chip.style.borderColor).toBeTruthy();
    expect(chip.style.borderColor).not.toBe(chip.style.color);
  });

  it("edge: si se omite `border`, el borde toma el color del texto", () => {
    render(<StatusBadge label="Neutro" bg="rgba(100,116,139,0.12)" color="rgb(71, 85, 105)" />);
    const chip = screen.getByRole("status");
    expect(chip.style.borderColor).toBe(chip.style.color);
  });
});

describe("HU #10494 — unificación de chips (tintado, sin cambiar vocabulario)", () => {
  const usuarios = read("components/atom/modules/Usuarios.tsx");
  const companies = read("components/admin/companies/CompanyListTable.tsx");
  const validaciones = read("components/atom/modules/Validaciones.tsx");
  const tramites = read("components/operacion/TramitesTable.tsx");

  it("Usuarios: STATUS_BADGE pasó de sólido (text-white) a tintado (bg/color/border) y usa StatusBadge", () => {
    expect(usuarios).toMatch(/StatusBadge/);
    expect(usuarios).toMatch(/active:\s*\{\s*label:[^}]*bg:[^}]*color:[^}]*border:/);
    // ya no queda el chip sólido de estado con text-white + background: badge.color
    expect(usuarios).not.toMatch(/text-white[^"]*"\s*style=\{\{\s*background:\s*badge\.color/);
  });

  it("Compañías: el badge Activa/Inactiva usa StatusBadge (no el span sólido text-white)", () => {
    expect(companies).toMatch(/StatusBadge/);
    expect(companies).not.toMatch(/text-white[^"]*"\s*style=\{\{\s*background:\s*c\.estadoActivo/);
  });

  it("Validaciones: ESTADO_META gana border y el chip usa StatusBadge", () => {
    expect(validaciones).toMatch(/StatusBadge/);
    expect(validaciones).toMatch(/aprobado:\s*\{[^}]*border:/);
  });

  it("Trámites: el chip de estado (base + async) usa StatusBadge, conservando estados.ts", () => {
    expect(tramites).toMatch(/StatusBadge/);
    expect(tramites).toMatch(/from '@\/lib\/tramites\/estados'/);
  });

  it("no quedan chips de ESTADO sólidos (text-white) en las tablas migradas", () => {
    for (const src of [usuarios, companies, validaciones, tramites]) {
      expect(src).not.toMatch(/rounded-full text-\[10px\] font-semibold text-white/);
    }
  });
});
