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

function renderSection(transitOfficeId?: string) {
  return render(
    <ToastProvider>
      <RulesSection transitOfficeId={transitOfficeId} />
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
      expect(updateOtRule).toHaveBeenCalledWith("rule-1", { isEnabled: false }, undefined),
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

  // HU12856 (adicional, Feature #12847) — Reglas resuelve el organismo por ?transitOfficeId
  // cuando el caller es Super Admin (mismo patrón que RequirementsSection, HU #12854 backend).
  it("HU12856 — consulta y muta reglas con el scope del SuperAdmin cuando viene transitOfficeId", async () => {
    const user = userEvent.setup();
    const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";
    renderSection(OT_ID);
    await screen.findByText("Bloqueo por deuda");

    expect(fetchOtRules).toHaveBeenCalledWith(expect.anything(), { transitOfficeId: OT_ID });

    await user.click(screen.getByRole("switch"));
    await waitFor(() =>
      expect(updateOtRule).toHaveBeenCalledWith(
        "rule-1",
        { isEnabled: false },
        { transitOfficeId: OT_ID },
      ),
    );
  });

  it("HU12856 — sin transitOfficeId (ot_admin) no manda scope, como hoy", async () => {
    renderSection();
    await screen.findByText("Bloqueo por deuda");
    expect(fetchOtRules).toHaveBeenCalledWith(expect.anything(), undefined);
  });

  it("HU12856 — crear una regla nueva pasa el scope del SuperAdmin a createOtRule", async () => {
    const user = userEvent.setup();
    const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";
    renderSection(OT_ID);
    await screen.findByText("Bloqueo por deuda");

    await user.click(screen.getByRole("button", { name: /^Nueva regla$/i }));
    await user.type(screen.getByLabelText(/^Nombre$/i), "Regla de scope");
    await user.type(screen.getByLabelText(/Valor condición 1/i), "algo");
    await user.click(screen.getByRole("button", { name: /Guardar regla/i }));

    await waitFor(() =>
      expect(createOtRule).toHaveBeenCalledWith(
        expect.objectContaining({ name: "Regla de scope" }),
        { transitOfficeId: OT_ID },
      ),
    );
  });

  it("HU #12731 — sin columnas Lógica/Acción; editar abre panel precargado", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Bloqueo por deuda");
    expect(screen.queryByRole("columnheader", { name: /Lógica/i })).not.toBeInTheDocument();
    expect(screen.queryByText("bloquear")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Editar regla Bloqueo por deuda/i }));
    expect(await screen.findByRole("dialog", { name: /Editar regla Bloqueo por deuda/i })).toBeInTheDocument();
    expect(screen.getByDisplayValue("Bloqueo por deuda")).toBeInTheDocument();
  });
});
