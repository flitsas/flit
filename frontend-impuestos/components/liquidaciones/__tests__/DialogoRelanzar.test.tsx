import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DialogoRelanzar } from "@/components/liquidaciones/DialogoRelanzar";
import { GridItems } from "@/components/liquidaciones/GridItems";
import type { Item } from "@/lib/liquidaciones/tipos";
import { errorRegla, fetchPorRuta, item, lote } from "./fixtures";

// Uso de ejemplo: <DialogoRelanzar open liquidacionId="L" item={{ id: "I", placa: "ABC123" }} empresa={null} onClose={…} onRelanzado={…} />
// relanza una placa como un nuevo intento del MISMO registro (contrato 1.3.0): sin casilla de posible duplicado; con el
// 200 entrega la liquidación a `onRelanzado` para que la pantalla vuelva a leer el ítem o el lote, sin navegar.

const push = vi.fn();
vi.mock("next/navigation", () => ({
  usePathname: () => "/liquidaciones/L",
  useRouter: () => ({ replace: vi.fn(), push, refresh: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
}));

const CONFIRMAR = "La placa se volverá a consultar como un nuevo intento de este mismo registro. El intento actual quedará en el historial.";
const NO_RELANZABLE =
  "Solo se relanzan placas en «Incierto — requiere decisión», con datos incompletos, con error reintentable o con placa no inscrita, que no estén en curso y cuya liquidación no se haya cerrado a mano.";

beforeEach(() => push.mockReset());
afterEach(() => vi.unstubAllGlobals());

const montar = (empresa: string | null = null, onRelanzado = vi.fn(), onClose = vi.fn()) => {
  render(<DialogoRelanzar open liquidacionId="L" item={{ id: "I", placa: "ABC123" }} empresa={empresa} onClose={onClose} onRelanzado={onRelanzado} />);
  return { onRelanzado, onClose };
};

describe("DialogoRelanzar", () => {
  it("título con la placa, texto del nuevo intento y «Relanzar» habilitado, sin la casilla de posible duplicado", () => {
    vi.stubGlobal("fetch", vi.fn());
    montar();
    const dialogo = screen.getByRole("dialog", { name: "Relanzar la placa ABC123" });
    expect(within(dialogo).getByText(CONFIRMAR)).toBeInTheDocument();
    expect(within(dialogo).getByRole("button", { name: "Relanzar" })).toBeEnabled();
    expect(within(dialogo).queryByRole("checkbox")).toBeNull();
    expect(within(dialogo).queryByText(/duplicad/i)).toBeNull();
  });

  it("con el 200 entrega la misma liquidación a onRelanzado, no navega y no envía confirmarPosibleDuplicado", async () => {
    const misma = { ...lote({ id: "L", estado: "EN_PROCESO" }), items: [item({ id: "I", liquidacionId: "L", estado: "PENDIENTE" })] };
    const fetchMock = fetchPorRuta([[/^POST \/api\/v1\/impuestos\/liquidaciones\/L\/items\/I\/relanzar\?empresa=e-9$/, () => Response.json(misma, { status: 200 })]]);
    vi.stubGlobal("fetch", fetchMock);
    const { onRelanzado } = montar("e-9");

    fireEvent.click(screen.getByRole("button", { name: "Relanzar" }));

    await waitFor(() => expect(onRelanzado).toHaveBeenCalledWith(expect.objectContaining({ id: "L" })));
    expect(push).not.toHaveBeenCalled();
    expect(fetchMock.mock.calls[0][1]?.body).toBeUndefined();
  });

  it("409 NO_RELANZABLE: el literal 1.3.0 dentro del diálogo, sin onRelanzado", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/relanzar/, () => errorRegla(409, "NO_RELANZABLE", NO_RELANZABLE)]]));
    const { onRelanzado } = montar();
    fireEvent.click(screen.getByRole("button", { name: "Relanzar" }));
    expect(await within(screen.getByRole("dialog")).findByText(NO_RELANZABLE)).toBeInTheDocument();
    expect(onRelanzado).not.toHaveBeenCalled();
    expect(push).not.toHaveBeenCalled();
  });

  it("Cancelar cierra sin llamar a la API", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    const { onClose } = montar();
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

  it("1.3.0: NO_APLICA muestra «No aplica» con su ayuda y sin «Relanzar»", () => {
    const servicio = [item({ id: "I-4", placa: "SPU123", estado: "NO_APLICA", errorCodigo: "servicio_publico", resultadoStatus: null, totalAPagar: null })] as Item[];
    render(<GridItems liquidacionId="L" items={servicio} empresa={null} puedeRelanzar onRelanzar={vi.fn()} />);
    const fila = within(screen.getByRole("table", { name: "Placas del lote" })).getAllByRole("row")[1];
    expect(within(fila).getByText("No aplica")).toHaveAttribute("title", "Vehículo de servicio público: no paga impuesto vehicular");
    fireEvent.click(within(fila).getByRole("button", { name: /Acciones de la placa/ }));
    expect(screen.queryByRole("menuitem", { name: "Relanzar" })).toBeNull();
  });

  it("1.3.0: con el lote CERRADO_MANUALMENTE ninguna fila ofrece «Relanzar»", () => {
    render(<GridItems liquidacionId="L" items={ITEMS} empresa={null} puedeRelanzar estadoLote="CERRADO_MANUALMENTE" onRelanzar={vi.fn()} />);
    const filas = within(screen.getByRole("table", { name: "Placas del lote" })).getAllByRole("row").slice(1);
    fireEvent.click(within(filas[0]).getByRole("button", { name: /Acciones de la placa/ }));
    expect(screen.getByRole("menuitem", { name: "Ver detalle" })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Relanzar" })).toBeNull();
  });
});
