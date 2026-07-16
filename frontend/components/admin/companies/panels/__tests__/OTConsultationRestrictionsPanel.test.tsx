// HU #10761 — panel de restricciones de consulta por OT. API y toast mockeados.
//
// Uso de ejemplo:
//   render(<OTConsultationRestrictionsPanel tenantId={TENANT} />);
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OTConsultationRestrictionsPanel } from "../OTConsultationRestrictionsPanel";
import type { OtConsultationRestriction, TransitOffice } from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
  fetchTransitGrants: vi.fn(),
  fetchOtConsultationRestrictions: vi.fn(),
  setOtConsultationRestriction: vi.fn(),
}));

const show = vi.fn();
vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show }),
}));

import {
  fetchOtConsultationRestrictions,
  fetchTransitGrants,
  fetchTransitOffices,
  setOtConsultationRestriction,
} from "@/lib/api/admin-companies";

const TENANT = "aaaaaaaa-0000-4000-8000-000000000001";

const offices: TransitOffice[] = [
  { id: "o1", code: "11001", name: "Secretaría de Movilidad Bogotá", departmentCode: "11", cityCode: "11001" },
  { id: "o2", code: "05001", name: "Medellín — Secretaría de Movilidad", departmentCode: "05", cityCode: "05001" },
];

/** Arranca el panel con catálogo completo, los grants dados y las filas dispersas dadas. */
function arrange(grantedIds: string[], restrictions: OtConsultationRestriction[] = []) {
  vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
  vi.mocked(fetchTransitGrants).mockResolvedValue({ transitOfficeIds: grantedIds });
  vi.mocked(fetchOtConsultationRestrictions).mockResolvedValue(restrictions);
}

/** Switch de un tipo de consulta dentro de la tarjeta de un OT concreto. */
function toggleOf(officeId: string, label: RegExp) {
  return within(screen.getByTestId(`ot-restrictions-${officeId}`)).getByRole("switch", {
    name: label,
  });
}

describe("OTConsultationRestrictionsPanel (HU #10761)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("muestra el skeleton de carga y luego la matriz (loading → ready)", async () => {
    arrange(["o1"]);
    render(<OTConsultationRestrictionsPanel tenantId={TENANT} />);

    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();

    expect(await screen.findByText(/Secretaría de Movilidad Bogotá/i)).toBeInTheDocument();
    expect(screen.queryByTestId("ui-loading")).not.toBeInTheDocument();
    expect(fetchOtConsultationRestrictions).toHaveBeenCalledWith(TENANT, expect.anything());
  });

  it("muestra el estado de error si falla la carga", async () => {
    vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
    vi.mocked(fetchTransitGrants).mockResolvedValue({ transitOfficeIds: ["o1"] });
    vi.mocked(fetchOtConsultationRestrictions).mockRejectedValue(new Error("boom"));
    render(<OTConsultationRestrictionsPanel tenantId={TENANT} />);

    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
    expect(screen.getByText(/no se pudieron cargar las restricciones/i)).toBeInTheDocument();
  });

  // AC3 — compañía sin organismos habilitados.
  it("muestra el estado vacío que invita a habilitar organismos si no hay grants", async () => {
    arrange([]);
    render(<OTConsultationRestrictionsPanel tenantId={TENANT} />);

    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByText(/habilita al menos uno en la matriz de organismos/i)).toBeInTheDocument();
  });

  // AC2 — solo los organismos habilitados son restringibles.
  it("lista únicamente los OT habilitados para la compañía", async () => {
    arrange(["o1"]);
    render(<OTConsultationRestrictionsPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    expect(within(screen.getByTestId("ot-restrictions-list")).getAllByRole("listitem")).toHaveLength(1);
    expect(screen.queryByText(/Medellín/i)).not.toBeInTheDocument();
  });

  // Default por tipo: RNMC es opt-in (off), comparendos es opt-out (on).
  it("aplica el default por tipo cuando no hay fila (RNMC off, comparendos on)", async () => {
    arrange(["o1"], []);
    render(<OTConsultationRestrictionsPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    // Sin fila: RNMC no corre hasta activarlo (opt-in); comparendos se consulta por defecto.
    expect(toggleOf("o1", /RNMC/i)).not.toBeChecked();
    expect(toggleOf("o1", /Comparendos/i)).toBeChecked();
  });

  // AC1 — activar RNMC (opt-in) se guarda con enabled=true.
  it("persiste al activar RNMC (opt-in) con enabled=true", async () => {
    const user = userEvent.setup();
    arrange(["o1"]);
    vi.mocked(setOtConsultationRestriction).mockResolvedValue(undefined);
    render(<OTConsultationRestrictionsPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    await user.click(toggleOf("o1", /RNMC/i));

    await waitFor(() =>
      expect(setOtConsultationRestriction).toHaveBeenCalledWith(TENANT, "o1", "rnmc", true),
    );
    expect(toggleOf("o1", /RNMC/i)).toBeChecked();
  });

  // AC4 — error al guardar: el control vuelve a su estado anterior + aviso.
  it("revierte el switch y avisa si falla la persistencia", async () => {
    const user = userEvent.setup();
    arrange(["o1"]);
    vi.mocked(setOtConsultationRestriction).mockRejectedValue(new Error("boom"));
    render(<OTConsultationRestrictionsPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    await user.click(toggleOf("o1", /Comparendos/i));

    // Rollback: vuelve a habilitada (su estado anterior).
    await waitFor(() => expect(toggleOf("o1", /Comparendos/i)).toBeChecked());
    expect(show).toHaveBeenCalledWith(expect.stringMatching(/no se pudo inhabilitar/i), "error");
  });

  it("muestra el mensaje del backend (422) cuando el OT no está habilitado", async () => {
    const user = userEvent.setup();
    const serverMessage =
      "El organismo no está habilitado para la compañía. Habilítelo primero en la matriz de organismos.";
    arrange(["o1"]);
    vi.mocked(setOtConsultationRestriction).mockRejectedValue(
      new ApiValidationError([{ field: "transitOfficeId", message: serverMessage }], 422),
    );
    render(<OTConsultationRestrictionsPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    await user.click(toggleOf("o1", /RNMC/i));

    await waitFor(() => expect(show).toHaveBeenCalledWith(serverMessage, "error"));
  });
});
