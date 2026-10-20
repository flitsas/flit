// HU #13654 — auditoría de permisos individuales (vista compartida del Panel SA).
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { TenantUser } from "@/lib/api/security";

vi.mock("@/lib/api/permission-overrides", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/permission-overrides")>("@/lib/api/permission-overrides");
  return { ...actual, listPermissionAudit: vi.fn() };
});

import { listPermissionAudit, type PermissionAuditRecord } from "@/lib/api/permission-overrides";
import { PermissionAuditView } from "../PermissionAuditView";

const mk = (over: Partial<TenantUser>): TenantUser => ({
  id: "u", fullName: "N", email: "n@flit.local", role: null, roleCode: null, roleId: null, status: "active",
  createdAt: null, isSuspended: false, rowVersion: 1, ...over,
});
const users: TenantUser[] = [
  mk({ id: "u1", fullName: "Ana Torres", email: "ana@flit.local", tenantId: "t1", tenantName: "Compañía Uno" }),
  mk({ id: "u2", fullName: "Beto Admin", email: "beto@flit.local", roleCode: "AdminCompany", tenantId: "t2", tenantName: "Compañía Dos" }),
];

const record = (over: Partial<PermissionAuditRecord> = {}): PermissionAuditRecord => ({
  id: "a1", tenantId: "t1", tenantName: "Compañía Uno", actorUserId: "u2", actorName: "Beto Admin", actorEmail: "beto@flit.local",
  targetUserId: "u1", targetName: "Ana Torres", targetEmail: "ana@flit.local", productCode: "tramites", moduleCode: "tramites",
  permissionSlug: "tramites.create", action: "GRANT", outcome: "OK", reasonCode: null, valueBefore: null, valueAfter: "allow",
  batchId: null, createdAt: "2026-10-19T19:05:00Z", ...over,
});
const page = (items: PermissionAuditRecord[], total = items.length) => ({ items, total, page: 1, pageSize: 10 });

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(listPermissionAudit).mockResolvedValue(page([record()]));
});

async function pick(label: string, option: string) {
  const input = screen.getByRole("combobox", { name: label });
  await userEvent.click(input);
  await userEvent.click(await screen.findByRole("option", { name: new RegExp(option) }));
}

describe("PermissionAuditView", () => {
  it("AC1: muestra la fecha DD/MM/YYYY HH:mm, quién, a quién y qué cambió", async () => {
    render(<PermissionAuditView mode="superadmin" users={users} />);
    const table = await screen.findByRole("table", { name: "Auditoría de permisos individuales" });
    const row = within(table).getAllByRole("row")[1];
    expect(within(row).getByText("19/10/2026 14:05")).toBeInTheDocument();
    expect(within(row).getByText("Beto Admin")).toBeInTheDocument();
    expect(within(row).getByText("Ana Torres")).toBeInTheDocument();
    expect(within(row).getByText("Compañía Uno")).toBeInTheDocument();
    expect(within(row).getByText("Otorgó")).toBeInTheDocument();
    expect(within(row).getByText("Lo que dicta el rol → Habilitado")).toBeInTheDocument();
  });

  it("AC2: filtra por usuario, compañía y administrador enviando los ids a la API", async () => {
    render(<PermissionAuditView mode="superadmin" users={users} />);
    await screen.findByRole("table");

    await pick("Usuario", "Ana Torres");
    await waitFor(() => expect(listPermissionAudit).toHaveBeenLastCalledWith(expect.objectContaining({ userId: "u1" })));

    await pick("Compañía", "Compañía Uno");
    await waitFor(() => expect(listPermissionAudit).toHaveBeenLastCalledWith(expect.objectContaining({ userId: "u1", tenantId: "t1" })));

    await pick("Administrador", "Beto Admin");
    await waitFor(() =>
      expect(listPermissionAudit).toHaveBeenLastCalledWith(expect.objectContaining({ userId: "u1", tenantId: "t1", actorId: "u2", page: 1 })),
    );
  });

  it("AC3: filtros sin coincidencias → estado vacío", async () => {
    vi.mocked(listPermissionAudit).mockResolvedValueOnce(page([record()])).mockResolvedValue(page([]));
    render(<PermissionAuditView mode="superadmin" users={users} />);
    await screen.findByRole("table");
    await pick("Usuario", "Ana Torres");
    expect(await screen.findByText("No hay registros de auditoría que coincidan con los filtros.")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("estado vacío sin filtros", async () => {
    vi.mocked(listPermissionAudit).mockResolvedValue(page([]));
    render(<PermissionAuditView mode="superadmin" users={users} />);
    expect(await screen.findByText("Aún no hay registros de auditoría de permisos individuales.")).toBeInTheDocument();
  });

  it("muestra los rechazos con su motivo en español", async () => {
    vi.mocked(listPermissionAudit).mockResolvedValue(
      page([record({ outcome: "REJECTED", reasonCode: "OUT_OF_CAP", valueAfter: null })]),
    );
    render(<PermissionAuditView mode="superadmin" users={users} />);
    expect(await screen.findByText("Rechazado: Ese permiso queda fuera de lo que puedes otorgar.")).toBeInTheDocument();
  });

  it("estado cargando y estado error con reintento", async () => {
    vi.mocked(listPermissionAudit).mockReturnValueOnce(new Promise(() => {}));
    const { unmount } = render(<PermissionAuditView mode="superadmin" users={users} />);
    expect(screen.getByText("Cargando auditoría de permisos…")).toBeInTheDocument();
    unmount();

    vi.mocked(listPermissionAudit).mockRejectedValueOnce(new Error("boom"));
    render(<PermissionAuditView mode="superadmin" users={users} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("No se pudo cargar la auditoría de permisos.");
    await userEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("table")).toBeInTheDocument();
  });

  it("pagina en el servidor con «Filas por página»", async () => {
    vi.mocked(listPermissionAudit).mockResolvedValue(page([record()], 45));
    render(<PermissionAuditView mode="superadmin" users={users} />);
    await screen.findByRole("table");
    await userEvent.click(await screen.findByRole("button", { name: /^2$|Página 2/ }));
    await waitFor(() => expect(listPermissionAudit).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2, pageSize: 10 })));
  });

  it("muestra nombres legibles, nunca códigos internos de permiso, módulo, acción o motivo", async () => {
    vi.mocked(listPermissionAudit).mockResolvedValue(
      page([
        record({ id: "a1", permissionSlug: "traspasos.read", permissionName: "Ver trámites de Traspasos", moduleCode: "tramites-traspasos", moduleName: "Trámites: Traspasos" }),
        record({ id: "a2", permissionSlug: null, moduleCode: "ot-mandatos", moduleName: "OT: Mandatos", action: "MODULE_SNAPSHOT", outcome: "REJECTED", reasonCode: "OT_NOT_APPLICABLE" }),
        record({ id: "a3", action: "ALGO_NUEVO", outcome: "REJECTED", reasonCode: "MOTIVO_DESCONOCIDO" }),
      ]),
    );
    render(<PermissionAuditView mode="superadmin" users={users} />);
    const table = await screen.findByRole("table");
    const [, r1, r2, r3] = within(table).getAllByRole("row");

    expect(within(r1).getByText("Ver trámites de Traspasos")).toBeInTheDocument();
    expect(within(r1).getByText("Trámites: Traspasos")).toBeInTheDocument();
    expect(within(r2).getByText("Módulo completo: OT: Mandatos")).toBeInTheDocument();
    expect(within(r2).getByText("Cambió el módulo completo")).toBeInTheDocument();
    expect(within(r2).getByText("Rechazado: Los permisos del organismo de tránsito no aplican a usuarios de una compañía.")).toBeInTheDocument();
    expect(within(r3).getByText("Cambió un permiso")).toBeInTheDocument();
    expect(within(r3).getByText("Rechazado")).toBeInTheDocument();
    expect(table).not.toHaveTextContent(/traspasos\.read|tramites-traspasos|MODULE_SNAPSHOT|OT_NOT_APPLICABLE|ALGO_NUEVO|MOTIVO_DESCONOCIDO/);
  });

  it("modo admin: sin filtro ni columna de compañía y sin tenantId en la consulta", async () => {
    render(<PermissionAuditView mode="admin" users={users} />);
    await screen.findByRole("table");
    expect(screen.queryByRole("combobox", { name: "Compañía" })).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Compañía" })).not.toBeInTheDocument();
    expect(listPermissionAudit).toHaveBeenLastCalledWith(expect.objectContaining({ tenantId: undefined }));
  });
});
