// HU #10236 — Hub consola OT: listado y navegación.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TransitOfficesList } from "../TransitOfficesList";
import type { TransitOffice } from "@/lib/api/types";

const mockPush = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush }),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
}));

import { fetchTransitOffices } from "@/lib/api/admin-companies";

const offices: TransitOffice[] = [
  {
    id: "aaaaaaaa-0001-4000-8000-000000000001",
    code: "11001",
    name: "Secretaría de Movilidad Bogotá",
    departmentCode: "11",
    cityCode: "11001",
  },
  {
    id: "aaaaaaaa-0001-4000-8000-000000000002",
    code: "05001",
    name: "Medellín — Secretaría de Movilidad",
    departmentCode: "05",
    cityCode: "05001",
  },
];

describe("TransitOfficesList — HU #10236", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
  });

  it("AC1 muestra el catálogo de OT", async () => {
    render(<TransitOfficesList />);
    expect(await screen.findByText("Secretaría de Movilidad Bogotá")).toBeInTheDocument();
    expect(screen.getByText("11001")).toBeInTheDocument();
  });

  it("AC1 filtra por nombre en tiempo real", async () => {
    const user = userEvent.setup();
    render(<TransitOfficesList />);
    await screen.findByText("Medellín — Secretaría de Movilidad");
    await user.type(screen.getByPlaceholderText(/Buscar por nombre/i), "bogota");
    await waitFor(() => {
      expect(screen.getByText("Secretaría de Movilidad Bogotá")).toBeInTheDocument();
      expect(screen.queryByText("Medellín — Secretaría de Movilidad")).not.toBeInTheDocument();
    });
  });

  it("AC2 navega al hub al administrar un OT", async () => {
    const user = userEvent.setup();
    render(<TransitOfficesList />);
    await screen.findByText("Secretaría de Movilidad Bogotá");
    // HU #10495: la acción "Administrar" pasó a icono con aria-label "Administrar {nombre}".
    const buttons = screen.getAllByRole("button", { name: /Administrar/ });
    await user.click(buttons[0]!);
    expect(mockPush).toHaveBeenCalledWith(
      "/admin/transit-offices/aaaaaaaa-0001-4000-8000-000000000001/tramites",
    );
  });
});
