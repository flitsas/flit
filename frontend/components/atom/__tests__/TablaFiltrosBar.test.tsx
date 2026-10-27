import { useMemo } from "react";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { EncabezadoFiltrable, TablaFiltrosBar, TablaFiltrosVacio } from "../TablaFiltrosBar";
import { filtrarFilas, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";

interface Fila {
  nombre: string;
  estado: string;
  creado: string;
}
const FILAS: Fila[] = [
  { nombre: "Alfa", estado: "ACTIVE", creado: "2020-01-10" },
  { nombre: "Beta", estado: "INACTIVE", creado: "2020-02-10" },
  { nombre: "Gamma", estado: "ACTIVE", creado: "2020-03-10" },
];
const COLUMNAS: ColumnaFiltro[] = [
  { id: "nombre", label: "Nombre", tipo: "texto" },
  {
    id: "estado",
    label: "Estado",
    tipo: "seleccion",
    opciones: [
      { value: "ACTIVE", label: "Activa" },
      { value: "INACTIVE", label: "Inactiva" },
    ],
  },
  { id: "creado", label: "Creación", tipo: "fecha" },
];

function Harness({
  buscador = false,
  slot = false,
  onCambio,
}: {
  buscador?: boolean;
  slot?: boolean;
  onCambio?: () => void;
}) {
  const filtros = useTablaFiltros({ columnas: COLUMNAS, periodoColumnaId: "creado", onCambio });
  const visibles = useMemo(
    () =>
      filtrarFilas(FILAS, filtros.condicionesTotales, filtros.busqueda, {
        columnas: { nombre: (f) => f.nombre, estado: (f) => f.estado, creado: (f) => f.creado },
        busqueda: (f) => [f.nombre],
      }),
    [filtros.condicionesTotales, filtros.busqueda],
  );
  return (
    <div data-testid="raiz">
      <TablaFiltrosBar
        filtros={filtros}
        buscador={buscador ? { placeholder: "Buscar..." } : undefined}
        columnSelector={slot ? <button type="button">Columnas</button> : undefined}
      />
      <table>
        <thead>
          <tr>
            <th style={{ textTransform: "uppercase" }}>
              <EncabezadoFiltrable filtros={filtros} columnaId="nombre">
                Nombre
              </EncabezadoFiltrable>
            </th>
            <th>
              <EncabezadoFiltrable filtros={filtros} columnaId="estado">
                Estado
              </EncabezadoFiltrable>
            </th>
            <th>
              <EncabezadoFiltrable filtros={filtros} columnaId="creado">
                Creación
              </EncabezadoFiltrable>
            </th>
            <th>
              <EncabezadoFiltrable filtros={filtros} columnaId="acciones">
                Acciones
              </EncabezadoFiltrable>
            </th>
          </tr>
        </thead>
      </table>
      <ul aria-label="filas">
        {visibles.map((f) => (
          <li key={f.nombre}>{f.nombre}</li>
        ))}
      </ul>
      {visibles.length === 0 ? <TablaFiltrosVacio onLimpiar={filtros.limpiar} /> : null}
    </div>
  );
}

const filas = () => within(screen.getByRole("list", { name: "filas" })).queryAllByRole("listitem");
const embudo = (col: string) =>
  screen.getByRole("button", { name: new RegExp(`filtrar por ${col}`, "i") });

async function filtrarTexto(user: ReturnType<typeof userEvent.setup>, valor: string) {
  await user.click(embudo("Nombre"));
  await user.type(screen.getByLabelText(/nombre contiene/i), valor);
  await user.click(screen.getByRole("button", { name: "Aplicar" }));
}

describe("EncabezadoFiltrable", () => {
  it("el embudo aparece solo en columnas filtrables", () => {
    render(<Harness />);
    expect(embudo("Nombre")).toHaveAttribute("aria-haspopup", "dialog");
    expect(embudo("Estado")).toBeInTheDocument();
    expect(embudo("Creación")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /filtrar por acciones/i })).not.toBeInTheDocument();
    expect(screen.getByText("Acciones")).toBeInTheDocument();
  });

  it("texto: abrir, aplicar y quitar", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Nombre"));
    expect(embudo("Nombre")).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("dialog", { name: /filtro de nombre/i })).toBeInTheDocument();
    await user.type(screen.getByLabelText(/nombre contiene/i), "alf");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas().map((l) => l.textContent)).toEqual(["Alfa"]);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await user.click(embudo("Nombre"));
    expect(screen.getByLabelText(/nombre contiene/i)).toHaveValue("alf");
    await user.click(screen.getByRole("button", { name: "Quitar filtro" }));
    expect(filas()).toHaveLength(3);
  });

  it("al abrir, el foco queda en la caja «X contiene» y Enter aplica", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Nombre"));
    await waitFor(() => expect(screen.getByLabelText(/nombre contiene/i)).toHaveFocus());
    await user.keyboard("alf{Enter}");
    expect(filas().map((l) => l.textContent)).toEqual(["Alfa"]);
  });

  it("al abrir una selección el foco va a la primera casilla; en fecha, a la opción marcada", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Estado"));
    await waitFor(() =>
      expect(screen.getAllByRole("checkbox")[0]).toHaveFocus(),
    );
    await user.keyboard("{Escape}");
    await user.click(embudo("Creación"));
    await waitFor(() =>
      expect(screen.getByRole("radio", { name: "Sin periodo" })).toHaveFocus(),
    );
  });

  it("selección: varias opciones son OR", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Estado"));
    await user.click(screen.getByRole("checkbox", { name: "Activa" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas().map((l) => l.textContent)).toEqual(["Alfa", "Gamma"]);
    await user.click(embudo("Estado"));
    await user.click(screen.getByRole("checkbox", { name: "Inactiva" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas()).toHaveLength(3);
  });

  it("fecha: ofrece los periodos de Trámites y aplica un rango propio", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Creación"));
    for (const p of [
      "Sin periodo",
      "Hoy",
      "Últimos 7 días",
      "Últimos 30 días",
      "Últimos 90 días",
      "Mes actual",
      "Mes anterior",
      "Rango propio",
    ]) {
      expect(screen.getByRole("radio", { name: p })).toBeInTheDocument();
    }
    await user.click(screen.getByRole("radio", { name: "Rango propio" }));
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2020-02-01" } });
    fireEvent.change(screen.getByLabelText("Hasta"), { target: { value: "2020-02-28" } });
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas().map((l) => l.textContent)).toEqual(["Beta"]);
    // Al reabrir queda en «Rango propio» con sus fechas.
    await user.click(embudo("Creación"));
    expect(screen.getByRole("radio", { name: "Rango propio" })).toBeChecked();
    expect(screen.getByLabelText("Desde")).toHaveValue("2020-02-01");
    await user.click(screen.getByRole("button", { name: "Quitar filtro" }));
    expect(filas()).toHaveLength(3);
  });

  it("fecha: un periodo predefinido filtra (Hoy no deja filas de 2020)", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Creación"));
    await user.click(screen.getByRole("radio", { name: "Hoy" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas()).toHaveLength(0);
    await user.click(embudo("Creación"));
    expect(screen.getByRole("radio", { name: "Hoy" })).toBeChecked();
  });

  it("fecha: rango inválido se muestra en el panel y no se aplica", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Creación"));
    await user.click(screen.getByRole("radio", { name: "Rango propio" }));
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2020-03-01" } });
    fireEvent.change(screen.getByLabelText("Hasta"), { target: { value: "2020-01-01" } });
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByRole("alert")).toHaveTextContent(/fecha inicial no puede ser posterior/i);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(filas()).toHaveLength(3);
  });

  it("columnas combinadas se unen con AND; ícono azul y aria con filtro activo", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    expect(embudo("Nombre")).not.toHaveAttribute("data-activo");
    await filtrarTexto(user, "a");
    await user.click(embudo("Estado"));
    await user.click(screen.getByRole("checkbox", { name: "Inactiva" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas().map((l) => l.textContent)).toEqual(["Beta"]);
    expect(embudo("Nombre")).toHaveAttribute("data-activo", "true");
    expect(embudo("Nombre")).toHaveAccessibleName(/Nombre: filtro activo/);
    expect(embudo("Nombre").className).toMatch(/text-\[#3B4FD6\]/);
    expect(embudo("Creación")).not.toHaveAttribute("data-activo");
  });

  it("Escape devuelve el foco al ícono", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Nombre"));
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(embudo("Nombre")).toHaveFocus();
  });

  it("clic fuera cierra el panel y devuelve el foco al ícono", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Nombre"));
    await user.click(document.body);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    await vi.waitFor(() => expect(embudo("Nombre")).toHaveFocus());
  });

  it("una condición vacía no se aplica", async () => {
    const user = userEvent.setup();
    const onCambio = vi.fn();
    render(<Harness onCambio={onCambio} />);
    await user.click(embudo("Nombre"));
    await user.type(screen.getByLabelText(/nombre contiene/i), "   ");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(embudo("Nombre")).not.toHaveAttribute("data-activo");
    expect(filas()).toHaveLength(3);
  });

  it("el texto se sanea (sin < ni >)", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(embudo("Nombre"));
    await user.type(screen.getByLabelText(/nombre contiene/i), "a<b>c");
    expect(screen.getByLabelText(/nombre contiene/i)).toHaveValue("abc");
  });
});

describe("TablaFiltrosBar (barra + encabezados, un solo estado)", () => {
  it("conserva buscador, Periodo, + Filtro y los slots", () => {
    render(<Harness buscador slot />);
    expect(screen.getByRole("searchbox", { name: "Buscar..." })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^periodo/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /\+ filtro/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Columnas" })).toBeInTheDocument();
  });

  it("lo aplicado en el encabezado aparece en «+ Filtro (n)» y se quita desde ahí", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await filtrarTexto(user, "alf");
    const mas = screen.getByRole("button", { name: /\+ filtro \(1\)/i });
    await user.click(mas);
    expect(
      within(screen.getByRole("list", { name: "Filtros agregados" })).getByText(/Nombre: «alf»/),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Quitar filtro Nombre" }));
    expect(filas()).toHaveLength(3);
    expect(embudo("Nombre")).not.toHaveAttribute("data-activo");
  });

  it("lo aplicado en «+ Filtro» pinta de azul el embudo de la columna", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Estado" }));
    await user.click(screen.getByRole("checkbox", { name: "Inactiva" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas().map((l) => l.textContent)).toEqual(["Beta"]);
    expect(embudo("Estado")).toHaveAttribute("data-activo", "true");
    expect(embudo("Estado")).toHaveAccessibleName(/Estado: filtro activo/);
  });

  it("Periodo de la barra y el encabezado de fecha comparten estado", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    // Desde el encabezado -> Periodo de la barra muestra el valor.
    await user.click(embudo("Creación"));
    await user.click(screen.getByRole("radio", { name: "Hoy" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByRole("button", { name: /^hoy/i })).toBeInTheDocument();
    expect(embudo("Creación")).toHaveAttribute("data-activo", "true");
    // Desde la barra -> el embudo se apaga.
    await user.click(screen.getByRole("button", { name: /^hoy/i }));
    await user.click(screen.getByRole("button", { name: "Limpiar" }));
    expect(embudo("Creación")).not.toHaveAttribute("data-activo");
    expect(filas()).toHaveLength(3);
  });

  it("«Empezar de cero» quita todo, incluido el buscador; onCambio avisa", async () => {
    const user = userEvent.setup();
    const onCambio = vi.fn();
    render(<Harness buscador onCambio={onCambio} />);
    await user.type(screen.getByRole("searchbox"), "a");
    await filtrarTexto(user, "gamma");
    expect(onCambio).toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: /\+ filtro \(1\)/i }));
    await user.click(screen.getByRole("button", { name: "Empezar de cero" }));
    expect(screen.getByRole("searchbox")).toHaveValue("");
    expect(filas()).toHaveLength(3);
    expect(embudo("Nombre")).not.toHaveAttribute("data-activo");
  });

  it("TablaFiltrosVacio limpia desde el estado vacío", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await filtrarTexto(user, "zzz");
    expect(filas()).toHaveLength(0);
    const vacio = screen.getByText("Ningún registro coincide con los filtros").parentElement!;
    await user.click(within(vacio).getByRole("button", { name: "Limpiar filtros" }));
    expect(filas()).toHaveLength(3);
  });
});
