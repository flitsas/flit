// HU #14006 (Feature #13996): filtros por columna del estándar HU #13997 en la tabla compartida de usuarios.
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { UsersTable, toUserRow, type UserRow } from "../UsersTable";

type User = ReturnType<typeof userEvent.setup>;

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
  row({
    id: "u1",
    fullName: "Ana Torres",
    email: "ana@gestor.co",
    role: "Administrador de Compañía",
    roleCode: "AdminCompany",
    profile: "GESTOR",
  }),
  row({
    id: "u2",
    fullName: "Beto Ruiz",
    email: "beto@ot.gov",
    role: "Administrador OT",
    roleCode: "ot_admin",
    profile: "OT",
    status: "pending",
  }),
  row({
    id: "u3",
    fullName: "Caro Díaz",
    role: "Super Administrador",
    roleCode: "SuperAdmin",
    profile: "FLIT",
  }),
  row({
    id: "u4",
    fullName: "Dani Soto",
    role: "Revisor documental",
    roleCode: "revisor",
    profile: "OT",
    isSuspended: true,
  }),
  row({ id: "u5", fullName: "Eva Lima", status: "cancelled", profile: "GESTOR" }),
];

function renderTable(rs: UserRow[] = rows, showTenantColumn = false) {
  return render(<UsersTable rows={rs} showTenantColumn={showTenantColumn} actionsFor={() => []} />);
}

async function filtroSeleccion(user: User, columna: string, opcion: string) {
  await user.click(screen.getByRole("button", { name: `Filtrar por ${columna}` }));
  await user.click(screen.getByRole("checkbox", { name: opcion }));
  await user.click(screen.getByRole("button", { name: "Aplicar" }));
}

async function filtroTexto(user: User, columna: string, texto: string) {
  await user.click(screen.getByRole("button", { name: `Filtrar por ${columna}` }));
  await user.type(screen.getByLabelText(`${columna} contiene`), texto);
  await user.click(screen.getByRole("button", { name: "Aplicar" }));
}

describe("UsersTable — filtros de arriba + filtro por columna (HU #14006)", () => {
  it("AC1/AC2 — conserva los filtros de consulta de siempre y NO monta la barra «+ Filtro»", () => {
    renderTable();
    expect(screen.getByLabelText("Buscar usuarios")).toBeInTheDocument();
    expect(screen.getByLabelText("Filtrar por perfil")).toBeInTheDocument();
    expect(screen.getByLabelText("Filtrar por rol")).toBeInTheDocument();
    expect(screen.getByLabelText("Filtrar por estado")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "+ Filtro" })).not.toBeInTheDocument();
  });

  it("AC1 — hay un embudo por columna con datos y ninguno en «Acciones»", () => {
    renderTable();
    for (const c of ["Usuario", "Perfil", "Rol", "Estado", "Fecha"]) {
      expect(screen.getByRole("button", { name: `Filtrar por ${c}` })).toBeInTheDocument();
    }
    expect(screen.queryByRole("button", { name: "Filtrar por Empresa" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Filtrar por Acciones" })).not.toBeInTheDocument();
  });

  it("AC1 — con la columna Empresa visible aparece su embudo de texto", () => {
    renderTable(rows, true);
    expect(screen.getByRole("button", { name: "Filtrar por Empresa" })).toBeInTheDocument();
  });

  it("AC1 — el embudo de Usuario busca en el nombre", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroTexto(user, "Usuario", "torres");
    expect(screen.getByText("Ana Torres")).toBeInTheDocument();
    expect(screen.queryByText("Beto Ruiz")).not.toBeInTheDocument();
  });

  it("AC1 — el embudo de Usuario también busca en el correo", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroTexto(user, "Usuario", "ot.gov");
    expect(screen.getByText("Beto Ruiz")).toBeInTheDocument();
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
  });

  it("AC2 — embudo de Perfil, incluidos los roles personalizados de un organismo", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroSeleccion(user, "Perfil", "OT");
    expect(screen.getByText("Beto Ruiz")).toBeInTheDocument();
    expect(screen.getByText("Dani Soto")).toBeInTheDocument();
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
    expect(screen.queryByText("Caro Díaz")).not.toBeInTheDocument();
  });

  it("AC2 — embudo de Rol", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroSeleccion(user, "Rol", "Revisor documental");
    expect(screen.getByText("Dani Soto")).toBeInTheDocument();
    expect(screen.queryByText("Beto Ruiz")).not.toBeInTheDocument();
  });

  it("AC2 — embudo de Estado: Bloqueado (suspendido) y Cancelada", async () => {
    const user = userEvent.setup();
    const { unmount } = renderTable();
    await filtroSeleccion(user, "Estado", "Bloqueado");
    expect(screen.getByText("Dani Soto")).toBeInTheDocument();
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
    unmount();

    renderTable();
    await filtroSeleccion(user, "Estado", "Cancelada");
    expect(screen.getByText("Eva Lima")).toBeInTheDocument();
    expect(screen.queryByText("Dani Soto")).not.toBeInTheDocument();
  });

  it("AC1 — varios embudos se combinan: solo quedan los que cumplen todos", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroSeleccion(user, "Perfil", "OT");
    await filtroSeleccion(user, "Estado", "Pendiente");
    expect(screen.getByText("Beto Ruiz")).toBeInTheDocument();
    expect(screen.queryByText("Dani Soto")).not.toBeInTheDocument();
  });

  it("AC1 — los filtros de arriba y los de columna se combinan con AND", async () => {
    const user = userEvent.setup();
    renderTable();
    await user.selectOptions(screen.getByLabelText("Filtrar por perfil"), "OT");
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
    await filtroSeleccion(user, "Estado", "Pendiente");
    expect(screen.getByText("Beto Ruiz")).toBeInTheDocument();
    expect(screen.queryByText("Dani Soto")).not.toBeInTheDocument();
  });

  it("AC2 — el buscador conserva nombre y correo", async () => {
    const user = userEvent.setup();
    renderTable();
    await user.type(screen.getByLabelText("Buscar usuarios"), "beto@ot");
    expect(screen.getByText("Beto Ruiz")).toBeInTheDocument();
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
  });

  it("AC3 — sin resultados muestra el vacío y «Limpiar filtros» limpia arriba y columnas", async () => {
    const user = userEvent.setup();
    renderTable();
    await user.type(screen.getByLabelText("Buscar usuarios"), "ana");
    await filtroSeleccion(user, "Estado", "Pendiente");
    expect(screen.getByText(/ningún usuario coincide/i)).toBeInTheDocument();
    // La tabla sigue montada: embudos visibles (el de Estado, activo) y el buscador de arriba.
    expect(screen.getByRole("table", { name: "Usuarios" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Filtrar por Estado \(/ })).toHaveAttribute(
      "data-activo",
      "true",
    );
    expect(screen.getByLabelText("Buscar usuarios")).toHaveValue("ana");
    // Se quita solo el filtro de columna; el buscador se conserva.
    await user.click(screen.getByRole("button", { name: /^Filtrar por Estado/ }));
    await user.click(screen.getByRole("button", { name: "Quitar filtro" }));
    expect(screen.getByText("Ana Torres")).toBeInTheDocument();
    await filtroSeleccion(user, "Estado", "Pendiente");
    await user.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(screen.getByText("Ana Torres")).toBeInTheDocument();
    expect(screen.getByText("Beto Ruiz")).toBeInTheDocument();
    expect(screen.getByLabelText("Buscar usuarios")).toHaveValue("");
  });

  it("vuelve a la página 1 al filtrar por un embudo", async () => {
    const user = userEvent.setup();
    const muchos = Array.from({ length: 12 }, (_, i) =>
      row({
        id: `m${i}`,
        fullName: `Usuario ${String(i).padStart(2, "0")}`,
        role: "Rol X",
        profile: "OT",
      }),
    );
    renderTable(muchos);
    await user.click(screen.getByRole("button", { name: /página 2|2/i }));
    expect(screen.getAllByRole("row")).toHaveLength(1 + 2);
    await filtroTexto(user, "Usuario", "Usuario 0");
    expect(screen.getByText(/Mostrando 1–10 de 10/)).toBeInTheDocument();
  });
});
