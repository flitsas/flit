import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { TransitGrantsPicker } from "../TransitGrantsPicker";
import type { TransitOffice } from "@/lib/api/types";
import type { TransitOfficeOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficesOperationalStatus: vi.fn(),
}));

import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchTransitOfficesOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

const catalog: TransitOffice[] = [
  { id: "ot-active", code: "11001", name: "OT Activo", departmentCode: "11", cityCode: "11001" },
  { id: "ot-inactive", code: "05001", name: "OT Inactivo", departmentCode: "05", cityCode: "05001" },
  { id: "ot-catalog", code: "76001", name: "OT Solo catálogo", departmentCode: "76", cityCode: "76001" },
];

function status(
  id: string,
  opts: { hasTenant: boolean; estadoActivo: boolean | null },
): TransitOfficeOperationalStatus {
  const office = catalog.find((o) => o.id === id)!;
  return {
    id: office.id,
    code: office.code,
    name: office.name,
    departmentCode: office.departmentCode,
    hasTenant: opts.hasTenant,
    tenantId: opts.hasTenant ? `t-${id}` : null,
    estadoActivo: opts.estadoActivo,
    operationMode: null,
    divipoCode: null,
    quipuxRegistration: false,
    quipuxTransfer: false,
    quipuxOther: false,
  };
}

describe("TransitGrantsPicker — OT activos en plataforma", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTransitOffices).mockResolvedValue(catalog);
  });

  it("solo lista OT con tenant activo y oculta inactivos o sin alta", async () => {
    vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue([
      status("ot-active", { hasTenant: true, estadoActivo: true }),
      status("ot-inactive", { hasTenant: true, estadoActivo: false }),
      status("ot-catalog", { hasTenant: false, estadoActivo: null }),
    ]);

    render(<TransitGrantsPicker selectedIds={[]} onChange={vi.fn()} />);

    expect(await screen.findByLabelText(/ot activo \(11001\)/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/ot inactivo/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/ot solo catálogo/i)).not.toBeInTheDocument();
  });

  it("muestra vacío cuando no hay OT activos en la plataforma", async () => {
    vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue([
      status("ot-inactive", { hasTenant: true, estadoActivo: false }),
    ]);

    render(<TransitGrantsPicker selectedIds={[]} onChange={vi.fn()} />);

    expect(
      await screen.findByText(/no hay organismos de tránsito activos en la plataforma/i),
    ).toBeInTheDocument();
  });
});
