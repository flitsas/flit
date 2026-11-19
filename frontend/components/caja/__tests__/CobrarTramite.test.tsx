// Feature #14453 — pestaña «Cobrar» del módulo Caja: cobro feliz, cada error por código, medio «Otro» sin detalle,
// prefactura inválida, total que cambió y bloqueo por intentos.
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CajaError, type CobroConfirmado, type OtCajaApi, type TramiteParaCobrar } from "@/lib/api/ot-caja";
import { CobrarTramite } from "../CobrarTramite";

const TRAMITE: TramiteParaCobrar = {
  codigo: "PN-2026-000123",
  tipoTramite: { codigo: "traspaso", nombre: "Traspaso" },
  organismo: { codigo: "OT-1", nombre: "Tránsito de Prueba" },
  lineas: [
    { concepto: "Derechos de tránsito", valorCop: 200000 },
    { concepto: "Servicio FLIT", valorCop: 45000 },
  ],
  totalCop: 245000,
  versionTarifa: "v1",
  venceEn: "2026-11-20T00:00:00Z",
};
const COBRADO: CobroConfirmado = { codigo: "PN-2026-000123", valorCop: 245000, medio: "efectivo", pagadoEn: "2026-11-17T15:00:00Z", mensaje: "ok" };

function api(extra: Partial<OtCajaApi> = {}): OtCajaApi {
  return {
    tramite: vi.fn().mockResolvedValue(TRAMITE),
    confirmar: vi.fn().mockResolvedValue(COBRADO),
    cobros: vi.fn(),
    anular: vi.fn(),
    ...extra,
  };
}

async function buscar(codigo = "pn-2026-000123") {
  await userEvent.type(screen.getByLabelText("ID del trámite"), codigo);
  await userEvent.click(screen.getByRole("button", { name: "Buscar" }));
}

const err = (status: number, code: string, espera?: number) => new CajaError(status, "x", code, { code }, espera);

describe("CobrarTramite", () => {
  it("cobro feliz: busca, muestra el desglose y el total sin campo de valor, confirma y deja cobrar otro", async () => {
    const a = api();
    const onCobrado = vi.fn();
    render(<CobrarTramite api={a} onCobrado={onCobrado} />);
    await buscar();
    expect(a.tramite).toHaveBeenCalledWith("PN-2026-000123");
    expect(await screen.findByText("Derechos de tránsito")).toBeInTheDocument();
    expect(screen.getByTestId("total-caja")).toHaveTextContent("$ 245.000");
    // El valor nunca se edita: ningún campo de monto.
    expect(screen.queryByLabelText(/valor|monto/i)).not.toBeInTheDocument();

    await userEvent.click(screen.getByLabelText("Efectivo"));
    await userEvent.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    await waitFor(() => expect(a.confirmar).toHaveBeenCalledTimes(1));
    expect(a.confirmar).toHaveBeenCalledWith(
      expect.objectContaining({ codigo: "PN-2026-000123", medio: "efectivo", totalVistoCop: 245000, prefactura: null, llave: expect.any(String) }),
    );
    expect(await screen.findByText("Pago registrado.")).toBeInTheDocument();
    expect(screen.getByText("$ 245.000")).toBeInTheDocument();
    expect(onCobrado).toHaveBeenCalled();

    await userEvent.click(screen.getByRole("button", { name: "Cobrar otro trámite" }));
    expect(screen.getByLabelText("ID del trámite")).toHaveValue("");
  });

  it("un ID sin el formato PN-AAAA-NNNNNN no llega al servidor", async () => {
    const a = api();
    render(<CobrarTramite api={a} />);
    await buscar("123");
    expect(await screen.findByText("No encontramos ese trámite.")).toBeInTheDocument();
    expect(a.tramite).not.toHaveBeenCalled();
  });

  it.each([
    ["no_encontrado", 404, "No encontramos ese trámite."],
    ["otro_organismo", 403, "Este trámite no es de este organismo."],
    ["ya_pagado", 409, "Este trámite ya fue pagado."],
    ["cobro_vencido", 409, "El pago en caja de este trámite venció."],
    ["no_habilitado", 409, "Este trámite no tiene el pago en caja habilitado."],
  ])("búsqueda: %s muestra su texto exacto", async (code, status, texto) => {
    render(<CobrarTramite api={api({ tramite: vi.fn().mockRejectedValue(err(status, code)) })} />);
    await buscar();
    expect(await screen.findByText(texto)).toBeInTheDocument();
    expect(screen.queryByText("Confirmar cobro")).not.toBeInTheDocument();
  });

  it("404 sin código = caja no habilitada para el organismo", async () => {
    render(<CobrarTramite api={api({ tramite: vi.fn().mockRejectedValue(new CajaError(404, "x", "")) })} />);
    await buscar();
    expect(await screen.findByText("La caja no está habilitada para tu organismo")).toBeInTheDocument();
  });

  it("429: avisa la espera y bloquea la búsqueda", async () => {
    render(<CobrarTramite api={api({ tramite: vi.fn().mockRejectedValue(err(429, "rate_limited", 120)) })} />);
    await buscar();
    expect(await screen.findByText("Demasiadas búsquedas sin resultado. Espere 2 minutos.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Buscar" })).toBeDisabled();
  });

  it("«Otro» sin detalle no confirma; con detalle lo envía", async () => {
    const a = api();
    render(<CobrarTramite api={a} />);
    await buscar();
    await screen.findByText("Derechos de tránsito");
    await userEvent.click(screen.getByLabelText("Otro"));
    await userEvent.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    expect(await screen.findByText("Escriba el detalle del medio.")).toBeInTheDocument();
    expect(a.confirmar).not.toHaveBeenCalled();

    await userEvent.type(screen.getByLabelText("¿Cuál?"), "Bono");
    await userEvent.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    await waitFor(() => expect(a.confirmar).toHaveBeenCalledWith(expect.objectContaining({ medio: "otro", medioDetalle: "Bono" })));
  });

  it("sin elegir cómo se cobró no confirma", async () => {
    const a = api();
    render(<CobrarTramite api={a} />);
    await buscar();
    await screen.findByText("Derechos de tránsito");
    await userEvent.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    expect(await screen.findByText("Elija cómo se cobró.")).toBeInTheDocument();
    expect(a.confirmar).not.toHaveBeenCalled();
  });

  it("prefactura inválida (tipo) se rechaza en el formulario; una válida viaja con el cobro", async () => {
    const a = api();
    render(<CobrarTramite api={a} />);
    await buscar();
    await screen.findByText("Derechos de tránsito");
    const campo = screen.getByLabelText(/Prefactura/) as HTMLInputElement;

    fireEvent.change(campo, { target: { files: [new File(["x"], "nota.txt", { type: "text/plain" })] } });
    expect(await screen.findByText("La prefactura debe ser PDF, JPG o PNG.")).toBeInTheDocument();

    const pdf = new File(["%PDF"], "pref.pdf", { type: "application/pdf" });
    fireEvent.change(campo, { target: { files: [pdf] } });
    await waitFor(() => expect(screen.queryByText("La prefactura debe ser PDF, JPG o PNG.")).not.toBeInTheDocument());
    await userEvent.click(screen.getByLabelText("Datáfono"));
    await userEvent.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    await waitFor(() => expect(a.confirmar).toHaveBeenCalledWith(expect.objectContaining({ medio: "datafono", prefactura: pdf })));
  });

  it("prefactura demasiado pesada (> 5 MB) se rechaza", async () => {
    render(<CobrarTramite api={api()} />);
    await buscar();
    await screen.findByText("Derechos de tránsito");
    const grande = new File(["x"], "grande.pdf", { type: "application/pdf" });
    Object.defineProperty(grande, "size", { value: 6 * 1024 * 1024 });
    fireEvent.change(screen.getByLabelText(/Prefactura/), { target: { files: [grande] } });
    expect(await screen.findByText("La prefactura debe pesar 5 MB o menos.")).toBeInTheDocument();
  });

  it("el servidor rechaza la prefactura: muestra su texto y no registra el pago", async () => {
    const a = api({ confirmar: vi.fn().mockRejectedValue(err(400, "prefactura_invalida")) });
    render(<CobrarTramite api={a} />);
    await buscar();
    await screen.findByText("Derechos de tránsito");
    await userEvent.click(screen.getByLabelText("Efectivo"));
    await userEvent.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    expect(await screen.findByText("La prefactura debe ser PDF, JPG o PNG de hasta 5 MB.")).toBeInTheDocument();
    expect(screen.queryByText("Pago registrado.")).not.toBeInTheDocument();
  });

  it("total cambiado (409): avisa, muestra el nuevo total y la siguiente confirmación lo usa", async () => {
    const confirmar = vi
      .fn()
      .mockRejectedValueOnce(new CajaError(409, "x", "total_cambio", { code: "total_cambio", totalActualCop: 260000 }, undefined, 260000))
      .mockResolvedValueOnce({ ...COBRADO, valorCop: 260000 });
    render(<CobrarTramite api={api({ confirmar })} />);
    await buscar();
    await screen.findByText("Derechos de tránsito");
    await userEvent.click(screen.getByLabelText("Efectivo"));
    await userEvent.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    expect(await screen.findByText("El total cambió. Revise el nuevo valor y confirme de nuevo.")).toBeInTheDocument();
    expect(screen.getByTestId("total-caja")).toHaveTextContent("$ 260.000");

    await userEvent.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    await waitFor(() => expect(confirmar).toHaveBeenCalledTimes(2));
    expect(confirmar.mock.calls[1][0]).toEqual(expect.objectContaining({ totalVistoCop: 260000 }));
    expect(await screen.findByText("Pago registrado.")).toBeInTheDocument();
  });

  it("un corte de red reintenta con la misma llave; ya pagado al confirmar avisa y no registra el pago", async () => {
    const confirmar = vi
      .fn()
      .mockRejectedValueOnce(new CajaError(0, "Sin conexión", "sin_conexion"))
      .mockRejectedValueOnce(err(409, "ya_pagado"));
    render(<CobrarTramite api={api({ confirmar })} />);
    await buscar();
    await screen.findByText("Derechos de tránsito");
    await userEvent.click(screen.getByLabelText("Efectivo"));
    await userEvent.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    await waitFor(() => expect(confirmar).toHaveBeenCalledTimes(1));
    await userEvent.click(await screen.findByRole("button", { name: "Confirmar cobro" }));
    expect(await screen.findByText("Este trámite ya fue pagado.")).toBeInTheDocument();
    expect(confirmar.mock.calls[1][0].llave).toBe(confirmar.mock.calls[0][0].llave);
    expect(screen.queryByText("Pago registrado.")).not.toBeInTheDocument();
  });

  it("estado cargando: mientras busca muestra el loader", async () => {
    let liberar: (t: TramiteParaCobrar) => void = () => {};
    const a = api({ tramite: vi.fn().mockReturnValue(new Promise<TramiteParaCobrar>((r) => (liberar = r))) });
    render(<CobrarTramite api={a} />);
    await buscar();
    expect(await screen.findByText("Buscando el trámite…")).toBeInTheDocument();
    liberar(TRAMITE);
    expect(await screen.findByText("Derechos de tránsito")).toBeInTheDocument();
  });
});
