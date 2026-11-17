import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { GestionSaneamiento } from "@/components/backoffice/GestionSaneamiento";
import { sesion } from "@/__tests__/configuracion/datos";
import { ResumenSaneamientosContext } from "@/lib/backoffice/useResumenSaneamientos";
import { TablaEditable } from "@flit/ui/TablaEditable";
import * as recetas from "@flit/ui/table-styles";
import { EMPRESAS, EMPRESA_A, RUTA_DETALLE, SID, deudaBackoffice, saneamientoBo, simularApi } from "./datosBackoffice";

// Uso de ejemplo: <TablaEditable apilado="contenedor" …/> pone `@container` en su envoltorio y pasa a tarjetas según el
// ancho del CONTENEDOR (container query), no del viewport. La gestión de un saneamiento (PS-5) lo usa para su tabla de
// conceptos, que en la columna derecha del diseño a dos columnas (≈680 px a 1280 px de pantalla) no cabe como tabla.
//
// Guarda del hallazgo E2E H-10 (HU #14577, capturas C-10): a 1280 px el segmentado Pendiente/Saneado/No procede se
// apilaba en vertical, la nota quedaba de ≈30 px y «Reintentar» se cortaba, porque la tabla seguía en modo tabla (el
// paso a tarjetas solo miraba el viewport, max-[900px]).

const h = vi.hoisted(() => ({ capacidades: { backoffice: true, gestionarSaneamiento: true } as Record<string, boolean> }));
vi.mock("@/components/DiagnosticoShell", () => ({ useSesion: () => ({ sesion: sesion(h.capacidades), cargando: false, error: null, recargar: () => {} }) }));
vi.mock("next/navigation", () => ({ usePathname: () => "/backoffice/saneamientos/x", useRouter: () => ({ replace: vi.fn(), push: vi.fn(), refresh: vi.fn() }) }));

const json = (cuerpo: unknown, status = 200) => Response.json(cuerpo, { status });
const clases = (receta: string | undefined) => (receta ?? "").split(/\s+/).filter(Boolean);
const r = recetas as Record<string, string | undefined>;

async function montarGestion() {
  simularApi({
    "GET /backoffice/empresas": () => json(EMPRESAS),
    [`GET ${RUTA_DETALLE}`]: () => json(saneamientoBo()),
    [`GET ${RUTA_DETALLE}/deuda`]: () => json(deudaBackoffice()),
  });
  render(
    <ResumenSaneamientosContext.Provider value={{ solicitados: 1, refrescar: vi.fn() }}>
      <GestionSaneamiento sid={SID} empresa={EMPRESA_A} />
    </ResumenSaneamientosContext.Provider>,
  );
  await screen.findByRole("heading", { level: 1, name: "TST001" });
  return screen.getByRole("table", { name: "Conceptos a sanear" });
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("H-10 — la tabla de conceptos de la gestión pasa a tarjetas por el ancho de su contenedor", () => {
  it("el envoltorio es un @container y la tabla, filas y celdas usan las recetas de contenedor, no las de viewport", async () => {
    const tabla = await montarGestion();
    expect(clases(r.TABLA_CONTENEDOR_TABLE_CLS).length).toBeGreaterThan(0);
    expect(tabla.parentElement?.classList).toContain("@container");
    for (const c of clases(r.TABLA_CONTENEDOR_TABLE_CLS)) expect(tabla.classList).toContain(c);
    for (const c of clases(recetas.TABLA_MOVIL_TABLE_CLS)) expect(tabla.classList).not.toContain(c);

    const filas = within(tabla).getAllByRole("row").filter((f) => f.closest("tbody"));
    expect(filas).toHaveLength(2);
    for (const fila of filas) {
      for (const c of clases(r.TABLA_CONTENEDOR_ROW_CLS)) expect(fila.classList).toContain(c);
      for (const celda of Array.from(fila.querySelectorAll("th, td"))) {
        for (const c of clases(r.TABLA_CONTENEDOR_CELL_CLS)) expect(celda.classList).toContain(c);
        for (const c of clases(recetas.TABLA_MOVIL_CELL_CLS)) expect(celda.classList).not.toContain(c);
      }
    }
    // Una tarjeta por renglón (el segmentado necesita el ancho de la tarjeta), no la rejilla de 300 px del viewport.
    const tbody = tabla.querySelector("tbody") as HTMLElement;
    for (const c of clases(r.TABLA_CONTENEDOR_TBODY_CLS)) expect(tbody.classList).toContain(c);
    expect(tbody.className).not.toMatch(/minmax\(min\(300px/);
  });

  it("el segmentado de resultado no lleva clases que lo apilen y la nota tiene un ancho legible (≥ 12rem)", async () => {
    const tabla = await montarGestion();
    const grupos = within(tabla).getAllByRole("radiogroup");
    expect(grupos).toHaveLength(2);
    for (const g of grupos) {
      expect(g.classList).toContain("flex-nowrap");
      expect(g.classList).not.toContain("flex-wrap");
      // Solo se parte con un contenedor más angosto que el propio segmentado (celular), nunca en la columna de 1280 px.
      expect(Array.from(g.classList).filter((c) => c.endsWith("flex-wrap"))).toEqual(["@max-[23rem]:flex-wrap"]);
      expect(g.className).not.toMatch(/(^|\s)(flex-col|grid)(\s|$)/);
    }
    const notas = within(tabla).getAllByRole("textbox");
    expect(notas).toHaveLength(2);
    for (const n of notas) {
      expect(n.classList).toContain("w-full");
      expect(n.parentElement?.classList).toContain("min-w-[12rem]");
    }
  });
});

describe("H-10 — TablaEditable: `apilado` es aditivo", () => {
  const props = {
    etiqueta: "T",
    filas: [{ id: "a", nombre: "A" }],
    columnas: [{ campo: "nombre" as const, titulo: "Nombre" }],
    clave: (f: { id: string }) => f.id,
    onGuardar: () => {},
  };

  it("sin la prop sigue con las recetas de viewport (max-[900px]) y sin @container", () => {
    render(<TablaEditable {...props} />);
    const tabla = screen.getByRole("table", { name: "T" });
    expect(tabla.parentElement?.classList).not.toContain("@container");
    for (const c of clases(recetas.TABLA_MOVIL_TABLE_CLS)) expect(tabla.classList).toContain(c);
  });

  it("con apilado=\"contenedor\" usa las recetas de contenedor en tabla, cabecera, cuerpo, filas y celdas", () => {
    render(<TablaEditable {...props} apilado="contenedor" />);
    const tabla = screen.getByRole("table", { name: "T" });
    expect(tabla.parentElement?.classList).toContain("@container");
    const thead = tabla.querySelector("thead") as HTMLElement;
    expect(clases(r.TABLA_CONTENEDOR_THEAD_CLS).length).toBeGreaterThan(0);
    for (const c of clases(r.TABLA_CONTENEDOR_THEAD_CLS)) expect(thead.classList).toContain(c);
    const celdaTitulo = tabla.querySelector("tbody th") as HTMLElement;
    for (const c of clases(r.TABLA_CONTENEDOR_CELL_TITULO_CLS)) expect(celdaTitulo.classList).toContain(c);
  });
});

// Hallazgo E2E H-13 (HU #14577, captura C-10-gestion-1023): en tarjeta, cada concepto mostraba una línea «Acciones» sin
// nada al lado, porque en guardado automático esa columna solo tiene «Reintentar» cuando el renglón falló. En tarjeta
// (contenedor < 80rem) la celda vacía se oculta; en la tabla ancha la celda y su cabecera siguen (estructura y lector).
// Uso de ejemplo: <TablaEditable apilado="contenedor" …/> → la celda «Acciones» sin botones lleva
// TABLA_CONTENEDOR_CELDA_SIN_CONTENIDO_CLS; con «Reintentar», «Guardar» o «Descartar», no.
describe("H-13 — la fila «Acciones» vacía no se pinta en la tarjeta", () => {
  const ocultaEnTarjeta = () => clases(r.TABLA_CONTENEDOR_CELDA_SIN_CONTENIDO_CLS);
  const celdaAcciones = (fila: Element) => fila.querySelector('td[data-label="Acciones"]') as HTMLElement;
  const props = {
    etiqueta: "T",
    filas: [{ id: "a", nombre: "A", nota: "x" }],
    columnas: [
      { campo: "nombre" as const, titulo: "Nombre" },
      { campo: "nota" as const, titulo: "Nota", editable: true },
    ],
    clave: (f: { id: string }) => f.id,
    onGuardar: () => {},
  };

  it("en la gestión, sin error, la celda «Acciones» de cada concepto se oculta en tarjeta y la cabecera sigue", async () => {
    const tabla = await montarGestion();
    expect(ocultaEnTarjeta().length).toBeGreaterThan(0);
    for (const c of ocultaEnTarjeta()) expect(c.startsWith("@max-[80rem]:")).toBe(true);
    const filas = within(tabla).getAllByRole("row").filter((f) => f.closest("tbody"));
    for (const fila of filas) {
      const celda = celdaAcciones(fila);
      expect(within(celda).queryAllByRole("button")).toHaveLength(0);
      for (const c of ocultaEnTarjeta()) expect(celda.classList).toContain(c);
    }
    expect(within(tabla).getByRole("columnheader", { name: "Acciones" })).toBeInTheDocument();
  });

  it("con «Reintentar» (renglón en error) la celda «Acciones» se ve en tarjeta", () => {
    render(<TablaEditable {...props} apilado="contenedor" guardado="automatico" estadoFila={() => "error"} />);
    const celda = celdaAcciones(screen.getByRole("row", { name: "a" }));
    expect(within(celda).getByRole("button", { name: "Reintentar a" })).toBeInTheDocument();
    expect(ocultaEnTarjeta().length).toBeGreaterThan(0);
    for (const c of ocultaEnTarjeta()) expect(celda.classList).not.toContain(c);
  });

  it("con «Guardar» y «Descartar» (guardado por botón, fila editada) la celda se ve; sin editar, se oculta", () => {
    render(<TablaEditable {...props} apilado="contenedor" />);
    const fila = screen.getByRole("row", { name: "a" });
    expect(ocultaEnTarjeta().length).toBeGreaterThan(0);
    for (const c of ocultaEnTarjeta()) expect(celdaAcciones(fila).classList).toContain(c);
    fireEvent.change(within(fila).getByRole("textbox", { name: "Nota de a" }), { target: { value: "y" } });
    expect(within(celdaAcciones(fila)).getByRole("button", { name: "Guardar a" })).toBeInTheDocument();
    for (const c of ocultaEnTarjeta()) expect(celdaAcciones(fila).classList).not.toContain(c);
  });

  it("aditivo: sin apilado=\"contenedor\" (viewport) la celda «Acciones» no cambia de clases", () => {
    render(<TablaEditable {...props} />);
    const celda = celdaAcciones(screen.getByRole("row", { name: "a" }));
    for (const c of clases(recetas.TABLA_MOVIL_CELL_CLS)) expect(celda.classList).toContain(c);
    expect(Array.from(celda.classList).some((c) => c.includes("hidden"))).toBe(false);
  });
});
