// Consola de Consultas de Comparendos (Reportes 2.0) contra el mock del contrato §4. Pasan por el cliente real.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";

const xlsx = vi.hoisted(() => ({ hojas: [] as { name: string; notes?: string[]; rows: unknown[][] }[] }));
vi.mock("@/lib/xlsx", async (original) => {
  const real = await original<typeof import("@/lib/xlsx")>();
  return {
    ...real,
    buildXlsx: (hoja: { name: string; notes?: string[]; rows: unknown[][] }) => {
      xlsx.hojas.push(hoja);
      return real.buildXlsx(hoja as Parameters<typeof real.buildXlsx>[0]);
    },
  };
});

import { QueryConsole } from "@/components/consultas-personalizadas/QueryConsole";
import { COMPARENDO_DEFAULT_COLUMNS, COMPARENDO_PRESETS, comparendoColumns, diaXlsx, etiquetaEstado } from "@/components/consultas-personalizadas/comparendo-columns";
import { decodeDefinition, encodeDefinition } from "@/components/consultas-personalizadas/link-state";
import { fuenteComparendos, type ComparendoQueryRow, type QueryDefinition, type QueryField } from "@/lib/api/consultas-personalizadas";
import { ID_FABRICA_SIN_GESTION, instalarServidor } from "./mockServidor";

const columnas = (fields: QueryField[]) => comparendoColumns(fields, { conEmpresa: false });

function montar(empresa: string | null = null) {
  return render(
    <QueryConsole<ComparendoQueryRow>
      source={fuenteComparendos(empresa)}
      columns={columnas}
      presets={COMPARENDO_PRESETS}
      defaultColumns={COMPARENDO_DEFAULT_COLUMNS}
      rowKey={(f) => f.id}
      sheetName="Comparendos"
      ambito="su empresa"
    />,
  );
}

beforeEach(() => {
  xlsx.hojas = [];
  window.history.replaceState(null, "", "/reportes/consultas");
  Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:x"), configurable: true });
  Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("cmp-query · consola de Consultas de Comparendos", () => {
  it("cmp-query: abre con las columnas por defecto y la fila del contrato", async () => {
    instalarServidor();
    montar();
    expect(await screen.findByText("11001000000041234567")).toBeInTheDocument();
    const tabla = screen.getByTestId("cmp-query-tabla");
    for (const titulo of ["No. comparendo", "Placa", "Infracción", "Organismo de tránsito", "Imposición", "Valor total", "Estado legal", "Estado de gestión", "Estado DP"])
      expect(within(tabla).getByRole("columnheader", { name: titulo })).toBeInTheDocument();
    // «Empresa» solo con el SuperAdmin en «todas».
    expect(within(tabla).queryByRole("columnheader", { name: "Empresa" })).not.toBeInTheDocument();
    // Los estados llegan como valor y se pintan con la etiqueta del catálogo (/fields), nunca el valor crudo.
    expect(await within(tabla).findByText("Todavía sin gestión")).toBeInTheDocument();
    expect(within(tabla).getByText("Dentro del plazo")).toBeInTheDocument();
    expect(within(tabla).queryByText("SinGestion")).not.toBeInTheDocument();
    expect(within(tabla).queryByText("EnTiempo")).not.toBeInTheDocument();
    expect(screen.getByTestId("cmp-query-total")).toHaveTextContent("2 comparendos");
  });

  it("cmp-query: la cobertura se ve con su motivo cuando un valor pedido no salió", async () => {
    instalarServidor({
      cobertura: [
        { campo: "numero", valor: "11001000000041234567", resultado: "encontrado", motivoCampo: null, motivo: null },
        { campo: "numero", valor: "99999", resultado: "no_existe", motivoCampo: null, motivo: "No está en su empresa." },
        { campo: "placa", valor: "QWE111", resultado: "excluido", motivoCampo: "imposicion", motivo: "Existe, pero su fecha de imposición queda fuera del rango." },
      ],
    });
    montar();
    const aviso = await screen.findByTestId("cmp-query-cobertura");
    expect(aviso).toHaveTextContent("2 valores que pediste no salieron");
    expect(aviso).toHaveTextContent("1 existe pero quedó fuera por los filtros.");
    expect(aviso).toHaveTextContent("1 no está en su empresa.");
    fireEvent.click(within(aviso).getByTestId("cmp-query-cobertura-detalle"));
    const lista = screen.getByTestId("cmp-query-cobertura-lista");
    expect(lista).toHaveTextContent("Placa QWE111");
    expect(lista).toHaveTextContent("Existe, pero su fecha de imposición queda fuera del rango.");
    expect(lista).toHaveTextContent("No. comparendo 99999");
  });

  it("cmp-query: las consultas del sistema salen como «Del sistema» y no se pueden borrar", async () => {
    instalarServidor();
    montar();
    const grupo = await screen.findByTestId("cmp-query-grupo-sistema");
    expect(grupo).toHaveTextContent("Del sistema");
    expect(within(grupo).getByTestId(`cmp-query-guardada-${ID_FABRICA_SIN_GESTION}`)).toHaveTextContent("Sin gestión");
    expect(screen.queryByRole("button", { name: "Borrar Sin gestión" })).not.toBeInTheDocument();
    // La propia sí se borra.
    expect(screen.getByRole("button", { name: "Borrar Mis placas de flota" })).toBeInTheDocument();
  });

  it("cmp-query: guardar sobre una del sistema no ofrece sobrescribirla: crea una nueva", async () => {
    const { peticiones } = instalarServidor();
    montar();
    fireEvent.click(await screen.findByTestId(`cmp-query-guardada-${ID_FABRICA_SIN_GESTION}`));
    // Abre con las columnas de su definición (ids = propiedades de la fila en camelCase).
    expect(await screen.findByRole("columnheader", { name: "Límite de gestión" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Guardar consulta" }));
    expect(screen.queryByTestId("cmp-query-guardar-sobrescribir")).not.toBeInTheDocument();
    fireEvent.change(screen.getByTestId("cmp-query-nombre-input"), { target: { value: "Mi copia" } });
    fireEvent.click(screen.getByTestId("cmp-query-guardar-confirmar"));
    await waitFor(() => expect(peticiones.some((p) => p.metodo === "POST" && p.ruta === "/saved")).toBe(true));
    const post = peticiones.find((p) => p.metodo === "POST" && p.ruta === "/saved")!;
    expect((post.cuerpo as { id?: string }).id).toBeUndefined();
  });

  it("cmp-query: un nombre repetido se dice junto al campo del nombre", async () => {
    instalarServidor();
    montar();
    await screen.findByText("11001000000041234567");
    fireEvent.click(screen.getByRole("button", { name: "Guardar consulta" }));
    const campo = screen.getByTestId("cmp-query-nombre-input");
    fireEvent.change(campo, { target: { value: "Mis placas de flota" } });
    fireEvent.click(screen.getByTestId("cmp-query-guardar-confirmar"));
    const error = await screen.findByText("Ya tienes una consulta llamada «Mis placas de flota».");
    expect(error.closest("#cmp-query-nombre-error")).not.toBeNull();
    expect(campo).toHaveAttribute("aria-invalid", "true");
    expect(campo).toHaveAttribute("aria-describedby", "cmp-query-nombre-error");
  });

  it("cmp-query: sin periodo anterior (null) no se compara contra nada", async () => {
    instalarServidor({ totalPeriodoAnterior: null });
    montar();
    await screen.findByText("11001000000041234567");
    expect(screen.queryByTestId("cmp-query-comparacion")).not.toBeInTheDocument();
    expect(screen.queryByText(/periodo anterior|periodo previo/)).not.toBeInTheDocument();
  });

  it("cmp-query: el enlace ?q= reproduce la misma consulta", async () => {
    const definicion: QueryDefinition = {
      fechas: { campo: "dp_radicacion", preset: "ultimos_90" },
      condiciones: [{ fieldId: "placa", operator: "es_alguno", values: ["ABC123", "XYZ987"] }],
      columnas: [],
    };
    window.history.replaceState(null, "", `/reportes/consultas?empresa=e1&q=${encodeURIComponent(encodeDefinition(definicion))}`);
    const { peticiones } = instalarServidor();
    const escribir = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText: escribir }, configurable: true });
    montar("e1");

    await waitFor(() => expect(peticiones.some((p) => p.ruta === "/run" && (p.cuerpo as QueryDefinition).fechas.campo === "dp_radicacion")).toBe(true));
    const corrida = peticiones.filter((p) => p.ruta === "/run").at(-1)!;
    expect(corrida.cuerpo).toEqual(definicion);
    expect(corrida.query.get("empresa")).toBe("e1");
    expect(await screen.findByTestId("cmp-query-chip-placa")).toHaveTextContent("Placa es ABC123 o XYZ987");

    fireEvent.click(screen.getByTestId("cmp-query-enlace"));
    await waitFor(() => expect(escribir).toHaveBeenCalled());
    const copiado = new URL(escribir.mock.calls[0][0] as string);
    expect(copiado.searchParams.get("empresa")).toBe("e1");
    expect(decodeDefinition(copiado.searchParams.get("q")!)).toEqual(definicion);
  });

  it("cmp-query: el Excel lleva la nota de cobertura y las fechas en día de Colombia", async () => {
    instalarServidor({
      cobertura: [{ campo: "placa", valor: "QWE111", resultado: "no_existe", motivoCampo: null, motivo: "No está en su empresa." }],
    });
    montar();
    await screen.findByText("11001000000041234567");
    fireEvent.click(screen.getByTestId("cmp-query-export-xlsx"));
    await waitFor(() => expect(xlsx.hojas).toHaveLength(1));
    const hoja = xlsx.hojas[0];
    expect(hoja.name).toBe("Comparendos");
    expect(hoja.notes).toEqual(["Placa QWE111: No está en su empresa."]);
    expect(hoja.rows).toHaveLength(2);
    const iImposicion = COMPARENDO_DEFAULT_COLUMNS.indexOf("fechaImposicion");
    expect(hoja.rows[0][iImposicion]).toEqual({ year: 2026, month: 10, day: 9 });
    // En el Excel también va la etiqueta del catálogo, no el valor.
    expect(hoja.rows[0][COMPARENDO_DEFAULT_COLUMNS.indexOf("estadoGestion")]).toBe("Todavía sin gestión");
    expect(await screen.findByText(/Se exportaron 2 comparendos con 9 columnas visibles/)).toBeInTheDocument();
  });

  it("cmp-query: el 422 de consulta demasiado amplia se muestra como aviso con el texto del servidor", async () => {
    const texto = "Esta consulta trae unos 48.936 comparendos. Acótala eligiendo un NIT o un rango de fecha más corto.";
    instalarServidor({ demasiadoAmplia: texto });
    montar();
    const aviso = await screen.findByTestId("cmp-query-demasiado-amplia");
    expect(aviso).toHaveTextContent("Acota la consulta");
    expect(aviso).toHaveTextContent(texto);
    // No es la caja de error genérica, y no queda nada que exportar.
    expect(screen.queryByText("No se pudo ejecutar la consulta.")).not.toBeInTheDocument();
    expect(screen.getByTestId("cmp-query-export-xlsx")).toBeDisabled();
    expect(screen.getByText("Acota los filtros para ver el resultado.")).toBeInTheDocument();
    // El contador no dice «0 comparendos», que se leería como «no hay ninguno».
    expect(screen.getByTestId("cmp-query-total")).toHaveTextContent("\u2014 comparendos");
  });

  it("cmp-query: Escape cierra el editor de una ficha y devuelve el foco al «+ Filtro»", async () => {
    instalarServidor();
    montar();
    await screen.findByText("11001000000041234567");
    const agregar = screen.getByTestId("cmp-query-agregar-filtro");
    fireEvent.click(agregar);
    fireEvent.click(within(screen.getByTestId("cmp-query-campos")).getByRole("button", { name: "Placa" }));
    expect(screen.getByTestId("cmp-query-editor-placa")).toBeInTheDocument();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByTestId("cmp-query-editor-placa")).not.toBeInTheDocument();
    expect(agregar).toHaveFocus();
  });
});

describe("cmp-query · etiquetas de estado", () => {
  it("cmp-query: sin catálogo usa la etiqueta de la bandeja y nunca el valor crudo", () => {
    expect(etiquetaEstado("estadoLegal", "EnTiempo", [])).toBe("En tiempo");
    expect(etiquetaEstado("estadoNotificacionConductor", "Aviso2", [])).toBe("Aviso 2");
    expect(etiquetaEstado("estadoGestion", "OtroDesconocido", [])).toBe("Otro desconocido");
  });
});

describe("cmp-query · fechas para Excel", () => {
  it("cmp-query: «2026-10-09» es el 9 de octubre, no el 8 por la medianoche UTC", () => {
    expect(diaXlsx("2026-10-09")).toEqual({ year: 2026, month: 10, day: 9 });
    expect(diaXlsx(null)).toBeNull();
  });
});
