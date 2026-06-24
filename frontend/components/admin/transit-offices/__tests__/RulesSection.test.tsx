// HU #10223 — Constructor visual de reglas AND/OR con hot-swap.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { RulesSection } from "../RulesSection";
import type { OtRule } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtRules: vi.fn(),
  createOtRule: vi.fn(),
  updateOtRule: vi.fn(),
}));

import { createOtRule, fetchOtRules, updateOtRule } from "@/lib/api/admin-ot";

const rule: OtRule = {
  id: "rule-1",
  name: "Bloqueo por deuda",
  isEnabled: true,
  logic: "AND",
  conditions: [{ field: "deuda_pendiente", op: "eq", value: true }],
  action: { type: "bloquear" },
};

function renderSection() {
  return render(
    <ToastProvider>
      <RulesSection />
    </ToastProvider>,
  );
}

describe("RulesSection — HU #10223", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtRules).mockResolvedValue({ data: [rule] });
    vi.mocked(createOtRule).mockResolvedValue({
      ...rule,
      id: "rule-new",
      name: "Nueva regla test",
    });
    vi.mocked(updateOtRule).mockResolvedValue({ ...rule, isEnabled: false });
  });

  it("AC1 lista reglas con badge Activa", async () => {
    renderSection();
    expect(await screen.findByText("Bloqueo por deuda")).toBeInTheDocument();
    expect(screen.getByText("Activa")).toBeInTheDocument();
  });

  it("AC2 toggle llama PATCH con isEnabled", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Bloqueo por deuda");
    const toggle = screen.getByRole("switch");
    await user.click(toggle);
    await waitFor(() =>
      expect(updateOtRule).toHaveBeenCalledWith("rule-1", { isEnabled: false }),
    );
  });

  it("AC4 validación sin condiciones al guardar vacío", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Bloqueo por deuda");
    await user.click(screen.getByRole("button", { name: /Nueva regla/i }));
    const saveBtn = screen.getByRole("button", { name: /Guardar regla/i });
    expect(saveBtn).toBeDisabled();
  });

  it("AC5 estado vacío muestra CTA crear primera regla", async () => {
    vi.mocked(fetchOtRules).mockResolvedValue({ data: [] });
    renderSection();
    expect(await screen.findByText(/No hay reglas configuradas/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Crear primera regla/i })).toBeInTheDocument();
  });
});
