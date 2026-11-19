import { afterEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { clearToken, storeToken } from "@/lib/auth/session";

/**
 * Bug #14593 — el «Filas por página» de una tabla era de la pestaña, no del usuario: el 50 que
 * puso un super admin en Usuarios lo heredaba quien entrara después en la misma pestaña, y el
 * cierre de sesión no lo borraba. Ahora el recuerdo va atado al usuario en sesión y el logout lo
 * limpia.
 */

function jwtDe(sub: string): string {
  const payload = btoa(JSON.stringify({ sub }));
  return `eyJhbGciOiJIUzI1NiJ9.${payload}.firma`;
}

function Tabla() {
  const pg = usePaginacion("usuarios.lista");
  return (
    <Pagination
      page={pg.page}
      pageSize={pg.pageSize}
      totalCount={200}
      onPageChange={pg.setPage}
      onPageSizeChange={pg.setPageSize}
      ariaLabel="Paginación de usuarios"
    />
  );
}

const selector = () => screen.getByRole("combobox", { name: "Filas por página" });

afterEach(() => {
  clearToken();
  sessionStorage.clear();
  localStorage.clear();
});

describe("Bug #14593 — «Filas por página» por usuario", () => {
  it("un usuario que elige 50 no lo transmite al siguiente usuario de la misma pestaña", () => {
    storeToken(jwtDe("super-admin"));
    const primero = render(<Tabla />);
    fireEvent.change(selector(), { target: { value: "50" } });
    primero.unmount();

    // Sin cerrar sesión: entra otro usuario en la misma pestaña.
    localStorage.setItem("flit:jwt", jwtDe("usuario-compania"));
    render(<Tabla />);

    expect(selector()).toHaveValue("10");
  });

  it("el mismo usuario sí recupera su tamaño al volver a la tabla", () => {
    storeToken(jwtDe("admin-compania"));
    const primera = render(<Tabla />);
    fireEvent.change(selector(), { target: { value: "30" } });
    primera.unmount();

    render(<Tabla />);

    expect(selector()).toHaveValue("30");
  });

  it("cerrar sesión borra los tamaños recordados", () => {
    storeToken(jwtDe("admin-compania"));
    const { unmount } = render(<Tabla />);
    fireEvent.change(selector(), { target: { value: "50" } });
    unmount();

    clearToken();
    storeToken(jwtDe("admin-compania"));
    render(<Tabla />);

    expect(selector()).toHaveValue("10");
    expect(
      Array.from({ length: sessionStorage.length }, (_, i) => sessionStorage.key(i)).filter((k) =>
        k?.startsWith("flit.filasPorPagina."),
      ),
    ).toHaveLength(0);
  });
});
