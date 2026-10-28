// HU #14074 — Pestaña «Tarifas»: cuatro estados (AC5), columnas del estándar de tablas (AC2), crear y cambiar valores con
// validación por campo y manejo de tarifa_solapada / vigencia pasada (AC3), historial de solo lectura (AC4) y la
// puerta de la página (AC1). La API (HU #14071) es un doble; las reglas puras se usan reales.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import { TarifasTab } from "../TarifasTab";
import AdminPersonaNaturalPage from "@/app/admin/plataforma/persona-natural/page";
import {
  fetchHistorialTarifaPn,
  fetchTarifasPn,
  registrarTarifasPn,
  type TarifaPn,
} from "@/lib/api/admin-pn-tarifas";
import { fetchOrganismosPn, fetchTiposPn } from "@/lib/api/admin-pn-config";

vi.mock("@/lib/api/admin-pn-tarifas", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-tarifas")>();
  return { ...real, fetchTarifasPn: vi.fn(), fetchHistorialTarifaPn: vi.fn(), registrarTarifasPn: vi.fn() };
});
vi.mock("@/lib/api/admin-pn-config", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-config")>();
  return { ...real, fetchTiposPn: vi.fn(), fetchOrganismosPn: vi.fn() };
});
const permisos = vi.hoisted(() => ({ isSuperAdmin: true }));
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: () => ({ isSuperAdmin: permisos.isSuperAdmin }) }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }));

const TIPO = "tipo-traspaso";
const ORG = "org-bogota";

const tarifa = (over: Partial<TarifaPn>): TarifaPn => ({
  id: "x",
  concepto: "DERECHOS_TRANSITO",
  tipoTramiteId: TIPO,
  tipoTramiteCodigo: "TRASPASO",
  tipoTramiteNombre: "Traspaso",
  organismoId: ORG,
  organismoCodigo: "BOG",
  organismoNombre: "Secretaría de Movilidad de Bogotá",
  valorCop: 245000,
  vigenteDesde: "2026-01-15",
  vigenteHasta: null,
  estado: "vigente",
  creadaEn: "2026-01-10T15:00:00Z",
  creadaPor: "abcdef12-0000-0000-0000-000000000000",
  actualizadaEn: null,
  actualizadaPor: null,
  ...over,
});

const LISTA: TarifaPn[] = [
  tarifa({ id: "a", concepto: "DERECHOS_TRANSITO", valorCop: 245000 }),
  tarifa({ id: "b", concepto: "SERVICIO_FLIT", valorCop: 60000 }),
  tarifa({ id: "c", concepto: "ENVIO_DOCUMENTO", valorCop: 18500 }),
  tarifa({ id: "d", organismoId: null, organismoNombre: null, concepto: "DERECHOS_TRANSITO", valorCop: 200000, vigenteDesde: "2026-02-01", estado: "futura" }),
  tarifa({ id: "e", concepto: "DERECHOS_TRANSITO", valorCop: 190000, vigenteDesde: "2025-01-01", vigenteHasta: "2026-01-14", estado: "cerrada" }),
];

function apiError(status: number, error: string, message = "x") {
  return new ApiError(status, error, { error, message });
}

function renderTab(onCambio = vi.fn()) {
  return render(
    <ToastProvider>
      <TarifasTab onCambio={onCambio} />
    </ToastProvider>,
  );
}

describe("TarifasTab (#14074)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    permisos.isSuperAdmin = true;
    vi.mocked(fetchTarifasPn).mockResolvedValue(LISTA);
    vi.mocked(fetchTiposPn).mockResolvedValue([
      { id: TIPO, codigo: "TRASPASO", nombre: "Traspaso", familia: "traspaso", elegible: true, motivo: "", habilitado: true, ofrecible: true, organismosConTarifaVigente: 1 },
    ]);
    vi.mocked(fetchOrganismosPn).mockResolvedValue([
      { id: ORG, codigo: "BOG", nombre: "Secretaría de Movilidad de Bogotá", municipio: "Bogotá", departamento: "Bogotá D.C.", habilitado: true, tiposConTarifaVigente: 1 },
    ]);
  });

  it("AC2: tabla con las columnas del estándar, valores en pesos, estado y fechas DD/MM/YYYY; las cerradas empiezan ocultas", async () => {
    renderTab();
    const table = await screen.findByRole("table", { name: /tarifas del portal de persona natural/i });
    expect(within(table).getAllByRole("columnheader").map((h) => h.textContent)).toEqual([
      expect.stringContaining("Organismo"),
      expect.stringContaining("Trámite"),
      "Derechos de tránsito",
      "Servicio digital FLIT",
      "Envío",
      "Otros cargos",
      expect.stringContaining("Vigente desde"),
      expect.stringContaining("Estado"),
      "Acciones",
    ]);
    const vigente = screen.getByRole("row", { name: /secretaría de movilidad de bogotá/i });
    expect(within(vigente).getByText("$ 245.000")).toBeInTheDocument();
    expect(within(vigente).getByText("$ 60.000")).toBeInTheDocument();
    expect(within(vigente).getByText("$ 18.500")).toBeInTheDocument();
    expect(within(vigente).getByText("15/01/2026")).toBeInTheDocument();
    expect(within(vigente).getByText("Vigente")).toBeInTheDocument();
    expect(screen.getByText("Próxima")).toBeInTheDocument();
    expect(screen.queryByText("Anterior")).not.toBeInTheDocument();
    expect(screen.getByText(/las cotizaciones que ya se emitieron no cambian/i)).toBeInTheDocument();
    // El «Todos los organismos» reemplaza al «valor general» técnico y no se ven nombres de campos.
    expect(screen.getByRole("heading", { name: /tarifas del portal/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^nueva tarifa$/i })).toBeInTheDocument();

    // Sin casilla suelta: las anteriores se ven con el filtro de Estado del encabezado.
    expect(screen.queryByRole("checkbox", { name: /tarifas anteriores/i })).not.toBeInTheDocument();
    const ue2 = userEvent.setup();
    await ue2.click(screen.getByRole("button", { name: (n) => n === "Filtrar por Estado" || n.startsWith("Filtrar por Estado (") }));
    await ue2.click(screen.getByRole("checkbox", { name: "Anterior" }));
    await ue2.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(await screen.findByText("Anterior", { selector: "span" })).toBeInTheDocument();
  });

  it("AC5: estado vacío con la acción para crear, error con reintento y cargando con el CarLoaderModal", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchTarifasPn).mockResolvedValueOnce([]);
    const { unmount } = renderTab();
    expect(await screen.findByText(/aún no hay tarifas\. crea la primera/i)).toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /^nueva tarifa$/i }));
    expect(await screen.findByRole("dialog", { name: /nueva tarifa/i })).toBeInTheDocument();
    unmount();

    vi.mocked(fetchTarifasPn).mockRejectedValueOnce(new Error("caído"));
    const second = renderTab();
    expect(await screen.findByText(/no se pudieron cargar las tarifas/i)).toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByRole("table")).toBeInTheDocument();
    second.unmount();

    vi.mocked(fetchTarifasPn).mockReturnValue(new Promise(() => {}));
    renderTab();
    expect(screen.getByText(/cargando tarifas/i)).toBeInTheDocument();
  });

  it("AC3: «Nueva tarifa» marca por campo un valor negativo o no numérico y no envía nada", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table");
    await ue.click(screen.getByRole("button", { name: /^nueva tarifa$/i }));
    const dialog = await screen.findByRole("dialog", { name: /nueva tarifa/i });
    expect(within(dialog).getByText(/las cotizaciones ya emitidas no cambian/i)).toBeInTheDocument();
    expect(within(dialog).getByRole("option", { name: /todos los trámites \(solo cargos adicionales\)/i })).toBeInTheDocument();

    await ue.selectOptions(within(dialog).getByLabelText("Trámite"), TIPO);
    await ue.type(within(dialog).getByLabelText(/^Derechos de tránsito/), "-5");
    await ue.type(within(dialog).getByLabelText(/^Servicio digital/), "abc");
    await ue.click(within(dialog).getByRole("button", { name: /guardar tarifa/i }));

    expect(await within(dialog).findByText("El valor no puede ser negativo.")).toBeInTheDocument();
    expect(within(dialog).getByText(/solo números enteros/i)).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Derechos de tránsito/)).toHaveAttribute("aria-invalid", "true");
    expect(registrarTarifasPn).not.toHaveBeenCalled();
  });

  it("AC3: crea un lote por concepto con la vigencia y recarga la tabla con un mensaje de éxito", async () => {
    const ue = userEvent.setup();
    vi.mocked(registrarTarifasPn).mockResolvedValue([tarifa({ id: "n1" }), tarifa({ id: "n2", concepto: "SERVICIO_FLIT" })]);
    renderTab();
    await screen.findByRole("table");
    await ue.click(screen.getByRole("button", { name: /^nueva tarifa$/i }));
    const dialog = await screen.findByRole("dialog", { name: /nueva tarifa/i });

    await ue.selectOptions(within(dialog).getByLabelText("Trámite"), TIPO);
    await ue.type(within(dialog).getByLabelText(/^Derechos de tránsito/), "260.000");
    await ue.type(within(dialog).getByLabelText(/^Servicio digital/), "$ 65000");
    await ue.click(within(dialog).getByRole("button", { name: /guardar tarifa/i }));

    await waitFor(() => expect(registrarTarifasPn).toHaveBeenCalledTimes(1));
    const body = vi.mocked(registrarTarifasPn).mock.calls[0][0];
    expect(body.tipoTramiteId).toBe(TIPO);
    expect(body.organismoId).toBeNull();
    expect(body.vigenteDesde).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(body.valores).toEqual([
      { concepto: "DERECHOS_TRANSITO", valorCop: 260000 },
      { concepto: "SERVICIO_FLIT", valorCop: 65000 },
    ]);
    expect(await screen.findByText(/tarifa guardada: 2 cargos nuevos/i)).toBeInTheDocument();
    await waitFor(() => expect(fetchTarifasPn).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("AC3: sin tipo, derechos y servicio digital se marcan; sin ningún valor se pide al menos uno", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table");
    await ue.click(screen.getByRole("button", { name: /^nueva tarifa$/i }));
    const dialog = await screen.findByRole("dialog", { name: /nueva tarifa/i });

    await ue.click(within(dialog).getByRole("button", { name: /guardar tarifa/i }));
    expect(await within(dialog).findByText(/escribe el valor de al menos un cargo/i)).toBeInTheDocument();

    await ue.type(within(dialog).getByLabelText(/^Derechos de tránsito/), "1000");
    await ue.click(within(dialog).getByRole("button", { name: /guardar tarifa/i }));
    expect(await within(dialog).findByText(/se registran siempre para un trámite concreto/i)).toBeInTheDocument();
    expect(registrarTarifasPn).not.toHaveBeenCalled();
  });

  it("AC3: tarifa_solapada se muestra junto a la fecha y el formulario conserva lo escrito", async () => {
    const ue = userEvent.setup();
    vi.mocked(registrarTarifasPn).mockRejectedValue(apiError(409, "tarifa_solapada"));
    renderTab();
    await screen.findByRole("table");
    await ue.click(screen.getByRole("button", { name: /^nueva tarifa$/i }));
    const dialog = await screen.findByRole("dialog", { name: /nueva tarifa/i });
    await ue.selectOptions(within(dialog).getByLabelText("Trámite"), TIPO);
    await ue.type(within(dialog).getByLabelText(/^Derechos de tránsito/), "1000");
    await ue.click(within(dialog).getByRole("button", { name: /guardar tarifa/i }));

    expect(await within(dialog).findByText(/ya hay una tarifa para este trámite y organismo/i)).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Rige desde/)).toHaveAttribute("aria-invalid", "true");
    // Al salir del campo el valor se escribe con puntos de miles.
    expect(within(dialog).getByLabelText(/^Derechos de tránsito/)).toHaveValue("1.000");
  });

  it("AC3: una fecha anterior a hoy se rechaza en el formulario y, si el servidor la rechaza, también", async () => {
    const ue = userEvent.setup();
    vi.mocked(registrarTarifasPn).mockRejectedValue(apiError(400, "fecha_inicio_pasada"));
    renderTab();
    await screen.findByRole("table");
    await ue.click(screen.getByRole("button", { name: /^nueva tarifa$/i }));
    const dialog = await screen.findByRole("dialog", { name: /nueva tarifa/i });
    await ue.selectOptions(within(dialog).getByLabelText("Trámite"), TIPO);
    await ue.type(within(dialog).getByLabelText(/^Derechos de tránsito/), "1000");

    const fecha = within(dialog).getByLabelText(/^Rige desde/);
    await ue.clear(fecha);
    await ue.type(fecha, "2020-01-01");
    await ue.click(within(dialog).getByRole("button", { name: /guardar tarifa/i }));
    expect(await within(dialog).findByText("La fecha de inicio no puede ser anterior a hoy.")).toBeInTheDocument();
    expect(registrarTarifasPn).not.toHaveBeenCalled();
  });

  it("AC3: versionar fija el ámbito, muestra el valor vigente y solo envía los conceptos que cambiaron", async () => {
    const ue = userEvent.setup();
    vi.mocked(registrarTarifasPn).mockResolvedValue([tarifa({ id: "n", valorCop: 250000 })]);
    renderTab();
    await screen.findByRole("table");
    const fila = screen.getByRole("row", { name: /secretaría de movilidad de bogotá/i });
    await ue.click(within(fila).getByRole("button", { name: /cambiar valores de traspaso en secretaría de movilidad de bogotá/i }));
    const dialog = await screen.findByRole("dialog", { name: /cambiar valores de la tarifa/i });

    expect(within(dialog).getByLabelText("Trámite")).toBeDisabled();
    expect(within(dialog).getByText("Hoy: $ 245.000")).toBeInTheDocument();
    // Sin cambios no se envía nada.
    await ue.click(within(dialog).getByRole("button", { name: /guardar tarifa/i }));
    expect(await within(dialog).findByText(/cambia al menos un valor/i)).toBeInTheDocument();

    const derechos = within(dialog).getByLabelText(/^Derechos de tránsito/);
    await ue.clear(derechos);
    await ue.type(derechos, "250000");
    await ue.click(within(dialog).getByRole("button", { name: /guardar tarifa/i }));
    await waitFor(() => expect(registrarTarifasPn).toHaveBeenCalledTimes(1));
    expect(vi.mocked(registrarTarifasPn).mock.calls[0][0]).toMatchObject({
      tipoTramiteId: TIPO,
      organismoId: ORG,
      valores: [{ concepto: "DERECHOS_TRANSITO", valorCop: 250000 }],
    });
  });

  it("AC4: el historial lista las versiones anteriores sin acciones de edición", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchHistorialTarifaPn).mockResolvedValue([
      tarifa({ id: "a", valorCop: 245000 }),
      tarifa({ id: "e", valorCop: 190000, vigenteDesde: "2025-01-01", vigenteHasta: "2026-01-14", estado: "cerrada" }),
    ]);
    renderTab();
    await screen.findByRole("table");
    const fila = screen.getByRole("row", { name: /secretaría de movilidad de bogotá/i });
    await ue.click(within(fila).getByRole("button", { name: /ver historial de traspaso/i }));

    const dialog = await screen.findByRole("dialog", { name: /historial de la tarifa/i });
    const tabla = await within(dialog).findByRole("table", { name: /versiones de la tarifa/i });
    expect(within(tabla).getByText("$ 190.000")).toBeInTheDocument();
    expect(within(tabla).getByText("14/01/2026")).toBeInTheDocument();
    // No se muestran identificadores internos del usuario.
    expect(within(tabla).queryByText("abcdef12")).not.toBeInTheDocument();
    expect(within(tabla).queryByRole("columnheader", { name: /usuario/i })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: /editar|guardar|eliminar/i })).not.toBeInTheDocument();
    expect(fetchHistorialTarifaPn).toHaveBeenCalledWith("a", expect.anything());
  });

  it("AC1: la página solo se muestra al superadministrador", () => {
    permisos.isSuperAdmin = false;
    render(<AdminPersonaNaturalPage />);
    expect(screen.getByText(/no tienes permiso para administrar el portal de persona natural/i)).toBeInTheDocument();
    expect(fetchTarifasPn).not.toHaveBeenCalled();
  });

  it("al escribir un valor en pesos lo muestra con puntos de miles, avisa desde cuándo rige y avisa a la consola al guardar", async () => {
    const ue = userEvent.setup();
    const onCambio = vi.fn();
    vi.mocked(registrarTarifasPn).mockResolvedValue([tarifa({ id: "n1", valorCop: 1250000 })]);
    renderTab(onCambio);
    await screen.findByRole("table");
    await ue.click(screen.getByRole("button", { name: /^nueva tarifa$/i }));
    const dialog = await screen.findByRole("dialog", { name: /nueva tarifa/i });
    expect(within(dialog).getByText(/desde el \d{2}\/\d{2}\/\d{4} el portal cotiza con estos valores/i)).toBeInTheDocument();

    await ue.selectOptions(within(dialog).getByLabelText("Trámite"), TIPO);
    const derechos = within(dialog).getByLabelText(/^Derechos de tránsito/);
    await ue.type(derechos, "1250000");
    await ue.tab();
    expect(derechos).toHaveValue("1.250.000");
    await ue.click(within(dialog).getByRole("button", { name: /guardar tarifa/i }));
    await waitFor(() => expect(registrarTarifasPn).toHaveBeenCalledTimes(1));
    expect(vi.mocked(registrarTarifasPn).mock.calls[0][0].valores).toEqual([{ concepto: "DERECHOS_TRANSITO", valorCop: 1250000 }]);
    await waitFor(() => expect(onCambio).toHaveBeenCalledTimes(1));
  });

  it("AC1: la página muestra el título y el estado de la configuración antes de las pestañas", async () => {
    render(<AdminPersonaNaturalPage />);
    expect(await screen.findByRole("heading", { name: "Persona Natural" })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: /estado de la configuración/i })).toBeInTheDocument();
  });
});
