// Reporte detallado: la columna «Persona» se reemplaza por Persona, Validación, Intentos y Motivo
// de rechazo, alineadas por actor.
import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";

import { DetailedReportGrid } from "@/components/atom/modules/_reportesDetallados/DetailedReportGrid";
import type { DetailedReportPage, DetailedProcedureRow } from "@/lib/api/detailed-report";

function item(over: Partial<DetailedProcedureRow> = {}): DetailedProcedureRow {
  return {
    id: "t1",
    referenceNumber: "R-1",
    procedureTypeName: "Traspaso",
    category: "traspasos",
    status: "aprobado",
    createdByDisplayName: "Ana",
    submittedAt: null,
    completedAt: null,
    personDocument: "123",
    personFullName: "Persona Vieja",
    hasTransformation: false,
    transformationDetail: null,
    isLeasing: false,
    paymentType: "",
    transferType: "",
    ...over,
  };
}

function pagina(items: DetailedProcedureRow[]): DetailedReportPage {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 50,
    summary: { totalCount: items.length, byStatus: [], byCategory: [], byProcedureType: [] },
  };
}

function grid(data: DetailedReportPage) {
  return render(
    <DetailedReportGrid data={data} uiStatus="ready" page={1} onPageChange={() => {}} />,
  );
}

describe("DetailedReportGrid — validación de identidad", () => {
  it("muestra las cuatro columnas y una línea por actor", () => {
    grid(
      pagina([
        item({
          actores: [
            { rol: "comprador", ordinal: 1, nombre: "Ana Pérez", tipoDocumento: "CC", documento: "10", estadoValidacion: "aprobado", motivoRechazo: null, intentos: 1 },
            { rol: "vendedor", ordinal: 1, nombre: "Luis Gil", tipoDocumento: "CC", documento: "20", estadoValidacion: "rechazado", motivoRechazo: "Documento ilegible", intentos: 3 },
          ],
        }),
      ]),
    );
    for (const h of ["Persona", "Validación", "Intentos", "Motivo de rechazo"]) {
      expect(screen.getByRole("columnheader", { name: h })).toBeTruthy();
    }
    const fila = screen.getAllByRole("row")[1];
    expect(within(fila).getAllByTestId("actor-persona")).toHaveLength(2);
    expect(within(fila).getAllByTestId("actor-validacion")).toHaveLength(2);
    expect(within(fila).getByText("Rechazado")).toBeTruthy();
    expect(within(fila).getByText("Documento ilegible")).toBeTruthy();
    expect(within(fila).queryByText("Persona Vieja")).toBeNull();
  });

  it("un backend anterior sin `actores` sigue mostrando la persona de siempre", () => {
    grid(pagina([item()]));
    expect(screen.getByText("Persona Vieja")).toBeTruthy();
  });
});
