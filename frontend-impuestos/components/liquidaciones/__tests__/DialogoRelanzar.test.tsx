import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DialogoRelanzar } from "@/components/liquidaciones/DialogoRelanzar";
import { GridItems } from "@/components/liquidaciones/GridItems";
import type { Item } from "@/lib/liquidaciones/tipos";
import { errorRegla, fetchPorRuta, item, lote } from "./fixtures";

// Uso de ejemplo: <DialogoRelanzar open liquidacionId="L" item={{ id: "I", placa: "ABC123" }} empresa={null} onClose={…} />
// relanza una placa en «Incierto — requiere decisión» o con error reintentable como una liquidación por placa nueva
// (plan §3.2), con aviso de posible duplicado y la casilla obligatoria; con el 201 navega al ítem nuevo.

const push = vi.fn();
vi.mock("next/navigation", () => ({
  usePathname: () => "/liquidaciones/L",
  useRouter: () => ({ replace: vi.fn(), push, refresh: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
}));

const CONFIRMAR =
  "Relanzar crea una liquidación nueva para la placa. El proveedor puede haber recibido la solicitud anterior: podría quedar duplicada.";

beforeEach(() => push.mockReset());
afterEach(() => vi.unstubAllGlobals());

const montar = (empresa: string | null = null) =>
  render(<DialogoRelanzar open liquidacionId="L" item={{ id: "I", placa: "ABC123" }} empresa={empresa} onClose={vi.fn()} />);

describe("DialogoRelanzar", () => {
  it("título con la placa, texto del contrato y «Relanzar» deshabilitado hasta marcar la casilla, con el motivo asociado", () => {
    vi.stubGlobal("fetch", vi.fn());
    montar();
    const dialogo = screen.getByRole("dialog", { name: "Relanzar la placa ABC123" });
    expect(within(dialogo).getByText(CONFIRMAR)).toBeInTheDocument();
    const relanzar = within(dialogo).getByRole("button", { name: "Relanzar" });
    expect(relanzar).toBeDisabled();
    const motivo = within(dialogo).getByText("Confirma que aceptas un posible duplicado en el proveedor.");
    expect(relanzar.getAttribute("aria-describedby")).toContain(motivo.id);

    fireEvent.click(within(dialogo).getByRole("checkbox", { name: "Entiendo que puede quedar duplicada" }));
    expect(relanzar).toBeEnabled();
  });

  it("con la casilla envía confirmarPosibleDuplicado=true y navega al ítem nuevo", async () => {
    const nuevo = { ...lote({ id: "L-2", tipo: "INDIVIDUAL", totalItems: 1 }), items: [item({ id: "I-2", liquidacionId: "L-2", posibleDuplicado: true })] };
    const fetchMock = fetchPorRuta([[/^POST \/api\/v1\/impuestos\/liquidaciones\/L\/items\/I\/relanzar\?empresa=e-9$/, () => Response.json(nuevo, { status: 201 })]]);
    vi.stubGlobal("fetch", fetchMock);
    montar("e-9");

    fireEvent.click(screen.getByRole("checkbox", { name: "Entiendo que puede quedar duplicada" }));
    fireEvent.click(screen.getByRole("button", { name: "Relanzar" }));

    await waitFor(() => expect(push).toHaveBeenCalledWith("/liquidaciones/L-2/items/I-2?empresa=e-9"));
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ confirmarPosibleDuplicado: true });
  });

  it("409 NO_RELANZABLE: el literal del contrato dentro del diálogo", async () => {
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([[/relanzar/, () => errorRegla(409, "NO_RELANZABLE", "Solo se relanzan placas en «Incierto — requiere decisión» o con error reintentable.")]]),
    );
    montar();
    fireEvent.click(screen.getByRole("checkbox", { name: "Entiendo que puede quedar duplicada" }));
    fireEvent.click(screen.getByRole("button", { name: "Relanzar" }));
    expect(await within(screen.getByRole("dialog")).findByText("Solo se relanzan placas en «Incierto — requiere decisión» o con error reintentable.")).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("Cancelar cierra sin llamar a la API", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    const onClose = vi.fn();
    render(<DialogoRelanzar open liquidacionId="L" item={{ id: "I", placa: "ABC123" }} empresa={null} onClose={onClose} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(onClose).toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("GridItems — «Relanzar» solo en placas relanzables y con permiso", () => {
  const ITEMS = [
    item({ id: "I-1", placa: "ABC123", estado: "INCIERTO_REQUIERE_DECISION", resultadoStatus: null, totalAPagar: null }),
    item({ id: "I-2", placa: "DEF456", estado: "ERROR", errorClase: "CORREGIR_DATOS", resultadoStatus: "error", totalAPagar: null }),
    item({ id: "I-3", placa: "GHI789", estado: "ERROR", errorClase: "REINTENTABLE", resultadoStatus: "error", totalAPagar: 0 }),
  ] as Item[];

  it("menú por fila: «Relanzar» en Incierto — requiere decisión y en error reintentable; nunca en corregir datos", () => {
    const onRelanzar = vi.fn();
    render(<GridItems liquidacionId="L" items={ITEMS} empresa={null} puedeRelanzar onRelanzar={onRelanzar} />);
    const tabla = screen.getByRole("table", { name: "Placas del lote" });
    const filas = within(tabla).getAllByRole("row").slice(1);

    const abrir = (fila: HTMLElement) => fireEvent.click(within(fila).getByRole("button", { name: /Acciones de la placa/ }));
    abrir(filas[0]);
    expect(screen.getByRole("menuitem", { name: "Relanzar" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("menuitem", { name: "Relanzar" }));
    expect(onRelanzar).toHaveBeenCalledWith(expect.objectContaining({ id: "I-1", placa: "ABC123" }));

    abrir(filas[1]);
    expect(screen.queryByRole("menuitem", { name: "Relanzar" })).toBeNull();
    expect(screen.getByRole("menuitem", { name: "Ver detalle" })).toBeInTheDocument();
  });

  it("sin permiso de crear no aparece «Relanzar»; un total 0 es «$ 0» y null «Sin dato»", () => {
    render(<GridItems liquidacionId="L" items={ITEMS} empresa={null} puedeRelanzar={false} onRelanzar={vi.fn()} />);
    const filas = within(screen.getByRole("table", { name: "Placas del lote" })).getAllByRole("row").slice(1);
    fireEvent.click(within(filas[0]).getByRole("button", { name: /Acciones de la placa/ }));
    expect(screen.queryByRole("menuitem", { name: "Relanzar" })).toBeNull();
    expect(within(filas[0]).getByText("Sin dato")).toBeInTheDocument();
    expect(within(filas[2]).getByText("$ 0")).toBeInTheDocument();
    expect(within(filas[0]).getByRole("link", { name: "ABC123" })).toHaveAttribute("href", "/liquidaciones/L/items/I-1");
  });
});
