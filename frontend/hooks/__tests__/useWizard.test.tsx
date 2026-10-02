// HU #10549 — el wizard ocultaba el paso de identidad cuando el OT la deshabilitaba.
// Bug #13194 (P4) — decisión: la firma se exige SIEMPRE, también en OT sin VID. El paso 4 se muestra
// aunque `identityValidationEnabled` llegue en false (flag deprecado; el backend lo manda en true).
// Uso de ejemplo: useWizard('inst-1').steps → incluye siempre { key: 'identidad' }
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useWizard } from "../useWizard";
import type { WizardState } from "@/lib/api/types/procedure-runtime";

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    getWizardState: vi.fn(),
  },
}));

import { tramitesClient } from "@/lib/api/tramites-client";

function wizard(identityValidationEnabled?: boolean): WizardState {
  return {
    modalidad: "matricula_inicial",
    tipologiaCodigo: "matricula_inicial",
    totalSteps: 5,
    steps: [
      { index: 1, key: "consulta_vin", label: "Consulta", status: "complete", reasons: [] },
      { index: 2, key: "documentos", label: "Documentos", status: "complete", reasons: [] },
      { index: 3, key: "comprador", label: "Comprador", status: "complete", reasons: [] },
      { index: 4, key: "identidad", label: "Identidad", status: "complete", reasons: [] },
      { index: 5, key: "fur", label: "FUR", status: "incomplete", reasons: [] },
    ],
    canSubmit: true,
    blockers: [],
    status: "borrador",
    allowedTransitions: [],
    identityValidationEnabled,
  };
}

describe("useWizard — HU #10549", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("Bug #13194 — conserva el paso de identidad aunque identityValidationEnabled = false", async () => {
    vi.mocked(tramitesClient.getWizardState).mockResolvedValue(wizard(false));
    const { result } = renderHook(() => useWizard("inst-1"));

    await waitFor(() => expect(result.current.steps.length).toBe(5));
    expect(result.current.steps.map((s) => s.key)).toEqual([
      "consulta_vin",
      "documentos",
      "comprador",
      "identidad",
      "fur",
    ]);
  });

  it("conserva el paso de identidad cuando la validación está habilitada", async () => {
    vi.mocked(tramitesClient.getWizardState).mockResolvedValue(wizard(true));
    const { result } = renderHook(() => useWizard("inst-1"));

    await waitFor(() => expect(result.current.steps.length).toBe(5));
    expect(result.current.steps.some((s) => s.key === "identidad")).toBe(true);
  });

  it("conserva el paso de identidad por defecto (flag ausente)", async () => {
    vi.mocked(tramitesClient.getWizardState).mockResolvedValue(wizard(undefined));
    const { result } = renderHook(() => useWizard("inst-1"));

    await waitFor(() => expect(result.current.steps.length).toBe(5));
    expect(result.current.steps.some((s) => s.key === "identidad")).toBe(true);
  });
});
