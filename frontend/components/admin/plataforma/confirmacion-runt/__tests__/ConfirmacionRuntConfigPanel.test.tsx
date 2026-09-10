// HU #12279 (Feature #12276) — pestaña Configuración: carga desde el backend, guardar/descartar,
// errores por campo del 400 y resumen de la última corrida.
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "@/components/admin/Toast";
import { ConfirmacionRuntConfigPanel } from "../ConfirmacionRuntConfigPanel";

const getRuntConfirmationSettings = vi.fn();
const putRuntConfirmationSettings = vi.fn();
const getLatestRuntConfirmationRun = vi.fn();

vi.mock("@/lib/api/admin-runt-confirmation", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-runt-confirmation")>(
    "@/lib/api/admin-runt-confirmation",
  );
  return {
    ...actual,
    getRuntConfirmationSettings: (...a: unknown[]) => getRuntConfirmationSettings(...a),
    putRuntConfirmationSettings: (...a: unknown[]) => putRuntConfirmationSettings(...a),
    getLatestRuntConfirmationRun: (...a: unknown[]) => getLatestRuntConfirmationRun(...a),
  };
});

const settings = {
  enabled: true,
  runAtLocal: "02:00",
  providerKey: "kyverum_runt",
  graceDays: 0,
  discrepancyAfterRuns: 3,
  maxAttempts: 10,
  updatedAt: null,
  updatedBy: null,
};

const run = {
  id: "r1",
  trigger: "scheduled",
  startedAt: "2026-09-10T07:00:00Z",
  finishedAt: "2026-09-10T07:01:30Z",
  providerKey: "kyverum_runt",
  skippedReason: null,
  consulted: 38,
  confirmed: 20,
  pending: 12,
  discrepancies: 3,
  unverifiable: 2,
  errors: 1,
  providerCalls: 45,
  errorMessage: null,
};

function renderPanel() {
  return render(
    <ToastProvider>
      <ConfirmacionRuntConfigPanel />
    </ToastProvider>,
  );
}

describe("ConfirmacionRuntConfigPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getRuntConfirmationSettings.mockResolvedValue(settings);
    getLatestRuntConfirmationRun.mockResolvedValue(run);
  });

  it("AC2 — el interruptor y los cinco campos cargan desde el backend, con hint y solo dos proveedores", async () => {
    renderPanel();

    const toggle = await screen.findByRole("switch", { name: "Consulta programada activa" });
    expect(toggle).toBeChecked();
    expect(screen.getByLabelText("Hora de ejecución diaria")).toHaveValue("02:00");
    expect(screen.getByLabelText("Proveedor activo")).toHaveValue("kyverum_runt");
    expect(screen.getByLabelText("Días de gracia tras la aprobación")).toHaveValue(0);
    expect(screen.getByLabelText("Corridas en NO antes de marcar discrepancia")).toHaveValue(3);
    expect(screen.getByLabelText("Tope de reintentos")).toHaveValue(10);

    const opciones = within(screen.getByLabelText("Proveedor activo")).getAllByRole("option").map((o) => o.textContent);
    expect(opciones).toEqual(["Kyverum", "Verifik"]);
    expect(screen.getByText(/Días de espera tras la aprobación/)).toBeInTheDocument();
  });

  it("AC3 — guardar envía los seis valores y avisa; descartar vuelve a lo persistido", async () => {
    putRuntConfirmationSettings.mockImplementation(async (input: typeof settings) => ({ ...settings, ...input, updatedAt: "2026-09-10T10:00:00Z" }));
    renderPanel();
    const user = userEvent.setup();

    const hora = await screen.findByLabelText("Hora de ejecución diaria");
    const guardar = screen.getByRole("button", { name: "Guardar configuración" });
    expect(guardar).toBeDisabled();

    await user.clear(hora);
    await user.type(hora, "03:30");
    const tope = screen.getByLabelText("Tope de reintentos");
    await user.clear(tope);
    await user.type(tope, "8");

    // Descartar antes de guardar devuelve los valores persistidos.
    await user.click(screen.getByRole("button", { name: "Descartar cambios" }));
    expect(screen.getByLabelText("Hora de ejecución diaria")).toHaveValue("02:00");
    expect(screen.getByLabelText("Tope de reintentos")).toHaveValue(10);

    await user.clear(hora);
    await user.type(hora, "03:30");
    await user.clear(tope);
    await user.type(tope, "8");
    await user.click(screen.getByRole("button", { name: "Guardar configuración" }));

    await waitFor(() => expect(putRuntConfirmationSettings).toHaveBeenCalledTimes(1));
    expect(putRuntConfirmationSettings).toHaveBeenCalledWith({
      enabled: true,
      runAtLocal: "03:30",
      providerKey: "kyverum_runt",
      graceDays: 0,
      discrepancyAfterRuns: 3,
      maxAttempts: 8,
    });
    expect(await screen.findByText("Configuración guardada")).toBeInTheDocument();
  });

  it("AC4 — un 400 que señala maxAttempts marca solo ese campo", async () => {
    const { RuntConfirmationSettingsInvalidError } = await import("@/lib/api/admin-runt-confirmation");
    putRuntConfirmationSettings.mockRejectedValue(
      new RuntConfirmationSettingsInvalidError([{ field: "maxAttempts", message: "El tope de reintentos no puede ser menor que las corridas antes de discrepancia." }]),
    );
    renderPanel();
    const user = userEvent.setup();

    const tope = await screen.findByLabelText("Tope de reintentos");
    await user.clear(tope);
    await user.type(tope, "2");
    await user.click(screen.getByRole("button", { name: "Guardar configuración" }));

    const error = await screen.findByRole("alert");
    expect(error).toHaveTextContent(/no puede ser menor/);
    expect(tope).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Días de gracia tras la aprobación")).not.toHaveAttribute("aria-invalid");
    expect(screen.getByLabelText("Hora de ejecución diaria")).not.toHaveAttribute("aria-invalid");
  });

  it("AC5 — muestra el resumen de la última corrida con contadores, duración y llamadas", async () => {
    renderPanel();

    const resumen = await screen.findByTestId("confirmacion-runt-ultima-corrida");
    expect(within(resumen).getByText("Kyverum")).toBeInTheDocument();
    expect(within(resumen).getByText("38")).toBeInTheDocument();
    expect(within(resumen).getByText("45")).toBeInTheDocument();
    expect(within(resumen).getByText("1 min 30 s")).toBeInTheDocument();
    expect(within(resumen).getByText("Programada")).toBeInTheDocument();
  });

  it("AC5 — sin corridas dice «Sin corridas todavía»", async () => {
    getLatestRuntConfirmationRun.mockResolvedValue(null);
    renderPanel();
    expect(await screen.findByText("Sin corridas todavía")).toBeInTheDocument();
  });

  it("AC6 — si la carga falla muestra el error con reintento, sin formulario a medias", async () => {
    getRuntConfirmationSettings.mockRejectedValueOnce(new Error("boom"));
    renderPanel();

    expect(await screen.findByText("No se pudo cargar la configuración")).toBeInTheDocument();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();

    getRuntConfirmationSettings.mockResolvedValue(settings);
    await userEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("switch", { name: "Consulta programada activa" })).toBeInTheDocument();
  });
});
