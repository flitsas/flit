import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { DataTable, type DataTableColumn, type DataTableProps } from "../DataTable";

// Uso de ejemplo:
//   <DataTable ariaLabel="Placas del lote" columns={…} rows={…} getRowKey={(f) => f.id} minWidth={960} />
// HU #14695 (axe scrollable-region-focusable, WCAG 2.1.1): el contenedor `overflow-x-auto` que envuelve la <table> se puede
// desplazar en horizontal; debe ser región con nombre y parada de Tab, con foco visible por token del kit.

interface Fila {
  id: string;
}
const COLUMNAS: DataTableColumn<Fila>[] = [{ key: "id", header: "Id", render: (f) => f.id }];
const FILAS: Fila[] = [{ id: "a" }, { id: "b" }];

const pintar = (props: Partial<DataTableProps<Fila>> = {}) =>
  renderToStaticMarkup(
    createElement(DataTable<Fila>, { columns: COLUMNAS, rows: FILAS, getRowKey: (f: Fila) => f.id, minWidth: 960, ...props }),
  );

/** Etiqueta de apertura del contenedor con scroll (el div padre de la <table>). */
const contenedor = (html: string) => {
  const m = html.match(/<div[^>]*overflow-x-auto[^>]*>(?=<table)/);
  expect(m, "no se encontró el contenedor overflow-x-auto de la tabla").not.toBeNull();
  return m![0];
};

describe("DataTable — contenedor desplazable accesible por teclado", () => {
  it("es región con parada de Tab y toma el nombre de ariaLabel", () => {
    const div = contenedor(pintar({ ariaLabel: "Placas del lote" }));
    expect(div).toContain('role="region"');
    expect(div).toContain('tabindex="0"');
    expect(div).toContain('aria-label="Placas del lote"');
  });

  it("sin ariaLabel usa un nombre por defecto en español (nunca una región sin nombre)", () => {
    const div = contenedor(pintar());
    expect(div).toContain('role="region"');
    expect(div).toContain('tabindex="0"');
    expect(div).toContain('aria-label="Tabla desplazable"');
  });

  it("el foco del contenedor se ve con el token de acción del kit (focus-visible)", () => {
    const div = contenedor(pintar({ ariaLabel: "Placas del lote" }));
    expect(div).toMatch(/focus-visible:outline-\[var\(--di-action\)\]/);
  });

  it("la tabla conserva su propio nombre y sigue dentro del contenedor", () => {
    const html = pintar({ ariaLabel: "Placas del lote" });
    expect(html).toMatch(/<div[^>]*role="region"[^>]*><table[^>]*aria-label="Placas del lote"/);
  });
});
