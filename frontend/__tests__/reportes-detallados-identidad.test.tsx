// Reporte detallado: la columna «Persona» se reemplaza por Persona, Validación, Intentos y Motivo
// de rechazo, alineadas por actor.
import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";

import { DetailedReportGrid } from "@/components/atom/modules/_reportesDetallados/DetailedReportGrid";
import type { ActorValidacionDto } from "@/lib/api/company-queries";
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

const OCHO = [
  "Comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador",
  "Vendedor", "Validación vendedor", "Intentos vendedor", "Motivo rechazo vendedor",
];

function actor(
  rol: "comprador" | "vendedor" | "propietario",
  ordinal: number,
  over: Partial<ActorValidacionDto> = {},
): ActorValidacionDto {
  return {
    rol, ordinal, nombre: `${rol} ${ordinal}`, tipoDocumento: "CC", documento: `${ordinal}0`,
    estadoValidacion: "aprobado", motivoRechazo: null, intentos: 1, ...over,
  };
}

describe("DetailedReportGrid — columnas por rol", () => {
  it("muestra las 8 columnas juntas, después de Canal, en lugar de Persona", () => {
    grid(pagina([item({ actores: [actor("comprador", 1), actor("vendedor", 1)] })]));
    const cabeceras = screen.getAllByRole("columnheader").map((h) => h.textContent);
    const i = cabeceras.indexOf("Canal");
    expect(cabeceras.slice(i + 1, i + 9)).toEqual(OCHO);
    expect(cabeceras).not.toContain("Persona");
    expect(cabeceras).not.toContain("Motivo de rechazo");
  });

  it("varios compradores en la misma celda, uno por línea, alineados con el vendedor", () => {
    grid(
      pagina([
        item({
          actores: [
            actor("comprador", 1, { nombre: "Ana Pérez" }),
            actor("comprador", 2, { estadoValidacion: "rechazado", intentos: 3, motivoRechazo: "El rostro no coincide con el documento." }),
            actor("vendedor", 1, { nombre: "Luis Gil" }),
          ],
        }),
      ]),
    );
    const fila = screen.getAllByRole("row")[1];
    expect(within(fila).getAllByTestId("actor-persona")).toHaveLength(3);
    expect(within(fila).getAllByTestId("actor-validacion")).toHaveLength(3);
    expect(within(fila).getByText("Ana Pérez")).toBeTruthy();
    expect(within(fila).getByText("Luis Gil")).toBeTruthy();
    expect(within(fila).getByText("Rechazado")).toBeTruthy();
    expect(within(fila).getByText("El rostro no coincide con el documento.")).toBeTruthy();
  });

  it("matrícula: el propietario va en Comprador y Vendedor dice «—» en sus 4 columnas", () => {
    grid(pagina([item({ actores: [actor("propietario", 1, { nombre: "Dueño Uno" })] })]));
    const fila = screen.getAllByRole("row")[1];
    expect(within(fila).getByText("Dueño Uno")).toBeTruthy();
    const celdas = within(fila).getAllByRole("cell");
    const cabeceras = screen.getAllByRole("columnheader").map((h) => h.textContent);
    const v = cabeceras.indexOf("Vendedor");
    expect(celdas.slice(v, v + 4).map((c) => c.textContent)).toEqual(["—", "—", "—", "—"]);
  });

  it.each([[undefined], [null], [[]]])(
    "un backend anterior (actores = %s) sigue mostrando la persona de siempre",
    (actores) => {
      grid(pagina([item({ actores: actores as ActorValidacionDto[] | null | undefined })]));
      expect(screen.getByText("Persona Vieja")).toBeTruthy();
    },
  );
});
