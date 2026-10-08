import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ParametrosMotorLotePanel } from "@/components/admin/plataforma/ParametrosMotorLotePanel";
import AdminDescargaMasivaPage from "@/app/admin/plataforma/descarga-masiva/page";
import {
  getParametrosMotorLote,
  putParametrosMotorLote,
  type ParametroMotorLoteLimite,
  type ParametrosMotorLote,
} from "@/lib/api/admin-plataforma-consolidado-lotes";
import { ApiError } from "@/lib/api/types";

// Uso de ejemplo (HU #13420): <ParametrosMotorLotePanel /> dentro de /admin/plataforma/descarga-masiva.
// Carga GET …/consolidados/lotes/parametros, valida con `limites` antes del PUT, pinta el 400 junto
// al campo, conserva lo escrito ante el 409 y advierte al apagar el motor.

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock("@/lib/api/admin-plataforma-consolidado-lotes", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-plataforma-consolidado-lotes")>();
  return { ...real, getParametrosMotorLote: vi.fn(), putParametrosMotorLote: vi.fn() };
});

const getMock = vi.mocked(getParametrosMotorLote);
const putMock = vi.mocked(putParametrosMotorLote);

const LIMITES: ParametroMotorLoteLimite[] = [
  { campo: "maxItemsPerBatch", minimo: 1, maximo: 32766, mayorQue: null },
  { campo: "maxPdfsPerPart", minimo: 1, maximo: 5000, mayorQue: null },
  { campo: "maxMbPerPart", minimo: 10, maximo: 2048, mayorQue: null },
  { campo: "itemSlots", minimo: 1, maximo: 6, mayorQue: null },
  { campo: "itemTimeoutSeconds", minimo: 1, maximo: null, mayorQue: null },
  { campo: "itemLeaseSeconds", minimo: null, maximo: null, mayorQue: "itemTimeoutSeconds" },
  { campo: "maxItemAttempts", minimo: 1, maximo: 10, mayorQue: null },
  { campo: "retryDelaySeconds", minimo: 5, maximo: null, mayorQue: null },
  { campo: "partTimeoutSeconds", minimo: 1, maximo: null, mayorQue: null },
  { campo: "partLeaseSeconds", minimo: null, maximo: null, mayorQue: "partTimeoutSeconds" },
  { campo: "maxPartAttempts", minimo: 1, maximo: 10, mayorQue: null },
  { campo: "retentionHours", minimo: 1, maximo: 168, mayorQue: null },
];

const PARAMETROS: ParametrosMotorLote = {
  maxItemsPerBatch: 10000,
  maxPdfsPerPart: 500,
  maxMbPerPart: 512,
  itemSlots: 2,
  itemTimeoutSeconds: 120,
  itemLeaseSeconds: 300,
  maxItemAttempts: 3,
  retryDelaySeconds: 30,
  partTimeoutSeconds: 600,
  partLeaseSeconds: 900,
  maxPartAttempts: 3,
  retentionHours: 72,
  isActive: true,
  updatedAt: "2026-10-07T15:30:00Z",
  updatedBy: "00000000-0000-0000-0000-0000000000aa",
  updatedByName: "Super Admin Demo",
  rowVersion: 7,
  limites: LIMITES,
};

const TOPE = /tope total de trámites por lote/i;

async function cargado() {
  render(<ParametrosMotorLotePanel />);
  return screen.findByRole("form", { name: /parámetros del motor/i });
}

async function escribir(label: RegExp, valor: string) {
  const input = screen.getByRole("spinbutton", { name: label });
  await userEvent.clear(input);
  if (valor) await userEvent.type(input, valor);
  return input;
}

const guardar = () => userEvent.click(screen.getByRole("button", { name: /guardar parámetros/i }));

describe("ParametrosMotorLotePanel — HU #13420", () => {
  beforeEach(() => {
    getMock.mockReset();
    putMock.mockReset();
  });

  it("AC7 — cargando: estado accesible mientras llega el GET", async () => {
    getMock.mockReturnValue(new Promise(() => {}));
    render(<ParametrosMotorLotePanel />);
    expect(screen.getByTestId("parametros-motor-loading")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(/cargando/i);
  });

  it("AC7 — error al cargar: mensaje y «Reintentar» vuelve a pedir el GET", async () => {
    getMock.mockRejectedValueOnce(new ApiError(500, "x")).mockResolvedValueOnce(PARAMETROS);
    render(<ParametrosMotorLotePanel />);
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent(/no se pudieron cargar los parámetros/i);
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByRole("form", { name: /parámetros del motor/i })).toBeInTheDocument();
    expect(getMock).toHaveBeenCalledTimes(2);
  });

  it("404 parametros_no_encontrados: estado de error con mensaje claro", async () => {
    getMock.mockRejectedValue(new ApiError(404, "x", { error: "parametros_no_encontrados" }));
    render(<ParametrosMotorLotePanel />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/no existen los parámetros/i);
  });

  it("AC5 — 403: la pantalla dice que hace falta el rol SuperAdmin", async () => {
    getMock.mockRejectedValue(new ApiError(403, "Forbidden"));
    render(<ParametrosMotorLotePanel />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/super ?admin/i);
  });

  it("AC1 — lleno: los 12 valores, el motor encendido y quién/cuándo cambió por última vez", async () => {
    getMock.mockResolvedValue(PARAMETROS);
    await cargado();
    expect(screen.getByRole("spinbutton", { name: TOPE })).toHaveValue(10000);
    expect(screen.getByRole("spinbutton", { name: /pdf por parte/i })).toHaveValue(500);
    expect(screen.getByRole("spinbutton", { name: /mb por parte/i })).toHaveValue(512);
    expect(screen.getByRole("spinbutton", { name: /carriles/i })).toHaveValue(2);
    expect(screen.getByRole("spinbutton", { name: /tiempo máximo por trámite/i })).toHaveValue(120);
    expect(screen.getByRole("spinbutton", { name: /lease por trámite/i })).toHaveValue(300);
    expect(screen.getByRole("spinbutton", { name: /reintentos por trámite/i })).toHaveValue(3);
    expect(screen.getByRole("spinbutton", { name: /espera entre reintentos/i })).toHaveValue(30);
    expect(screen.getByRole("spinbutton", { name: /tiempo máximo por parte/i })).toHaveValue(600);
    expect(screen.getByRole("spinbutton", { name: /lease por parte/i })).toHaveValue(900);
    expect(screen.getByRole("spinbutton", { name: /reintentos por parte/i })).toHaveValue(3);
    expect(screen.getByRole("spinbutton", { name: /horas de retención/i })).toHaveValue(72);
    expect(screen.getByRole("switch", { name: /motor encendido/i })).toBeChecked();
    const ultimo = screen.getByTestId("parametros-motor-ultimo-cambio");
    expect(ultimo).toHaveTextContent("Super Admin Demo");
    expect(ultimo).toHaveTextContent("07/10/2026");
  });

  it("AC1 — nunca editado: lo dice en vez de inventar autor", async () => {
    getMock.mockResolvedValue({ ...PARAMETROS, updatedAt: null, updatedBy: null, updatedByName: null });
    await cargado();
    expect(screen.getByTestId("parametros-motor-ultimo-cambio")).toHaveTextContent(/sin cambios/i);
  });

  it("AC7 — accesible: cada campo con nombre, rango del contrato como ayuda y botones con texto", async () => {
    getMock.mockResolvedValue(PARAMETROS);
    await cargado();
    const campos = screen.getAllByRole("spinbutton");
    expect(campos).toHaveLength(12);
    for (const c of campos) {
      expect(c).toHaveAccessibleName();
      expect(c.getAttribute("aria-describedby")).toBeTruthy();
    }
    expect(screen.getByRole("spinbutton", { name: TOPE })).toHaveAccessibleDescription(/1.*32\.766/);
    for (const b of screen.getAllByRole("button")) expect(b).toHaveAccessibleName();
  });

  it("AC3 — fuera de rango: error junto al campo, foco en él y NO llama al PUT", async () => {
    getMock.mockResolvedValue(PARAMETROS);
    await cargado();
    const input = await escribir(TOPE, "40000");
    await guardar();
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAccessibleDescription(/32\.766/);
    expect(input).toHaveFocus();
    expect(putMock).not.toHaveBeenCalled();
  });

  it("code review Obs2 — valor mayor que int32 en un campo sin máximo: error junto al campo y NO llama al PUT", async () => {
    getMock.mockResolvedValue(PARAMETROS);
    await cargado();
    const input = await escribir(/tiempo máximo por trámite/i, "3000000000");
    await guardar();
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAccessibleDescription(/no puede superar 2\.147\.483\.647/i);
    expect(input).toHaveFocus();
    expect(putMock).not.toHaveBeenCalled();
    expect(screen.getByTestId("parametros-motor-resultado")).toHaveTextContent(/revisa los campos marcados/i);
  });

  it("AC3 — lease ≤ timeout: error en el lease y NO llama al PUT", async () => {
    getMock.mockResolvedValue(PARAMETROS);
    await cargado();
    const lease = await escribir(/lease por trámite/i, "120");
    await guardar();
    expect(lease).toHaveAttribute("aria-invalid", "true");
    expect(lease).toHaveAccessibleDescription(/mayor que.*tiempo máximo por trámite/i);
    expect(putMock).not.toHaveBeenCalled();
  });

  it("AC3 — 400 del backend: el mensaje se pinta junto al campo señalado", async () => {
    getMock.mockResolvedValue(PARAMETROS);
    putMock.mockRejectedValue(
      new ApiError(400, "parametros_invalidos", {
        error: "parametros_invalidos",
        errors: { maxPdfsPerPart: ["Mensaje del backend para PDF por parte."] },
      }),
    );
    await cargado();
    await escribir(/pdf por parte/i, "600");
    await guardar();
    const pdf = screen.getByRole("spinbutton", { name: /pdf por parte/i });
    await waitFor(() => expect(pdf).toHaveAttribute("aria-invalid", "true"));
    expect(pdf).toHaveAccessibleDescription(/mensaje del backend para pdf por parte/i);
    expect(pdf).toHaveFocus();
  });

  it("AC2 — guardar válido: PUT con los valores + rowVersion y resultado anunciado en aria-live", async () => {
    getMock.mockResolvedValue(PARAMETROS);
    putMock.mockResolvedValue({
      ...PARAMETROS,
      maxItemsPerBatch: 5000,
      rowVersion: 8,
      updatedByName: "Otra Persona",
    });
    await cargado();
    await escribir(TOPE, "5000");
    await guardar();
    await waitFor(() => expect(putMock).toHaveBeenCalledTimes(1));
    expect(putMock.mock.calls[0][0]).toEqual({
      maxItemsPerBatch: 5000,
      maxPdfsPerPart: 500,
      maxMbPerPart: 512,
      itemSlots: 2,
      itemTimeoutSeconds: 120,
      itemLeaseSeconds: 300,
      maxItemAttempts: 3,
      retryDelaySeconds: 30,
      partTimeoutSeconds: 600,
      partLeaseSeconds: 900,
      maxPartAttempts: 3,
      retentionHours: 72,
      isActive: true,
      rowVersion: 7,
    });
    const resultado = screen.getByTestId("parametros-motor-resultado");
    expect(resultado).toHaveAttribute("aria-live", "polite");
    await waitFor(() => expect(resultado).toHaveTextContent(/parámetros guardados/i));
    expect(screen.getByTestId("parametros-motor-ultimo-cambio")).toHaveTextContent("Otra Persona");
  });

  it("AC4 — 409: avisa, conserva lo escrito y «recargar» trae el rowVersion nuevo sin borrar el borrador", async () => {
    getMock
      .mockResolvedValueOnce(PARAMETROS)
      .mockResolvedValueOnce({ ...PARAMETROS, retentionHours: 24, rowVersion: 9, updatedByName: "Otra Persona" });
    putMock
      .mockRejectedValueOnce(new ApiError(409, "row_version_conflict", { error: "row_version_conflict" }))
      .mockResolvedValueOnce({ ...PARAMETROS, maxItemsPerBatch: 5000, rowVersion: 10 });
    await cargado();
    await escribir(TOPE, "5000");
    await guardar();
    const aviso = await screen.findByTestId("parametros-motor-conflicto");
    expect(aviso).toHaveTextContent(/otro super admin/i);
    expect(screen.getByRole("spinbutton", { name: TOPE })).toHaveValue(5000);

    await userEvent.click(within(aviso).getByRole("button", { name: /recargar/i }));
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByTestId("parametros-motor-conflicto")).not.toBeInTheDocument());
    expect(screen.getByRole("spinbutton", { name: TOPE })).toHaveValue(5000);
    expect(screen.getByTestId("parametros-motor-ultimo-cambio")).toHaveTextContent("Otra Persona");

    await guardar();
    await waitFor(() => expect(putMock).toHaveBeenCalledTimes(2));
    expect(putMock.mock.calls[1][0]).toMatchObject({ maxItemsPerBatch: 5000, rowVersion: 9 });
  });

  it("AC6 — apagar el motor: advierte antes de guardar y, al guardar, anuncia el 503 motor_inactivo y la pausa", async () => {
    getMock.mockResolvedValue(PARAMETROS);
    putMock.mockResolvedValue({ ...PARAMETROS, isActive: false, rowVersion: 8 });
    await cargado();
    expect(screen.queryByTestId("parametros-motor-aviso-apagado")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("switch", { name: /motor encendido/i }));
    const aviso = screen.getByTestId("parametros-motor-aviso-apagado");
    expect(aviso).toHaveTextContent(/no se crean lotes nuevos/i);
    expect(aviso).toHaveTextContent(/motor_inactivo/);
    expect(aviso).toHaveTextContent(/en curso quedan en pausa hasta encenderlo/i);
    await guardar();
    await waitFor(() => expect(putMock).toHaveBeenCalledTimes(1));
    expect(putMock.mock.calls[0][0].isActive).toBe(false);
    await waitFor(() =>
      expect(screen.getByTestId("parametros-motor-resultado")).toHaveTextContent(/no se crean lotes nuevos/i),
    );
  });

  it("página /admin/plataforma/descarga-masiva: título y panel", async () => {
    getMock.mockResolvedValue(PARAMETROS);
    await act(async () => {
      render(<AdminDescargaMasivaPage />);
    });
    expect(screen.getByRole("heading", { name: /descarga masiva/i })).toBeInTheDocument();
    expect(await screen.findByRole("form", { name: /parámetros del motor/i })).toBeInTheDocument();
  });
});
