import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";

/**
 * HU #13917 — cada tabla recuerda su «Filas por página» mientras dure la sesión del navegador
 * (`sessionStorage`, una clave por tabla). Desmontar y volver a montar es lo que ocurre al navegar a
 * otro módulo y regresar.
 */

function Tabla({ clave }: { clave: string }) {
  const pg = usePaginacion(clave);
  return (
    <Pagination
      page={pg.page}
      pageSize={pg.pageSize}
      totalCount={200}
      onPageChange={pg.setPage}
      onPageSizeChange={pg.setPageSize}
      ariaLabel={`Paginación de ${clave}`}
    />
  );
}

const selector = () => screen.getByRole("combobox", { name: "Filas por página" });
const elegir = (n: string) => fireEvent.change(selector(), { target: { value: n } });

afterEach(() => {
  vi.restoreAllMocks();
  sessionStorage.clear();
});

describe("HU #13917 — «Filas por página» recordado en la sesión", () => {
  it("AC1 — se conserva al salir de Usuarios y volver, en la página 1", () => {
    const { unmount } = render(<Tabla clave="usuarios.lista" />);
    fireEvent.click(screen.getByRole("button", { name: "Página 2" }));
    elegir("50");
    unmount();

    render(<Tabla clave="usuarios.lista" />);

    expect(selector()).toHaveValue("50");
    expect(screen.getByText("Mostrando 1–50 de 200")).toBeInTheDocument();
  });

  it("AC2 — cada tabla recuerda lo suyo", () => {
    const usuarios = render(<Tabla clave="usuarios.lista" />);
    elegir("50");
    usuarios.unmount();
    const auditoria = render(<Tabla clave="auditoria.lista" />);
    elegir("20");
    auditoria.unmount();

    const otraVezUsuarios = render(<Tabla clave="usuarios.lista" />);
    expect(selector()).toHaveValue("50");
    otraVezUsuarios.unmount();
    render(<Tabla clave="auditoria.lista" />);
    expect(selector()).toHaveValue("20");
  });

  it.each([
    ["un 100 de antes del cambio de tamaños", "100"],
    ["un 25 de antes del cambio de tamaños", "25"],
    ["un valor corrupto", "muchas"],
  ])("AC3 — %s vuelve a 10 sin error", (_caso, guardado) => {
    sessionStorage.setItem("flit.filasPorPagina.usuarios.lista", guardado);

    render(<Tabla clave="usuarios.lista" />);

    expect(selector()).toHaveValue("10");
    expect(screen.getByText("Mostrando 1–10 de 200")).toBeInTheDocument();
  });

  it("AC4 — con sessionStorage bloqueado el tamaño aplica en pantalla y no hay error", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("bloqueado", "SecurityError");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("bloqueado", "SecurityError");
    });

    render(<Tabla clave="usuarios.lista" />);
    expect(selector()).toHaveValue("10");
    elegir("30");

    expect(selector()).toHaveValue("30");
    expect(screen.getByText("Mostrando 1–30 de 200")).toBeInTheDocument();
  });

  it("AC5 — en una sesión nueva (almacenamiento vacío) toda tabla abre en 10", () => {
    render(<Tabla clave="usuarios.lista" />);
    expect(selector()).toHaveValue("10");
  });

  it("borde — el número de las otras apps de la suite fija el tamaño inicial y no se recuerda", () => {
    function Legado() {
      const pg = usePaginacion(25);
      return (
        <Pagination
          page={pg.page}
          pageSize={pg.pageSize}
          totalCount={200}
          onPageChange={pg.setPage}
          onPageSizeChange={pg.setPageSize}
        />
      );
    }
    const { unmount } = render(<Legado />);
    expect(selector()).toHaveValue("25");
    elegir("50");
    unmount();

    render(<Legado />);
    expect(selector()).toHaveValue("25");
    expect(sessionStorage.length).toBe(0);
  });
});
