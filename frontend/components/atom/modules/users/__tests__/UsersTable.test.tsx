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

describe("UsersTable — columnas Perfil y Rol separadas (HU #11551)", () => {
  it("expone encabezados 'Perfil' y 'Rol' como columnas distintas", () => {
    renderTable();
    // La cabecera de la tabla vive en el mismo grid que "Usuario"/"Estado"/"Fecha"; el label
    // "Perfil" también existe en la barra de filtros, así que se acota al encabezado.
    const encabezado = screen.getByText("Usuario").closest("tr") as HTMLElement;
    expect(within(encabezado).getByText("Perfil")).toBeInTheDocument();
    expect(within(encabezado).getByText("Rol")).toBeInTheDocument();
    // Ya no existe la columna compuesta "Perfil / Rol".
    expect(screen.queryByText("Perfil / Rol")).not.toBeInTheDocument();
  });

  // AC1 — Administrador de Compañía en tenant compañía: Perfil = Gestor, Rol = Administrador
  // de Compañía, en celdas distintas y legibles (sin truncar por opacidad baja).
  it("AC1 — perfil Gestor y rol Administrador de Compañía en celdas separadas", () => {
    renderTable();
    const fila = screen.getByText("Ana Torres").closest("tr") as HTMLElement;
    expect(within(fila).getByText("Gestor · Admin")).toBeInTheDocument();
    const rol = within(fila).getByText("Administrador de Compañía");
    expect(rol).toBeInTheDocument();
    // La celda de rol ya no lleva la opacidad reducida que la hacía ilegible.
    expect(rol.className).not.toContain("opacity-80");
  });

  // AC2 — usuario sin rol asignado: columna Rol muestra "Sin rol".
  it("AC2 — usuario sin rol asignado muestra 'Sin rol' en la columna Rol", () => {
    render(
      <UsersTable
        rows={[row({ id: "u6", fullName: "Fer Ríos", profile: "GESTOR", role: null, roleCode: null })]}
        actionsFor={() => []}
      />,
    );
    const fila = screen.getByText("Fer Ríos").closest("tr") as HTMLElement;
    expect(within(fila).getByText("Sin rol")).toBeInTheDocument();
  });

  // AC3 — un usuario FLIT y otro OT: cada uno con su perfil correcto y su rol respectivo.
  it("AC3 — perfiles FLIT y OT muestran cada uno su perfil y su rol", () => {
    renderTable();
    const filaFlit = screen.getByText("Caro Díaz").closest("tr") as HTMLElement;
    expect(within(filaFlit).getByText("FLIT")).toBeInTheDocument();
    expect(within(filaFlit).getByText("Super Administrador")).toBeInTheDocument();

    const filaOt = screen.getByText("Beto Ruiz").closest("tr") as HTMLElement;
    expect(within(filaOt).getByText("OT · Admin")).toBeInTheDocument();
    expect(within(filaOt).getByText("Administrador OT")).toBeInTheDocument();
  });

  it("usa el perfil del backend aunque el rol sea personalizado", () => {
    renderTable();
    const fila = screen.getByText("Dani Soto").closest("tr") as HTMLElement;
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
    expect(screen.getByText("Cargando usuarios…")).toBeInTheDocument();

    rerender(<UsersTable rows={[]} actionsFor={() => []} />);
    expect(screen.getByTestId("ui-empty")).toBeInTheDocument();

    rerender(<UsersTable rows={[]} error="Falló" actionsFor={() => []} />);
    expect(screen.getByTestId("ui-error")).toBeInTheDocument();
  });
});

// Bug #13055 — tabla homologada con el modelo de trámites: `<table>` semántica; la columna Acciones
// se ajusta al contenido (ya no hay ancho fijo que desborde los botones) y la lista pagina.
describe("UsersTable — tabla semántica y paginación (Bug #13055)", () => {
  const cuatro = (r: UserRow) =>
    ["Editar", "Restablecer", "Suspender", "Desactivar"].map((a) => ({
      icon: Pencil,
      label: `${a} ${r.fullName}`,
      onClick: vi.fn(),
    }));

  it("es una <table> con cabeceras de columna y las 4 acciones dentro de la celda de Acciones", () => {
    renderTable({ actionsFor: cuatro });
    expect(screen.getByRole("table", { name: "Usuarios" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Acciones" })).toBeInTheDocument();
    const fila = screen.getByText("Ana Torres").closest("tr") as HTMLElement;
    expect(within(fila).getAllByRole("button")).toHaveLength(4);
    const celda = within(fila).getByRole("button", { name: "Editar Ana Torres" }).closest("td");
    expect(celda?.className).toContain("whitespace-nowrap");
  });

  it("muestra la paginación estándar con «Filas por página»", () => {
    renderTable();
    expect(screen.getByRole("combobox", { name: "Filas por página" })).toBeInTheDocument();
    expect(screen.getByText(/Mostrando 1–4 de 4/)).toBeInTheDocument();
  });

  it("pagina en cliente y vuelve a la página 1 al filtrar", async () => {
    const user = userEvent.setup();
    const muchos = Array.from({ length: 12 }, (_, i) =>
      row({ id: `m${i}`, fullName: `Usuario ${String(i).padStart(2, "0")}`, role: "Rol X", roleCode: "x", profile: "OT" }),
    );
    render(<UsersTable rows={muchos} actionsFor={() => []} />);
    expect(screen.getAllByRole("row")).toHaveLength(1 + 10);
    await user.click(screen.getByRole("button", { name: /página 2|2/i }));
    expect(screen.getAllByRole("row")).toHaveLength(1 + 2);
    await user.type(screen.getByLabelText("Buscar usuarios"), "Usuario 0");
    expect(screen.getByText(/Mostrando 1–10 de 10/)).toBeInTheDocument();
  });
});
