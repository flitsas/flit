import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { SelectorModo } from "@/components/nuevo/SelectorModo";

// Uso de ejemplo: <SelectorModo modo={modo} onModo={setModo} bloqueado={cabeceraCreada}
//   explicacion={cabeceraCreada ? "Ya se creó la cabecera del lote: el modo no cambia." : null} />
// HU #14562 · M10 (auditoría): la explicación se asocia con `describedBy` del kit (sin setAttribute en un efecto) y el
// bloqueo deja el selector enfocable para que teclado y lector lleguen y oigan el motivo.

describe("SelectorModo (HU #14562 · M10)", () => {
  it("M10: la explicación visible es la descripción accesible del radiogroup desde el primer render", () => {
    render(<SelectorModo modo="express" onModo={vi.fn()} />);
    expect(screen.getByRole("radiogroup", { name: "Tipo de diagnóstico" })).toHaveAccessibleDescription(/Express: un vehículo por placa o VIN/);
  });

  it("M10: bloqueado sigue enfocable (aria-disabled, sin disabled) y anuncia el motivo", () => {
    const onModo = vi.fn();
    render(<SelectorModo modo="masivo" onModo={onModo} bloqueado explicacion="Ya se creó la cabecera del lote." />);
    const grupo = screen.getByRole("radiogroup", { name: "Tipo de diagnóstico" });
    expect(grupo).toHaveAttribute("aria-disabled", "true");
    expect(grupo).toHaveAccessibleDescription("Ya se creó la cabecera del lote.");
    const masivo = screen.getByRole("radio", { name: "Masivo" });
    expect(masivo).not.toBeDisabled();
    expect(masivo).toHaveAttribute("tabindex", "0");
    masivo.focus();
    expect(masivo).toHaveFocus();
    fireEvent.click(screen.getByRole("radio", { name: "Express" }));
    expect(onModo).not.toHaveBeenCalled();
  });

  it("M10: el componente ya no toca el DOM del kit con setAttribute", async () => {
    const { readFileSync } = await import("node:fs");
    const { resolve } = await import("node:path");
    const fuente = readFileSync(resolve(__dirname, "../../components/nuevo/SelectorModo.tsx"), "utf8");
    expect(fuente).not.toMatch(/setAttribute/);
  });
});
