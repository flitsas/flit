// Ayuda de pruebas (HU #14584/#14585, spec-ui §1 y A7.1): comprueba que una tabla de Diagnóstico usa la «tabla por defecto»
// del kit — contenedor CONTENEDOR_TABLA_CLS (borde y radio del listado principal), cabecera CABECERA_TABLA_CLS/ESTILO de 12 px
// y filas con el hover --table-row-hover — en vez de recetas a mano.
import { expect } from "vitest";
import { CABECERA_TABLA_CLS, CONTENEDOR_TABLA_CLS, FILA_TABLA_HOVER_CLS } from "@/components/tablero/vistasDetalle/comun";

const clases = (el: Element | null) => (el?.getAttribute("class") ?? "").split(/\s+/);

/** Uso de ejemplo: esperarTablaPorDefecto(screen.getByRole("table", { name: "Saneamientos" })). */
export function esperarTablaPorDefecto(tabla: HTMLElement, { conHover = true }: { conHover?: boolean } = {}) {
  const contenedor = tabla.parentElement!;
  for (const c of CONTENEDOR_TABLA_CLS.split(" ")) expect(clases(contenedor), `contenedor sin ${c}`).toContain(c);
  for (const th of tabla.querySelectorAll("thead th")) {
    for (const c of CABECERA_TABLA_CLS.split(" ")) expect(clases(th), `cabecera sin ${c}`).toContain(c);
    expect((th as HTMLElement).style.background).toBe("var(--table-header-bg)");
  }
  if (!conHover) return;
  const filas = tabla.querySelectorAll("tbody tr");
  expect(filas.length).toBeGreaterThan(0);
  for (const tr of filas) for (const c of FILA_TABLA_HOVER_CLS.split(" ")) expect(clases(tr), `fila sin ${c}`).toContain(c);
}

/** La cabecera de la tabla por defecto es de 12 px (T4, plan §Dudas resueltas), no de 10 px. */
export function esperarCabecera12px() {
  expect(CABECERA_TABLA_CLS.split(" ")).toContain("text-xs");
  expect(CABECERA_TABLA_CLS).not.toContain("text-[10px]");
}
