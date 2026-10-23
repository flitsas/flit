import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

/**
 * HU #13920 — guarda del estándar de tablas (Feature #13915). Toda tabla de FLIT 2.0, existente o
 * nueva, usa el MISMO diseño (el de la tabla de trámites: `DataTable` o `<table>` con
 * `table-styles`) y la MISMA paginación (`Pagination`/`PageNav` con «Filas por página» y estado de
 * `usePaginacion("<clave>")`). Sin lista de excepciones: la única tabla sin paginación es una ficha
 * de pares fijos campo/valor, y se reconoce porque su `<tbody>` no recorre ninguna colección.
 *
 * Es una revisión estática del código fuente, no de la pantalla: lo que asegura es que nadie dibuja
 * una tabla a mano ni con un paginador propio. Si falla, el mensaje dice el archivo y la regla.
 */

const FE_ROOT = path.resolve(__dirname, "..");
const CARPETAS = ["app", "components"];

function fuentes(dir: string): string[] {
  return readdirSync(dir).flatMap((nombre) => {
    const ruta = path.join(dir, nombre);
    if (statSync(ruta).isDirectory()) return nombre === "__tests__" ? [] : fuentes(ruta);
    return ruta.endsWith(".tsx") ? [ruta] : [];
  });
}

/**
 * Quita los comentarios antes de mirar el código: los comentarios del repo nombran `<table>` a menudo
 * («antes tenía su propia <table>…») y no deben contar como una tabla dibujada.
 */
const sinComentarios = (src: string) =>
  src.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/[^\n]*/g, "$1");

const ARCHIVOS = CARPETAS.flatMap((c) => fuentes(path.join(FE_ROOT, c))).map((ruta) => ({
  rel: path.relative(FE_ROOT, ruta).split(path.sep).join("/"),
  src: sinComentarios(readFileSync(ruta, "utf8")),
}));

/**
 * Filas separadas como tarjetas: por clase (`border-separate`), por estilo
 * (`borderCollapse: "separate"`) o con la receta `table-cardlist` (`CARDLIST_TABLE`).
 */
const filasSeparadas = (src: string) =>
  /border-separate/.test(src) || /borderCollapse:\s*["']separate["']/.test(src) || /CARDLIST_TABLE\b/.test(src);

/**
 * Cabecera del modelo: las constantes de `table-styles` o la receta `table-cardlist` (CARDLIST_*),
 * que es la misma imagen expresada con los tokens `--table-*` y ya resuelve el tema oscuro.
 */
const cabeceraEstandar = (src: string) =>
  (/TABLA_HEADER_CELL_CLS/.test(src) && /TABLA_HEADER_BG/.test(src)) || /CARDLIST_TH\b/.test(src);

/** Fila-tarjeta que se eleva con sombra azul al pasar el puntero. */
const filaEstandar = (src: string) => /TABLA_ROW_HOVER_CLS/.test(src) || /CARDLIST_ROW\b/.test(src);

const usaPaginacionEstandar = (src: string) =>
  /<(Pagination|PageNav|OtTablePagination)\b/.test(src) || /\bpagination=\{/.test(src);

/** Cuerpos `<tbody>…</tbody>` de un archivo. */
const cuerpos = (src: string) => [...src.matchAll(/<tbody\b[\s\S]*?<\/tbody>/g)].map((m) => m[0]);

/** Archivos con un `<table>` propio (no el de `DataTable`). */
const tablasPropias = ARCHIVOS.filter((a) => /<table\b/.test(a.src));

describe("HU #13920 — diseño estándar de toda tabla", () => {
  it("toda <table> propia usa la cabecera del modelo", () => {
    const fuera = tablasPropias
      .filter((a) => !cabeceraEstandar(a.src))
      .map((a) => `${a.rel} — <table> sin la cabecera del modelo (usa DataTable, table-styles o table-cardlist)`);
    expect(fuera).toEqual([]);
  });

  it("toda <table> propia con filas de una colección usa la fila-tarjeta con sombra azul", () => {
    const fuera = tablasPropias
      .filter((a) => cuerpos(a.src).some((c) => /\.map\(/.test(c)))
      .filter((a) => !filaEstandar(a.src))
      .map((a) => `${a.rel} — filas sin la fila-tarjeta del modelo (TABLA_ROW_HOVER_CLS / CARDLIST_ROW)`);
    expect(fuera).toEqual([]);
  });

  it("toda <table> propia separa las filas como tarjetas (border-separate), no las colapsa", () => {
    const fuera = tablasPropias
      .filter((a) => !filasSeparadas(a.src))
      .map((a) => `${a.rel} — <table> sin border-separate border-spacing-y-2`);
    expect(fuera).toEqual([]);
  });
});

describe("HU #13920 — paginación estándar de toda tabla", () => {
  it("toda <table> cuyas filas salen de una colección tiene la paginación estándar", () => {
    const fuera = tablasPropias
      .filter((a) => cuerpos(a.src).some((c) => /\.map\(/.test(c)))
      .filter((a) => !usaPaginacionEstandar(a.src))
      .map((a) => `${a.rel} — tabla de registros sin Pagination/PageNav`);
    expect(fuera).toEqual([]);
  });

  it("todo DataTable recibe `pagination`", () => {
    const fuera = ARCHIVOS.filter((a) => {
      const tablas = (a.src.match(/<DataTable\b/g) ?? []).length;
      const paginadas = (a.src.match(/\bpagination=\{/g) ?? []).length;
      return tablas > paginadas;
    }).map((a) => `${a.rel} — <DataTable> sin pagination`);
    expect(fuera).toEqual([]);
  });

  it("toda paginación ofrece «Filas por página»", () => {
    const fuera = ARCHIVOS.filter((a) => usaPaginacionEstandar(a.src))
      .filter((a) => !a.rel.endsWith("OtTablePagination.tsx"))
      .filter((a) => !/onPageSizeChange|onTakeChange|Filas por página/.test(a.src))
      .map((a) => `${a.rel} — paginación sin selector «Filas por página»`);
    expect(fuera).toEqual([]);
  });

  it("nadie define tamaños de página propios: son siempre 10/20/30/50", () => {
    const fuera = ARCHIVOS.filter(
      (a) => /pageSizeOptions/.test(a.src) || /const TAMANOS_DE_PAGINA\s*=/.test(a.src),
    ).map((a) => `${a.rel} — tamaños de página propios`);
    expect(fuera).toEqual([]);
  });

  it("usePaginacion lleva siempre su clave de tabla (para recordar el tamaño en la sesión)", () => {
    const fuera = ARCHIVOS.filter((a) => /usePaginacion\(\s*(\)|\d)/.test(a.src)).map(
      (a) => `${a.rel} — usePaginacion() sin clave`,
    );
    expect(fuera).toEqual([]);
  });

  it("cada tabla tiene su propia clave: no se repiten entre pantallas", () => {
    const vistas = new Map<string, string[]>();
    for (const a of ARCHIVOS) {
      const literales = [
        ...a.src.matchAll(/use(?:Paginacion|TamanoDePagina)\(\s*["'`]([^"'`]+)["'`]/g),
        ...a.src.matchAll(/const CLAVE_TABLA\s*=\s*["'`]([^"'`]+)["'`]/g),
      ].map((m) => m[1]);
      for (const clave of literales) vistas.set(clave, [...(vistas.get(clave) ?? []), a.rel]);
    }
    const repetidas = [...vistas].filter(([, en]) => en.length > 1).map(([c, en]) => `${c}: ${en.join(", ")}`);
    expect(repetidas).toEqual([]);
  });
});

describe("HU #13920 — la guarda detecta lo que debe (negativos)", () => {
  // Los mismos predicados, contra fuentes de ejemplo: si un refactor los afloja, esto lo nota.
  const tablaAMano = `<table className="w-full"><thead><tr><th>Placa</th></tr></thead>
    <tbody>{filas.map((f) => <tr key={f.id}><td>{f.placa}</td></tr>)}</tbody></table>`;
  const fichaFija = `<table className="w-full border-separate border-spacing-y-2"><thead><tr>
    <th className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG }}>Campo</th></tr></thead>
    <tbody><tr><th>Nombre</th><td>{nombre}</td></tr></tbody></table>`;

  it("una tabla nueva hecha a mano falla en diseño y en paginación", () => {
    expect(cabeceraEstandar(tablaAMano)).toBe(false);
    expect(filaEstandar(tablaAMano)).toBe(false);
    expect(filasSeparadas(tablaAMano)).toBe(false);
    expect(cuerpos(tablaAMano).some((c) => /\.map\(/.test(c))).toBe(true);
    expect(usaPaginacionEstandar(tablaAMano)).toBe(false);
  });

  it("una ficha fija campo/valor exige el diseño pero no la paginación", () => {
    expect(cabeceraEstandar(fichaFija)).toBe(true);
    expect(filasSeparadas(fichaFija)).toBe(true);
    expect(cuerpos(fichaFija).some((c) => /\.map\(/.test(c))).toBe(false);
  });

  it("usePaginacion() sin clave se detecta", () => {
    expect(/usePaginacion\(\s*(\)|\d)/.test("const pg = usePaginacion();")).toBe(true);
    expect(/usePaginacion\(\s*(\)|\d)/.test('const pg = usePaginacion("usuarios.lista");')).toBe(false);
  });
});
