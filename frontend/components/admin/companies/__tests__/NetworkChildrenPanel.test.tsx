import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { NetworkChildrenPanel } from "../NetworkChildrenPanel";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompanyChildren: vi.fn(),
  createChildCompany: vi.fn(),
}));

import { fetchCompanyChildren } from "@/lib/api/admin-companies";

describe("NetworkChildrenPanel (HU #12356)", () => {
  beforeEach(() => {
    vi.mocked(fetchCompanyChildren).mockReset();
  });

  it("muestra estado vacío con CTA de alta", async () => {
    vi.mocked(fetchCompanyChildren).mockResolvedValue([]);

    render(<NetworkChildrenPanel headTenantId="head-1" headTenantType="CONCESION" />);

    expect(await screen.findByText(/aún no tienes clientes en tu red/i)).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /agregar cliente a la red/i })[0]).toBeInTheDocument();
  });

  it("muestra hijos en estado lleno", async () => {
    vi.mocked(fetchCompanyChildren).mockResolvedValue([
      {
        id: "child-1",
        nit: "900111222-3",
        razonSocial: "Hijo Demo S.A.S.",
        code: "HIJODEMO",
        tenantType: "CONCESIONARIO",
        estadoActivo: true,
        fechaVinculacion: "2026-09-01T00:00:00Z",
        rowVersion: 1,
      },
    ]);

    render(<NetworkChildrenPanel headTenantId="head-1" headTenantType="CONCESION" />);

    await waitFor(() => expect(screen.getByText("Hijo Demo S.A.S.")).toBeInTheDocument());
    expect(screen.queryByText(/vincular cliente existente/i)).not.toBeInTheDocument();
  });
});
