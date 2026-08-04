// Tabla de usuarios compartida (context/usuarios-contex.md): columna Perfil / Rol con filtros
// y columna de acciones con el área de clic de RowActions.
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Pencil } from "lucide-react";
import { UsersTable, toUserRow, type UserRow } from "../UsersTable";

function row(over: Partial<UserRow> & Pick<UserRow, "id" | "fullName">): UserRow {
  return toUserRow({
    email: `${over.id}@flit.local`,
    role: null,
    roleCode: null,
    status: "active",
    isSuspended: false,
    createdAt: "2026-07-01T10:00:00Z",
    ...over,
  });
}

const rows: UserRow[] = [
  row({ id: "u1", fullName: "Ana Torres", role: "Administrador de Compañía", roleCode: "AdminCompany", profile: "GESTOR" }),
  row({ id: "u2", fullName: "Beto Ruiz", role: "Administrador OT", roleCode: "ot_admin", profile: "OT" }),
  row({ id: "u3", fullName: "Caro Díaz", role: "Super Administrador", roleCode: "SuperAdmin", profile: "FLIT" }),
  // Rol personalizado dentro de un organismo: el perfil lo dice el backend, no el roleCode.
  row({ id: "u4", fullName: "Dani Soto", role: "Revisor documental", roleCode: "revisor", profile: "OT" }),
];

function renderTable(overrides: Partial<React.ComponentProps<typeof UsersTable>> = {}) {
  return render(
    <UsersTable
      rows={rows}
      actionsFor={(r) => [
        { icon: Pencil, label: `Editar usuario ${r.fullName}`, onClick: vi.fn() },
      ]}
      {...overrides}
    />,
  );
}

describe("UsersTable — columna Perfil / Rol", () => {
  it("muestra el perfil y el rol de cada usuario", () => {
    renderTable();
    const fila = screen.getByText("Ana Torres").closest("div.grid") as HTMLElement;
    expect(within(fila).getByText("Gestor")).toBeInTheDocument();
    expect(within(fila).getByText("Administrador de Compañía")).toBeInTheDocument();
  });

  it("usa el perfil del backend aunque el rol sea personalizado", () => {
    renderTable();
    const fila = screen.getByText("Dani Soto").closest("div.grid") as HTMLElement;
    expect(within(fila).getByText("OT")).toBeInTheDocument();
    expect(within(fila).getByText("Revisor documental")).toBeInTheDocument();
  });
});

describe("UsersTable — filtros", () => {
  it("filtra por perfil, incluidos los roles personalizados de un organismo", async () => {
    const user = userEvent.setup();
    renderTable();

    await user.selectOptions(screen.getByLabelText("Filtrar por perfil"), "OT");

    expect(screen.getByText("Beto Ruiz")).toBeInTheDocument();
    expect(screen.getByText("Dani Soto")).toBeInTheDocument();
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
    expect(screen.queryByText("Caro Díaz")).not.toBeInTheDocument();
  });

  it("filtra por rol", async () => {
    const user = userEvent.setup();
    renderTable();

    await user.selectOptions(screen.getByLabelText("Filtrar por rol"), "Revisor documental");

    expect(screen.getByText("Dani Soto")).toBeInTheDocument();
    expect(screen.queryByText("Beto Ruiz")).not.toBeInTheDocument();
  });

  it("filtra por estado bloqueado", async () => {
    const user = userEvent.setup();
    render(
      <UsersTable
        rows={[...rows, row({ id: "u5", fullName: "Eva Lima", isSuspended: true })]}
        actionsFor={() => []}
      />,
    );

    await user.selectOptions(screen.getByLabelText("Filtrar por estado"), "blocked");

    expect(screen.getByText("Eva Lima")).toBeInTheDocument();
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
  });

  it("busca por nombre y permite limpiar los filtros", async () => {
    const user = userEvent.setup();
    renderTable();

    await user.type(screen.getByLabelText("Buscar usuarios"), "beto");
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /limpiar filtros/i }));
    expect(screen.getByText("Ana Torres")).toBeInTheDocument();
  });

  it("avisa cuando los filtros no dejan ninguna fila", async () => {
    const user = userEvent.setup();
    renderTable();

    await user.type(screen.getByLabelText("Buscar usuarios"), "zzzz");

    expect(screen.getByText(/ningún usuario coincide/i)).toBeInTheDocument();
  });
});

describe("UsersTable — acciones", () => {
  // HU19: el cursor SVG de FLIT desplaza el punto de clic, así que los botones de solo icono
  // necesitan 40x40 de área activa. Con el tamaño anterior (28px) muchos clics caían fuera.
  it("los botones de icono conservan el área de clic mínima de RowActions", () => {
    renderTable();
    const boton = screen.getByRole("button", { name: "Editar usuario Ana Torres" });
    expect(boton.className).toContain("min-h-[40px]");
    expect(boton.className).toContain("min-w-[40px]");
  });

  it("un solo clic dispara la acción", async () => {
    const onClick = vi.fn();
    const user = userEvent.setup();
    render(
      <UsersTable
        rows={rows}
        actionsFor={(r) => [{ icon: Pencil, label: `Editar usuario ${r.fullName}`, onClick }]}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Editar usuario Ana Torres" }));

    expect(onClick).toHaveBeenCalledTimes(1);
  });
});

describe("UsersTable — estados", () => {
  it("expone los mismos data-testid de estado que UiStateBoundary", () => {
    const { rerender } = render(<UsersTable rows={[]} loading actionsFor={() => []} />);
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();

    rerender(<UsersTable rows={[]} actionsFor={() => []} />);
    expect(screen.getByTestId("ui-empty")).toBeInTheDocument();

    rerender(<UsersTable rows={[]} error="Falló" actionsFor={() => []} />);
    expect(screen.getByTestId("ui-error")).toBeInTheDocument();
  });
});
