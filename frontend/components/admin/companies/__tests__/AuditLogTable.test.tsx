// AC5 — Historial de auditoría: columnas (Fecha, Campo, Valor anterior, Valor
// nuevo, Operador), orden DESC preservado y rediseño legible (etiquetas, badges, detalle, filtros).
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AuditLogTable } from "../AuditLogTable";
import { humanizeKey, parseAuditValue } from "../auditLogLabels";
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
    entityName: "tenant_operational_policies",
    fieldName: "generate_improntas",
    oldValue: "false",
    newValue: "true",
    changedBy: null,
    changedAt: "2026-03-01T09:00:00Z",
  },
  {
    entityName: "tenant_settings",
    fieldName: "consultation_provider_config",
    oldValue: "{}",
    newValue: '{"vehicle_plate":{"primary":"runt","fallback":["x"]}}',
    changedBy: "bbbbbbbb-1111-2222-3333-444444444444",
    changedAt: "2026-02-20T09:00:00Z",
  },
];

function renderTable(props: Partial<React.ComponentProps<typeof AuditLogTable>> = {}) {
  return render(
    <AuditLogTable entries={entries} totalCount={3} page={1} pageSize={10} onPageChange={vi.fn()} {...props} />,
  );
}

describe("AuditLogTable (AC5)", () => {
  it("renderiza las 5 columnas requeridas", () => {
    renderTable();
    expect(screen.getByText("Fecha")).toBeInTheDocument();
    expect(screen.getByText("Campo modificado")).toBeInTheDocument();
    expect(screen.getByText("Valor anterior")).toBeInTheDocument();
    expect(screen.getByText("Valor nuevo")).toBeInTheDocument();
    expect(screen.getByText("Operador")).toBeInTheDocument();
  });

  it("preserva el orden DESC recibido y muestra etiquetas legibles", () => {
    renderTable();
    const rows = screen.getAllByRole("row");
    expect(within(rows[1]).getByText(/organismo de tránsito/i)).toBeInTheDocument();
    expect(within(rows[2]).getByText("Generación de improntas")).toBeInTheDocument();
    expect(screen.queryByText(/tenant_operational_policies\.generate_improntas/)).not.toBeInTheDocument();
    // El nombre técnico sigue disponible como tooltip.
    expect(within(rows[2]).getByTitle("tenant_operational_policies.generate_improntas")).toBeInTheDocument();
  });

  it("pinta los booleanos como badges con texto", () => {
    renderTable();
    const row = screen.getAllByRole("row")[2];
    expect(within(row).getByText("Deshabilitada")).toBeInTheDocument();
    expect(within(row).getByText("Habilitada")).toBeInTheDocument();
  });

  it("muestra '—' para valores nulos y operador ausente, y el operador con tooltip", () => {
    renderTable();
    const rows = screen.getAllByRole("row");
    expect(within(rows[1]).getAllByText("—").length).toBeGreaterThan(0);
    expect(within(rows[2]).getByLabelText("Sin operador")).toBeInTheDocument();
    expect(within(rows[1]).getByLabelText("Usuario aaaaaaaa-1111-2222-3333-444444444444")).toBeInTheDocument();
  });

  it("el JSON largo queda detrás de «Ver detalle» (modal)", async () => {
    renderTable();
    await userEvent.click(screen.getByRole("button", { name: /ver detalle/i }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("Detalle del cambio")).toBeInTheDocument();
    expect(within(dialog).getByText("tenant_settings.consultation_provider_config")).toBeInTheDocument();
    expect(within(dialog).getByText(/"primary": "runt"/)).toBeInTheDocument();
    await userEvent.click(within(dialog).getAllByRole("button", { name: "Cerrar" })[1]);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("filtra por texto y por tipo de cambio sobre la página cargada", async () => {
    renderTable();
    await userEvent.type(screen.getByLabelText("Buscar en el historial"), "improntas");
    expect(screen.getAllByRole("row")).toHaveLength(2);
    await userEvent.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(screen.getAllByRole("row")).toHaveLength(4);

    await userEvent.selectOptions(screen.getByLabelText("Tipo de cambio"), "Proveedores de consulta");
    expect(screen.getAllByRole("row")).toHaveLength(2);
  });

  it("muestra el vacío de filtros cuando nada coincide", async () => {
    renderTable();
    await userEvent.type(screen.getByLabelText("Buscar en el historial"), "zzzz-no-existe");
    expect(screen.getByText(/ningún cambio de esta página coincide/i)).toBeInTheDocument();
  });

  it("dispara onPageChange con la página siguiente y ofrece «Filas por página»", () => {
    const onPageChange = vi.fn();
    renderTable({ totalCount: 25, pageSize: 10, onPageChange, onPageSizeChange: vi.fn() });
    fireEvent.click(screen.getByRole("button", { name: /página siguiente/i }));
    expect(onPageChange).toHaveBeenCalledWith(2);
    expect(screen.getByText(/filas por página/i)).toBeInTheDocument();
  });
});

describe("auditLogLabels", () => {
  it("humaniza claves desconocidas", () => {
    expect(humanizeKey("some_new_field")).toBe("Some new field");
  });

  it("interpreta booleanos, textos y objetos", () => {
    expect(parseAuditValue("true", "only_own_vehicles")).toMatchObject({ kind: "boolean", positive: "Sí" });
    expect(parseAuditValue('"internal"', "fines_query_source")).toEqual({ kind: "text", text: "Interna" });
    expect(parseAuditValue('{"a":1}', "x").kind).toBe("complex");
    expect(parseAuditValue(null, "x").kind).toBe("empty");
  });
});
