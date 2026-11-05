import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { SeccionModo, type SeccionModoProps } from "@/components/parametrizacion/SeccionModo";
import { TIPOS } from "./datos";

// Uso de ejemplo: <SeccionModo modo="CON_ANALISIS" tipos={{ tipo: "listo", tipos }} tipoCodigo="" onModo={...}
// onTipo={...} onReintentarTipos={...} /> muestra el combobox de tipo de trámite.

function montar(props: Partial<SeccionModoProps> = {}) {
  const base: SeccionModoProps = {
    modo: "SIMPLE",
    onModo: vi.fn(),
    tipoCodigo: "",
    onTipo: vi.fn(),
    tipos: { tipo: "listo", tipos: TIPOS },
    onReintentarTipos: vi.fn(),
  };
  const todas = { ...base, ...props };
  const r = render(<SeccionModo {...todas} />);
  return { ...todas, rerender: (p: Partial<SeccionModoProps>) => r.rerender(<SeccionModo {...todas} {...p} />) };
}

describe("SeccionModo — exclusión de opciones (AC 4)", () => {
  it("Simple y Con Análisis IA son dos opciones excluyentes", () => {
    const { onModo, rerender } = montar();
    const simple = screen.getByRole("radio", { name: "Simple" });
    const ia = screen.getByRole("radio", { name: "Con Análisis IA" });
    expect(simple).toBeChecked();
    expect(ia).not.toBeChecked();
    expect(simple).toHaveAttribute("name", ia.getAttribute("name"));

    fireEvent.click(ia);
    expect(onModo).toHaveBeenCalledWith("CON_ANALISIS");

    rerender({ modo: "CON_ANALISIS" });
    expect(screen.getByRole("radio", { name: "Simple" })).not.toBeChecked();
    expect(screen.getByRole("radio", { name: "Con Análisis IA" })).toBeChecked();
  });

  it("en Simple no se pide el tipo de trámite", () => {
    montar();
    expect(screen.queryByRole("combobox", { name: /tipo de trámite/i })).toBeNull();
  });

  it("en Con Análisis IA el combobox es obligatorio y trae los tipos de GET /tipos-tramite", () => {
    const { onTipo } = montar({ modo: "CON_ANALISIS" });
    const combo = screen.getByRole("combobox", { name: /tipo de trámite/i });
    expect(combo).toBeRequired();
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Elige un tipo de trámite", "TRASPASO", "TRASPASO UNILATERAL"]);

    fireEvent.change(combo, { target: { value: "TRASPASO_UNILATERAL" } });
    expect(onTipo).toHaveBeenCalledWith("TRASPASO_UNILATERAL");
  });

  it("el error del tipo queda asociado al combobox", () => {
    montar({ modo: "CON_ANALISIS", errorTipo: "Elige el tipo de trámite." });
    const combo = screen.getByRole("combobox", { name: /tipo de trámite/i });
    expect(combo).toHaveAttribute("aria-invalid", "true");
    expect(combo).toHaveAccessibleDescription(/elige el tipo de trámite/i);
  });
});

describe("SeccionModo — catálogo no disponible (503 con Retry-After)", () => {
  it("muestra el error y no deja reintentar hasta que pase el tiempo", () => {
    const { onReintentarTipos, rerender } = montar({
      modo: "CON_ANALISIS",
      tipos: { tipo: "error", mensaje: "El catálogo no está disponible.", segundosRestantes: 30 },
    });
    expect(screen.getByRole("alert")).toHaveTextContent("El catálogo no está disponible.");
    expect(screen.getByRole("button", { name: "Reintentar en 30 s" })).toBeDisabled();

    rerender({ tipos: { tipo: "error", mensaje: "El catálogo no está disponible.", segundosRestantes: 0 } });
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(onReintentarTipos).toHaveBeenCalledTimes(1);
  });

  it("mientras carga el catálogo lo anuncia", () => {
    montar({ modo: "CON_ANALISIS", tipos: { tipo: "cargando" } });
    expect(screen.getByRole("status")).toHaveTextContent(/cargando los tipos de trámite/i);
  });
});
