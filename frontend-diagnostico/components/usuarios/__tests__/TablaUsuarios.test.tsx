import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { TablaUsuarios } from "@/components/usuarios/TablaUsuarios";
import type { UsuarioDiagnostico } from "@/lib/usuarios/tipos";

// Uso de ejemplo: <TablaUsuarios usuarios={lista.usuarios} onAccion={(accion, usuario, disparador) => abrirDialogo(...)} />
// pinta la tabla por defecto del kit (Nombre · Correo · Rol · Estado · Acciones). Las acciones de cada fila salen solo de
// `acciones` del servidor; la fila propia y las invitaciones no tienen menú (SP-20, H-U2). Datos: placeholders.

const u = (id: string, nombre: string, extra: Partial<UsuarioDiagnostico> = {}): UsuarioDiagnostico => ({
  id,
  tipo: "USUARIO",
  nombre,
  correo: `${nombre.toLowerCase().replace(/\s+/g, ".")}@ejemplo.com`,
  rol: "usuario_diagnostico",
  rol_etiqueta: "Usuario de Diagnóstico",
  estado: "ACTIVO",
  acciones: [],
  motivo_sin_acciones: null,
  ...extra,
});

const USUARIOS: UsuarioDiagnostico[] = [
  u("p", "Admin Demo", { rol: "admin_diagnostico", rol_etiqueta: "Administrador de Diagnóstico", motivo_sin_acciones: "USTED_MISMO" }),
  u("1", "Usuario Uno", { acciones: ["CAMBIAR_ROL", "SUSPENDER"] }),
  u("2", "Usuario Dos", { estado: "SUSPENDIDO", acciones: ["REACTIVAR"] }),
  u("3", "Invitada Tres", { tipo: "INVITACION", estado: "INVITADO", motivo_sin_acciones: "INVITACION" }),
];

const fila = (nombre: string) => screen.getAllByRole("row").find((r) => r.textContent?.includes(nombre))!;

describe("HU14588 TablaUsuarios", () => {
  it("columnas Nombre · Correo · Rol · Estado · Acciones, sin «Último ingreso» (H-UI 1)", () => {
    render(<TablaUsuarios usuarios={USUARIOS} onAccion={vi.fn()} />);
    const tabla = screen.getByRole("table");
    const cabeceras = within(tabla).getAllByRole("columnheader").map((c) => c.textContent);
    expect(cabeceras).toEqual(["Nombre", "Correo", "Rol", "Estado", "Acciones"]);
    expect(within(tabla).queryByRole("columnheader", { name: /Último ingreso/ })).toBeNull();
  });

  it("rol como texto de Diagnóstico y estado con texto (no solo color)", () => {
    render(<TablaUsuarios usuarios={USUARIOS} onAccion={vi.fn()} />);
    expect(fila("Usuario Uno")).toHaveTextContent("Usuario de Diagnóstico");
    expect(fila("Admin Demo")).toHaveTextContent("Administrador de Diagnóstico");
    expect(fila("Usuario Uno")).toHaveTextContent("Activo");
    expect(fila("Usuario Dos")).toHaveTextContent("Suspendido");
    expect(fila("Invitada Tres")).toHaveTextContent("Invitado");
  });

  it("la fila propia y la invitación no tienen botón de acciones (SP-20, H-U2)", () => {
    render(<TablaUsuarios usuarios={USUARIOS} onAccion={vi.fn()} />);
    expect(within(fila("Admin Demo")).queryByRole("button", { name: /^Acciones/ })).toBeNull();
    expect(within(fila("Invitada Tres")).queryByRole("button", { name: /^Acciones/ })).toBeNull();
    expect(within(fila("Usuario Uno")).getByRole("button", { name: /^Acciones/ })).toBeInTheDocument();
  });

  it("el menú ofrece solo las acciones del servidor y avisa la elegida con su disparador", () => {
    const onAccion = vi.fn();
    render(<TablaUsuarios usuarios={USUARIOS} onAccion={onAccion} />);
    const disparador = within(fila("Usuario Uno")).getByRole("button", { name: /^Acciones/ });
    fireEvent.click(disparador);
    const items = within(screen.getByRole("menu")).getAllByRole("menuitem").map((i) => i.textContent);
    expect(items.some((t) => t?.startsWith("Cambiar rol"))).toBe(true);
    expect(items.some((t) => t?.startsWith("Suspender"))).toBe(true);
    expect(items.some((t) => t?.startsWith("Reactivar"))).toBe(false);
    fireEvent.click(within(screen.getByRole("menu")).getByRole("menuitem", { name: /^Suspender/ }));
    expect(onAccion).toHaveBeenCalledWith("SUSPENDER", expect.objectContaining({ id: "1" }), disparador);
  });

  it("la fila suspendida solo ofrece «Reactivar»", () => {
    render(<TablaUsuarios usuarios={USUARIOS} onAccion={vi.fn()} />);
    fireEvent.click(within(fila("Usuario Dos")).getByRole("button", { name: /^Acciones/ }));
    expect(within(screen.getByRole("menu")).getAllByRole("menuitem").map((i) => i.textContent?.split(/\s/)[0])).toEqual(["Reactivar"]);
  });

  it("el correo solo va en la celda: ningún aria-label, title ni data-* lo lleva (SP-20)", () => {
    const { container } = render(<TablaUsuarios usuarios={USUARIOS} onAccion={vi.fn()} />);
    fireEvent.click(within(fila("Usuario Uno")).getByRole("button", { name: /^Acciones/ }));
    const atributos = Array.from(document.body.querySelectorAll("*")).flatMap((n) =>
      Array.from(n.attributes)
        .filter((a) => a.name === "aria-label" || a.name === "title" || a.name.startsWith("data-") || a.name === "aria-describedby")
        .map((a) => a.value),
    );
    expect(atributos.filter((v) => v.includes("@"))).toEqual([]);
    expect(within(container).getByText("usuario.uno@ejemplo.com")).toBeInTheDocument();
  });

  it("cada celda lleva data-label para la vista en tarjetas (< 900 px)", () => {
    render(<TablaUsuarios usuarios={USUARIOS} onAccion={vi.fn()} />);
    const etiquetas = within(fila("Usuario Uno"))
      .getAllByRole("cell")
      .map((c) => c.getAttribute("data-label"));
    expect(etiquetas).toEqual(["Nombre", "Correo", "Rol", "Estado", "Acciones"]);
  });
});

// Code review HU #14588 (CR-1): el nombre de la persona no va en aria-label ni en title; el disparador se llama «Acciones
// del usuario» y se describe con la celda del nombre.
describe("HU14588 TablaUsuarios — nombre fuera de atributos (CR-1)", () => {
  it("ningún aria-label ni title contiene el nombre de una fila; el disparador se describe con la celda del nombre", () => {
    render(<TablaUsuarios usuarios={USUARIOS} onAccion={vi.fn()} />);
    const disparador = within(fila("Usuario Uno")).getByRole("button", { name: "Acciones del usuario" });
    expect(disparador).toHaveAccessibleDescription("Usuario Uno");
    fireEvent.click(disparador);
    const valores = Array.from(document.body.querySelectorAll("[aria-label], [title]")).flatMap((n) => [n.getAttribute("aria-label") ?? "", n.getAttribute("title") ?? ""]);
    for (const u of USUARIOS) expect(valores.filter((v) => v.includes(u.nombre))).toEqual([]);
  });
});
