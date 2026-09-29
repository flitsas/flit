import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Pencil, Settings2 } from "lucide-react";
import { RowActions } from "@/components/atom/RowActions";
import { Pagination } from "@/components/atom/Pagination";

/**
 * HU #10495 — DataTable unificada: acciones (RowActions, iconos sin texto — D2) y
 * paginación (Pagination centrada, "Mostrando X–Y de N").
 */

const FE_ROOT = path.resolve(__dirname, "..");
const read = (rel: string) => readFileSync(path.join(FE_ROOT, rel), "utf8");

describe("HU #10495 — RowActions (iconos sin texto, D2)", () => {
  it("happy path: cada acción es un botón de solo icono con aria-label y title", () => {
    const onClick = vi.fn();
    render(<RowActions actions={[{ icon: Pencil, label: "Editar FLIT SAS", onClick }]} />);
    const btn = screen.getByRole("button", { name: "Editar FLIT SAS" });
    expect(btn).toHaveAttribute("title", "Editar FLIT SAS");
    expect(btn.textContent).toBe(""); // sin texto visible, solo el icono (svg)
    fireEvent.click(btn);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("contrato: soporta varias acciones y conserva el nombre accesible de cada una", () => {
    render(
      <RowActions
        actions={[
          { icon: Pencil, label: "Editar", onClick: vi.fn() },
          { icon: Settings2, label: "Configurar", onClick: vi.fn(), tone: "primary" },
        ]}
      />,
    );
    expect(screen.getByRole("button", { name: "Editar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Configurar" })).toBeInTheDocument();
  });

  it("edge: una acción deshabilitada no dispara onClick y usa disabledTitle", () => {
    const onClick = vi.fn();
    render(
      <RowActions
        actions={[
          { icon: Pencil, label: "Editar", onClick, disabled: true, disabledTitle: "No editable" },
        ]}
      />,
    );
    const btn = screen.getByRole("button", { name: "Editar" });
    expect(btn).toBeDisabled();
    expect(btn).toHaveAttribute("title", "No editable");
    fireEvent.click(btn);
    expect(onClick).not.toHaveBeenCalled();
  });
});

// Bug #13055 — la paginación estándar es la del listado de trámites: «Filas por página» a la
// izquierda y el conteo con la navegación numerada a la derecha (ya no va centrada).
describe("HU #10495 / Bug #13055 — Pagination estándar ('Mostrando X–Y de N' + navegación numerada)", () => {
  it("happy path: muestra el conteo y los controles cuando hay más de una página", () => {
    const onPageChange = vi.fn();
    render(<Pagination page={1} pageSize={10} totalCount={25} onPageChange={onPageChange} />);
    expect(screen.getByText(/Mostrando 1–10 de 25/)).toBeInTheDocument();
    const nav = screen.getByRole("navigation", { name: /paginación/i });
    expect(nav.className).toContain("justify-end");
    expect(screen.getByRole("button", { name: "Página 2" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /página siguiente/i }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it("contrato: 'Anterior' deshabilitado en la primera página", () => {
    render(<Pagination page={1} pageSize={10} totalCount={25} onPageChange={vi.fn()} />);
    expect(screen.getByRole("button", { name: /página anterior/i })).toBeDisabled();
  });

  it("con onPageSizeChange muestra «Filas por página» (10/25/50/100) y el cambio llega al manejador", () => {
    const onPageSizeChange = vi.fn();
    render(
      <Pagination page={1} pageSize={10} totalCount={25} onPageChange={vi.fn()} onPageSizeChange={onPageSizeChange} />,
    );
    const select = screen.getByRole("combobox", { name: "Filas por página" });
    expect([...(select as HTMLSelectElement).options].map((o) => o.value)).toEqual(["10", "25", "50", "100"]);
    fireEvent.change(select, { target: { value: "25" } });
    expect(onPageSizeChange).toHaveBeenCalledWith(25);
  });

  it("edge: una sola página muestra el conteo pero oculta los controles de navegación", () => {
    render(<Pagination page={1} pageSize={10} totalCount={5} onPageChange={vi.fn()} />);
    expect(screen.getByText(/Mostrando 1–5 de 5/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /página siguiente/i })).not.toBeInTheDocument();
  });
});

describe("HU #10495 — unificación en las tablas", () => {
  const files = [
    "components/admin/companies/CompanyListTable.tsx",
    "components/admin/documents/DocumentTypeListTable.tsx",
    "components/admin/improntas/ImprontaHistorialTable.tsx",
    "components/admin/transit-offices/TransitOfficesList.tsx",
    "components/admin/transit-offices/ClientProceduresTable.tsx",
    "components/admin/transit-offices/WebhooksSection.tsx",
  ];

  it("las tablas migradas usan RowActions y/o la Pagination compartida", () => {
    for (const f of files) {
      const src = read(f);
      expect(src).toMatch(/RowActions|Pagination/);
    }
  });

  it("OtTablePagination delega en el componente Pagination unificado", () => {
    const ot = read("components/admin/transit-offices/OtTablePagination.tsx");
    expect(ot).toMatch(/import \{ Pagination \}/);
    expect(ot).toMatch(/<Pagination\s/);
  });

  it("no quedan botones de acción con texto en las tablas migradas (D2: solo icono)", () => {
    // Ya no debe existir el patrón de botón con texto visible "Editar/Configurar/Administrar/Aprobar".
    expect(read("components/admin/transit-offices/TransitOfficesList.tsx")).not.toMatch(/>\s*Administrar\s*</);
    expect(read("components/admin/transit-offices/ClientProceduresTable.tsx")).not.toMatch(/>\s*Aprobar\s*</);
  });
});
