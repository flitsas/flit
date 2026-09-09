// HU-01 (Feature #12201) — CF-17/CF-21/CF-22 en la tabla del historial.
// Uso de ejemplo: <HistorialTable rows={rows} totalCount={2} page={1} pageSize={20} onPageChange={fn} />
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { StandaloneDocumentListItem } from "@/lib/api/types-generacion-documental";
import { HistorialTable } from "../HistorialTable";

const rows: StandaloneDocumentListItem[] = [
  {
    id: "11111111-1111-1111-1111-111111111111",
    documentType: "certificado_rues",
    scenario: null,
    status: "generated",
    companyName: "Renting Demo S.A.S.",
    createdByUserName: "Ana Gestora",
    createdAt: "2026-09-01T14:30:00.000Z",
  },
  {
    id: "22222222-2222-2222-2222-222222222222",
    documentType: "transferencia_dominio_generada",
    scenario: "A",
    status: "processing",
    companyName: "Renting Demo S.A.S.",
    createdByUserName: "Ana Gestora",
    createdAt: "2026-09-02T09:00:00.000Z",
  },
  {
    id: "33333333-3333-3333-3333-333333333333",
    documentType: "certificado_rues",
    scenario: null,
    status: "pending",
    companyName: null,
    createdByUserName: null,
    createdAt: "2026-09-03T09:00:00.000Z",
  },
  {
    id: "44444444-4444-4444-4444-444444444444",
    documentType: "certificado_rues",
    scenario: null,
    status: "error",
    errorCode: "rues_not_found",
    companyName: "Renting Demo S.A.S.",
    createdByUserName: "Ana Gestora",
    createdAt: "2026-09-04T09:00:00.000Z",
  },
];

function renderTable(props: Partial<React.ComponentProps<typeof HistorialTable>> = {}) {
  return render(
    <HistorialTable
      rows={rows}
      totalCount={rows.length}
      page={1}
      pageSize={20}
      onPageChange={vi.fn()}
      {...props}
    />,
  );
}

describe("HistorialTable", () => {
  it("CF-17: muestra tipo, escenario, empresa, usuario, fecha y resultado", () => {
    renderTable();
    const headers = screen.getAllByRole("columnheader").map((h) => h.textContent);
    expect(headers).toEqual(["Tipo", "Escenario", "Empresa", "Usuario", "Fecha", "Resultado", "Acciones"]);
    expect(screen.getAllByText("Certificado RUES").length).toBeGreaterThan(0);
    expect(screen.getByText("Transferencia de dominio")).toBeInTheDocument();
    expect(screen.getAllByText("Renting Demo S.A.S.").length).toBe(3);
  });

  it("CF-21: pending y processing se pintan ambos como «En proceso»", () => {
    renderTable();
    expect(screen.getAllByText("En proceso")).toHaveLength(2);
    expect(screen.getByText("Generado")).toBeInTheDocument();
    expect(screen.getByText("Error")).toBeInTheDocument();
  });

  it("CF-22: el resultado lleva texto accesible, no solo color", () => {
    renderTable();
    expect(screen.getByLabelText("Estado: Generado")).toBeInTheDocument();
    expect(screen.getAllByLabelText("Estado: En proceso")).toHaveLength(2);
  });

  it("nunca expone contenido de document_snapshot (el contrato del listado no lo trae)", () => {
    const { container } = renderTable();
    expect(container.textContent).not.toMatch(/snapshot/i);
  });

  it("solo ofrece descarga en filas 'generated' y con handler; el botón tiene nombre accesible", async () => {
    const onDownload = vi.fn();
    renderTable({ onDownload });

    const buttons = screen.getAllByRole("button", { name: /^Descargar / });
    expect(buttons).toHaveLength(1);
    await userEvent.click(buttons[0]);
    expect(onDownload).toHaveBeenCalledWith(expect.objectContaining({ id: rows[0].id }));
  });

  it("sin handler de descarga no se ofrece la acción (HU-03)", () => {
    renderTable();
    expect(screen.queryByRole("button", { name: /^Descargar / })).not.toBeInTheDocument();
  });

  it("no lanza con lista vacía y muestra el conteo en 0", () => {
    expect(() => render(
      <HistorialTable rows={[]} totalCount={0} page={1} pageSize={20} onPageChange={vi.fn()} />,
    )).not.toThrow();
    const nav = screen.getByRole("navigation", { name: "Paginación" });
    expect(within(nav).getByText(/de 0/)).toBeInTheDocument();
  });

  it("tolera empresa y usuario nulos sin romper la fila", () => {
    renderTable();
    expect(screen.getAllByText("—").length).toBeGreaterThan(0);
  });
});
