// HU #14390 (Feature #14387) — «Precios» en el hub del OT: solo su organismo y sin selector (AC1), cargos bloqueados en
// el formulario del ot_admin (AC2), valor general marcado y sin edición (AC3), historial con el autor (AC4) y SuperAdmin
// con todos los cargos del organismo del hub (AC5). La API del OT es un doble; el servidor decide `editable`.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import { OtPreciosSection } from "../OtPreciosSection";
import {
  fetchHistorialTarifaOt,
  fetchTarifasOt,
  fetchTiposTarifaOt,
  registrarTarifasOt,
  type TarifaOt,
} from "@/lib/api/admin-ot-portal-tarifas";

vi.mock("@/lib/api/admin-ot-portal-tarifas", () => ({
  fetchTarifasOt: vi.fn(),
  fetchHistorialTarifaOt: vi.fn(),
  fetchTiposTarifaOt: vi.fn(),
  registrarTarifasOt: vi.fn(),
}));
const permisos = vi.hoisted(() => ({ isSuperAdmin: false }));
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({ isSuperAdmin: permisos.isSuperAdmin }),
}));

const OT = "11111111-1111-1111-1111-111111111111";
const TIPO = "tipo-traspaso";
const TIPO2 = "tipo-matricula";

const tarifa = (over: Partial<TarifaOt>): TarifaOt => ({
  id: "x",
  concepto: "DERECHOS_TRANSITO",
  tipoTramiteId: TIPO,
  tipoTramiteCodigo: "TRASPASO",
  tipoTramiteNombre: "Traspaso",
  organismoId: OT,
  organismoCodigo: "BOG",
  organismoNombre: "Movilidad de Bogotá",
  valorCop: 245000,
  vigenteDesde: "2026-01-15",
  vigenteHasta: null,
  estado: "vigente",
  creadaEn: "2026-01-10T15:00:00Z",
  creadaPor: "u1",
  actualizadaEn: null,
  actualizadaPor: null,
  editable: true,
  creadaPorNombre: "Laura Gómez",
  ...over,
});

const LISTA: TarifaOt[] = [
  tarifa({ id: "a" }),
  tarifa({
    id: "g",
    tipoTramiteId: TIPO2,
    tipoTramiteNombre: "Matrícula inicial",
    organismoId: null,
    organismoNombre: null,
    valorCop: 150000,
    editable: false,
    creadaPorNombre: null,
  }),
];

function renderSection() {
  return render(
    <ToastProvider>
      <OtPreciosSection transitOfficeId={OT} />
    </ToastProvider>,
  );
}

describe("OtPreciosSection (#14390)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    permisos.isSuperAdmin = false;
    vi.mocked(fetchTarifasOt).mockResolvedValue(LISTA);
    vi.mocked(fetchTiposTarifaOt).mockResolvedValue([
      { id: TIPO, nombre: "Traspaso" },
      { id: TIPO2, nombre: "Matrícula inicial" },
    ]);
    vi.mocked(registrarTarifasOt).mockResolvedValue([tarifa({ id: "n" })]);
  });

  it("AC1: el ot_admin ve la tabla de su organismo, sin columna ni selector de organismo, y no envía el organismo", async () => {
    renderSection();
    const table = await screen.findByRole("table", { name: /tarifas del portal/i });
    const encabezados = within(table)
      .getAllByRole("columnheader")
      .map((h) => h.textContent ?? "");
    expect(encabezados.some((h) => /organismo/i.test(h))).toBe(false);
    expect(encabezados).toContain("Origen");
    expect(fetchTarifasOt).toHaveBeenCalledWith({}, undefined, expect.anything());

    await userEvent.click(screen.getByRole("button", { name: /nueva tarifa/i }));
    const modal = await screen.findByRole("dialog");
    expect(within(modal).queryByText(/^organismo$/i)).not.toBeInTheDocument();
    expect(within(modal).queryByText(/todos los organismos/i)).not.toBeInTheDocument();
  });

  it("AC2: servicio FLIT y envío no están activos para el ot_admin; los demás cargos sí", async () => {
    renderSection();
    await userEvent.click(await screen.findByRole("button", { name: /nueva tarifa/i }));
    const modal = await screen.findByRole("dialog");
    expect(within(modal).getByLabelText(/servicio digital/i)).toBeDisabled();
    expect(within(modal).getByLabelText(/envío del documento/i)).toBeDisabled();
    expect(within(modal).getByLabelText(/derechos de tránsito/i)).toBeEnabled();
    expect(within(modal).getByLabelText(/cambio de color/i)).toBeEnabled();
    expect(within(modal).getByLabelText(/conversión de combustible/i)).toBeEnabled();
    expect(within(modal).getByLabelText(/cambio de carrocería/i)).toBeEnabled();
  });

  it("guarda sin organismo en el cuerpo, con el trámite elegido y solo los cargos escritos", async () => {
    renderSection();
    await userEvent.click(await screen.findByRole("button", { name: /nueva tarifa/i }));
    const modal = await screen.findByRole("dialog");
    await userEvent.selectOptions(within(modal).getByLabelText(/^trámite$/i), TIPO2);
    await userEvent.type(within(modal).getByLabelText(/derechos de tránsito/i), "320000");
    await userEvent.click(within(modal).getByRole("button", { name: /guardar tarifa/i }));
    await waitFor(() => expect(registrarTarifasOt).toHaveBeenCalledTimes(1));
    const [body, scope] = vi.mocked(registrarTarifasOt).mock.calls[0] as unknown as [
      Record<string, unknown>,
      unknown,
    ];
    expect(body).toMatchObject({
      tipoTramiteId: TIPO2,
      valores: [{ concepto: "DERECHOS_TRANSITO", valorCop: 320000 }],
    });
    expect(scope).toBeUndefined();
  });

  it("exige el trámite en el formulario del OT (sin opción «Todos los trámites»)", async () => {
    renderSection();
    await userEvent.click(await screen.findByRole("button", { name: /nueva tarifa/i }));
    const modal = await screen.findByRole("dialog");
    expect(
      within(modal).queryByRole("option", { name: /todos los trámites/i }),
    ).not.toBeInTheDocument();
    await userEvent.type(within(modal).getByLabelText(/derechos de tránsito/i), "1000");
    await userEvent.click(within(modal).getByRole("button", { name: /guardar tarifa/i }));
    expect(
      await within(modal).findByText(/elige el trámite al que corresponde/i),
    ).toBeInTheDocument();
    expect(registrarTarifasOt).not.toHaveBeenCalled();
  });

  it("muestra el motivo exacto cuando el servidor responde 403", async () => {
    vi.mocked(registrarTarifasOt).mockRejectedValue(
      new ApiError(403, "forbidden", {
        code: "concepto_no_permitido",
        message: "El servicio digital lo define FLIT.",
      }),
    );
    renderSection();
    await userEvent.click(await screen.findByRole("button", { name: /nueva tarifa/i }));
    const modal = await screen.findByRole("dialog");
    await userEvent.selectOptions(within(modal).getByLabelText(/^trámite$/i), TIPO);
    await userEvent.type(within(modal).getByLabelText(/derechos de tránsito/i), "1000");
    await userEvent.click(within(modal).getByRole("button", { name: /guardar tarifa/i }));
    expect(
      await within(modal).findByText("El servicio digital lo define FLIT."),
    ).toBeInTheDocument();
  });

  it("AC3: el valor general se muestra marcado como general y no se puede editar", async () => {
    renderSection();
    const general = await screen.findByRole("row", { name: /matrícula inicial/i });
    expect(within(general).getByText("Valor general")).toBeInTheDocument();
    expect(within(general).getByText("$ 150.000")).toBeInTheDocument();
    expect(
      within(general).queryByRole("button", { name: /cambiar valores/i }),
    ).not.toBeInTheDocument();
    expect(within(general).getByRole("button", { name: /ver historial/i })).toBeInTheDocument();
    const propio = screen.getByRole("row", { name: /traspaso/i });
    expect(within(propio).getByText("De tu organismo")).toBeInTheDocument();
    expect(within(propio).getByRole("button", { name: /cambiar valores/i })).toBeInTheDocument();
  });

  it("AC4: el historial muestra quién hizo cada cambio", async () => {
    vi.mocked(fetchHistorialTarifaOt).mockResolvedValue([
      tarifa({ id: "a", creadaPorNombre: "Laura Gómez" }),
      tarifa({
        id: "a0",
        valorCop: 200000,
        estado: "cerrada",
        vigenteHasta: "2026-01-14",
        vigenteDesde: "2025-01-01",
        creadaPorNombre: "Pedro Ruiz",
      }),
    ]);
    renderSection();
    const fila = await screen.findByRole("row", { name: /traspaso/i });
    await userEvent.click(within(fila).getByRole("button", { name: /ver historial/i }));
    const modal = await screen.findByRole("dialog");
    expect(await within(modal).findByText("Laura Gómez")).toBeInTheDocument();
    expect(within(modal).getByText("Pedro Ruiz")).toBeInTheDocument();
    expect(
      within(modal).getByRole("columnheader", { name: /registrada por/i }),
    ).toBeInTheDocument();
    expect(fetchHistorialTarifaOt).toHaveBeenCalledWith("a", undefined, expect.anything());
  });

  it("AC5: el SuperAdmin edita todos los cargos del organismo del hub, enviando su identificador, sin selector", async () => {
    permisos.isSuperAdmin = true;
    renderSection();
    await screen.findByRole("table", { name: /tarifas del portal/i });
    expect(fetchTarifasOt).toHaveBeenCalledWith({}, { transitOfficeId: OT }, expect.anything());
    await userEvent.click(screen.getByRole("button", { name: /nueva tarifa/i }));
    const modal = await screen.findByRole("dialog");
    expect(within(modal).getByLabelText(/servicio digital/i)).toBeEnabled();
    expect(within(modal).getByLabelText(/envío del documento/i)).toBeEnabled();
    expect(within(modal).queryByText(/^organismo$/i)).not.toBeInTheDocument();
    await userEvent.selectOptions(within(modal).getByLabelText(/^trámite$/i), TIPO);
    await userEvent.type(within(modal).getByLabelText(/servicio digital/i), "60000");
    await userEvent.click(within(modal).getByRole("button", { name: /guardar tarifa/i }));
    await waitFor(() => expect(registrarTarifasOt).toHaveBeenCalledTimes(1));
    expect(vi.mocked(registrarTarifasOt).mock.calls[0][1]).toEqual({ transitOfficeId: OT });
  });

  it("un cargo que el servidor marca no editable queda bloqueado al cambiar valores", async () => {
    vi.mocked(fetchTarifasOt).mockResolvedValue([
      tarifa({ id: "a" }),
      tarifa({ id: "s", concepto: "SERVICIO_FLIT", valorCop: 60000, editable: false }),
    ]);
    renderSection();
    const fila = await screen.findByRole("row", { name: /traspaso/i });
    await userEvent.click(within(fila).getByRole("button", { name: /cambiar valores/i }));
    const modal = await screen.findByRole("dialog");
    expect(within(modal).getByLabelText(/servicio digital/i)).toBeDisabled();
    expect(within(modal).getByLabelText(/derechos de tránsito/i)).toBeEnabled();
  });

  it("estados: cargando, vacío y error con reintento", async () => {
    vi.mocked(fetchTarifasOt).mockResolvedValueOnce([]);
    const { unmount } = renderSection();
    expect(screen.getByText(/cargando tarifas/i)).toBeInTheDocument();
    expect(await screen.findByText(/aún no hay tarifas/i)).toBeInTheDocument();
    unmount();

    vi.mocked(fetchTarifasOt).mockRejectedValueOnce(new Error("x")).mockResolvedValueOnce(LISTA);
    renderSection();
    expect(await screen.findByText(/no se pudieron cargar las tarifas/i)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByRole("table", { name: /tarifas del portal/i })).toBeInTheDocument();
  });
});
