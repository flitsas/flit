import { useState } from "react";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { ClientProceduresTable } from "../ClientProceduresTable";
import { useColumnWidths } from "@/components/atom/ColumnResizeHandle";
import type { OtClientProcedure } from "@/lib/api/types-ot";

// HU #14741 — la bandeja del OT lleva tirador de ancho en cada cabecera de columna.

beforeAll(() => {
  if (typeof window.PointerEvent === "undefined") {
    class PointerEventPoly extends MouseEvent {
      pointerId: number;
      constructor(type: string, init: PointerEventInit = {}) {
        super(type, init);
        this.pointerId = init.pointerId ?? 1;
      }
    }
    (window as unknown as { PointerEvent: unknown }).PointerEvent = PointerEventPoly;
  }
});

const ROW: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "tenant-1",
  procedureTypeId: "tipo-1",
  procedureTypeName: "Matrícula inicial",
  clientTenantName: "Empresa Demo",
  referenceNumber: "FT1-0000230",
  status: "borrador",
  createdAt: "2026-10-28T15:21:00Z",
  gestorNombre: "Gestor De Prueba",
  placa: "ABC123",
};

const KEYS = ["radicado", "vin", "placa", "vendedor", "comprador", "tipoTramite", "empresaGestor", "estado", "fechaRadicacion"];

function Montaje({ onSave }: { onSave: (k: string, px: number) => void }) {
  const [saved, setSaved] = useState<Record<string, number>>({});
  const columnWidths = useColumnWidths(
    saved,
    KEYS,
    (k, px) => {
      onSave(k, px);
      setSaved((s) => ({ ...s, [k]: px }));
    },
    () => setSaved({}),
  );
  return (
    <ClientProceduresTable
      rows={[ROW]}
      totalCount={1}
      page={1}
      pageSize={20}
      onPageChange={vi.fn()}
      onPageSizeChange={vi.fn()}
      onApprove={vi.fn()}
      onReject={vi.fn()}
      showApprovalActions={false}
      onSortChange={vi.fn()}
      columnWidths={columnWidths}
    />
  );
}

describe("ClientProceduresTable — ancho de columnas (HU #14741)", () => {
  it("cada columna tiene su tirador; Acciones no", () => {
    render(<Montaje onSave={vi.fn()} />);
    expect(screen.getAllByRole("separator")).toHaveLength(KEYS.length);
    expect(screen.getByRole("separator", { name: "Ancho de Placa" })).toBeInTheDocument();
  });

  it("AC1: arrastrar cambia el ancho de la cabecera y de las celdas, y guarda al soltar", () => {
    const onSave = vi.fn();
    render(<Montaje onSave={onSave} />);
    const t = screen.getByRole("separator", { name: "Ancho de Placa" });
    fireEvent.pointerDown(t, { clientX: 10, button: 0, pointerId: 1 });
    fireEvent.pointerMove(t, { clientX: 110, pointerId: 1 });
    const th = t.closest("th")!;
    expect(th.style.width).toBe("240px");
    fireEvent.pointerUp(t, { clientX: 110, pointerId: 1 });
    expect(onSave).toHaveBeenCalledWith("placa", 240);
    const idx = Array.from(th.parentElement!.children).indexOf(th);
    const td = screen.getAllByRole("row")[1].children[idx] as HTMLElement;
    expect(td.style.width).toBe("240px");
  });

  it("AC4: con teclado ←/→ cambia aria-valuenow", () => {
    render(<Montaje onSave={vi.fn()} />);
    const t = screen.getByRole("separator", { name: "Ancho de Placa" });
    fireEvent.keyDown(t, { key: "ArrowRight" });
    expect(t).toHaveAttribute("aria-valuenow", "156");
  });
});
