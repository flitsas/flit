// HU #12351 AC4 — asistente no continúa con lista efectiva de OT vacía.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { WizardState } from "@/lib/api/types/procedure-runtime";

const mocks = vi.hoisted(() => ({
  getWizardPreview: vi.fn(),
  createInstanceFromConsulta: vi.fn(),
  runPreflightPreview: vi.fn(),
  getConsultationConfig: vi.fn(),
  listTransitOffices: vi.fn(),
  listVehicleServiceTypes: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: "tenant-dev",
  DEV_USER_ID: "user-dev",
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
  isTransitOfficeUnavailable: () => false,
  isVehicleBodyTypeMissing: () => false,
  isVehiclePrendaMissing: () => false,
}));

vi.mock("@/lib/api/admin-plate-ranges", () => ({
  getPlatePreassignStatus: vi.fn().mockResolvedValue({ enabled: false }),
}));

vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show: vi.fn() }),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from "@/components/operacion/TramiteWizard";

const VIN = "9BWZZZ377VT004251";

function wizard(): WizardState {
  return {
    modalidad: "matricula_inicial",
    tipologiaCodigo: "matricula_inicial",
    totalSteps: 3,
    canSubmit: false,
    blockers: [],
    status: "borrador",
    allowedTransitions: [],
    steps: [
      { index: 1, key: "consulta_vin", label: "Consulta VIN", status: "incomplete", reasons: [] },
      { index: 2, key: "documentos", label: "Documentos", status: "locked", reasons: [] },
      { index: 3, key: "fur", label: "FUR", status: "locked", reasons: [] },
    ],
  };
}

describe("HU #12351 — wizard OT lista vacía", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getWizardPreview.mockResolvedValue(wizard());
    mocks.listTransitOffices.mockResolvedValue([]);
    mocks.listVehicleServiceTypes.mockResolvedValue([]);
    mocks.getConsultationConfig.mockResolvedValue({
      vehiclePlate: "kyverum_runt",
      onlyOwnVehicles: false,
      onlyOwnVehiclesByFamily: { matriculas: false, traspaso: false, otros: false },
      blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
    });
    mocks.runPreflightPreview.mockResolvedValue({
      previewToken: "tok",
      preflight: { overall: "green", checks: [] },
      vehicleFields: [
        {
          formFieldId: "",
          fieldKey: "vin",
          valueText: VIN,
          valueJson: null,
          source: "consultation",
        },
      ],
    });
  });

  it("muestra mensaje explicativo y bloquea Continuar", async () => {
    const user = userEvent.setup();
    render(
      <TramiteWizard
        procedureTypeCode="MATRICULA_NUEVA"
        family="MATRICULAS"
        title="Matrícula inicial"
        onCreated={() => {}}
        onExit={() => {}}
      />,
    );

    await user.type(await screen.findByLabelText("Número VIN"), VIN);
    await user.click(screen.getByRole("button", { name: /consultar runt/i }));

    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await waitFor(() => {
      expect(
        screen.getAllByText(/no tiene organismos de tránsito habilitados/i).length,
      ).toBeGreaterThanOrEqual(1);
    });

    const continuar = screen.getByRole("button", { name: /continuar/i });
    expect(continuar).toBeDisabled();
  }, 15_000);
});
