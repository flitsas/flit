// HU #11552 / ADR-0048 — "cancelled" es un cuarto valor de `status`, visible con su propio
// badge y filtrable. Complementa UsersTable.test.tsx (no lo sobreescribe).
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

describe("UsersTable — estado 'Cancelada' (HU #11552 / ADR-0048)", () => {
  it("pinta el badge 'Cancelada' con tono neutral para una fila cancelled", () => {
    render(
      <UsersTable
        rows={[row({ id: "inv-1", fullName: "Marcia Ospina", status: "cancelled" })]}
        actionsFor={() => []}
      />,
    );
    const badge = screen.getByRole("status", { name: /^Estado: Cancelada$/i });
    expect(badge).toBeInTheDocument();
  });

  it("expone 'Cancelada' como opción del filtro de estado", () => {
    render(<UsersTable rows={[]} actionsFor={() => []} />);
    expect(
      (screen.getByLabelText("Filtrar por estado") as HTMLSelectElement).querySelector(
        'option[value="cancelled"]',
      ),
    ).not.toBeNull();
  });

  it("el filtro 'Cancelada' aísla las filas cancelled del resto de estados", async () => {
    const user = userEvent.setup();
    render(
      <UsersTable
        rows={[
          row({ id: "inv-2", fullName: "Cancelada Uno", status: "cancelled" }),
          row({ id: "u-1", fullName: "Activo Uno", status: "active" }),
          row({ id: "inv-3", fullName: "Pendiente Uno", status: "pending" }),
        ]}
        actionsFor={() => []}
      />,
    );

    await user.selectOptions(screen.getByLabelText("Filtrar por estado"), "cancelled");

    expect(screen.getByText("Cancelada Uno")).toBeInTheDocument();
    expect(screen.queryByText("Activo Uno")).not.toBeInTheDocument();
    expect(screen.queryByText("Pendiente Uno")).not.toBeInTheDocument();
  });

  it("extraActionsFor recibe la fila cancelled — el padre puede ofrecer 'Reactivar'", () => {
    render(
      <UsersTable
        rows={[row({ id: "inv-4", fullName: "Rosa Peña", status: "cancelled" })]}
        actionsFor={() => []}
        extraActionsFor={(r) => (r.status === "cancelled" ? <button>Reactivar {r.fullName}</button> : null)}
      />,
    );
    expect(screen.getByRole("button", { name: /reactivar rosa peña/i })).toBeInTheDocument();
  });
});
