// HU #14006 (Feature #13996): filtros por columna del estándar HU #13997 en la tabla compartida de usuarios.
import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
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
  await user.click(screen.getByRole("button", { name: /^\+ Filtro/ }));
  await user.click(screen.getByRole("button", { name: columna }));
  await user.click(screen.getByRole("checkbox", { name: opcion }));
  await user.click(screen.getByRole("button", { name: "Aplicar" }));
}

async function filtroTexto(user: User, columna: string, texto: string) {
  await user.click(screen.getByRole("button", { name: /^\+ Filtro/ }));
  await user.click(screen.getByRole("button", { name: columna }));
  await user.type(screen.getByLabelText(`${columna} contiene`), texto);
  await user.click(screen.getByRole("button", { name: "Aplicar" }));
}

describe("UsersTable — filtros por columna (HU #14006)", () => {
  it("AC1/AC2 — ya no hay selects propios: la barra es la del estándar", () => {
    renderTable();
    expect(screen.queryByLabelText("Filtrar por perfil")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "+ Filtro" })).toBeInTheDocument();
    expect(screen.getByLabelText("Buscar usuarios")).toBeInTheDocument();
  });

  it("AC1 — ofrece un filtro por columna con datos y nunca «Acciones»", async () => {
    const user = userEvent.setup();
    renderTable();
    await user.click(screen.getByRole("button", { name: "+ Filtro" }));
    const lista = screen.getByRole("list", { name: "Columnas filtrables" });
    const nombres = within(lista)
      .getAllByRole("button")
      .map((b) => b.textContent);
    expect(nombres).toEqual(["Nombre", "Correo", "Perfil", "Rol", "Estado"]);
  });

  it("AC1 — con la columna Empresa visible aparece su filtro de texto", async () => {
    const user = userEvent.setup();
    renderTable(rows, true);
    await user.click(screen.getByRole("button", { name: "+ Filtro" }));
    expect(screen.getByRole("button", { name: "Empresa" })).toBeInTheDocument();
  });

  it("AC1 — filtra por nombre (contiene)", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroTexto(user, "Nombre", "torres");
    expect(screen.getByText("Ana Torres")).toBeInTheDocument();
    expect(screen.queryByText("Beto Ruiz")).not.toBeInTheDocument();
  });

  it("AC1 — filtra por correo", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroTexto(user, "Correo", "ot.gov");
    expect(screen.getByText("Beto Ruiz")).toBeInTheDocument();
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
  });

  it("AC2 — filtra por perfil, incluidos los roles personalizados de un organismo", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroSeleccion(user, "Perfil", "OT");
    expect(screen.getByText("Beto Ruiz")).toBeInTheDocument();
    expect(screen.getByText("Dani Soto")).toBeInTheDocument();
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
    expect(screen.queryByText("Caro Díaz")).not.toBeInTheDocument();
  });

  it("AC2 — filtra por rol", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroSeleccion(user, "Rol", "Revisor documental");
    expect(screen.getByText("Dani Soto")).toBeInTheDocument();
    expect(screen.queryByText("Beto Ruiz")).not.toBeInTheDocument();
  });

  it("AC2 — filtra por estado Bloqueado (suspendido) y por Cancelada", async () => {
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

  it("AC1 — combina varias columnas: solo quedan los usuarios que cumplen todas", async () => {
    const user = userEvent.setup();
    renderTable();
    await filtroSeleccion(user, "Perfil", "OT");
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

  it("AC3 — sin resultados muestra el estado vacío y «Limpiar filtros» restaura la lista", async () => {
    const user = userEvent.setup();
    renderTable();
    await user.type(screen.getByLabelText("Buscar usuarios"), "zzzz");
    expect(screen.getByText(/ningún usuario coincide/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(screen.getByText("Ana Torres")).toBeInTheDocument();
    expect(screen.getByLabelText("Buscar usuarios")).toHaveValue("");
  });

  it("vuelve a la página 1 al filtrar", async () => {
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
    await filtroTexto(user, "Nombre", "Usuario 0");
    expect(screen.getByText(/Mostrando 1–10 de 10/)).toBeInTheDocument();
  });
});
