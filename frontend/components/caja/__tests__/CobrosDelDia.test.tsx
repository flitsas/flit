// Feature #14453 — pestaña «Cobros del día»: tabla estándar, total sin anulados, anulación solo para el Admin OT,
// «Anular» deshabilitado con su motivo y los estados cargando / vacío / error.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CajaError, type CajaCobro, type CajaCobroLista, type OtCajaApi } from "@/lib/api/ot-caja";
import { CAJA_COBROS_CARGANDO, CAJA_COBROS_VACIO, CAJA_MOTIVO_ERROR, CobrosDelDia } from "../CobrosDelDia";

const COBRO: CajaCobro = {
  cobroId: "c1",
  codigo: "PN-2026-000001",
  valorCop: 245000,
  medio: "efectivo",
  pagadoEn: "2026-11-10T15:30:00Z",
  pagadoPorNombre: "Ana Caja",
  estado: "pagado",
  anulable: true,
  rowVersion: 3,
};
const NO_ANULABLE: CajaCobro = {
  ...COBRO,
  cobroId: "c2",
  codigo: "PN-2026-000002",
  medio: "otro",
  medioDetalle: "Bono",
  anulable: false,
  motivoNoAnulable: "El trámite ya está en FLIT 2.0 y su cobro no se puede anular.",
};
const ANULADO: CajaCobro = {
  ...COBRO,
  cobroId: "c3",
  codigo: "PN-2026-000003",
  estado: "anulado",
  anulable: false,
  anuladoEn: "2026-11-10T16:00:00Z",
  anuladoPorNombre: "Luis Admin",
  anulacionMotivo: "Cobro duplicado",
};

const lista = (items: CajaCobro[], totalDiaCop = 245000): CajaCobroLista => ({ items, total: items.length, totalDiaCop });

function api(items: CajaCobro[], extra: Partial<OtCajaApi> = {}): OtCajaApi {
  return {
    tramite: vi.fn(),
    confirmar: vi.fn(),
    cobros: vi.fn().mockResolvedValue(lista(items)),
    anular: vi.fn().mockResolvedValue({}),
    ...extra,
  };
}

async function abrirAnular(codigo: string) {
  await userEvent.click(await screen.findByRole("button", { name: `Acciones del cobro ${codigo}` }));
  await userEvent.click(await screen.findByRole("menuitem", { name: new RegExp(`Anular el cobro ${codigo}`) }));
  return screen.findByRole("dialog");
}

describe("CobrosDelDia", () => {
  it("lista ID, valor, medio, fecha y hora DD/MM/YYYY HH:mm, usuario y estado; el anulado se ve tachado y marcado", async () => {
    const a = api([COBRO, NO_ANULABLE, ANULADO]);
    render(<CobrosDelDia api={a} puedeAnular />);
    expect(await screen.findByText("PN-2026-000001")).toBeInTheDocument();
    expect(screen.getAllByText("10/11/2026 10:30").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Ana Caja").length).toBeGreaterThan(0);
    expect(screen.getByText("Otro · Bono")).toBeInTheDocument();
    expect(screen.getByText("Anulado")).toBeInTheDocument();
    expect(screen.getByText(/El 10\/11\/2026 11:00 por Luis Admin\. Motivo: Cobro duplicado/)).toBeInTheDocument();
    expect(screen.getByText("PN-2026-000003")).toHaveClass("line-through");
    expect(screen.getByText("PN-2026-000001")).not.toHaveClass("line-through");
    expect(screen.getByTestId("total-dia")).toHaveTextContent("Total del día: $ 245.000");
    expect(screen.getByTestId("total-dia")).toHaveTextContent("sin cobros anulados");
    expect(a.cobros).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 10 }), expect.anything());
  });

  it("paginación estándar con «Filas por página» 10/20/30/50", async () => {
    render(<CobrosDelDia api={api([COBRO])} puedeAnular />);
    await screen.findByText("PN-2026-000001");
    const selector = screen.getByLabelText(/Filas por página/i);
    const opciones = within(selector).getAllByRole("option").map((o) => o.textContent);
    expect(opciones).toEqual(["10", "20", "30", "50"]);
  });

  it("Admin OT: anula con motivo desde «Acciones» y recarga la lista", async () => {
    const a = api([COBRO]);
    render(<CobrosDelDia api={a} puedeAnular />);
    const dlg = await abrirAnular("PN-2026-000001");
    await userEvent.type(within(dlg).getByLabelText("Motivo de la anulación"), "Se cobró dos veces");
    await userEvent.click(within(dlg).getByRole("button", { name: "Anular cobro" }));
    await waitFor(() => expect(a.anular).toHaveBeenCalledWith(COBRO, "Se cobró dos veces"));
    expect(await screen.findByText("Cobro anulado.")).toBeInTheDocument();
    expect(a.cobros).toHaveBeenCalledTimes(2);
  });

  it("el motivo debe tener de 10 a 300 caracteres", async () => {
    const a = api([COBRO]);
    render(<CobrosDelDia api={a} puedeAnular />);
    const dlg = await abrirAnular("PN-2026-000001");
    await userEvent.type(within(dlg).getByLabelText("Motivo de la anulación"), "corto");
    await userEvent.click(within(dlg).getByRole("button", { name: "Anular cobro" }));
    expect(await within(dlg).findByText(CAJA_MOTIVO_ERROR)).toBeInTheDocument();
    expect(a.anular).not.toHaveBeenCalled();
  });

  it("«Anular» queda deshabilitado con el motivo cuando el cobro no es anulable", async () => {
    render(<CobrosDelDia api={api([NO_ANULABLE])} puedeAnular />);
    await userEvent.click(await screen.findByRole("button", { name: "Acciones del cobro PN-2026-000002" }));
    const item = await screen.findByRole("menuitem", { name: /Anular el cobro PN-2026-000002/ });
    expect(item).toHaveAttribute("aria-disabled", "true");
    expect(item).toHaveAttribute("title", "El trámite ya está en FLIT 2.0 y su cobro no se puede anular.");
  });

  it("409 cobro_no_anulable muestra el texto exacto y refresca la lista", async () => {
    const e = new CajaError(409, "x", "cobro_no_anulable", { code: "cobro_no_anulable" });
    const a = api([COBRO], { anular: vi.fn().mockRejectedValue(e) });
    render(<CobrosDelDia api={a} puedeAnular />);
    const dlg = await abrirAnular("PN-2026-000001");
    await userEvent.type(within(dlg).getByLabelText("Motivo de la anulación"), "Se cobró dos veces");
    await userEvent.click(within(dlg).getByRole("button", { name: "Anular cobro" }));
    expect(await within(dlg).findByText("El trámite ya está en FLIT 2.0 y su cobro no se puede anular.")).toBeInTheDocument();
    await waitFor(() => expect(a.cobros).toHaveBeenCalledTimes(2));
  });

  it("la persona de caja ve la lista pero no tiene «Acciones» ni selector de día", async () => {
    render(<CobrosDelDia api={api([COBRO])} puedeAnular={false} />);
    expect(await screen.findByText("PN-2026-000001")).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Acciones" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Acciones del cobro/ })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Día")).not.toBeInTheDocument();
  });

  it("estado vacío", async () => {
    render(<CobrosDelDia api={api([])} puedeAnular />);
    expect(await screen.findByText(CAJA_COBROS_VACIO)).toBeInTheDocument();
  });

  it("estado cargando", async () => {
    const a = api([], { cobros: vi.fn().mockReturnValue(new Promise(() => {})) });
    render(<CobrosDelDia api={a} puedeAnular />);
    expect(await screen.findByText(CAJA_COBROS_CARGANDO)).toBeInTheDocument();
  });

  it("estado error: texto del servicio y reintento", async () => {
    const cobros = vi
      .fn()
      .mockRejectedValueOnce(new CajaError(503, "x", ""))
      .mockResolvedValueOnce(lista([COBRO]));
    render(<CobrosDelDia api={api([], { cobros })} puedeAnular />);
    expect(await screen.findByText("El servicio no está disponible. Intente de nuevo en unos minutos.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("PN-2026-000001")).toBeInTheDocument();
  });

  it("caja apagada (404 sin código): avisa que no está habilitada para el organismo", async () => {
    render(<CobrosDelDia api={api([], { cobros: vi.fn().mockRejectedValue(new CajaError(404, "x", "")) })} puedeAnular />);
    expect(await screen.findByText("La caja no está habilitada para tu organismo")).toBeInTheDocument();
  });
});
