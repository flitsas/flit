import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { IctJobSettingsForm } from "../IctJobSettingsForm";
import { ApiError } from "@/lib/api/types";
import type { IctJobSettings } from "@/lib/api/admin-ict-job-settings";

vi.mock("@/lib/api/admin-ict-job-settings", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-ict-job-settings")>();
  return {
    ...actual,
    fetchIctJobSettings: vi.fn(),
    saveIctJobSettings: vi.fn(),
  };
});

import { fetchIctJobSettings, saveIctJobSettings } from "@/lib/api/admin-ict-job-settings";

function settings(overrides: Partial<IctJobSettings> = {}): IctJobSettings {
  return {
    windowStartHour: 8,
    windowEndHour: 20,
    businessPollSeconds: 45,
    externalPollSeconds: 45,
    orchestratorPollSeconds: 20,
    orchestratorConcurrency: 10,
    orchestratorBatchSize: 50,
    sendPollSeconds: 20,
    sendConcurrency: 5,
    sendBatchSize: 50,
    webhookPollSeconds: 10,
    webhookBatchSize: 50,
    businessBatchSize: 500,
    externalBatchSize: 500,
    updatedAt: "2026-09-11T15:00:00Z",
    updatedBy: "11111111-1111-1111-1111-111111111111",
    ...overrides,
  };
}

function renderForm() {
  return render(
    <ToastProvider>
      <IctJobSettingsForm />
    </ToastProvider>,
  );
}

describe("IctJobSettingsForm — HU #12123", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(saveIctJobSettings).mockResolvedValue(settings());
  });

  it("muestra estado de carga y luego el formulario lleno", async () => {
    vi.mocked(fetchIctJobSettings).mockResolvedValue(settings());
    renderForm();
    expect(screen.getByRole("status", { busy: true })).toBeInTheDocument();
    expect(screen.getByText("Cargando cadencia ICT…")).toBeInTheDocument();
    expect(await screen.findByLabelText("Hora de inicio")).toHaveValue("8");
    expect(screen.getByLabelText("Lote", { selector: "#ict-business-batch" })).toHaveValue("500");
    expect(screen.getByRole("link", { name: /últimas corridas/i })).toHaveAttribute(
      "href",
      "/?m=ict-reportes&ictReportesTab=jobs",
    );
  });

  it("estado vacío cuando aún no hay updatedAt", async () => {
    vi.mocked(fetchIctJobSettings).mockResolvedValue(settings({ updatedAt: null, updatedBy: null }));
    renderForm();
    expect(await screen.findByText(/todavía no hay una configuración guardada/i)).toBeInTheDocument();
  });

  it("estado de error de carga", async () => {
    vi.mocked(fetchIctJobSettings).mockRejectedValue(new Error("network"));
    renderForm();
    expect(await screen.findByRole("alert")).toHaveTextContent(/no se pudo cargar/i);
  });

  it("guarda valores válidos y muestra confirmación", async () => {
    vi.mocked(fetchIctJobSettings).mockResolvedValue(settings());
    const user = userEvent.setup();
    renderForm();
    await screen.findByLabelText("Hora de inicio");
    await user.click(screen.getByRole("button", { name: /guardar configuración/i }));
    await waitFor(() => expect(saveIctJobSettings).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(/configuración ict guardada/i)).toBeInTheDocument();
  });

  it("valores inválidos no viajan al PUT", async () => {
    vi.mocked(fetchIctJobSettings).mockResolvedValue(settings());
    const user = userEvent.setup();
    renderForm();
    const poll = await screen.findByLabelText("Intervalo (segundos)", {
      selector: "#ict-business-poll",
    });
    await user.clear(poll);
    await user.type(poll, "0");
    await user.click(screen.getByRole("button", { name: /guardar configuración/i }));
    expect(saveIctJobSettings).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent(/fuera de rango/i);
    expect(screen.getByText(/entre 1 y 3600 segundos/i)).toBeInTheDocument();
  });

  it("error de PUT no simula éxito y conserva el formulario", async () => {
    vi.mocked(fetchIctJobSettings).mockResolvedValue(settings());
    vi.mocked(saveIctJobSettings).mockRejectedValue(new ApiError(500, "boom"));
    const user = userEvent.setup();
    renderForm();
    await screen.findByLabelText("Hora de inicio");
    await user.click(screen.getByRole("button", { name: /guardar configuración/i }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/no se pudo guardar/i);
    expect(screen.queryByText(/configuración ict guardada/i)).not.toBeInTheDocument();
    expect(screen.getByLabelText("Hora de inicio")).toHaveValue("8");
  });
});
