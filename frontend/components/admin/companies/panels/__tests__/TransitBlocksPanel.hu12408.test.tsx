// HU #12408 — consola SuperAdmin de bloqueos OT en Marca Blanca.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TransitBlocksPanel } from "../TransitBlocksPanel";
import type { TransitOffice } from "@/lib/api/types";

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
  fetchTransitBlocks: vi.fn(),
  addTransitBlock: vi.fn(),
  removeTransitBlock: vi.fn(),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficesOperationalStatus: vi.fn(),
}));

vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show: vi.fn() }),
}));

import { addTransitBlock, fetchTransitBlocks, fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchTransitOfficesOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

const TENANT = "mb-head";

const offices: TransitOffice[] = [
  { id: "o1", code: "11001", name: "Secretaría Bogotá", departmentCode: "11", cityCode: "11001" },
];

describe("TransitBlocksPanel HU #12408", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
    vi.mocked(fetchTransitBlocks).mockResolvedValue({ transitOfficeIds: [] });
    vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue([
      {
        id: "o1",
        code: "11001",
        name: "Secretaría Bogotá",
        departmentCode: "11",
        hasTenant: true,
        tenantId: "t1",
        estadoActivo: true,
        operationMode: null,
        divipoCode: null,
        quipuxRegistration: false,
        quipuxTransfer: false,
        quipuxOther: false,
      },
    ]);
  });

  it("AC1/AC2 — SuperAdmin bloquea OT con confirmación de alcance", async () => {
    const user = userEvent.setup();
    vi.mocked(addTransitBlock).mockResolvedValue(undefined);
    render(<TransitBlocksPanel tenantId={TENANT} activeChildrenCount={3} />);

    await screen.findByText("Secretaría Bogotá");
    await user.click(screen.getByRole("button", { name: /bloquear secretaría bogotá/i }));

    expect(await screen.findByText(/3 clientes hijos vigentes/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /confirmar cambio/i }));

    await waitFor(() => expect(addTransitBlock).toHaveBeenCalledWith(TENANT, "o1"));
  });
});
