// HU #13439 (Feature #13436, Épica #12750) — /admin/banners es exclusivo del Super Admin FLIT.
//
// Uso de ejemplo: render(<AdminBannersPage />) muestra la consola solo si usePermissions().isSuperAdmin.
import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const permissions = vi.hoisted(() => ({ isSuperAdmin: false, permissions: [] as string[] }));

vi.mock("@/hooks/usePermissions", () => ({ usePermissions: () => permissions }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }) }));
vi.mock("@/lib/api/admin-banners", () => ({
  fetchBanners: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 10 }),
  createBanner: vi.fn(),
  updateBanner: vi.fn(),
}));

import AdminBannersPage from "../page";

describe("AdminBannersPage — acceso", () => {
  beforeEach(() => {
    permissions.isSuperAdmin = false;
    permissions.permissions = [];
  });

  it("AC2: un AdminCompany con banners.manage ve el aviso de falta de permiso, no la consola", () => {
    permissions.permissions = ["banners.manage"];

    render(<AdminBannersPage />);

    expect(screen.getByText(/No tienes permiso para administrar banners promocionales/i)).toBeInTheDocument();
  });

  it("AC2: un usuario sin permisos ve el aviso de falta de permiso", () => {
    render(<AdminBannersPage />);

    expect(screen.getByText(/No tienes permiso para administrar banners promocionales/i)).toBeInTheDocument();
  });

  it("AC1: el Super Admin no ve el aviso de falta de permiso", () => {
    permissions.isSuperAdmin = true;

    render(<AdminBannersPage />);

    expect(screen.queryByText(/No tienes permiso para administrar banners promocionales/i)).not.toBeInTheDocument();
  });
});
