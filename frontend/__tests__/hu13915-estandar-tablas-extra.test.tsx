import { useState } from "react";
import { Eye } from "lucide-react";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { DataTable } from "@/components/atom/DataTable";
import { RowActions } from "@/components/atom/RowActions";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { TABLA_HEADER_BG } from "@/components/atom/table-styles";
import { PasoDatos } from "@/app/verificacion/[token]/_components/PasoDatos";
import { RENDERED_CONSENT_TEXT_VERSION } from "@/lib/captura-manual/consent";
import type { ManualCaptureClient, ManualCaptureView } from "@/lib/captura-manual/types";

/**
 * Feature #13915 — escenarios comunes a las HU de migración (#13918, #13919, #13923) que no dependen
 * de una pantalla concreta: el filtro que achica el resultado, una acción de fila en la página 2 y
 * la ficha de pares fijos del portal, que lleva el diseño pero no la paginación.
 *
 * Uso de ejemplo: una tabla migrada monta `usePaginacion("<clave>")` + `DataTable` con `pagination`.
 */

type Fila = { id: string; nombre: string; grupo: "A" | "B" };
/** 35 registros: los 4 primeros son del grupo A, el resto del B. */
const filas = (n: number): Fila[] =>
  Array.from({ length: n }, (_, i) => ({ id: `f${i + 1}`, nombre: `Registro ${i + 1}`, grupo: i < 4 ? "A" : "B" }));

/** Tabla migrada tal como la dejan las HU: DataTable + estado estándar + filtro + acción por fila. */
function TablaMigrada({ onVer }: { onVer: (f: Fila) => void }) {
  const [filtro, setFiltro] = useState("");
  const pg = usePaginacion("prueba.hu13915");
  const visibles = filas(35).filter((f) => !filtro || f.grupo === filtro);
  return (
    <div>
      <input aria-label="Filtro" value={filtro} onChange={(e) => setFiltro(e.target.value)} />
      <DataTable
        ariaLabel="Registros"
        rows={pg.paginar(visibles)}
        getRowKey={(f) => f.id}
        columns={[
          { key: "nombre", header: "Nombre", render: (f) => f.nombre },
          {
            key: "acciones",
            header: "Acciones",
            render: (f) => <RowActions actions={[{ icon: Eye, label: `Ver ${f.nombre}`, onClick: () => onVer(f) }]} />,
          },
        ]}
        pagination={{
          page: pg.page,
          pageSize: pg.pageSize,
          totalCount: visibles.length,
          onPageChange: pg.setPage,
          onPageSizeChange: pg.setPageSize,
        }}
      />
    </div>
  );
}

describe("Feature #13915 — tablas migradas", () => {
  it("#13918 AC4 / #13923 AC6 — un filtro que deja 4 resultados en la página 3 muestra esos 4, sin tabla vacía", () => {
    sessionStorage.clear();
    render(<TablaMigrada onVer={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Página 2" }));
    fireEvent.click(screen.getByRole("button", { name: "Página 3" }));
    expect(screen.getByText("Mostrando 21–30 de 35")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Filtro"), { target: { value: "A" } });

    const tabla = screen.getByRole("table", { name: "Registros" });
    expect(within(tabla).getAllByRole("row")).toHaveLength(1 + 4);
    expect(screen.getByText("Registro 1")).toBeInTheDocument();
    expect(screen.getByText("Mostrando 1–4 de 4")).toBeInTheDocument();
  });

  it("#13918 AC5 — una acción de fila en la página 2 actúa sobre ESE registro y la tabla sigue en la página 2", () => {
    sessionStorage.clear();
    const onVer = vi.fn();
    render(<TablaMigrada onVer={onVer} />);
    fireEvent.click(screen.getByRole("button", { name: "Página 2" }));

    fireEvent.click(screen.getByRole("button", { name: "Ver Registro 14" }));

    expect(onVer).toHaveBeenCalledWith({ id: "f14", nombre: "Registro 14", grupo: "B" });
    expect(screen.getByRole("button", { name: "Página 2" })).toHaveAttribute("aria-current", "page");
  });

  it("#13919 AC5 — la ficha «Tus datos» del portal usa la cabecera del modelo y no se pagina", () => {
    const view: ManualCaptureView = {
      fullName: "Persona de Prueba",
      documentType: "CC",
      documentNumber: "1000000000",
      productName: "FLIT 2.0",
      expiresAt: "2030-01-01T00:00:00Z",
      consentTextVersion: RENDERED_CONSENT_TEXT_VERSION,
    };
    const client: ManualCaptureClient = { getManualCapture: vi.fn(), postConsent: vi.fn(), submit: vi.fn() };
    render(<PasoDatos token="tok" view={view} client={client} onDone={vi.fn()} />);

    const ficha = screen.getByRole("table", { name: "Tus datos" });
    const cabecera = within(ficha).getByRole("columnheader", { name: "Tus datos" });
    expect(cabecera).toHaveStyle({ background: TABLA_HEADER_BG });
    expect(ficha.className).toContain("border-separate");
    expect(within(ficha).getByRole("rowheader", { name: "Nombre" })).toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Filas por página" })).not.toBeInTheDocument();
    expect(screen.queryByRole("navigation", { name: /paginación/i })).not.toBeInTheDocument();
  });
});
