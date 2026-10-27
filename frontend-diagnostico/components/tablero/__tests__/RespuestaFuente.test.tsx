import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { RespuestaFuente } from "@/components/tablero/RespuestaFuente";

// Uso de ejemplo: <RespuestaFuente datos={detalle.resultado} etiqueta="Respuesta de la fuente" /> pinta TODOS los campos tal
// como llegan del API de detalle (HU #14087 AC8): nombres verbatim (snake_case), valores verbatim (null → «null», true/false,
// números sin formato, textos tal cual); objeto → tabla campo/valor; array de objetos → tabla con una columna por clave;
// array de primitivos → lista; objeto anidado → subsección con el nombre del campo; array vacío → «[]».

const filaDe = (tabla: HTMLElement, campo: string) => {
  const fila = within(tabla)
    .getAllByRole("row")
    .find((r) => r.querySelector("th[scope='row']")?.textContent === campo);
  if (!fila) throw new Error(`Sin fila ${campo}`);
  return fila;
};
const valorDe = (tabla: HTMLElement, campo: string) => within(filaDe(tabla, campo)).getByRole("cell");

describe("RespuestaFuente — AC8: campos y valores verbatim", () => {
  const datos = {
    monto_total: 350000,
    valor_decimal: 1250000.5,
    texto: "PARTICULAR  con espacios",
    vacio_nulo: null,
    activo: true,
    cancelado: false,
    cero: 0,
  };

  it("objeto → tabla campo/valor con los nombres snake_case y los valores sin formatear", () => {
    render(<RespuestaFuente datos={datos} etiqueta="Respuesta de la fuente" />);
    const tabla = screen.getByRole("table", { name: "Respuesta de la fuente" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Campo", "Valor"]);
    for (const c of within(tabla).getAllByRole("columnheader")) expect(c).toHaveAttribute("scope", "col");
    expect(within(tabla).getAllByRole("rowheader").map((r) => r.textContent)).toEqual(Object.keys(datos));
    expect(valorDe(tabla, "monto_total").textContent).toBe("350000");
    expect(valorDe(tabla, "valor_decimal").textContent).toBe("1250000.5");
    expect(valorDe(tabla, "texto").textContent).toBe("PARTICULAR  con espacios");
    expect(valorDe(tabla, "vacio_nulo").textContent).toBe("null");
    expect(valorDe(tabla, "activo").textContent).toBe("true");
    expect(valorDe(tabla, "cancelado").textContent).toBe("false");
    expect(valorDe(tabla, "cero").textContent).toBe("0");
    expect(tabla.textContent).not.toMatch(/\$|350\.000/);
  });

  it("array de objetos → subsección con el nombre del campo y tabla con una columna por clave (unión, en orden de aparición)", () => {
    render(
      <RespuestaFuente
        datos={{ comparendos: [{ numero: "C-0001", valor: "500000" }, { numero: "C-0002", estado: "PAGADO", valor: null }] }}
        etiqueta="Respuesta de la fuente"
      />,
    );
    const seccion = screen.getByRole("region", { name: "comparendos" });
    expect(within(seccion).getByRole("heading", { name: "comparendos" })).toBeInTheDocument();
    const tabla = within(seccion).getByRole("table", { name: "comparendos" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["numero", "valor", "estado"]);
    for (const c of within(tabla).getAllByRole("columnheader")) expect(c).toHaveAttribute("scope", "col");
    const filas = within(tabla).getAllByRole("row").slice(1);
    expect(filas.map((f) => within(f).getAllByRole("cell").map((c) => c.textContent))).toEqual([
      ["C-0001", "500000", ""],
      ["C-0002", "null", "PAGADO"],
    ]);
  });

  it("array de primitivos → lista; array vacío → «[]»", () => {
    render(<RespuestaFuente datos={{ categorias: ["B1", "C1"], licencias: [] }} etiqueta="Respuesta de la fuente" />);
    const tabla = screen.getByRole("table", { name: "Respuesta de la fuente" });
    expect(within(valorDe(tabla, "categorias")).getAllByRole("listitem").map((li) => li.textContent)).toEqual(["B1", "C1"]);
    expect(valorDe(tabla, "licencias").textContent).toBe("[]");
  });

  it("objeto anidado → subsección con el nombre del campo como título y su propia tabla campo/valor", () => {
    render(<RespuestaFuente datos={{ placa: "TST001", detalle_tecnico: { cilindraje: 1600, blindado: false } }} etiqueta="Respuesta de la fuente" />);
    const seccion = screen.getByRole("region", { name: "detalle_tecnico" });
    const tabla = within(seccion).getByRole("table", { name: "detalle_tecnico" });
    expect(valorDe(tabla, "cilindraje").textContent).toBe("1600");
    expect(valorDe(tabla, "blindado").textContent).toBe("false");
    expect(valorDe(screen.getByRole("table", { name: "Respuesta de la fuente" }), "placa").textContent).toBe("TST001");
  });

  it("dentro de una tabla de array, un valor que es lista u objeto se pinta anidado, sin perder campos", () => {
    render(<RespuestaFuente datos={{ personas: [{ doc: "****1222", licencias: [{ categoria: "B1" }], tags: ["x"] }] }} etiqueta="Respuesta de la fuente" />);
    const tabla = screen.getByRole("table", { name: "personas" });
    expect(within(tabla).getByRole("table", { name: "licencias" })).toHaveTextContent("B1");
    expect(within(tabla).getByRole("listitem")).toHaveTextContent("x");
  });

  it("R-20: claves de proveedor, transacción, origen, usuario o departamento no se pintan aunque llegaran", () => {
    render(
      <RespuestaFuente
        datos={{ monto_total: 1, proveedor: "PROV-FICTICIO", transaccion_proveedor_id: "TX-1", origen: "MANUAL", vigencias: [{ anio: 2026, proveedor: "PROV-FICTICIO" }] }}
        etiqueta="Respuesta de la fuente"
      />,
    );
    expect(document.body.textContent).not.toMatch(/PROV-FICTICIO|TX-1|MANUAL|proveedor|transacci|origen/i);
    expect(within(screen.getByRole("table", { name: "vigencias" })).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["anio"]);
  });
});
