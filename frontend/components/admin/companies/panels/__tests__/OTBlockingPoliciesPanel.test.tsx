// FEATURE 05 — panel de criterios de bloqueo por OT. API y toast mockeados.
//
// Uso de ejemplo:
//   render(<OTBlockingPoliciesPanel tenantId={TENANT} />);
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OTBlockingPoliciesPanel } from "../OTBlockingPoliciesPanel";
import type { OtBlockingPolicy, TransitOffice } from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
  fetchTransitGrants: vi.fn(),
  fetchOtBlockingPolicies: vi.fn(),
  setOtBlockingPolicy: vi.fn(),
}));

const show = vi.fn();
vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show }),
}));

import {
  fetchOtBlockingPolicies,
  fetchTransitGrants,
  fetchTransitOffices,
  setOtBlockingPolicy,
} from "@/lib/api/admin-companies";

const TENANT = "aaaaaaaa-0000-4000-8000-000000000001";

const offices: TransitOffice[] = [
  { id: "o1", code: "11001", name: "Secretaría de Movilidad Bogotá", departmentCode: "11", cityCode: "11001" },
  { id: "o2", code: "05001", name: "Medellín — Secretaría de Movilidad", departmentCode: "05", cityCode: "05001" },
];

/** Arranca el panel con catálogo completo, los grants dados y las filas dispersas dadas. */
function arrange(grantedIds: string[], policies: OtBlockingPolicy[] = []) {
  vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
  vi.mocked(fetchTransitGrants).mockResolvedValue({ transitOfficeIds: grantedIds });
  vi.mocked(fetchOtBlockingPolicies).mockResolvedValue(policies);
}

/** Switch de un criterio dentro de la tarjeta de un OT concreto. */
function toggleOf(officeId: string, label: RegExp) {
  return within(screen.getByTestId(`ot-blocking-${officeId}`)).getByRole("switch", {
    name: label,
  });
}

describe("OTBlockingPoliciesPanel (FEATURE 05)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("muestra el skeleton de carga y luego la matriz (loading → ready)", async () => {
    arrange(["o1"]);
    render(<OTBlockingPoliciesPanel tenantId={TENANT} />);

    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();

    expect(await screen.findByText(/Secretaría de Movilidad Bogotá/i)).toBeInTheDocument();
    expect(screen.queryByTestId("ui-loading")).not.toBeInTheDocument();
    expect(fetchOtBlockingPolicies).toHaveBeenCalledWith(TENANT, expect.anything());
  });

  it("muestra el estado de error si falla la carga", async () => {
    vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
    vi.mocked(fetchTransitGrants).mockResolvedValue({ transitOfficeIds: ["o1"] });
    vi.mocked(fetchOtBlockingPolicies).mockRejectedValue(new Error("boom"));
    render(<OTBlockingPoliciesPanel tenantId={TENANT} />);

    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
    expect(screen.getByText(/no se pudieron cargar los criterios de bloqueo/i)).toBeInTheDocument();
  });

  it("muestra el estado vacío que invita a habilitar organismos si no hay grants", async () => {
    arrange([]);
    render(<OTBlockingPoliciesPanel tenantId={TENANT} />);

    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByText(/habilita al menos uno en la matriz de organismos/i)).toBeInTheDocument();
  });

  it("lista únicamente los OT habilitados para la compañía", async () => {
    arrange(["o1"]);
    render(<OTBlockingPoliciesPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    expect(within(screen.getByTestId("ot-blocking-list")).getAllByRole("listitem")).toHaveLength(1);
    expect(screen.queryByText(/Medellín/i)).not.toBeInTheDocument();
  });

  // Semántica dispersa + defaults por criterio: sin fila ⇒ default del criterio.
  it("aplica el default por criterio cuando no hay fila configurada", async () => {
    arrange(["o1"], [{ transitOfficeId: "o1", criterion: "fines", blocks: true }]);
    render(<OTBlockingPoliciesPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    // SOAT sin fila → default bloquea (ON); comparendos con fila explícita blocks=true → ON.
    expect(toggleOf("o1", /SOAT/i)).toBeChecked();
    expect(toggleOf("o1", /Comparendos/i)).toBeChecked();
    // RNMC sin fila → default NO bloquea (OFF).
    expect(toggleOf("o1", /RNMC/i)).not.toBeChecked();
  });

  it("persiste el cambio al desactivar el bloqueo de un criterio", async () => {
    const user = userEvent.setup();
    arrange(["o1"]);
    vi.mocked(setOtBlockingPolicy).mockResolvedValue(undefined);
    render(<OTBlockingPoliciesPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    // SOAT arranca ON (default bloquea); al desactivarlo se persiste blocks=false.
    await user.click(toggleOf("o1", /SOAT/i));

    await waitFor(() =>
      expect(setOtBlockingPolicy).toHaveBeenCalledWith(TENANT, "o1", "soat", false),
    );
    expect(toggleOf("o1", /SOAT/i)).not.toBeChecked();
  });

  it("revierte el switch y avisa si falla la persistencia", async () => {
    const user = userEvent.setup();
    arrange(["o1"]);
    vi.mocked(setOtBlockingPolicy).mockRejectedValue(new Error("boom"));
    render(<OTBlockingPoliciesPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    // SOAT arranca ON; al intentar apagarlo y fallar, vuelve a ON.
    await user.click(toggleOf("o1", /SOAT/i));

    await waitFor(() => expect(toggleOf("o1", /SOAT/i)).toBeChecked());
    expect(show).toHaveBeenCalledWith(expect.stringMatching(/no se pudo cambiar el bloqueo/i), "error");
  });

  it("muestra el mensaje del backend (422) cuando el OT no está habilitado", async () => {
    const user = userEvent.setup();
    const serverMessage =
      "Este organismo no está habilitado para la compañía. Habilítelo primero en los Organismos de Tránsito.";
    arrange(["o1"]);
    vi.mocked(setOtBlockingPolicy).mockRejectedValue(
      new ApiValidationError([{ field: "transitOfficeId", message: serverMessage }], 422),
    );
    render(<OTBlockingPoliciesPanel tenantId={TENANT} />);

    await screen.findByText(/Secretaría de Movilidad Bogotá/i);
    await user.click(toggleOf("o1", /SOAT/i));

    await waitFor(() => expect(show).toHaveBeenCalledWith(serverMessage, "error"));
  });
});
