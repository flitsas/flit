import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { DetalleLote } from "@/components/liquidaciones/DetalleLote";
import { errorRegla, fetchPorRuta, item, lote } from "./fixtures";

// Uso de ejemplo: <DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} /> en /liquidaciones/L.
// Cabecera del lote con su avance y sus placas. Contrato 1.1.0: el cierre manual muestra quién, cuándo y el motivo
// (delta 5: motivoCierre); el Super Admin lee con ?empresa (sin ella, «Elige una compañía.» y no se pide nada); un error
// de compañía va en línea, nunca a la 404 ni a la 403. El cierre y el relanzamiento se prueban en sus diálogos.

const replace = vi.fn();
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => "/liquidaciones/L",
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => busqueda,
  notFound: () => {
    throw new Error("NEXT_NOT_FOUND");
  },
}));

const ITEMS = /^GET \/api\/v1\/impuestos\/liquidaciones\/L\/items/;
const LOTE = /^GET \/api\/v1\/impuestos\/liquidaciones\/L(\?|$)/;
const pagina = (items: unknown[]) => ({ items, total: items.length, page: 1, pageSize: 10 });

function montar(cabecera: Record<string, unknown>, props: Partial<Parameters<typeof DetalleLote>[0]> = {}) {
  const fetchMock = fetchPorRuta([
    [ITEMS, () => Response.json(pagina([item({ liquidacionId: "L" })]))],
    [LOTE, () => Response.json(lote({ id: "L", ...cabecera }))],
  ]);
  vi.stubGlobal("fetch", fetchMock);
  render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} {...props} />);
  return fetchMock;
}

beforeEach(() => {
  replace.mockReset();
  busqueda = new URLSearchParams();
});
afterEach(() => vi.unstubAllGlobals());

describe("DetalleLote — cierre manual (delta 5: motivoCierre, cerradaPor, cerradaEn)", () => {
  const CERRADO = { estado: "CERRADO_MANUALMENTE", cerradaPor: { id: "u", nombre: "Usuaria Demo" }, cerradaEn: "2026-11-24T19:05:00Z" };

  it("con motivo: quién, cuándo y el motivo en texto plano", async () => {
    montar({ ...CERRADO, motivoCierre: "El proveedor no responde <b>hoy</b>" });
    expect(await screen.findByText("Cerrado manualmente el 24/11/2026 14:05 por Usuaria Demo.")).toBeInTheDocument();
    expect(screen.getByText("Motivo: El proveedor no responde <b>hoy</b>")).toBeInTheDocument();
    expect(document.querySelector("b")).toBeNull();
  });

  it("sin motivo (null o vacío) no pinta «Motivo:»", async () => {
    montar({ ...CERRADO, motivoCierre: "   " });
    await screen.findByText("Cerrado manualmente el 24/11/2026 14:05 por Usuaria Demo.");
    expect(screen.queryByText(/Motivo:/)).toBeNull();
  });

  it("un lote que terminó solo (cerradaPor y motivoCierre null) no muestra el aviso de cierre", async () => {
    montar({ estado: "COMPLETADO", motivoCierre: null });
    await screen.findByRole("table", { name: "Placas del lote" });
    expect(screen.queryByText(/Cerrado manualmente el/)).toBeNull();
  });
});

describe("DetalleLote — Super Admin y ?empresa (regla del backend en las lecturas)", () => {
  it("el Super Admin sin compañía no pide nada y ve «Elige una compañía.»", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin />);
    expect(screen.getByText("Elige una compañía.")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("el Super Admin con compañía la envía en la cabecera y en las placas", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    const fetchMock = montar({ estado: "COMPLETADO" }, { esSuperAdmin: true });
    await screen.findByRole("table", { name: "Placas del lote" });
    const urls = fetchMock.mock.calls.map(([u]) => new URL(String(u), "http://app"));
    expect(urls.length).toBeGreaterThanOrEqual(2);
    for (const url of urls) expect(url.searchParams.get("empresa")).toBe("e-9");
  });

  it.each([
    [404, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos."],
    [400, "EMPRESA_REQUERIDA", "Elige una compañía."],
    [403, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía."],
  ])("un %i %s va en línea con su literal (no es la 404 del lote ni la 403)", async (status, code, literal) => {
    busqueda = new URLSearchParams("empresa=e-9");
    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\/L/, () => errorRegla(status, code, "detalle interno")]]));
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin />);
    expect(await screen.findByText("No se pudo cargar el lote")).toBeInTheDocument();
    expect(screen.getByText(literal)).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });
});

describe("DetalleLote — individual (delta 4)", () => {
  it("una individual redirige con el itemId de la cabecera, conservando ?empresa", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([
        [ITEMS, () => Response.json(pagina([]))],
        [LOTE, () => Response.json(lote({ id: "L", tipo: "INDIVIDUAL", totalItems: 1, itemId: "I-9", estadoItem: "PENDIENTE" }))],
      ]),
    );
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/liquidaciones/L/items/I-9?empresa=e-9"));
  });
});
