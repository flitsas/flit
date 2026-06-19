// AC5 — Historial de auditoría: columnas (Fecha, Campo, Valor anterior, Valor
// nuevo, Operador) y orden DESC preservado del backend.
//
// Uso de ejemplo:
//   render(<AuditLogTable entries={entries} totalCount={25} page={1} pageSize={20} ... />);
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { AuditLogTable } from "../AuditLogTable";
import type { AuditLogEntry } from "@/lib/api/types";

const entries: AuditLogEntry[] = [
  {
    entityName: "tenant_transit_office_grants",
    fieldName: "transit_office_id",
    oldValue: null,
    newValue: '"office-24"',
    changedBy: "aaaaaaaa-1111-2222-3333-444444444444",
    changedAt: "2026-03-02T12:00:00Z",
  },
  {
    entityName: "tenant_settings",
    fieldName: "only_own_vehicles",
    oldValue: "true",
    newValue: "false",
    changedBy: null,
    changedAt: "2026-03-01T09:00:00Z",
  },
];

describe("AuditLogTable (AC5)", () => {
  it("renderiza las 5 columnas requeridas", () => {
    render(
      <AuditLogTable entries={entries} totalCount={2} page={1} pageSize={20} onPageChange={vi.fn()} />,
    );
    expect(screen.getByText("Fecha")).toBeInTheDocument();
    expect(screen.getByText("Campo modificado")).toBeInTheDocument();
    expect(screen.getByText("Valor anterior")).toBeInTheDocument();
    expect(screen.getByText("Valor nuevo")).toBeInTheDocument();
    expect(screen.getByText("Operador")).toBeInTheDocument();
  });

  it("preserva el orden DESC recibido (más reciente primero)", () => {
    render(
      <AuditLogTable entries={entries} totalCount={2} page={1} pageSize={20} onPageChange={vi.fn()} />,
    );
    const rows = screen.getAllByRole("row");
    // rows[0] es el header; rows[1] es la entrada más reciente.
    expect(within(rows[1]).getByText(/tenant_transit_office_grants\.transit_office_id/)).toBeInTheDocument();
    expect(within(rows[2]).getByText(/tenant_settings\.only_own_vehicles/)).toBeInTheDocument();
  });

  it("muestra '—' para valores y operador nulos", () => {
    render(
      <AuditLogTable entries={entries} totalCount={2} page={1} pageSize={20} onPageChange={vi.fn()} />,
    );
    const rows = screen.getAllByRole("row");
    // La segunda entrada tiene changedBy null → operador "—".
    expect(within(rows[2]).getAllByText("—").length).toBeGreaterThan(0);
  });

  it("dispara onPageChange con la página siguiente", () => {
    const onPageChange = vi.fn();
    render(
      <AuditLogTable entries={entries} totalCount={25} page={1} pageSize={20} onPageChange={onPageChange} />,
    );
    fireEvent.click(screen.getByRole("button", { name: /página siguiente/i }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });
});
