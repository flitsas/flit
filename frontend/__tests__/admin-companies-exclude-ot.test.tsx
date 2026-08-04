/** HU #11222 / #11227 — Admin Compañías: exclude OT + toggle SuperAdmin. */
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const fetchCompaniesIndex = vi.fn();
const mockReplace = vi.fn();

const permissionsState = vi.hoisted(() => ({
  isSuperAdmin: true,
  isAdminCompany: false,
  isOtAdmin: false,
  tenantId: "11111111-1111-1111-1111-111111111111" as string | null,
  permissions: [] as string[],
  userId: "u1" as string | null,
  roleId: null as string | null,
  roleCode: "SuperAdmin" as string | null,
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: mockReplace, prefetch: vi.fn() }),
}));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => permissionsState,
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: (...args: unknown[]) => fetchCompaniesIndex(...args),
  createCompany: vi.fn(),
  updateCompany: vi.fn(),
}));

import AdminCompaniesPage from "@/app/admin/companies/page";

describe("AdminCompaniesPage HU #11222 / #11227", () => {
  beforeEach(() => {
    fetchCompaniesIndex.mockReset();
    mockReplace.mockReset();
    permissionsState.isSuperAdmin = true;
    permissionsState.isAdminCompany = false;
    permissionsState.tenantId = "11111111-1111-1111-1111-111111111111";
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

  it("HU #11227: SuperAdmin ve el toggle y al activarlo pide excludeTransitOffices=false", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);

    await waitFor(() => expect(fetchCompaniesIndex).toHaveBeenCalled());
    fetchCompaniesIndex.mockClear();

    const toggle = screen.getByLabelText(/Incluir organismos de tránsito/i);
    await user.click(toggle);

    await waitFor(() =>
      expect(fetchCompaniesIndex).toHaveBeenCalledWith(
        expect.objectContaining({ excludeTransitOffices: false }),
        expect.any(AbortSignal),
      ),
    );
  });

  it("HU #11228: AdminCompany redirige a /admin/companies/{tenantId}", async () => {
    permissionsState.isSuperAdmin = false;
    permissionsState.isAdminCompany = true;
    permissionsState.tenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    render(<AdminCompaniesPage />);

    await waitFor(() =>
      expect(mockReplace).toHaveBeenCalledWith(
        "/admin/companies/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      ),
    );
    expect(fetchCompaniesIndex).not.toHaveBeenCalled();
  });
});
