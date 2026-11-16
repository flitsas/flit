// HU #14428 (Feature #14420) — «Importar Excel» en la sección Usuarios del hub OT, con el destino fijo
// en el organismo. El cliente OT lleva el transitOfficeId del hub; el catálogo de roles (sin
// administradores para un Gestor OT) lo entrega el backend en la plantilla.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers } from "@/lib/api/admin-ot-security";
import { ApiError } from "@/lib/api/types";

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
vi.mock("@/components/consultas/export", () => ({ download: vi.fn() }));

const bulk = vi.hoisted(() => ({
  downloadTemplate: vi.fn(),
  submit: vi.fn(),
  create: vi.fn(),
}));
vi.mock("@/lib/api/bulk-invitations-client", async (orig) => ({
  ...(await orig<typeof import("@/lib/api/bulk-invitations-client")>()),
  createOtBulkInvitationsClient: (scope: unknown) => {
    bulk.create(scope);
    return { downloadTemplate: bulk.downloadTemplate, submit: bulk.submit };
  },
}));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false,
    isAdminCompany: false,
    isOtAdmin: true,
    permissions: [],
    tenantId: "ot-tenant-1",
    userId: "u-self",
    roleId: "r-adm",
    roleCode: "ot_admin",
  }),
}));

const renderSection = () =>
  render(
    <ToastProvider>
      <OtUsersSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchOtUsers).mockResolvedValue({ data: [] });
});

describe("OtUsersSection — Importar Excel", () => {
  it("AC1: aparece junto a Invitar y abre el asistente con el destino fijo en el organismo", async () => {
    const user = userEvent.setup();
    renderSection();

    expect(await screen.findByRole("button", { name: /invitar usuario/i })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /importar excel/i }));

    expect(await screen.findByRole("dialog", { name: /importar usuarios desde excel/i })).toBeInTheDocument();
    expect(screen.getByTestId("bulk-invite-context")).toHaveTextContent(/organismo de tránsito · este organismo/i);
    // Destino fijo: no hay selector de perfil ni de destino.
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.queryByTestId("bulk-invite-context-picker")).not.toBeInTheDocument();
    expect(bulk.create).toHaveBeenCalledWith({ transitOfficeId: "ot-1" });
  });

  it("descarga la plantilla y valida con el cliente OT", async () => {
    const user = userEvent.setup();
    bulk.downloadTemplate.mockResolvedValue(undefined);
    bulk.submit.mockResolvedValue({ dryRun: true, total: 1, valid: 1, created: 0, rejected: 0, notProcessed: 0, rows: [] });
    renderSection();

    await user.click(await screen.findByRole("button", { name: /importar excel/i }));
    await user.click(screen.getByRole("button", { name: /descargar plantilla/i }));
    await user.upload(screen.getByLabelText("Archivo Excel (.xlsx)"), new File(["x"], "equipo.xlsx"));
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));

    expect(bulk.downloadTemplate).toHaveBeenCalledTimes(1);
    expect(await screen.findByTestId("bulk-invite-preview")).toBeInTheDocument();
    expect(bulk.submit).toHaveBeenCalledWith(expect.any(File), expect.objectContaining({ dryRun: true }));
  });

  it("AC2: ante un 403 del backend muestra un error sin romper la pantalla", async () => {
    const user = userEvent.setup();
    bulk.submit.mockRejectedValue(new ApiError(403, "x", null));
    renderSection();

    await user.click(await screen.findByRole("button", { name: /importar excel/i }));
    await user.upload(screen.getByLabelText("Archivo Excel (.xlsx)"), new File(["x"], "equipo.xlsx"));
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no tienes permiso/i);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /invitar usuario/i })).toBeInTheDocument();
  });

  it("tras un commit con filas creadas recarga el listado de usuarios", async () => {
    const user = userEvent.setup();
    const row = { rowNumber: 2, email: "a@x.co", fullName: "A", roleName: "R", errorCode: null };
    bulk.submit
      .mockResolvedValueOnce({ dryRun: true, total: 1, valid: 1, created: 0, rejected: 0, notProcessed: 0, rows: [{ ...row, status: "valid" }] })
      .mockResolvedValueOnce({ dryRun: false, total: 1, valid: 1, created: 1, rejected: 0, notProcessed: 0, rows: [{ ...row, status: "created" }] });
    renderSection();
    await waitFor(() => expect(fetchOtUsers).toHaveBeenCalledTimes(1));

    await user.click(await screen.findByRole("button", { name: /importar excel/i }));
    await user.upload(screen.getByLabelText("Archivo Excel (.xlsx)"), new File(["x"], "equipo.xlsx"));
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));
    await user.click(await screen.findByRole("button", { name: /confirmar e invitar/i }));

    await screen.findByTestId("bulk-invite-result");
    await waitFor(() => expect(fetchOtUsers).toHaveBeenCalledTimes(2));
  });
});
