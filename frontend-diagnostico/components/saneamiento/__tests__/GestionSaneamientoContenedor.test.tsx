import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
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
