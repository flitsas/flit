import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";

/**
 * HU #13916 — paginación estándar de toda tabla: saltar a la primera y a la última página, con los
 * extremos apagados cuando ya se está ahí, y «Filas por página» en 10/20/30/50 (por defecto 10).
 */

/** Tabla mínima paginada en cliente con el estado estándar: lo que monta cualquier pantalla real. */
function TablaDePrueba({ total }: { total: number }) {
  const pg = usePaginacion("prueba.hu13916");
  const filas = Array.from({ length: total }, (_, i) => `Fila ${i + 1}`);
  return (
    <div>
      <ul>
        {pg.paginar(filas).map((f) => (
          <li key={f}>{f}</li>
        ))}
      </ul>
      <Pagination
        page={pg.page}
        pageSize={pg.pageSize}
        totalCount={filas.length}
        onPageChange={pg.setPage}
        onPageSizeChange={pg.setPageSize}
      />
    </div>
  );
}

const boton = (nombre: string) => screen.getByRole("button", { name: nombre });

describe("HU #13916 — primera / última página", () => {
  it("AC1 — «Última página» lleva a la página 10 de 95 registros y el resumen lo dice", () => {
    sessionStorage.clear();
    render(<TablaDePrueba total={95} />);

    fireEvent.click(boton("Última página"));

    expect(boton("Página 10")).toHaveAttribute("aria-current", "page");
    expect(screen.getByText("Mostrando 91–95 de 95")).toBeInTheDocument();
    expect(screen.getByText("Fila 95")).toBeInTheDocument();
  });

  it("AC2 — «Primera página» vuelve a la página 1 desde la 7", () => {
    sessionStorage.clear();
    function EnLaSiete() {
      const [page, setPage] = useState(7);
      return <Pagination page={page} pageSize={10} totalCount={95} onPageChange={setPage} />;
    }
    render(<EnLaSiete />);

    fireEvent.click(boton("Primera página"));

    expect(boton("Página 1")).toHaveAttribute("aria-current", "page");
    expect(screen.getByText("Mostrando 1–10 de 95")).toBeInTheDocument();
  });

  it("AC3 — en la página 1 se apagan «Primera» y «Anterior»; en la última, «Siguiente» y «Última»", () => {
    const { rerender } = render(
      <Pagination page={1} pageSize={10} totalCount={95} onPageChange={vi.fn()} />,
    );
    expect(boton("Primera página")).toBeDisabled();
    expect(boton("Página anterior")).toBeDisabled();
    expect(boton("Página siguiente")).toBeEnabled();
    expect(boton("Última página")).toBeEnabled();

    rerender(<Pagination page={10} pageSize={10} totalCount={95} onPageChange={vi.fn()} />);
    expect(boton("Primera página")).toBeEnabled();
    expect(boton("Página anterior")).toBeEnabled();
    expect(boton("Página siguiente")).toBeDisabled();
    expect(boton("Última página")).toBeDisabled();
  });

  it("AC3 (negativo) — un extremo apagado no navega aunque se pulse", () => {
    const onPageChange = vi.fn();
    render(<Pagination page={1} pageSize={10} totalCount={95} onPageChange={onPageChange} />);

    fireEvent.click(boton("Primera página"));

    expect(onPageChange).not.toHaveBeenCalled();
  });
});

describe("HU #13916 — «Filas por página» 10/20/30/50", () => {
  it("AC4 — ofrece exactamente 10, 20, 30 y 50, con 10 por defecto", () => {
    sessionStorage.clear();
    render(<TablaDePrueba total={95} />);

    const selector = screen.getByRole("combobox", { name: "Filas por página" });
    expect(within(selector).getAllByRole("option").map((o) => o.textContent)).toEqual(["10", "20", "30", "50"]);
    expect(selector).toHaveValue("10");
  });

  it("AC5 — cambiar de 10 a 30 en la página 4 muestra 30 filas desde la página 1", () => {
    sessionStorage.clear();
    render(<TablaDePrueba total={95} />);
    fireEvent.click(boton("Página 2"));
    fireEvent.click(boton("Página 3"));
    fireEvent.click(boton("Página 4"));
    expect(screen.getByText("Mostrando 31–40 de 95")).toBeInTheDocument();

    fireEvent.change(screen.getByRole("combobox", { name: "Filas por página" }), { target: { value: "30" } });

    expect(screen.getByText("Mostrando 1–30 de 95")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(30);
    expect(boton("Página 1")).toHaveAttribute("aria-current", "page");
  });

  it("AC6 — con una sola página no hay navegación, pero el resumen y el selector siguen", () => {
    sessionStorage.clear();
    render(<TablaDePrueba total={8} />);

    expect(screen.getByText("Mostrando 1–8 de 8")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Filas por página" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Última página" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Primera página" })).not.toBeInTheDocument();
  });
});
