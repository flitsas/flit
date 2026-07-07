// Refactor adminOT — AdminTransitOfficesPage: ot_admin salta directo a su propio hub
// (igual que AdminCompany entra directo a /empresa/usuarios); SuperAdmin ve el
// catálogo con el botón de alta de tenant OT.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import AdminTransitOfficesPage from "../page";

const mockPush = vi.fn();
const mockReplace = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush, replace: mockReplace }),
}));

vi.mock("@/lib/api/client", () => ({
  getToken: vi.fn(),
}));

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn().mockResolvedValue([]),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 100 }),
}));

import { getToken } from "@/lib/api/client";
import { fetchOtProfile } from "@/lib/api/admin-ot";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

describe("AdminTransitOfficesPage — refactor adminOT", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("ot_admin: se redirige directo a su propio hub sin ver el catálogo", async () => {
    vi.mocked(getToken).mockReturnValue(makeToken({ sub: "u1", role: "ot_admin" }));
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "ot-own-id",
      featureFlags: [],
    });

    render(<AdminTransitOfficesPage />);

    await waitFor(() =>
      expect(mockReplace).toHaveBeenCalledWith("/admin/transit-offices/ot-own-id/tramites"),
    );
    expect(screen.queryByText("Administración de organismos de tránsito")).not.toBeInTheDocument();
  });

  it("SuperAdmin: ve el catálogo con el botón de alta de tenant OT", async () => {
    vi.mocked(getToken).mockReturnValue(makeToken({ sub: "u1", role: "SuperAdmin" }));

    render(<AdminTransitOfficesPage />);

    expect(
      await screen.findByText("Administración de organismos de tránsito"),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Dar de alta Organismo de Tránsito/i }),
    ).toBeInTheDocument();
    expect(fetchOtProfile).not.toHaveBeenCalled();
  });
});
