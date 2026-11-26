import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { etiquetaKit } from "@flit/ui/StatusBadge";
import { DetalleLote } from "@/components/liquidaciones/DetalleLote";
import { CONTADORES_CERO, errorRegla, fetchPorRuta, item, lote } from "./fixtures";

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

  it.each([null, { id: "u", nombre: "   " }, { id: "u", nombre: "" }])("sin actor (cerradaPor %j) no dice «por» suelto", async (cerradaPor) => {
    montar({ ...CERRADO, cerradaPor, motivoCierre: null });
    expect(await screen.findByText("Cerrado manualmente el 24/11/2026 14:05.", { exact: true })).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/ por\s*\./);
  });

  it("el nombre del actor sale recortado", async () => {
    montar({ ...CERRADO, cerradaPor: { id: "u", nombre: "  Usuaria Demo  " }, motivoCierre: null });
    expect(await screen.findByText("Cerrado manualmente el 24/11/2026 14:05 por Usuaria Demo.", { exact: true })).toBeInTheDocument();
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

describe("DetalleLote — tras «Cerrar lote» se releen las placas (F2, E2E liquidacion-masiva.spec.ts:130)", () => {
  it("la fila pasa de «Reintento programado» a «Sin resultado (lote cerrado)» con una lectura final de los ítems", async () => {
    let cerrado = false;
    const fetchMock = fetchPorRuta([
      [
        ITEMS,
        () =>
          Response.json(
            pagina([item({ liquidacionId: "L", estado: cerrado ? "SIN_RESULTADO_LOTE_CERRADO" : "REINTENTO_PROGRAMADO", resultadoStatus: null, totalAPagar: null })]),
          ),
      ],
      [LOTE, () => Response.json(lote({ id: "L", estado: "EN_PROCESO" }))],
      [
        /^POST .*cerrar/,
        () => {
          cerrado = true;
          return Response.json(
            lote({
              id: "L",
              estado: "CERRADO_MANUALMENTE",
              cerradaPor: { id: "u", nombre: "Usuaria Demo" },
              cerradaEn: "2026-11-24T19:05:00Z",
              contadores: { ...CONTADORES_CERO, sinResultado: 1 },
            }),
          );
        },
      ],
    ]);
    vi.stubGlobal("fetch", fetchMock);
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);

    const tabla = await screen.findByRole("table", { name: "Placas del lote" });
    expect(within(tabla).getByText("Reintento programado")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Cerrar lote" }));
    fireEvent.click(within(screen.getByRole("dialog")).getAllByRole("button", { name: "Cerrar lote" }).at(-1)!);

    await screen.findByText("Cerrado manualmente el 24/11/2026 14:05 por Usuaria Demo.");
    expect(await within(screen.getByRole("table", { name: "Placas del lote" })).findByText("Sin resultado (lote cerrado)")).toBeInTheDocument();
    expect(screen.queryByText("Reintento programado")).toBeNull();
  });
});

describe("DetalleLote — grilla: placa no inscrita en advertencia y relanzable", () => {
  const NO_INSCRITA = "EviewDetailVehicles: La placa ingresada no se encuentra inscrita en nuestra base de datos.";
  const filaCon = (parcial: Record<string, unknown>) => {
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([
        [ITEMS, () => Response.json(pagina([item({ liquidacionId: "L", estado: "ERROR", resultadoStatus: "error", totalAPagar: null, errorClase: "DEPENDE", errorCodigo: "portal_rejected", ...parcial })]))],
        [LOTE, () => Response.json(lote({ id: "L", estado: "COMPLETADO_CON_NOVEDADES" }))],
      ]),
    );
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);
  };
  const tonoDe = (el: HTMLElement) => el.style.getPropertyValue("--c");

  it("el chip de la placa no inscrita usa el tono warning y la fila ofrece «Relanzar»", async () => {
    filaCon({ errorMensaje: NO_INSCRITA });
    const tabla = await screen.findByRole("table", { name: "Placas del lote" });
    expect(tonoDe(within(tabla).getByText("Con novedad"))).toBe(etiquetaKit("warning").style["--c" as never]);
    fireEvent.click(within(tabla).getByRole("button", { name: /Acciones de la placa/ }));
    expect(await screen.findByRole("menuitem", { name: "Relanzar" })).toBeInTheDocument();
  });

  it("otro portal_rejected sigue en danger y sin «Relanzar»", async () => {
    filaCon({ errorMensaje: "El documento del propietario no coincide." });
    const tabla = await screen.findByRole("table", { name: "Placas del lote" });
    expect(tonoDe(within(tabla).getByText("Con error"))).toBe(etiquetaKit("danger").style["--c" as never]);
    fireEvent.click(within(tabla).getByRole("button", { name: /Acciones de la placa/ }));
    await screen.findByRole("menuitem", { name: "Ver detalle" });
    expect(screen.queryByRole("menuitem", { name: "Relanzar" })).toBeNull();
  });
});

describe("DetalleLote — contrato 1.3.0: «No aplica» en el embudo y relanzar en el mismo lote", () => {
  it("el embudo lista «No aplica» con su cifra y filtra la tabla por estado=NO_APLICA", async () => {
    const fetchMock = montar({ estado: "COMPLETADO_CON_NOVEDADES", contadores: { ...CONTADORES_CERO, exitosas: 8, noAplica: 2 } });
    const grupo = await screen.findByRole("group", { name: "Filtros del lote" });
    const chip = within(grupo).getByRole("button", { name: /^2\s*No aplica$/ });
    fireEvent.click(chip);
    await waitFor(() => expect(fetchMock.mock.calls.some(([u]) => /\/items\?.*estado=NO_APLICA/.test(String(u)))).toBe(true));
  });

  it("relanzar desde la tabla con 200: no navega, cierra el diálogo y vuelve a leer el lote y sus placas", async () => {
    let relanzado = false;
    const fetchMock = fetchPorRuta([
      [/^POST \/api\/v1\/impuestos\/liquidaciones\/L\/items\/I-1\/relanzar$/, () => {
        relanzado = true;
        return Response.json({ ...lote({ id: "L", estado: "EN_PROCESO" }), items: [] });
      }],
      [
        ITEMS,
        () =>
          Response.json(
            pagina([
              item({
                id: "I-1",
                liquidacionId: "L",
                estado: relanzado ? "PENDIENTE" : "ERROR",
                errorClase: relanzado ? null : "REINTENTABLE",
                errorCodigo: relanzado ? null : "timeout",
                resultadoStatus: null,
                totalAPagar: null,
              }),
            ]),
          ),
      ],
      [LOTE, () => Response.json(lote({ id: "L", estado: relanzado ? "EN_PROCESO" : "COMPLETADO_CON_NOVEDADES" }))],
    ]);
    vi.stubGlobal("fetch", fetchMock);
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);

    const tabla = await screen.findByRole("table", { name: "Placas del lote" });
    fireEvent.click(within(tabla).getByRole("button", { name: /Acciones de la placa/ }));
    fireEvent.click(await screen.findByRole("menuitem", { name: "Relanzar" }));
    const dialogo = await screen.findByRole("dialog", { name: "Relanzar la placa ABC123" });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Relanzar" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(await within(screen.getByRole("table", { name: "Placas del lote" })).findByText("Pendiente")).toBeInTheDocument();
    // Sigue en el mismo lote: no redirige a otra liquidación.
    expect(replace).not.toHaveBeenCalled();
  });

  it("con el lote CERRADO_MANUALMENTE la fila relanzable no ofrece «Relanzar»", async () => {
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([
        [ITEMS, () => Response.json(pagina([item({ liquidacionId: "L", estado: "ERROR", errorClase: "REINTENTABLE", errorCodigo: "timeout", resultadoStatus: "error", totalAPagar: null })]))],
        [LOTE, () => Response.json(lote({ id: "L", estado: "CERRADO_MANUALMENTE", cerradaEn: "2026-11-24T19:05:00Z" }))],
      ]),
    );
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);
    const tabla = await screen.findByRole("table", { name: "Placas del lote" });
    fireEvent.click(within(tabla).getByRole("button", { name: /Acciones de la placa/ }));
    await screen.findByRole("menuitem", { name: "Ver detalle" });
    expect(screen.queryByRole("menuitem", { name: "Relanzar" })).toBeNull();
  });
});
