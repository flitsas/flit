// Calidad de interfaz del módulo "Historial por placa" (Feature #12189 · HU #12197).
//
// Es la pasada de fidelidad al design system sobre lo que construyeron las HUs #12194-#12196.
// Cubre los criterios de la HU:
//   AC1 — tokens de color/tipografía/espaciado en vez de literales (la deuda de hex heredada).
//   AC2 — modo oscuro: toda superficie clara declara su variante `dark:` en el mismo className.
//   AC3 — responsive: la tabla ancha desborda DENTRO de su contenedor, no en la página.
//   AC4 — los cuatro estados con textos redactados, no genéricos.
//   AC5 — contraste AA: los pares de color se resuelven por token (la verificación numérica vive
//         en el informe de la HU; aquí se fija el contrato de qué token usa cada superficie).
//   AC6 — foco visible y recorrido completo por teclado hasta la acción de detalle.
//
// El estilo de los tests de token es el de `hu10492-dark-mode.test.ts`: aserciones estáticas sobre
// el fuente, que es como este repo verifica invariantes visuales que no se pueden leer en jsdom
// (jsdom no aplica Tailwind, así que un `toHaveStyle` sobre una clase utilitaria no probaría nada).
//
// Uso de ejemplo:
//   fuenteSinComentarios(SRC) -> código del módulo sin comentarios -> /#[0-9A-Fa-f]{6}/ -> solo
//   la superficie oscura #0B0F14, que es convención de todo el producto.
import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { InstanceSummary } from "@/lib/api/types/procedure-runtime";

const mocks = vi.hoisted(() => ({
  listPlateHistory: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: { listPlateHistory: mocks.listPlateHistory },
}));

import { HistorialPlaca } from "@/components/atom/modules/HistorialPlaca";

const MODULE_PATH = path.resolve(__dirname, "../HistorialPlaca.tsx");
const SRC = readFileSync(MODULE_PATH, "utf8");

/** El fuente sin comentarios: un hex citado en una nota explicativa no es un hex hardcodeado. */
function fuenteSinComentarios(src: string): string {
  return src.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|\s)\/\/[^\n]*/g, "$1");
}

const CODIGO = fuenteSinComentarios(SRC);

function fila(overrides: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    referenceNumber: "RAD-0001",
    modalidad: "TRASPASO",
    tipoNombre: "Traspaso",
    estado: "aprobado",
    placa: "ABC123",
    vin: "9BWZZZ377VT004251",
    compradorNombre: "Compradora Uno",
    vendedorNombre: "Vendedor Uno",
    organismoTransito: "OT Medellín",
    createdAt: "2026-08-01T10:00:00Z",
    tenantId: "tenant-aaa",
    companiaNombre: "Compañía Alfa",
    updatedAt: "2026-08-05T09:00:00Z",
    gestorNombre: "Gestora Uno",
    ...overrides,
  } as InstanceSummary;
}

beforeEach(() => {
  mocks.listPlateHistory.mockReset();
});

async function consultar(placa = "ABC123") {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText("Placa"), placa);
  await user.click(screen.getByRole("button", { name: /consultar/i }));
  return user;
}

describe("HU #12197 — AC1: color por token, no por literal", () => {
  it("happy path: no queda ningún hex hardcodeado salvo la superficie oscura del producto", () => {
    const hexes = [...CODIGO.matchAll(/#[0-9A-Fa-f]{6}/g)].map((m) => m[0].toUpperCase());
    // #0B0F14 es la superficie oscura de paneles/inputs en TODO el producto (toolbar de Auditoría,
    // Modal, LOG QX…). Cambiarla solo aquí sería drift, no corrección.
    expect([...new Set(hexes)]).toEqual(["#0B0F14"]);
  });

  it("contrato: los tres hex de la deuda declarada quedaron sustituidos por su token", () => {
    // #557EFF -> flit-brand ; #DFE5ED -> `border` (--border) ; #162244 -> flit-primary (#162744,
    // que además corrige el navy equivocado que venía copiado del patrón vecino).
    expect(CODIGO).not.toMatch(/#557EFF/i);
    expect(CODIGO).not.toMatch(/#DFE5ED/i);
    expect(CODIGO).not.toMatch(/#162244/i);
    expect(CODIGO).toMatch(/\btext-flit-brand\b/);
    expect(CODIGO).toMatch(/\bbg-flit-brand\b/);
    expect(CODIGO).toMatch(/\bring-flit-brand\b/);
    expect(CODIGO).toMatch(/\btext-flit-primary\b/);
    expect(CODIGO).toMatch(/\btext-muted-foreground\b/);
  });

  it("edge: la CTA ya no pinta su color por `style` inline (no se puede tematizar)", () => {
    expect(CODIGO).not.toMatch(/style=\{\{\s*background/);
  });

  it("contrato: el encabezado es la tarjeta de título unificada, no un h1 sobre el fondo", () => {
    expect(CODIGO).toMatch(/<ModuleTitle/);
    expect(CODIGO).not.toMatch(/<h1/);
    // Cáscara canónica de módulo (idéntica a Auditoría / LOG QX / Reportes).
    expect(CODIGO).toMatch(/app-bg[^"]*min-h-screen[^"]*px-6 pt-6 pb-10/);
  });
});

describe("HU #12197 — AC2: modo oscuro", () => {
  it("happy path: ningún `bg-white` viaja sin su pareja `dark:bg-` en el mismo className", () => {
    const sinPareja = [...CODIGO.matchAll(/className=\{?"([^"]*\bbg-white\b[^"]*)"/g)].filter(
      (m) => !/dark:bg-/.test(m[1]),
    );
    expect(sinPareja.map((m) => m[1])).toEqual([]);
  });

  it("contrato: el anillo de foco declara su color de offset (si no, en oscuro se ve un halo blanco)", () => {
    const conOffset = [...CODIGO.matchAll(/focus-visible:ring-offset-2/g)].length;
    const conColor = [...CODIGO.matchAll(/focus-visible:ring-offset-background/g)].length;
    expect(conOffset).toBeGreaterThan(0);
    expect(conColor).toBe(conOffset);
  });
});

describe("HU #12197 — AC3: responsive", () => {
  it("happy path: la tabla ancha desborda dentro de su contenedor con scroll horizontal", async () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [fila()], total: 1 });
    render(<HistorialPlaca />);
    await consultar();

    const tabla = await screen.findByRole("table");
    expect(tabla).toHaveStyle({ minWidth: "1400px" });
    const scroller = tabla.closest("div.overflow-x-auto");
    expect(scroller).not.toBeNull();
  });

  it("contrato: la raíz del módulo no fija ancho propio (la página no scrollea en horizontal)", () => {
    const raiz = render(<HistorialPlaca />).container.querySelector(
      '[data-testid="historial-placa-module"]',
    );
    expect(raiz).not.toBeNull();
    expect(raiz!.className).not.toMatch(/\b(w-\[|min-w-\[|overflow-x-)/);
  });
});

describe("HU #12197 — AC4: los cuatro estados con textos redactados", () => {
  it("happy path: el estado inicial dice qué hacer, no 'no hay información'", () => {
    render(<HistorialPlaca />);
    expect(
      screen.getByText("Ingrese una placa y pulse Consultar para ver su historial de trámites."),
    ).toBeInTheDocument();
    expect(screen.queryByText("No hay información para mostrar.")).not.toBeInTheDocument();
  });

  it("happy path: el vacío nombra la placa consultada", async () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [], total: 0 });
    render(<HistorialPlaca />);
    await consultar("XYZ789");

    expect(
      await screen.findByTestId("ui-empty"),
    ).toHaveTextContent("No hay trámites registrados para la placa XYZ789.");
    expect(screen.queryByText("No hay información para mostrar.")).not.toBeInTheDocument();
  });

  it("edge: el error nombra la operación, no el genérico del boundary", async () => {
    mocks.listPlateHistory.mockRejectedValue(new Error(""));
    render(<HistorialPlaca />);
    await consultar();

    expect(await screen.findByTestId("ui-error")).toHaveTextContent(
      "No se pudo consultar el historial de la placa.",
    );
    expect(
      screen.queryByText("Ocurrió un error al cargar la información."),
    ).not.toBeInTheDocument();
  });

  it("contrato: el estado de carga se anuncia además por texto, no solo por el esqueleto", async () => {
    mocks.listPlateHistory.mockReturnValue(new Promise(() => {}));
    render(<HistorialPlaca />);
    await consultar("ABC123");

    expect(await screen.findByTestId("ui-loading")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByTestId("historial-placa-anuncio")).toHaveTextContent(
        "Consultando el historial de la placa ABC123",
      ),
    );
  });
});

describe("HU #12197 — AC6: foco visible y navegación por teclado", () => {
  it("happy path: el recorrido por teclado es input -> Consultar, y Enter dispara la consulta", async () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [fila()], total: 1 });
    render(<HistorialPlaca />);
    const user = userEvent.setup();

    await user.tab();
    expect(document.activeElement).toBe(screen.getByLabelText("Placa"));
    await user.keyboard("ABC123");
    await user.tab();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: /consultar/i }));

    // Sin ratón: Enter sobre la CTA envía el formulario.
    await user.keyboard("{Enter}");
    await waitFor(() =>
      expect(mocks.listPlateHistory).toHaveBeenCalledWith({ placa: "ABC123", skip: 0, take: 20 }),
    );
  });

  it("happy path: desde la CTA se llega por teclado a la acción de detalle de la primera fila", async () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [fila()], total: 1 });
    render(<HistorialPlaca />);
    const user = await consultar();

    const detalle = await screen.findByRole("button", {
      name: "Ver detalle del trámite RAD-0001",
    });
    screen.getByRole("button", { name: /consultar/i }).focus();
    await user.tab();
    expect(document.activeElement).toBe(detalle);
  });

  it("contrato: input, CTA y acción de fila declaran anillo de foco visible", () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [fila()], total: 1 });
    render(<HistorialPlaca />);

    expect(screen.getByLabelText("Placa").className).toMatch(/focus-visible:ring-2/);
    expect(screen.getByRole("button", { name: /consultar/i }).className).toMatch(
      /focus-visible:ring-2/,
    );
    // La acción de fila se comprueba en el fuente porque solo existe con resultados.
    const bloqueAccion = CODIGO.slice(CODIGO.indexOf("historial-placa-ver-"));
    expect(bloqueAccion.slice(0, 600)).toMatch(/focus-visible:ring-2/);
  });

  it("edge: ningún control del módulo se apoya solo en `outline-none` sin anillo de reemplazo", () => {
    const outlines = [...CODIGO.matchAll(/className=\{?"([^"]*outline-none[^"]*)"/g)].map(
      (m) => m[1],
    );
    const sinAnillo = outlines.filter((c) => !/focus-visible:ring-2/.test(c));
    expect(sinAnillo).toEqual([]);
  });
});
