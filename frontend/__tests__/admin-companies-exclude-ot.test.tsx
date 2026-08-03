/** HU #11222 — Admin Compañías lista solo B2B (excludeTransitOffices=true). */
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";

const fetchCompaniesIndex = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: (...args: unknown[]) => fetchCompaniesIndex(...args),
  createCompany: vi.fn(),
  updateCompany: vi.fn(),
}));

import AdminCompaniesPage from "@/app/admin/companies/page";

describe("AdminCompaniesPage HU #11222", () => {
  beforeEach(() => {
    fetchCompaniesIndex.mockReset();
    fetchCompaniesIndex.mockResolvedValue({
      data: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
    });
  });

  it("solicita el listado con excludeTransitOffices=true en la carga inicial", async () => {
    render(<AdminCompaniesPage />);

    await waitFor(() => expect(fetchCompaniesIndex).toHaveBeenCalled());

    expect(fetchCompaniesIndex).toHaveBeenCalledWith(
      expect.objectContaining({ excludeTransitOffices: true, page: 1, pageSize: 20 }),
      expect.any(AbortSignal),
    );
  });

  it("muestra mensaje de vacío orientado a compañías B2B", async () => {
    render(<AdminCompaniesPage />);

    expect(
      await screen.findByText(/no se encontraron compañías b2b con los filtros aplicados/i),
    ).toBeInTheDocument();
  });
});
