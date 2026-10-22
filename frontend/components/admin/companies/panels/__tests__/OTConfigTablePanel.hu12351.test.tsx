// HU #12351 — OT heredados en solo lectura para hijo de Concesión y advertencia SuperAdmin.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OTConfigTablePanel } from "../OTConfigTablePanel";
import type { TransitOffice } from "@/lib/api/types";

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
  fetchTransitGrants: vi.fn(),
  fetchTransitAgreements: vi.fn(),
  fetchOtBlockingPolicies: vi.fn(),
  fetchOtConsultationRestrictions: vi.fn(),
  fetchOtPrendaDocumentPolicies: vi.fn(),
  fetchTransitBlocks: vi.fn(),
  addTransitGrant: vi.fn(),
  addTransitBlock: vi.fn(),
  removeTransitBlock: vi.fn(),
  removeTransitGrant: vi.fn(),
  setTransitAgreement: vi.fn(),
  setOtBlockingPolicy: vi.fn(),
  setOtConsultationRestriction: vi.fn(),
  setOtPrendaDocumentPolicy: vi.fn(),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficesOperationalStatus: vi.fn(),
}));

vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show: vi.fn() }),
}));

import {
  addTransitBlock,
  addTransitGrant,
  removeTransitBlock,
  removeTransitGrant,
  fetchOtBlockingPolicies,
  fetchOtConsultationRestrictions,
  fetchOtPrendaDocumentPolicies,
  fetchTransitAgreements,
  fetchTransitBlocks,
  fetchTransitGrants,
  fetchTransitOffices,
} from "@/lib/api/admin-companies";
import { fetchTransitOfficesOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

const TENANT = "aaaaaaaa-0000-4000-8000-000000000001";

const offices: TransitOffice[] = [
  { id: "o1", code: "11001", name: "Secretaría Bogotá", departmentCode: "11", cityCode: "11001" },
  { id: "o2", code: "05001", name: "Medellín", departmentCode: "05", cityCode: "05001" },
];

function arrange(grantedIds: string[]) {
  vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
  vi.mocked(fetchTransitGrants).mockResolvedValue({ transitOfficeIds: grantedIds });
  vi.mocked(fetchTransitAgreements).mockResolvedValue({ transitOfficeIds: [] });
  vi.mocked(fetchOtBlockingPolicies).mockResolvedValue([]);
  vi.mocked(fetchOtConsultationRestrictions).mockResolvedValue([]);
  vi.mocked(fetchOtPrendaDocumentPolicies).mockResolvedValue([]);
  vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue([
    {
      id: "o1",
      code: "11001",
      name: "Secretaría Bogotá",
      departmentCode: "11",
      hasTenant: true,
      tenantId: "t-o1",
      estadoActivo: true,
      operationMode: null,
      divipoCode: null,
      quipuxRegistration: false,
      quipuxTransfer: false,
      quipuxOther: false,
    },
    {
      id: "o2",
      code: "05001",
      name: "Medellín",
      departmentCode: "05",
      hasTenant: true,
      tenantId: "t-o2",
      estadoActivo: true,
      operationMode: null,
      divipoCode: null,
      quipuxRegistration: false,
      quipuxTransfer: false,
      quipuxOther: false,
    },
  ]);
}

describe("OTConfigTablePanel HU #12351", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC1/AC2 — hijo de Concesión ve leyenda heredada sin switches ni acciones de escritura", async () => {
    arrange(["o1"]);
    render(<OTConfigTablePanel tenantId={TENANT} mode="readonly-concession-inherited" />);

    expect(await screen.findByText(/gobierna su Concesión/i)).toBeInTheDocument();
    const table = screen.getByRole("table", { name: /organismos de tránsito/i });
    expect(within(table).getByText("Secretaría Bogotá")).toBeInTheDocument();
    expect(within(table).queryByText("Medellín")).not.toBeInTheDocument();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /acciones para/i })).not.toBeInTheDocument();
    expect(addTransitGrant).not.toHaveBeenCalled();
  });

  it("SuperAdmin en cabeza Marca Blanca: misma tabla; el bloqueado sale no habilitado y habilitarlo lo desbloquea", async () => {
    const user = userEvent.setup();
    arrange([]);
    vi.mocked(fetchTransitBlocks).mockResolvedValue({ transitOfficeIds: ["o2"] });
    vi.mocked(removeTransitBlock).mockResolvedValue(undefined);
    render(<OTConfigTablePanel tenantId={TENANT} mode="superadmin-marca-blanca" />);

    const table = await screen.findByRole("table", { name: /organismos de tránsito/i });
    expect(within(table).getByText("Secretaría Bogotá")).toBeInTheDocument();
    expect(within(table).getByText("Medellín")).toBeInTheDocument();
    // Mismo interruptor que una compañía normal: Bogotá habilitado, Medellín (bloqueado) no.
    expect(screen.getByRole("switch", { name: /deshabilitar secretaría bogotá/i })).toBeInTheDocument();
    expect(screen.getByRole("switch", { name: /^habilitar medellín/i })).toBeInTheDocument();
    expect(fetchTransitGrants).not.toHaveBeenCalled();

    await user.click(screen.getByRole("switch", { name: /habilitar medellín/i }));

    await waitFor(() => expect(removeTransitBlock).toHaveBeenCalledWith(TENANT, "o2"));
    expect(addTransitGrant).not.toHaveBeenCalled();
  });

  it("SuperAdmin en cabeza Marca Blanca: deshabilitar bloquea el OT para la red", async () => {
    const user = userEvent.setup();
    arrange([]);
    vi.mocked(fetchTransitBlocks).mockResolvedValue({ transitOfficeIds: [] });
    vi.mocked(addTransitBlock).mockResolvedValue(undefined);
    render(<OTConfigTablePanel tenantId={TENANT} mode="superadmin-marca-blanca" />);

    await screen.findByText("Medellín");
    await user.click(screen.getByRole("switch", { name: /deshabilitar medellín/i }));

    await waitFor(() => expect(addTransitBlock).toHaveBeenCalledWith(TENANT, "o2"));
    expect(removeTransitGrant).not.toHaveBeenCalled();
  });

  it("AC5 — SuperAdmin en Concesión pide confirmación de alcance antes de habilitar", async () => {
    const user = userEvent.setup();
    arrange([]);
    vi.mocked(addTransitGrant).mockResolvedValue(undefined);
    render(
      <OTConfigTablePanel
        tenantId={TENANT}
        mode="superadmin-concession"
        grantScopeWarningCount={2}
      />,
    );

    await screen.findByText("Medellín");
    const toggle = screen.getByRole("switch", { name: /habilitar medellín/i });
    await user.click(toggle);

    expect(await screen.findByRole("dialog", { name: /confirmar cambio/i })).toBeInTheDocument();
    expect(screen.getByText(/2 clientes hijos vigentes/i)).toBeInTheDocument();
    expect(addTransitGrant).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /confirmar cambio/i }));
    await waitFor(() => expect(addTransitGrant).toHaveBeenCalledWith(TENANT, "o2"));
  });

  it("Marca Blanca — lista operables menos bloqueados sin pedir los grants (no se usan)", async () => {
    arrange(["o2"]);
    vi.mocked(fetchTransitBlocks).mockResolvedValue({ transitOfficeIds: ["o2"] });
    render(<OTConfigTablePanel tenantId={TENANT} mode="readonly-marca-blanca" />);

    const table = await screen.findByRole("table", { name: /organismos de tránsito/i });
    expect(within(table).getByText("Secretaría Bogotá")).toBeInTheDocument();
    expect(within(table).queryByText("Medellín")).not.toBeInTheDocument();
    expect(fetchTransitGrants).not.toHaveBeenCalled();
  });
});
