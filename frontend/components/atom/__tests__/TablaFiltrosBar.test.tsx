import { useMemo } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { TablaFiltrosBar, TablaFiltrosVacio } from "../TablaFiltrosBar";
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
  columnas = COLUMNAS,
  periodo = "creado",
  buscador = true,
  onCambio,
}: {
  columnas?: ColumnaFiltro[];
  periodo?: string;
  buscador?: boolean;
  onCambio?: () => void;
}) {
  const filtros = useTablaFiltros({
    columnas,
    periodoColumnaId: periodo || undefined,
    onCambio,
  });
  const visibles = useMemo(
    () =>
      filtrarFilas(FILAS, filtros.condicionesTotales, filtros.busqueda, {
        columnas: { nombre: (f) => f.nombre, estado: (f) => f.estado, creado: (f) => f.creado },
        busqueda: (f) => [f.nombre],
      }),
    [filtros.condicionesTotales, filtros.busqueda],
  );
  return (
    <div>
      <TablaFiltrosBar
        filtros={filtros}
        buscador={buscador ? { placeholder: "Buscar..." } : undefined}
        columnSelector={<button type="button">Columnas</button>}
      />
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

async function agregarTexto(user: ReturnType<typeof userEvent.setup>, valor: string) {
  await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
  await user.click(screen.getByRole("button", { name: "Nombre" }));
  await user.type(screen.getByLabelText(/nombre contiene/i), valor);
  await user.click(screen.getByRole("button", { name: "Aplicar" }));
}

describe("TablaFiltrosBar", () => {
  it("AC1: una línea con buscador, Periodo, + Filtro y Columnas", () => {
    render(<Harness />);
    expect(screen.getByRole("searchbox", { name: "Buscar..." })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /periodo/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /\+ filtro/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Columnas" })).toBeInTheDocument();
  });

  it("AC1: sin columna de fecha no hay Periodo; sin buscador declarado no hay buscador", () => {
    render(<Harness columnas={COLUMNAS.slice(0, 2)} periodo="" buscador={false} />);
    expect(screen.queryByRole("button", { name: /periodo/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("searchbox")).not.toBeInTheDocument();
  });

  it("AC2: elegir columna muestra el editor con Volver; Aplicar suma condiciones y numera el disparador", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Nombre" }));
    expect(screen.getByRole("button", { name: /volver/i })).toBeInTheDocument();
    await user.type(screen.getByLabelText(/nombre contiene/i), "alf");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas().map((l) => l.textContent)).toEqual(["Alfa"]);
    expect(screen.getByRole("button", { name: "+ Filtro (1)" })).toHaveClass("text-[#3B4FD6]");

    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Estado" }));
    await user.click(screen.getByRole("checkbox", { name: "Inactiva" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas()).toHaveLength(0);
    expect(screen.getByRole("button", { name: "+ Filtro (2)" })).toBeInTheDocument();
  });

  it("AC3: Empezar de cero quita todo y avisa para volver a la página 1", async () => {
    const user = userEvent.setup();
    const onCambio = vi.fn();
    render(<Harness onCambio={onCambio} />);
    await agregarTexto(user, "alf");
    expect(filas()).toHaveLength(1);
    onCambio.mockClear();
    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Empezar de cero" }));
    expect(filas()).toHaveLength(3);
    expect(screen.getByRole("button", { name: "+ Filtro" })).toBeInTheDocument();
    expect(onCambio).toHaveBeenCalled();
  });

  it("AC4: sin resultados muestra el estado vacío y Limpiar filtros limpia también el buscador", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.type(screen.getByRole("searchbox"), "zzz");
    expect(screen.getByText("Ningún registro coincide con los filtros")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(screen.getByRole("searchbox")).toHaveValue("");
    expect(filas()).toHaveLength(3);
  });

  it("AC5: descarta < y >; un valor vacío no agrega condición", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.type(screen.getByRole("searchbox"), "<b>lf");
    expect(screen.getByRole("searchbox")).toHaveValue("blf");
    await user.clear(screen.getByRole("searchbox"));

    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Nombre" }));
    await user.type(screen.getByLabelText(/nombre contiene/i), "<>");
    expect(screen.getByLabelText(/nombre contiene/i)).toHaveValue("");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByRole("button", { name: "+ Filtro" })).toBeInTheDocument();
    expect(filas()).toHaveLength(3);
  });

  it("AC6: Escape cierra el popover y devuelve el foco; clic fuera cierra; hay aria y labels", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    const disparador = screen.getByRole("button", { name: /\+ filtro/i });
    expect(disparador).toHaveAttribute("aria-haspopup", "dialog");
    expect(disparador).toHaveAttribute("aria-expanded", "false");
    await user.click(disparador);
    expect(disparador).toHaveAttribute("aria-expanded", "true");
    await user.click(screen.getByRole("button", { name: "Nombre" }));
    expect(screen.getByLabelText(/nombre contiene/i)).toBeInTheDocument();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(disparador).toHaveFocus();

    await user.click(disparador);
    await user.click(document.body);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("Periodo filtra por la columna de fecha elegida (Rango propio)", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole("button", { name: /periodo/i }));
    await user.selectOptions(screen.getByLabelText("Periodo"), "Rango propio");
    await user.type(screen.getByLabelText("Fecha inicial del rango propio"), "2020-02-01");
    await user.type(screen.getByLabelText("Fecha final del rango propio"), "2020-02-28");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(filas().map((l) => l.textContent)).toEqual(["Beta"]);
  });
});
