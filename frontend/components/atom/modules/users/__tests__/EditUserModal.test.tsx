// HU #10622 — Modal "Editar usuario": formulario prellenado (AC1), mapeo de error de
// correo en uso sin perder lo escrito (AC2) y aviso de conflicto de concurrencia (AC3).
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EditUserModal } from "../EditUserModal";
import { ApiError } from "@/lib/api/types";

const user = {
  id: "user-1",
  fullName: "Laura García",
  email: "laura@flit.local",
  rowVersion: 3,
};

describe("EditUserModal (#10622)", () => {
  it("AC1: precarga el formulario con nombre y correo del usuario, con foco inicial en nombre", () => {
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={vi.fn()} onUpdate={vi.fn()} />);

    const nameInput = screen.getByLabelText(/nombre completo/i);
    expect(nameInput).toHaveValue("Laura García");
    expect(screen.getByLabelText(/correo electrónico/i)).toHaveValue("laura@flit.local");
    expect(nameInput).toHaveFocus();
  });

  it("envía displayName recortado y rowVersion, y notifica onSaved en éxito", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi.fn().mockResolvedValue(undefined);
    const onSaved = vi.fn();
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={onSaved} onUpdate={onUpdate} />);

    const nameInput = screen.getByLabelText(/nombre completo/i);
    await ue.clear(nameInput);
    await ue.type(nameInput, "Laura García Ruiz");
    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(onUpdate).toHaveBeenCalledWith("user-1", {
        displayName: "Laura García Ruiz",
        rowVersion: 3,
      }),
    );
    expect(onSaved).toHaveBeenCalledTimes(1);
  });

  // El correo es la credencial de acceso: se muestra pero no se edita, y no viaja en el PATCH.
  it("muestra el correo en solo lectura y no lo envía al guardar", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi.fn().mockResolvedValue(undefined);
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={vi.fn()} onUpdate={onUpdate} />);

    const emailInput = screen.getByLabelText(/correo electrónico/i);
    expect(emailInput).toHaveValue("laura@flit.local");
    expect(emailInput).toHaveAttribute("readonly");

    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(onUpdate).toHaveBeenCalledWith("user-1", {
        displayName: "Laura García",
        rowVersion: 3,
      }),
    );
  });

  it("AC2: mapea el 409 USER_ALREADY_EXISTS sin perder lo escrito en el formulario", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "USER_ALREADY_EXISTS" }));
    const onSaved = vi.fn();
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={onSaved} onUpdate={onUpdate} />);

    const nameInput = screen.getByLabelText(/nombre completo/i);
    await ue.clear(nameInput);
    await ue.type(nameInput, "Laura G.");
    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/ese correo ya está en uso/i);
    // No se pierde lo escrito.
    expect(screen.getByLabelText(/nombre completo/i)).toHaveValue("Laura G.");
    expect(onSaved).not.toHaveBeenCalled();
  });

  it("AC2: mapea el 409 EMAIL_BELONGS_TO_DELETED_USER", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "EMAIL_BELONGS_TO_DELETED_USER" }));
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={vi.fn()} onUpdate={onUpdate} />);

    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /pertenece a una cuenta eliminada/i,
    );
  });

  it("AC3: aviso de conflicto de concurrencia (CONCURRENCY_CONFLICT), formulario se mantiene abierto", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "CONCURRENCY_CONFLICT" }));
    const onClose = vi.fn();
    const onSaved = vi.fn();
    render(<EditUserModal user={user} onClose={onClose} onSaved={onSaved} onUpdate={onUpdate} />);

    const nameInput = screen.getByLabelText(/nombre completo/i);
    await ue.clear(nameInput);
    await ue.type(nameInput, "Nombre editado sin guardar");
    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /modificado por otra persona.*cierra el diálogo/i,
    );
    // El modal no se cierra solo ni pierde lo escrito.
    expect(onClose).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/nombre completo/i)).toHaveValue("Nombre editado sin guardar");
  });

  it("mapea el 404 (usuario ya no existe)", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi.fn().mockRejectedValue(new ApiError(404, "Not found"));
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={vi.fn()} onUpdate={onUpdate} />);

    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/el usuario ya no existe/i);
  });

  it("cierra el modal al presionar Escape (sin operaciones en curso)", async () => {
    const ue = userEvent.setup();
    const onClose = vi.fn();
    render(<EditUserModal user={user} onClose={onClose} onSaved={vi.fn()} onUpdate={vi.fn()} />);

    await ue.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

// Al editar hay que ver a qué perfil pertenece el usuario (para saber en qué relación se está
// parado) y el selector de rol debe ofrecer solo los roles de ese perfil.
describe("EditUserModal — perfil y roles del perfil", () => {
  const catalogo = [
    { id: "r-super", code: "SuperAdmin", name: "Super Administrador", description: null, isSystem: true, permissionCount: 0, createdAt: "" },
    { id: "r-admin", code: "AdminCompany", name: "Administrador de Compañía", description: null, isSystem: true, permissionCount: 0, createdAt: "" },
    { id: "r-radicador", code: "Radicador", name: "Radicador", description: null, isSystem: false, permissionCount: 0, createdAt: "" },
  ];

  function renderWithRoles(profile: "FLIT" | "GESTOR" | "OT", over = {}) {
    const onAssignRole = vi.fn().mockResolvedValue(undefined);
    render(
      <EditUserModal
        user={user}
        onClose={vi.fn()}
        onSaved={vi.fn()}
        onUpdate={vi.fn()}
        profile={profile}
        roleSection={{
          currentRoleName: "Radicador",
          currentRoleId: "r-radicador",
          roles: catalogo,
          rolesLoading: false,
          onAssignRole,
          ...over,
        }}
      />,
    );
    return { onAssignRole };
  }

  it("muestra el perfil del usuario que se está editando", () => {
    renderWithRoles("GESTOR");
    expect(screen.getByText("Perfil")).toBeInTheDocument();
    expect(screen.getByText("Gestor")).toBeInTheDocument();
    expect(screen.getByText(/empresa cliente que radica trámites/i)).toBeInTheDocument();
  });

  it("muestra el perfil aunque no haya sección de rol", () => {
    render(
      <EditUserModal user={user} onClose={vi.fn()} onSaved={vi.fn()} onUpdate={vi.fn()} profile="FLIT" />,
    );
    expect(screen.getByText("Perfil")).toBeInTheDocument();
    expect(screen.getByText("FLIT")).toBeInTheDocument();
  });

  it("no ofrece el rol Super Administrador al editar un Gestor", () => {
    renderWithRoles("GESTOR");
    const select = screen.getByLabelText(/cambiar rol del usuario/i);
    const opciones = Array.from(select.querySelectorAll("option")).map((o) => o.textContent);
    expect(opciones).toContain("Radicador");
    expect(opciones).toContain("Administrador de Compañía");
    expect(opciones).not.toContain("Super Administrador");
  });

  it("tampoco lo ofrece al editar un usuario de un organismo", () => {
    renderWithRoles("OT");
    const select = screen.getByLabelText(/cambiar rol del usuario/i);
    const opciones = Array.from(select.querySelectorAll("option")).map((o) => o.textContent);
    expect(opciones).not.toContain("Super Administrador");
  });

  it("mantiene visible el rol vigente aunque quede fuera del catálogo del perfil", () => {
    renderWithRoles("GESTOR", { currentRoleId: "r-super", currentRoleName: "Super Administrador" });
    const select = screen.getByLabelText(/cambiar rol del usuario/i) as HTMLSelectElement;
    // Se ve como opción deshabilitada —no en blanco— pero no se puede volver a elegir.
    const actual = Array.from(select.querySelectorAll("option")).find(
      (o) => o.textContent === "Super Administrador",
    );
    expect(actual).toBeDefined();
    expect(actual).toBeDisabled();
    expect(select.value).toBe("");
  });

  it("avisa cuando el perfil no tiene roles disponibles", () => {
    renderWithRoles("GESTOR", { roles: [catalogo[0]] });
    expect(screen.getByText(/no hay roles disponibles para este perfil/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/cambiar rol del usuario/i)).toBeDisabled();
  });
});
