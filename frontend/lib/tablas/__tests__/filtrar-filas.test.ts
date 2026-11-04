import { describe, expect, it } from "vitest";
import {
  condicionesAParams,
  filtrarFilas,
  normalizarTexto,
  type AccesoresFiltro,
  type ColumnaFiltro,
} from "../filtrar-filas";

interface Fila {
  nombre: string;
  estado: string;
  creado: string;
  nit: string;
}
const FILAS: Fila[] = [
  { nombre: "Ópera S.A.S.", estado: "ACTIVE", creado: "2026-03-01", nit: "900-123" },
  { nombre: "Beta Ltda", estado: "INACTIVE", creado: "2026-03-15T12:00:00", nit: "800456" },
  { nombre: "Gamma", estado: "PENDING", creado: "2026-04-01", nit: "700789" },
];
const ACC: AccesoresFiltro<Fila> = {
  columnas: { nombre: (f) => f.nombre, estado: (f) => f.estado, creado: (f) => f.creado },
  busqueda: (f) => [f.nombre, f.nit],
};

describe("filtrarFilas", () => {
  it("texto: contiene, sin mayúsculas, tildes, puntos, guiones ni espacios", () => {
    expect(normalizarTexto("Ópera S.A.S.")).toBe("operasas");
    const r = filtrarFilas(
      FILAS,
      [{ columnaId: "nombre", tipo: "texto", texto: "OPERA s-a-s" }],
      "",
      ACC,
    );
    expect(r.map((f) => f.nombre)).toEqual(["Ópera S.A.S."]);
  });

  it("selección: pertenece (OR dentro de la columna)", () => {
    const r = filtrarFilas(
      FILAS,
      [{ columnaId: "estado", tipo: "seleccion", valores: ["ACTIVE", "PENDING"] }],
      "",
      ACC,
    );
    expect(r).toHaveLength(2);
  });

  it("fecha: rango inclusivo en fecha local", () => {
    const r = filtrarFilas(
      FILAS,
      [{ columnaId: "creado", tipo: "fecha", desde: "2026-03-01", hasta: "2026-03-15" }],
      "",
      ACC,
    );
    expect(r.map((f) => f.nombre)).toEqual(["Ópera S.A.S.", "Beta Ltda"]);
  });

  it("combina condiciones y buscador con AND", () => {
    const r = filtrarFilas(
      FILAS,
      [{ columnaId: "estado", tipo: "seleccion", valores: ["ACTIVE", "INACTIVE"] }],
      "900123",
      ACC,
    );
    expect(r.map((f) => f.nombre)).toEqual(["Ópera S.A.S."]);
  });

  it("ignora condiciones vacías", () => {
    expect(
      filtrarFilas(FILAS, [{ columnaId: "nombre", tipo: "texto", texto: "  " }], "", ACC),
    ).toHaveLength(3);
  });
});

describe("condicionesAParams", () => {
  const COLS: ColumnaFiltro[] = [
    { id: "nombre", label: "Nombre", tipo: "texto", param: "name" },
    { id: "estado", label: "Estado", tipo: "seleccion", param: "status" },
    { id: "creado", label: "Creación", tipo: "fecha", param: "created" },
    {
      id: "vence",
      label: "Vence",
      tipo: "fecha",
      paramDesde: "expiresAfter",
      paramHasta: "expiresBefore",
    },
  ];
  it("mapea texto, selección y fechas, y omite lo vacío", () => {
    const p = condicionesAParams(
      [
        { columnaId: "nombre", tipo: "texto", texto: "abc" },
        { columnaId: "estado", tipo: "seleccion", valores: ["A", "B"] },
        { columnaId: "creado", tipo: "fecha", desde: "2026-01-01", hasta: "" },
        { columnaId: "vence", tipo: "fecha", desde: "2026-02-01", hasta: "2026-02-28" },
        { columnaId: "estado", tipo: "seleccion", valores: [] },
      ],
      COLS,
      " hola ",
    );
    expect(p).toEqual({
      search: "hola",
      name: "abc",
      status: "A,B",
      createdFrom: "2026-01-01",
      expiresAfter: "2026-02-01",
      expiresBefore: "2026-02-28",
    });
  });
});
