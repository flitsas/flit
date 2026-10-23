// HU #12732 AC2 — superficie plana en consolas admin (sin card contenedora de layout).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

const mockPush = vi.fn();
const mockReplace = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush, replace: mockReplace }),
}));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: true,
    isAdminCompany: false,
    isGroupParent: false,
    tenantId: null,
    permissions: [],
    userId: "u1",
  }),
}));

vi.mock("@/lib/api/client", () => ({ getToken: vi.fn().mockReturnValue("token") }));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  createTransitOfficeTenant: vi.fn(),
  fetchTransitOfficesOperationalStatus: vi.fn().mockResolvedValue([]),
  hasQuipuxFlagsWithoutDivipo: () => false,
  isQuipuxElegible: () => false,
}));

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: vi.fn(),
}));

vi.mock("@/lib/auth/jwt", () => ({
  decodeJwtPayload: vi.fn().mockReturnValue({ role: "SuperAdmin" }),
  isOtUser: vi.fn().mockReturnValue(false),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({
    data: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
  }),
  fetchCompanyChildren: vi.fn().mockResolvedValue([]),
  createCompany: vi.fn(),
  updateCompany: vi.fn(),
}));

import AdminTransitOfficesPage from "@/app/admin/transit-offices/page";
import AdminCompaniesPage from "@/app/admin/companies/page";
import { decodeJwtPayload, isOtUser } from "@/lib/auth/jwt";

const LAYOUT_CARD_RE = /rounded-2xl border bg-white\/60|rounded-2xl border bg-card/;

describe("HU #12732 — superficie plana en consolas admin", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(decodeJwtPayload).mockReturnValue({ role: "SuperAdmin" });
    vi.mocked(isOtUser).mockReturnValue(false);
  });

  it("AdminTransitOfficesPage no envuelve el listado en card de layout", async () => {
    const { container } = render(<AdminTransitOfficesPage />);
    await screen.findByText("Administración de organismos de tránsito");
    expect(container.innerHTML).not.toMatch(LAYOUT_CARD_RE);
  });

  it("AdminCompaniesPage no envuelve el listado en card de layout", async () => {
    const { container } = render(<AdminCompaniesPage />);
    await screen.findByText("Administración de compañías");
    expect(container.innerHTML).not.toMatch(LAYOUT_CARD_RE);
  });
});
