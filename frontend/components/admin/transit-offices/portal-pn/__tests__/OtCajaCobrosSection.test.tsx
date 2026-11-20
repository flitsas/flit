// HU #14462 (Feature #14453) — «Cobros de caja» en el hub del OT: lista del día con total (AC1), anular con motivo
// obligatorio (AC2), «Anular» deshabilitado con su motivo cuando no es anulable (AC3) y errores exactos (AC4).
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";
import type { CajaApi, CajaCobro } from "@/lib/api/admin-ot-portal-caja";
import { CAJA_COBROS_VACIO, CAJA_MOTIVO_ERROR, OtCajaCobrosSection } from "../OtCajaCobrosSection";

vi.mock("@/hooks/usePermissions", () => ({ usePermissions: () => ({ isSuperAdmin: false }) }));

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

function api(items: CajaCobro[], extra?: Partial<CajaApi>): CajaApi {
  return {
    listarUsuarios: vi.fn(),
    invitar: vi.fn(),
    suspender: vi.fn(),
    reactivar: vi.fn(),
    reenviar: vi.fn(),
    listarCobros: vi.fn().mockResolvedValue({ items, total: items.length, totalDiaCop: 245000 }),
    anular: vi.fn().mockResolvedValue({}),
    ...extra,
  };
}

const montar = (a: CajaApi) => render(<OtCajaCobrosSection transitOfficeId="ot-1" api={a} />);

async function abrirAnular(codigo: string) {
  await userEvent.click(await screen.findByRole("button", { name: `Acciones del cobro ${codigo}` }));
  await userEvent.click(await screen.findByRole("menuitem", { name: new RegExp(`Anular el cobro ${codigo}`) }));
  return screen.findByRole("dialog");
}

describe("OtCajaCobrosSection", () => {
  it("AC1: lista los cobros del día con el total sin anulados y la fecha DD/MM/YYYY HH:mm", async () => {
    const a = api([COBRO, ANULADO]);
    montar(a);
    expect(await screen.findByText("PN-2026-000001")).toBeInTheDocument();
    expect(screen.getByText(/Total del día: \$ 245\.000/)).toBeInTheDocument();
    expect(screen.getAllByText("10/11/2026 10:30")[0]).toBeInTheDocument();
    expect(screen.getByText(/Anulado el 10\/11\/2026 11:00 por Luis Admin\. Motivo: Cobro duplicado/)).toBeInTheDocument();
    expect(a.listarCobros).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 10 }), expect.anything());
  });

  it("muestra el vacío del día", async () => {
    montar(api([]));
    expect(await screen.findByText(CAJA_COBROS_VACIO)).toBeInTheDocument();
  });

  it("muestra el error si el portal no responde (503)", async () => {
    const err = new ApiError(503, "x", { code: "PORTAL_NO_DISPONIBLE", message: "El portal de persona natural no responde. Intente de nuevo en unos minutos." });
    montar(api([], { listarCobros: vi.fn().mockRejectedValue(err) }));
    expect(await screen.findByText(/FLIT tuvo un problema|no responde/)).toBeInTheDocument();
  });

  it("caja apagada (404): avisa que no está habilitada", async () => {
    montar(api([], { listarCobros: vi.fn().mockRejectedValue(new ApiError(404, "x")) }));
    expect(await screen.findByText("La caja no está habilitada.")).toBeInTheDocument();
  });

  it("AC2: anula con motivo y recarga la lista", async () => {
    const a = api([COBRO]);
    montar(a);
    const dlg = await abrirAnular("PN-2026-000001");
    await userEvent.type(within(dlg).getByLabelText("Motivo de la anulación"), "Se cobró dos veces");
    await userEvent.click(within(dlg).getByRole("button", { name: "Anular cobro" }));
    await waitFor(() => expect(a.anular).toHaveBeenCalledWith(COBRO, "Se cobró dos veces"));
    expect(await screen.findByText("Cobro anulado.")).toBeInTheDocument();
    expect(a.listarCobros).toHaveBeenCalledTimes(2);
  });

  it("AC2: sin motivo de 10 a 300 caracteres no anula", async () => {
    const a = api([COBRO]);
    montar(a);
    const dlg = await abrirAnular("PN-2026-000001");
    await userEvent.type(within(dlg).getByLabelText("Motivo de la anulación"), "corto");
    await userEvent.click(within(dlg).getByRole("button", { name: "Anular cobro" }));
    expect(await within(dlg).findByText(CAJA_MOTIVO_ERROR)).toBeInTheDocument();
    expect(a.anular).not.toHaveBeenCalled();
  });

  it("AC3: «Anular» queda deshabilitado con el motivo cuando el trámite ya está en FLIT 2.0", async () => {
    montar(api([NO_ANULABLE]));
    await userEvent.click(await screen.findByRole("button", { name: "Acciones del cobro PN-2026-000002" }));
    const item = await screen.findByRole("menuitem", { name: /Anular el cobro PN-2026-000002/ });
    expect(item).toHaveAttribute("aria-disabled", "true");
    expect(item).toHaveAttribute("title", "El trámite ya está en FLIT 2.0 y su cobro no se puede anular.");
  });

  it("AC4: 409 COBRO_NO_ANULABLE muestra el texto exacto y refresca la lista", async () => {
    const err = new ApiError(409, "x", { code: "COBRO_NO_ANULABLE", message: "otro texto" });
    const a = api([COBRO], { anular: vi.fn().mockRejectedValue(err) });
    montar(a);
    const dlg = await abrirAnular("PN-2026-000001");
    await userEvent.type(within(dlg).getByLabelText("Motivo de la anulación"), "Se cobró dos veces");
    await userEvent.click(within(dlg).getByRole("button", { name: "Anular cobro" }));
    expect(await within(dlg).findByText("El trámite ya está en FLIT 2.0 y su cobro no se puede anular.")).toBeInTheDocument();
    await waitFor(() => expect(a.listarCobros).toHaveBeenCalledTimes(2));
  });

  it("AC4: 403 OTRO_ORGANISMO muestra el motivo del servidor", async () => {
    const err = new ApiError(403, "x", { code: "OTRO_ORGANISMO", message: "Ese organismo no es el tuyo." });
    const a = api([COBRO], { anular: vi.fn().mockRejectedValue(err) });
    montar(a);
    const dlg = await abrirAnular("PN-2026-000001");
    await userEvent.type(within(dlg).getByLabelText("Motivo de la anulación"), "Se cobró dos veces");
    await userEvent.click(within(dlg).getByRole("button", { name: "Anular cobro" }));
    expect(await within(dlg).findByText("Ese organismo no es el tuyo.")).toBeInTheDocument();
  });
});
