// Historial de cambios del usuario (context/usuarios-contex.md): debe responder quién, cuándo
// y QUÉ cambió en lenguaje entendible — antes mostraba el uuid del actor y el código crudo de
// la operación (`assign_role`).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import type { AdminAuditLogEntry } from "@/lib/api/types";

vi.mock("@/lib/api/audit", () => ({
  fetchAdminAuditLog: vi.fn(),
}));

import { fetchAdminAuditLog } from "@/lib/api/audit";
import { UserAuditHistoryDrawer, actorLabel, describeChanges } from "../UserAuditHistoryDrawer";

function entry(over: Partial<AdminAuditLogEntry> = {}): AdminAuditLogEntry {
  return {
    id: "a-1",
    entityName: "user",
    module: "users",
    operation: "update",
    result: "success",
    changedBy: "11111111-1111-1111-1111-111111111111",
    changedAt: "2026-07-02T15:04:00Z",
    ...over,
  };
}

describe("describeChanges", () => {
  it("lista solo los campos que cambiaron, con antes y después", () => {
    const changes = describeChanges(
      entry({
        oldValue: JSON.stringify({ nombre: "Ana Torres", correo: "ana@flit.local" }),
        newValue: JSON.stringify({ nombre: "Ana María Torres", correo: "ana@flit.local" }),
      }),
    );

    expect(changes).toEqual([
      { field: "Nombre", before: "Ana Torres", after: "Ana María Torres" },
    ]);
  });

  it("formatea listas de roles", () => {
    const changes = describeChanges(
      entry({
        operation: "assign_role",
        oldValue: JSON.stringify({ roles: ["Radicador"] }),
        newValue: JSON.stringify({ rolAsignado: "Administrador de Compañía" }),
      }),
    );

    expect(changes).toContainEqual({ field: "Roles", before: "Radicador", after: "—" });
    expect(changes).toContainEqual({
      field: "Rol asignado",
      before: "—",
      after: "Administrador de Compañía",
    });
  });

  it("no revienta con detalle ausente o malformado", () => {
    expect(describeChanges(entry())).toEqual([]);
    expect(describeChanges(entry({ newValue: "no-es-json" }))).toEqual([]);
  });
});

describe("actorLabel", () => {
  it("prefiere el nombre y cae al correo", () => {
    expect(actorLabel(entry({ changedByName: "Ana Torres" }))).toBe("Ana Torres");
    expect(actorLabel(entry({ changedByEmail: "ana@flit.local" }))).toBe("ana@flit.local");
  });

  it("distingue un evento del sistema de un actor no resoluble", () => {
    expect(actorLabel(entry({ changedBy: null }))).toBe("Sistema");
    expect(actorLabel(entry())).toBe("Usuario no disponible");
  });
});

describe("UserAuditHistoryDrawer", () => {
  beforeEach(() => {
    vi.mocked(fetchAdminAuditLog).mockReset();
  });

  it("muestra quién, qué y cuándo sin exponer uuids ni códigos crudos", async () => {
    vi.mocked(fetchAdminAuditLog).mockResolvedValue({
      data: [
        entry({
          operation: "assign_role",
          module: "roles",
          changedByName: "Samuel Cárdenas",
          targetName: "Ana Torres",
          oldValue: JSON.stringify({ roles: ["Radicador"] }),
          newValue: JSON.stringify({ rolAsignado: "Administrador de Compañía" }),
        }),
      ],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    });

    render(
      <UserAuditHistoryDrawer userId="u-1" userLabel="Ana Torres" onClose={vi.fn()} />,
    );

    expect(await screen.findByText("Asignó un rol")).toBeInTheDocument();
    expect(screen.getByText("Samuel Cárdenas")).toBeInTheDocument();
    expect(screen.getByText("Rol asignado:")).toBeInTheDocument();
    expect(screen.getByText("Administrador de Compañía")).toBeInTheDocument();
    expect(screen.getByText("Roles")).toBeInTheDocument();

    // Ni el uuid del actor ni el verbo técnico deben llegar a la pantalla.
    expect(screen.queryByText(/11111111/)).not.toBeInTheDocument();
    expect(screen.queryByText("assign_role")).not.toBeInTheDocument();
  });

  it("marca los eventos que no se completaron", async () => {
    vi.mocked(fetchAdminAuditLog).mockResolvedValue({
      data: [
        entry({
          operation: "delete_user",
          result: "failure",
          errorCode: "last_active_admin",
          changedByName: "Samuel Cárdenas",
        }),
      ],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    });

    render(<UserAuditHistoryDrawer userId="u-1" userLabel="Ana Torres" onClose={vi.fn()} />);

    expect(await screen.findByText("Eliminó el usuario")).toBeInTheDocument();
    expect(screen.getByText(/No se completó \(last_active_admin\)/)).toBeInTheDocument();
  });
});
