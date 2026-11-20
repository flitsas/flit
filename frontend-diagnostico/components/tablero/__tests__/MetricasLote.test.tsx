import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { MetricasLote } from "@/components/tablero/MetricasLote";
import { grilla } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <MetricasLote grilla={grilla} filtro={filtro} onFiltro={setFiltro} /> → embudo del kit en una sola fila con
// seis casillas (ícono + etiqueta + cifra con el tono del estado); cada una es un filtro exclusivo con aria-pressed.
// HU #14584 (spec-ui §6.3, A6.5).

const CONTEOS = { vehiculos: 12, terminados: 9, con_error: 2, con_deuda: 0, bloqueados: 1, listos_tramite: 4 };
const pintar = (filtro: Parameters<typeof MetricasLote>[0]["filtro"] = null, onFiltro = vi.fn()) =>
  render(<MetricasLote grilla={grilla({ conteos: CONTEOS })} filtro={filtro} onFiltro={onFiltro} />);

describe("MetricasLote — HU #14584 A6.5: una sola fila con ícono y tono por estado", () => {
  it("es un grupo «Filtros del lote» con las seis casillas en orden y su nombre accesible «Etiqueta: cifra»", () => {
    pintar();
    const grupo = screen.getByRole("group", { name: "Filtros del lote" });
    expect(within(grupo).getAllByRole("button").map((b) => b.getAttribute("aria-label"))).toEqual([
      "Vehículos: 12",
      "Terminados: 9",
      "Con error: 2",
      "Con deuda: 0",
      "Bloqueados: 1",
      "Listos para trámite: 4",
    ]);
  });

  it("el embudo se fuerza a una fila (grid-flow-col auto-cols-fr) y conserva el carril deslizable < 640 px", () => {
    pintar();
    const clases = screen.getByRole("group", { name: "Filtros del lote" }).className.split(/\s+/);
    expect(clases).toContain("grid-flow-col");
    expect(clases).toContain("auto-cols-fr");
    expect(clases.some((c) => c.startsWith("grid-cols-"))).toBe(false);
    expect(clases).toContain("max-sm:overflow-x-auto");
  });

  it("cada casilla lleva su ícono decorativo del disco del kit", () => {
    const { container } = pintar();
    const iconos = [...container.querySelectorAll("img")];
    expect(iconos.map((i) => i.getAttribute("src"))).toEqual([
      "/assets/kit/estado-placa.svg",
      "/assets/kit/estado-aprobado.svg",
      "/assets/kit/aviso-error.svg",
      "/assets/kit/aviso-alerta.svg",
      "/assets/kit/estado-anulado.svg",
      "/assets/kit/estado-entregado.svg",
    ]);
    for (const i of iconos) expect(i).toHaveAttribute("alt", "");
  });

  it("la cifra se pinta con el token de su tono; una cifra 0 va en neutral", () => {
    pintar();
    const cifra = (nombre: string) => screen.getByRole("button", { name: nombre }).querySelector("[data-tono]")!;
    expect(cifra("Vehículos: 12")).toHaveAttribute("data-tono", "info");
    expect(cifra("Terminados: 9")).toHaveAttribute("data-tono", "success");
    expect(cifra("Con error: 2")).toHaveAttribute("data-tono", "danger");
    expect(cifra("Con deuda: 0")).toHaveAttribute("data-tono", "neutral");
    expect(cifra("Bloqueados: 1")).toHaveAttribute("data-tono", "danger");
    expect(cifra("Listos para trámite: 4")).toHaveAttribute("data-tono", "entregado");
    expect((cifra("Con error: 2") as HTMLElement).style.color).toBe("var(--badge-danger-fg)");
    expect((cifra("Listos para trámite: 4") as HTMLElement).style.color).toBe("var(--estado-entregado-fg)");
    expect((cifra("Con deuda: 0") as HTMLElement).style.color).toBe("var(--badge-neutral-fg)");
  });

  it("filtro exclusivo: pulsar activa la casilla; pulsar la activa la apaga", () => {
    const onFiltro = vi.fn();
    const { rerender } = pintar(null, onFiltro);
    fireEvent.click(screen.getByRole("button", { name: "Con error: 2" }));
    expect(onFiltro).toHaveBeenLastCalledWith("con_error");
    rerender(<MetricasLote grilla={grilla({ conteos: CONTEOS })} filtro="con_error" onFiltro={onFiltro} />);
    const activa = screen.getByRole("button", { name: "Con error: 2" });
    expect(activa).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Vehículos: 12" })).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(activa);
    expect(onFiltro).toHaveBeenLastCalledWith(null);
  });

  it("sin conteos (API previa) quedan Vehículos y Terminados del avance", () => {
    render(<MetricasLote grilla={grilla()} filtro={null} onFiltro={vi.fn()} />);
    expect(within(screen.getByRole("group", { name: "Filtros del lote" })).getAllByRole("button")).toHaveLength(2);
  });
});
