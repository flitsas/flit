// HU #10547 — Pantalla de requisitos configurables del OT.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { RequirementsSection } from "../RequirementsSection";
import type { OtRequirements } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtRequirements: vi.fn(),
  updateOtRequirements: vi.fn(),
}));

import { fetchOtRequirements, updateOtRequirements } from "@/lib/api/admin-ot";

const baseRequirements: OtRequirements = {
  transitOfficeId: "aaaaaaaa-0001-4000-8000-000000000001",
  requiresRnmc: false,
  allowPlatePreassign: false,
  identityValidationEnabled: true,
};

function renderSection(transitOfficeId?: string) {
  return render(
    <ToastProvider>
      <RequirementsSection transitOfficeId={transitOfficeId} />
    </ToastProvider>,
  );
}

describe("RequirementsSection — HU #10547", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtRequirements).mockResolvedValue({ ...baseRequirements });
    vi.mocked(updateOtRequirements).mockImplementation((body) =>
      Promise.resolve({ ...baseRequirements, ...body }),
    );
  });

  it("AC1 — activar 'Exige RNMC' y guardar persiste la configuración", async () => {
    const user = userEvent.setup();
    renderSection();

    const rnmc = await screen.findByRole("switch", { name: /Exige RNMC/i });
    expect(rnmc).toHaveAttribute("aria-checked", "false");

    await user.click(rnmc);
    expect(rnmc).toHaveAttribute("aria-checked", "true");

    await user.click(screen.getByRole("button", { name: /Guardar requisitos/i }));

    await waitFor(() =>
      expect(updateOtRequirements).toHaveBeenCalledWith(
        expect.objectContaining({ requiresRnmc: true }),
        undefined,
      ),
    );
  });

  it("renderiza los tres requisitos con su estado inicial (identidad exigida por defecto)", async () => {
    renderSection();

    expect(await screen.findByRole("switch", { name: /Exige RNMC/i })).toHaveAttribute(
      "aria-checked",
      "false",
    );
    expect(screen.getByRole("switch", { name: /Ruta de placa preasignada/i })).toHaveAttribute(
      "aria-checked",
      "false",
    );
    expect(screen.getByRole("switch", { name: /Validación de identidad/i })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });

  it("consulta los requisitos con el scope del SuperAdmin", async () => {
    renderSection("aaaaaaaa-0001-4000-8000-000000000001");
    await waitFor(() =>
      expect(fetchOtRequirements).toHaveBeenCalledWith(expect.anything(), {
        transitOfficeId: "aaaaaaaa-0001-4000-8000-000000000001",
      }),
    );
  });

  it("muestra el estado de error si la carga falla", async () => {
    vi.mocked(fetchOtRequirements).mockRejectedValueOnce(new Error("boom"));
    renderSection();
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
  });
});
