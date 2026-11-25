import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DetalleLote } from "@/components/liquidaciones/DetalleLote";
import { DialogoCerrarLote } from "@/components/liquidaciones/DialogoCerrarLote";
import { errorRegla, fetchPorRuta, item, lote } from "./fixtures";

// Uso de ejemplo: <DialogoCerrarLote open liquidacionId="L" empresa={null} onClose={…} onCerrado={(l) => …} /> abre el
// cierre manual del lote (DA-6b, F3-CF12): las placas pendientes quedan «Sin resultado (lote cerrado)». Solo Administrador
// Impuestos y Super Admin lo ven en el detalle del lote; el Gestor no tiene el botón.

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

const CONFIRMAR =
  "Cerrar el lote deja las placas pendientes en «Sin resultado (lote cerrado)». Si después llega un resultado, se aplica a la placa sin reabrir el lote.";

beforeEach(() => {
  replace.mockReset();
  busqueda = new URLSearchParams();
});
afterEach(() => vi.unstubAllGlobals());

describe("DialogoCerrarLote", () => {
  it("texto del contrato, motivo opcional de hasta 300 y foco inicial en Cancelar", () => {
    render(<DialogoCerrarLote open liquidacionId="L" empresa={null} onClose={vi.fn()} onCerrado={vi.fn()} />);
    const dialogo = screen.getByRole("dialog", { name: "Cerrar lote" });
    expect(within(dialogo).getByText(CONFIRMAR)).toBeInTheDocument();
    expect(within(dialogo).getByRole("textbox", { name: "Motivo (opcional)" })).toHaveAttribute("maxLength", "300");
    expect(within(dialogo).getByRole("button", { name: "Cancelar" })).toHaveFocus();
  });

  it("cierra con POST /cerrar (motivo y ?empresa=) y avisa con la liquidación devuelta", async () => {
    const cerrado = lote({ id: "L", estado: "CERRADO_MANUALMENTE" });
    const fetchMock = fetchPorRuta([[/^POST \/api\/v1\/impuestos\/liquidaciones\/L\/cerrar\?empresa=e-9$/, () => Response.json(cerrado)]]);
    vi.stubGlobal("fetch", fetchMock);
    const onCerrado = vi.fn();
    render(<DialogoCerrarLote open liquidacionId="L" empresa="e-9" onClose={vi.fn()} onCerrado={onCerrado} />);

    fireEvent.change(screen.getByRole("textbox", { name: "Motivo (opcional)" }), { target: { value: "El proveedor no responde" } });
    fireEvent.click(within(screen.getByRole("dialog")).getAllByRole("button", { name: "Cerrar lote" }).at(-1)!);

    await waitFor(() => expect(onCerrado).toHaveBeenCalledWith(cerrado));
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ motivo: "El proveedor no responde" });
  });

  it("sin motivo envía motivo null", async () => {
    const fetchMock = fetchPorRuta([[/cerrar/, () => Response.json(lote({ estado: "CERRADO_MANUALMENTE" }))]]);
    vi.stubGlobal("fetch", fetchMock);
    render(<DialogoCerrarLote open liquidacionId="L" empresa={null} onClose={vi.fn()} onCerrado={vi.fn()} />);
    fireEvent.click(within(screen.getByRole("dialog")).getAllByRole("button", { name: "Cerrar lote" }).at(-1)!);
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ motivo: null });
  });

  it("409 YA_TERMINADA: el literal del contrato dentro del diálogo, que no se cierra", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/cerrar/, () => errorRegla(409, "YA_TERMINADA", "La liquidación ya terminó: no se puede cerrar.")]]));
    const onClose = vi.fn();
    render(<DialogoCerrarLote open liquidacionId="L" empresa={null} onClose={onClose} onCerrado={vi.fn()} />);
    fireEvent.click(within(screen.getByRole("dialog")).getAllByRole("button", { name: "Cerrar lote" }).at(-1)!);
    expect(await within(screen.getByRole("dialog")).findByText("La liquidación ya terminó: no se puede cerrar.")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("otro error: «No se pudo cerrar el lote. Intenta de nuevo.»", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/cerrar/, () => new Response("", { status: 500 })]]));
    render(<DialogoCerrarLote open liquidacionId="L" empresa={null} onClose={vi.fn()} onCerrado={vi.fn()} />);
    fireEvent.click(within(screen.getByRole("dialog")).getAllByRole("button", { name: "Cerrar lote" }).at(-1)!);
    expect(await screen.findByText("No se pudo cerrar el lote. Intenta de nuevo.")).toBeInTheDocument();
  });
});

describe("DetalleLote — «Cerrar lote» solo para Administrador y Super Admin (DA-6b)", () => {
  function api(estado = "EN_PROCESO") {
    return fetchPorRuta([
      [/^GET \/api\/v1\/impuestos\/liquidaciones\/L\/items/, () => Response.json({ items: [item({ liquidacionId: "L" })], total: 1, page: 1, pageSize: 10 })],
      [/^GET \/api\/v1\/impuestos\/liquidaciones\/L(\?|$)/, () => Response.json(lote({ id: "L", estado }))],
      [/^POST .*cerrar/, () => Response.json(lote({ id: "L", estado: "CERRADO_MANUALMENTE", cerradaPor: { id: "u", nombre: "Usuaria Demo" }, cerradaEn: "2026-11-24T19:05:00Z" }))],
    ]);
  }

  it("el Gestor no tiene el botón en el DOM", async () => {
    vi.stubGlobal("fetch", api());
    render(<DetalleLote liquidacionId="L" puedeCerrar={false} puedeRelanzar esSuperAdmin={false} />);
    await screen.findByRole("table", { name: "Placas del lote" });
    expect(screen.queryByRole("button", { name: "Cerrar lote" })).toBeNull();
  });

  it("un lote terminado no ofrece cerrar", async () => {
    vi.stubGlobal("fetch", api("COMPLETADO"));
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);
    await screen.findByRole("table", { name: "Placas del lote" });
    expect(screen.queryByRole("button", { name: "Cerrar lote" })).toBeNull();
  });

  it("el Administrador cierra y la cabecera muestra «Cerrado manualmente» con quién y cuándo", async () => {
    vi.stubGlobal("fetch", api());
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);
    fireEvent.click(await screen.findByRole("button", { name: "Cerrar lote" }));
    fireEvent.click(within(screen.getByRole("dialog")).getAllByRole("button", { name: "Cerrar lote" }).at(-1)!);

    expect(await screen.findByText("Cerrado manualmente el 24/11/2026 14:05 por Usuaria Demo.")).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "Estado: Cerrado manualmente" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("un lote cerrado sin actor muestra solo la fecha: «Cerrado manualmente el {fecha}.»", async () => {
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([
        [/^GET \/api\/v1\/impuestos\/liquidaciones\/L\/items/, () => Response.json({ items: [item({ liquidacionId: "L" })], total: 1, page: 1, pageSize: 10 })],
        [/^GET \/api\/v1\/impuestos\/liquidaciones\/L(\?|$)/, () => Response.json(lote({ id: "L", estado: "CERRADO_MANUALMENTE", cerradaPor: null, cerradaEn: "2026-11-24T19:05:00Z" }))],
      ]),
    );
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);

    expect(await screen.findByText("Cerrado manualmente el 24/11/2026 14:05.")).toBeInTheDocument();
    expect(screen.queryByText(/por \.$/)).toBeNull();
  });

  it("cuatro estados del lote: cargando y error con Reintentar", async () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => {})));
    const { unmount } = render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);
    expect(screen.getByRole("status", { name: "Cargando el lote…" })).toBeInTheDocument();
    unmount();

    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\/L/, () => new Response("", { status: 500 })]]));
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);
    expect(await screen.findByText("No se pudo cargar el lote")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reintentar" })).toBeInTheDocument();
  });

  it("lote sin placas todavía: vacío propio", async () => {
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([
        [/items/, () => Response.json({ items: [], total: 0, page: 1, pageSize: 10 })],
        [/liquidaciones\/L/, () => Response.json(lote({ id: "L", estado: "CREADO" }))],
      ]),
    );
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);
    expect(await screen.findByText("Este lote todavía no tiene placas en proceso")).toBeInTheDocument();
  });

  it("una liquidación individual redirige al detalle de su placa", async () => {
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([
        [/items/, () => Response.json({ items: [item({ id: "I-1", liquidacionId: "L" })], total: 1, page: 1, pageSize: 10 })],
        [/liquidaciones\/L/, () => Response.json(lote({ id: "L", tipo: "INDIVIDUAL", totalItems: 1 }))],
      ]),
    );
    render(<DetalleLote liquidacionId="L" puedeCerrar puedeRelanzar esSuperAdmin={false} />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/liquidaciones/L/items/I-1"));
  });
});
