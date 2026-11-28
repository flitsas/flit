import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DrFlitVisibilidadPanel } from "../DrFlitVisibilidadPanel";

const getSettings = vi.fn();
const putSettings = vi.fn();

vi.mock("@/lib/api/admin-plataforma-dr-flit", () => ({
  getDrFlitPlatformSettings: (...args: unknown[]) => getSettings(...args),
  putDrFlitPlatformSettings: (...args: unknown[]) => putSettings(...args),
  interpretarErrorDrFlitSettings: () => "Error de prueba",
}));

describe("DrFlitVisibilidadPanel (HU #14772)", () => {
  beforeEach(() => {
    getSettings.mockReset();
    putSettings.mockReset();
  });

  it("AC4 — estado carga y luego ready con switch", async () => {
    getSettings.mockResolvedValue({
      uiEnabled: false,
      rowVersion: 1,
      updatedAt: null,
      updatedBy: null,
    });
    render(<DrFlitVisibilidadPanel />);
    expect(screen.getByTestId("dr-flit-settings-loading")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId("dr-flit-settings-ready")).toBeInTheDocument());
    expect(screen.getByRole("switch", { name: "Mostrar asistente DR. FLIT" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });

  it("AC2 — al activar el switch hace PUT y refleja el valor", async () => {
    getSettings.mockResolvedValue({
      uiEnabled: false,
      rowVersion: 2,
      updatedAt: null,
      updatedBy: null,
    });
    putSettings.mockResolvedValue({
      uiEnabled: true,
      rowVersion: 3,
      updatedAt: "2026-11-27T20:00:00Z",
      updatedBy: "14771000-0000-4000-8000-0000000000b1",
    });
    render(<DrFlitVisibilidadPanel />);
    await waitFor(() => screen.getByTestId("dr-flit-settings-ready"));
    await userEvent.click(screen.getByRole("switch", { name: "Mostrar asistente DR. FLIT" }));
    await waitFor(() => expect(putSettings).toHaveBeenCalled());
    expect(putSettings.mock.calls[0][0]).toEqual({ uiEnabled: true, rowVersion: 2 });
    expect(screen.getByRole("switch", { name: "Mostrar asistente DR. FLIT" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });

  it("AC4 — estado error con reintentar", async () => {
    getSettings.mockRejectedValue(new Error("fail"));
    render(<DrFlitVisibilidadPanel />);
    await waitFor(() => expect(screen.getByText("No se pudo cargar la configuración")).toBeInTheDocument());
    getSettings.mockResolvedValue({
      uiEnabled: true,
      rowVersion: 1,
      updatedAt: null,
      updatedBy: null,
    });
    await userEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(screen.getByTestId("dr-flit-settings-ready")).toBeInTheDocument());
  });
});
