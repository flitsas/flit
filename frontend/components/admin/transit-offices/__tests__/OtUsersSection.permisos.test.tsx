// HU #13655 (Feature #13104 F-D2) — el Admin OT configura los permisos de los
// usuarios de su organismo (modo admin).
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers } from "@/lib/api/admin-ot-security";
import type { OtUserItem } from "@/lib/api/admin-ot-security";
import { listGrantablePermissions } from "@/lib/api/permission-overrides";

vi.mock("@/lib/api/admin-ot-security", () => ({
  fetchOtUsers: vi.fn(),
  inviteOtUser: vi.fn(),
  suspendOtUser: vi.fn(),
  unsuspendOtUser: vi.fn(),
  updateOtUser: vi.fn(),
  deleteOtUser: vi.fn(),
  resendOtInvitation: vi.fn(),
  cancelOtInvitation: vi.fn(),
  reactivateOtInvitation: vi.fn(),
}));
vi.mock("@/lib/api/security", () => ({
  restoreUser: vi.fn(),
  assignRole: vi.fn(),
  getRoles: vi.fn().mockResolvedValue([]),
  getAccessibleModules: vi.fn().mockResolvedValue([]),
  getTenantRole: vi.fn().mockResolvedValue({ permissions: [] }),
}));
vi.mock("@/lib/api/superadmin-client", () => ({ superadminClient: { getRole: vi.fn(), listRoles: vi.fn() } }));
vi.mock("@/lib/api/permission-overrides", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/permission-overrides")>("@/lib/api/permission-overrides");
  return {
    ...actual,
    listGrantablePermissions: vi.fn().mockResolvedValue([]),
    listUserOverrides: vi.fn().mockResolvedValue([]),
  };
});
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false, isAdminCompany: false, isOtAdmin: true, permissions: [], tenantId: "ot-tenant-1",
    userId: "u-self", roleId: "role-1", roleCode: "ot_admin",
  }),
}));

const mk = (over: Partial<OtUserItem>): OtUserItem => ({
  id: "u-1", fullName: "Ana Torres", email: "ana@transito.gov.co", role: "Operador", roleCode: "operador_ot", roleId: "r-op",
  status: "active", createdAt: "2026-07-01T00:00:00Z", isSuspended: false, rowVersion: 1, ...over,
});
const ana = mk({});

const renderSection = () =>
  render(
    <ToastProvider>
      <OtUsersSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchOtUsers).mockResolvedValue({ data: [ana] });
});

describe("OtUsersSection — Admin OT (#13655)", () => {
  it("#13655 AC1: abre «Permisos por módulo» de un usuario del organismo en modo admin", async () => {
    renderSection();
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Ana Torres" }));
    expect(await screen.findByText("Permisos por módulo — Ana Torres")).toBeInTheDocument();
    expect(listGrantablePermissions).toHaveBeenCalledWith(undefined);
  });
});
