// HU #10620 (Feature #10618) — unificación de la UI de suspender/desactivar usuario en el
// módulo de Compañía (Usuarios.tsx). AC1 (desactivar indefinidamente no exige fecha), AC2
// (suspender temporalmente sí la exige) y AC3 (mapeo del error de "último administrador
// activo" a un mensaje claro en el modal).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { ApiError } from "@/lib/api/types";

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false,
    isAdminCompany: true,
    isOtAdmin: false,
    permissions: [],
    tenantId: "tenant-1",
    userId: "admin-1",
    roleId: "role-admin",
    roleCode: "AdminCompany",
  }),
}));

const activeUser = {
  id: "u-1",
  fullName: "Ana Torres",
  email: "ana@empresa.com",
  role: "Supervisor",
  roleCode: "SUPERVISOR",
  roleId: "role-1",
  status: "active" as const,
  createdAt: "2026-06-01T00:00:00Z",
  isSuspended: false,
  rowVersion: 0,
};

vi.mock("@/lib/api/security", () => ({
  getUsers: vi.fn(),
  getRoles: vi.fn().mockResolvedValue([]),
  createInvitation: vi.fn(),
  assignRole: vi.fn(),
  blockUser: vi.fn(),
  unblockUser: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

import { getUsers, blockUser } from "@/lib/api/security";

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getUsers).mockResolvedValue([activeUser]);
});

describe("Usuarios — suspender/desactivar (HU #10620)", () => {
  it("AC2: 'Suspender temporalmente' exige fecha de fin y la envía como ISO", async () => {
    vi.mocked(blockUser).mockResolvedValue({ id: "sus-1" });
    const user = userEvent.setup();
    render(<Usuarios />);
    await screen.findByText("Ana Torres");

    await user.click(screen.getByRole("button", { name: /Suspender temporalmente a Ana Torres/i }));
    expect(screen.getByLabelText(/Suspendido hasta/i)).toBeRequired();

    await user.type(screen.getByLabelText(/Motivo/i), "Incumplimiento");
    await user.click(screen.getByRole("button", { name: /^Suspender usuario$/i }));

    await waitFor(() => expect(blockUser).toHaveBeenCalled());
    const [, , endsAt] = vi.mocked(blockUser).mock.calls[0]!;
    expect(endsAt).not.toBeNull();
    expect(() => new Date(endsAt as string).toISOString()).not.toThrow();
  });

  it("AC1: 'Desactivar indefinidamente' no exige fecha de fin y envía endsAt=null", async () => {
    vi.mocked(blockUser).mockResolvedValue({ id: "sus-2" });
    const user = userEvent.setup();
    render(<Usuarios />);
    await screen.findByText("Ana Torres");

    await user.click(screen.getByRole("button", { name: /Desactivar indefinidamente a Ana Torres/i }));
    expect(screen.queryByLabelText(/Suspendido hasta/i)).not.toBeInTheDocument();
    expect(screen.getByText(/Sin fecha de fin/i)).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Motivo/i), "Incumplimiento grave y reiterado");
    await user.click(screen.getByRole("button", { name: /^Desactivar usuario$/i }));

    await waitFor(() =>
      expect(blockUser).toHaveBeenCalledWith("u-1", "Incumplimiento grave y reiterado", null),
    );
  });

  it("permite cambiar de opinión dentro del modal (temporal → indefinido) vía el toggle", async () => {
    vi.mocked(blockUser).mockResolvedValue({ id: "sus-3" });
    const user = userEvent.setup();
    render(<Usuarios />);
    await screen.findByText("Ana Torres");

    await user.click(screen.getByRole("button", { name: /Suspender temporalmente a Ana Torres/i }));
    expect(screen.getByLabelText(/Suspendido hasta/i)).toBeInTheDocument();

    await user.click(screen.getByRole("checkbox", { name: /Sin fecha de fin/i }));
    expect(screen.queryByLabelText(/Suspendido hasta/i)).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/Motivo/i), "Cambio de decisión");
    await user.click(screen.getByRole("button", { name: /^Desactivar usuario$/i }));

    await waitFor(() =>
      expect(blockUser).toHaveBeenCalledWith("u-1", "Cambio de decisión", null),
    );
  });

  it("AC3 (negativo): mapea el error de último administrador activo a un mensaje claro", async () => {
    vi.mocked(blockUser).mockRejectedValue(
      new ApiError(409, "Conflict", { code: "LAST_ACTIVE_ADMIN", message: "…" }),
    );
    const user = userEvent.setup();
    render(<Usuarios />);
    await screen.findByText("Ana Torres");

    await user.click(screen.getByRole("button", { name: /Desactivar indefinidamente a Ana Torres/i }));
    await user.type(screen.getByLabelText(/Motivo/i), "Intento de desactivación");
    await user.click(screen.getByRole("button", { name: /^Desactivar usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /No puedes dejar este tenant sin administradores activos/i,
    );
    // El modal permanece abierto (no se cierra en error) para que el admin vea el motivo.
    expect(screen.getByLabelText(/Motivo/i)).toBeInTheDocument();
  });
});
