import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CAMPO_CLS, CAMPO_FECHA_CLS } from "@/components/configuracion/estilos";
import { FiltrosVehiculos } from "@/components/vehiculos/FiltrosVehiculos";
import { filtrosVehiculosDesdeUrl } from "@/lib/vehiculos/listado";

// Uso de ejemplo (hallazgo E2E H-7 de HU #14577, PS-4 bandeja-360):
//   import { CAMPO_FECHA_CLS } from "@/components/configuracion/estilos";
//   <input type="date" className={CAMPO_FECHA_CLS} />
// En Chromium el <input type="date"> tiene una parada de Tab en el botón del calendario (shadow DOM del agente de usuario).
// Con el foco ahí el host deja de casar con :focus y :focus-visible, así que CAMPO_CLS no pinta ni contorno ni sombra;
// :focus-within sí casa. CAMPO_FECHA_CLS repite el foco del kit con focus-within para esa parada.

const RAIZ = join(__dirname, "..", "..");
/** Archivos de la oleada de saneamiento con campos de fecha (los demás quedan reportados fuera de alcance). */
const ARCHIVOS_OLEADA = ["backoffice/BandejaSaneamientos.tsx", "vehiculos/FiltrosVehiculos.tsx"];
const FOCO_DENTRO = [
  "focus-within:border-[color:var(--di-action)]",
  "focus-within:shadow-[var(--kit-sombra)]",
  "focus-within:outline-2",
  "focus-within:outline-offset-2",
  "focus-within:outline-ring",
];

describe("CAMPO_FECHA_CLS — foco visible en el botón del calendario (H-7)", () => {
  it("parte de CAMPO_CLS y añade el borde, la sombra y el contorno del kit con focus-within", () => {
    expect(CAMPO_FECHA_CLS.startsWith(CAMPO_CLS)).toBe(true);
    const clases = CAMPO_FECHA_CLS.split(/\s+/);
    for (const c of FOCO_DENTRO) expect(clases).toContain(c);
  });

  it("CAMPO_CLS no cambia para los demás consumidores (texto, select, textarea)", () => {
    expect(CAMPO_CLS).not.toContain("focus-within:");
  });

  it("los <input type=\"date\"> de la oleada usan CAMPO_FECHA_CLS y no CAMPO_CLS a secas", () => {
    for (const archivo of ARCHIVOS_OLEADA) {
      const fuente = readFileSync(join(RAIZ, archivo), "utf8");
      const fechas = fuente.match(/<input\b[^>]*?type="date"[\s\S]*?\/>/g) ?? [];
      expect(fechas.length, archivo).toBeGreaterThan(0);
      for (const f of fechas) expect(f, archivo).not.toMatch(/className=\{CAMPO_CLS\}/);
      expect(fuente, archivo).toContain("CAMPO_FECHA_CLS");
    }
  });

  it("FiltrosVehiculos pinta «Desde» y «Hasta» con el foco focus-within y conserva el ancho", () => {
    render(
      <FiltrosVehiculos valor={filtrosVehiculosDesdeUrl(new URLSearchParams())} error={null} onCambiar={vi.fn()} onLimpiar={vi.fn()} onActualizar={vi.fn()} />,
    );
    for (const nombre of ["Desde", "Hasta"]) {
      const campo = screen.getByLabelText(nombre);
      for (const c of FOCO_DENTRO) expect(campo, nombre).toHaveClass(c);
      expect(campo, nombre).toHaveClass("min-[480px]:w-44");
    }
    expect(screen.getByRole("textbox", { name: "Placa" })).not.toHaveClass("focus-within:outline-2");
  });
});
