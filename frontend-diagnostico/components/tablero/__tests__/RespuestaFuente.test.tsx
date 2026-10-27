import { describe, expect, it } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { RespuestaFuente } from "@/components/tablero/RespuestaFuente";
import { respuestaFuenteRunt } from "@/lib/tablero/__tests__/datosDetalle";

// Uso de ejemplo: <RespuestaFuente datos={detalle.resultado} etiqueta="Respuesta de la fuente" /> pinta TODOS los campos tal
// como llegan del API de detalle (HU #14087 AC8): nombres verbatim (snake_case), valores verbatim (null → «null», true/false,
// números sin formato, textos tal cual); objeto → tabla campo/valor; array de objetos → tabla con una columna por clave;
// array de primitivos → lista; objeto anidado → subsección con el nombre del campo; array vacío → «[]». AC8 ajustado: pinta la
// respuesta completa (respuesta_fuente) sin ocultar ninguna clave; subsecciones de primer nivel plegables y abiertas, las de
// segundo nivel plegables y cerradas, con «Expandir todo / Contraer todo» y el número de elementos de cada lista.

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
    const seccion = screen.getByRole("region", { name: "comparendos (2)" });
    expect(within(seccion).getByRole("heading", { name: "comparendos (2)" })).toBeInTheDocument();
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

  it("sin filtro de claves: proveedor, transacción, origen, usuario y departamento se pintan verbatim como cualquier otra", () => {
    render(
      <RespuestaFuente
        datos={{ monto_total: 1, proveedor: "PROV-FICTICIO", transaccion_proveedor_id: "TX-1", origen: "API", usuario: "", vigencias: [{ anio: 2026, departamento: "DEPTO PRUEBA" }] }}
        etiqueta="Respuesta de la fuente"
      />,
    );
    const tabla = screen.getByRole("table", { name: "Respuesta de la fuente" });
    expect(within(tabla).getAllByRole("rowheader").map((r) => r.textContent)).toEqual(["monto_total", "proveedor", "transaccion_proveedor_id", "origen", "usuario"]);
    expect(valorDe(tabla, "proveedor").textContent).toBe("PROV-FICTICIO");
    expect(valorDe(tabla, "usuario").textContent).toBe("");
    expect(within(screen.getByRole("table", { name: "vigencias" })).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["anio", "departamento"]);
  });
});

describe("RespuestaFuente — AC8 ajustado: respuesta completa grande y profunda", () => {
  const pintar = () => render(<RespuestaFuente datos={respuestaFuenteRunt()} etiqueta="Respuesta de la fuente" />);

  it("pinta todos los campos de primer nivel con los valores por defecto verbatim (\"\", 0, [], false)", () => {
    pintar();
    const tabla = screen.getByRole("table", { name: "Respuesta de la fuente" });
    expect(within(tabla).getAllByRole("rowheader").map((r) => r.textContent)).toEqual(["proveedor", "tipo_error", "mensaje_error", "http_status", "duracion_ms", "errores"]);
    expect(valorDe(tabla, "tipo_error").textContent).toBe("");
    expect(valorDe(tabla, "duracion_ms").textContent).toBe("0");
    expect(valorDe(tabla, "errores").textContent).toBe("[]");
    expect(valorDe(within(screen.getByRole("region", { name: "resultado" })).getByRole("table", { name: "resultado" }), "desde_cache").textContent).toBe("false");
  });

  it("anidados profundos: subsección por objeto, tabla por array de objetos con contador, mapas clave-valor como tabla", () => {
    pintar();
    const vehiculo = screen.getByRole("region", { name: "vehiculo" });
    expect(valorDe(within(vehiculo).getByRole("table", { name: "vehiculo" }), "plate").textContent).toBe("TST001");
    const general = within(vehiculo).getByRole("region", { name: "informacion_general" });
    expect(valorDe(within(general).getByRole("table", { name: "informacion_general" }), "cilindraje").textContent).toBe("0");
    expect(valorDe(within(general).getByRole("table", { name: "informacion_general" }), "color").textContent).toBe("");
    const soat = within(vehiculo).getByRole("region", { name: "soat (2)" });
    expect(within(within(soat).getByRole("table", { name: "soat" })).getAllByRole("row").slice(1)).toHaveLength(2);
    expect(valorDe(within(vehiculo).getByRole("table", { name: "vehiculo" }), "tecno_mecanica").textContent).toBe("[]");
    const propietarios = within(vehiculo).getByRole("table", { name: "propietarios_actuales" });
    expect(within(propietarios).getByRole("table", { name: "direcciones" })).toHaveTextContent("DEPTO PRUEBA");
    expect(within(propietarios).getByText("PERSONA FICTICIA UNO")).toBeInTheDocument();
    const accidentes = within(vehiculo).getByRole("table", { name: "accident_rate" });
    const campos = within(accidentes).getByRole("table", { name: "campos" });
    expect(valorDe(campos, "aseguradora").textContent).toBe("ASEGURADORA PRUEBA");
  });

  it("plegables: el primer nivel abierto, el segundo cerrado; el título es el summary con nombre accesible", () => {
    const { container } = pintar();
    const vehiculo = screen.getByRole("region", { name: "vehiculo" });
    const plegable = vehiculo.querySelector("details")!;
    expect(plegable).toHaveAttribute("open");
    expect(plegable.querySelector(":scope > summary")).toHaveTextContent("vehiculo");
    const soat = within(vehiculo).getByRole("region", { name: "soat (2)" }).querySelector("details")!;
    expect(soat).not.toHaveAttribute("open");
    expect(soat.querySelector(":scope > summary")).toHaveTextContent("soat (2)");
    for (const s of container.querySelectorAll("summary")) expect(s.className).toMatch(/focus-visible:outline/);
  });

  it("«Expandir todo» abre todas las subsecciones y «Contraer todo» las cierra, sin rehacer el árbol", () => {
    const { container } = pintar();
    const plegables = Array.from(container.querySelectorAll("details"));
    expect(plegables.length).toBeGreaterThan(2);
    fireEvent.click(screen.getByRole("button", { name: "Expandir todo" }));
    expect(plegables.every((d) => d.open)).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Contraer todo" }));
    expect(plegables.every((d) => !d.open)).toBe(true);
    expect(Array.from(container.querySelectorAll("details"))).toEqual(plegables);
  });

  it("sin subsecciones no ofrece «Expandir todo»", () => {
    render(<RespuestaFuente datos={{ placa: "TST002", activo: true }} etiqueta="Respuesta de la fuente" />);
    expect(screen.queryByRole("button", { name: /expandir todo/i })).not.toBeInTheDocument();
  });
});
