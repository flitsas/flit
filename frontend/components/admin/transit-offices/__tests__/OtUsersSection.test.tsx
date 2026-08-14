// Pestaña "Usuarios" del hub OT: 4 estados de UI + invitar (con selector de rol) +
// suspender/reactivar. Desde la unificación de tablas comparte UsersTable con el módulo
// Usuarios y la ficha de compañía, así que el vocabulario de estados es el común
// ("Bloqueado", no "Suspendido") y la barra de filtros repite esos labels en un <select>.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import type { OtUserItem } from "@/lib/api/admin-ot-security";
import { ApiError } from "@/lib/api/types";

vi.mock("@/lib/api/admin-ot-security", () => ({
  fetchOtUsers: vi.fn(),
  inviteOtUser: vi.fn(),
  suspendOtUser: vi.fn(),
  unsuspendOtUser: vi.fn(),
  updateOtUser: vi.fn(),
  deleteOtUser: vi.fn(),
  resendOtInvitation: vi.fn(),
}));

// Bloquear/desactivar/reactivar es EXCLUSIVO de SuperAdmin, así que este flujo (suspender/
// reactivar) se prueba como SuperAdmin. Sin este mock el hook real leería un JWT vacío.
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: true,
    isAdminCompany: false,
    isOtAdmin: false,
    permissions: [],
    tenantId: "ot-tenant-1",
    userId: "superadmin-user",
    roleId: "role-super",
    roleCode: "SuperAdmin",
  }),
}));

import {
  fetchOtUsers,
  inviteOtUser,
  suspendOtUser,
  unsuspendOtUser,
} from "@/lib/api/admin-ot-security";

const activeUser: OtUserItem = {
  id: "u-1",
  fullName: "Laura García",
  email: "laura@transito.gov.co",
  role: "Admin OT",
  roleCode: "ot_admin",
  roleId: "role-1",
  status: "active",
  createdAt: "2026-06-23T10:00:00Z",
  isSuspended: false,
  rowVersion: 1,
};

const suspendedUser: OtUserItem = {
  id: "u-2",
  fullName: "Carlos Pérez",
  email: "carlos@transito.gov.co",
  role: "Admin OT",
  roleCode: "ot_admin",
  roleId: "role-1",
  status: "active",
  createdAt: "2026-06-20T10:00:00Z",
  isSuspended: true,
  rowVersion: 5,
};

function renderSection() {
  return render(
    <ToastProvider>
      <OtUsersSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );
}

describe("OtUsersSection — refactor adminOT", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("estado cargando: muestra el skeleton", () => {
    vi.mocked(fetchOtUsers).mockReturnValue(new Promise(() => {}));
    renderSection();
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("estado vacío: sin usuarios muestra CTA de invitar", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [] });
    renderSection();
    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByText(/No hay usuarios en este organismo/i)).toBeInTheDocument();
  });

  it("estado error: muestra reintentar y vuelve a cargar", async () => {
    vi.mocked(fetchOtUsers).mockRejectedValueOnce(new Error("network"));
    const user = userEvent.setup();
    renderSection();
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();

    vi.mocked(fetchOtUsers).mockResolvedValueOnce({ data: [activeUser] });
    await user.click(screen.getByRole("button", { name: /Reintentar/i }));
    expect(await screen.findByText("Laura García")).toBeInTheDocument();
  });

  it("estado lleno: lista usuarios con su estado", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser, suspendedUser] });
    renderSection();
    // El chip se busca DENTRO de la fila: los mismos labels existen también como opciones
    // del filtro de estado de la barra superior.
    const activa = (await screen.findByText("Laura García")).closest("div.grid") as HTMLElement;
    const bloqueada = screen.getByText("Carlos Pérez").closest("div.grid") as HTMLElement;
    expect(within(activa).getByText("Activo")).toBeInTheDocument();
    expect(within(bloqueada).getByText("Bloqueado")).toBeInTheDocument();
  });

  it("invita a un usuario nuevo sin marcar rol: el backend asigna ot_admin", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(inviteOtUser).mockResolvedValue({
      invitationId: "inv-1",
      email: "nuevo@transito.gov.co",
      emailSent: true,
    });
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Laura García");

    await user.click(screen.getByRole("button", { name: /Invitar usuario/i }));

    await user.type(screen.getByLabelText(/Nombre completo/i), "Nuevo Colaborador");
    await user.type(screen.getByLabelText(/Correo electrónico/i), "nuevo@transito.gov.co");
    await user.click(screen.getByRole("button", { name: /Enviar invitación/i }));

    await waitFor(() =>
      expect(inviteOtUser).toHaveBeenCalledWith(
        // roleIds ausente = sin selección explícita: el backend conserva ot_admin.
        { email: "nuevo@transito.gov.co", fullName: "Nuevo Colaborador", roleIds: undefined },
        { transitOfficeId: "ot-1" },
      ),
    );
  });

  it("suspende temporalmente a un usuario activo", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(suspendOtUser).mockResolvedValue({ id: "sus-1" });
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Laura García");

    await user.click(screen.getByRole("button", { name: /Suspender temporalmente a Laura García/i }));
    await user.type(screen.getByLabelText(/Motivo/i), "Incumplimiento");
    await user.click(screen.getByRole("button", { name: /^Suspender usuario$/i }));

    await waitFor(() => expect(suspendOtUser).toHaveBeenCalled());
    expect(vi.mocked(suspendOtUser).mock.calls[0]?.[0]).toBe("u-1");
    expect(vi.mocked(suspendOtUser).mock.calls[0]?.[1]?.endsAt).not.toBeNull();
    expect(vi.mocked(suspendOtUser).mock.calls[0]?.[2]).toEqual({ transitOfficeId: "ot-1" });
  });

  it("desactiva indefinidamente a un usuario activo (AC1/AC4 — paridad con Compañía)", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(suspendOtUser).mockResolvedValue({ id: "sus-2" });
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Laura García");

    await user.click(screen.getByRole("button", { name: /Desactivar indefinidamente a Laura García/i }));
    expect(screen.queryByLabelText(/Suspendido hasta/i)).not.toBeInTheDocument();
    await user.type(screen.getByLabelText(/Motivo/i), "Incumplimiento grave");
    await user.click(screen.getByRole("button", { name: /^Desactivar usuario$/i }));

    await waitFor(() => expect(suspendOtUser).toHaveBeenCalled());
    expect(vi.mocked(suspendOtUser).mock.calls[0]?.[0]).toBe("u-1");
    expect(vi.mocked(suspendOtUser).mock.calls[0]?.[1]).toEqual({
      reason: "Incumplimiento grave",
      endsAt: null,
    });
  });

  it("AC3 — mapea el error de último administrador activo a un mensaje claro en el modal", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(suspendOtUser).mockRejectedValue(
      new ApiError(409, "Conflict", { error: "LAST_ACTIVE_ADMIN", message: "…" }),
    );
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Laura García");

    await user.click(screen.getByRole("button", { name: /Suspender temporalmente a Laura García/i }));
    await user.type(screen.getByLabelText(/Motivo/i), "Incumplimiento");
    await user.click(screen.getByRole("button", { name: /^Suspender usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /No puedes dejar este tenant sin administradores activos/i,
    );
  });

  it("reactiva a un usuario suspendido", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [suspendedUser] });
    vi.mocked(unsuspendOtUser).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Carlos Pérez");

    await user.click(screen.getByRole("button", { name: /Reactivar usuario Carlos Pérez/i }));
    await waitFor(() =>
      expect(unsuspendOtUser).toHaveBeenCalledWith("u-2", { transitOfficeId: "ot-1" }),
    );
  });

  // HU #11550 AC4 — la ruta OT (AdminOtEndpoints) responde con `{ error, message }`
  // (no `code`, a diferencia de SecurityEndpoints); igual debe mostrar el mismo mensaje
  // unificado que la ruta Security/AdminCompany para los tres conflictos de correo.
  it.each([
    ["INVITATION_ALREADY_PENDING", "AC1"],
    ["USER_ALREADY_EXISTS", "AC2"],
    ["EMAIL_BELONGS_TO_DELETED_USER", "AC3"],
  ])("AC4 — %s (%s): invitar desde el hub OT muestra el mensaje unificado", async (code) => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(inviteOtUser).mockRejectedValue(
      new ApiError(409, "Conflict", { error: code, message: "…" }),
    );
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Laura García");

    await user.click(screen.getByRole("button", { name: /Invitar usuario/i }));
    await user.type(screen.getByLabelText(/Nombre completo/i), "Nuevo Colaborador");
    await user.type(screen.getByLabelText(/Correo electrónico/i), "nuevo@transito.gov.co");
    await user.click(screen.getByRole("button", { name: /Enviar invitación/i }));

    expect(
      await screen.findByText(/el correo utilizado ya se encuentra asociado a otra cuenta/i),
    ).toBeInTheDocument();
  });

  it("un 409 que no es conflicto de correo al invitar desde el hub OT NO usa el mensaje unificado", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(inviteOtUser).mockRejectedValue(
      new ApiError(409, "Conflict", { error: "SOME_OTHER_CONFLICT" }),
    );
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Laura García");

    await user.click(screen.getByRole("button", { name: /Invitar usuario/i }));
    await user.type(screen.getByLabelText(/Nombre completo/i), "Nuevo Colaborador");
    await user.type(screen.getByLabelText(/Correo electrónico/i), "nuevo@transito.gov.co");
    await user.click(screen.getByRole("button", { name: /Enviar invitación/i }));

    expect(
      await screen.findByText(/no se pudo completar la invitación por un conflicto/i),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/el correo utilizado ya se encuentra asociado a otra cuenta/i),
    ).not.toBeInTheDocument();
  });
});
