import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { TablaFilasCorregibles } from "@/components/carga/TablaFilasCorregibles";
import type { FilaResumen } from "@/lib/carga/api";
import { FILA_ERRONEA, ID, cuerpoDe, fila, llamadas, resumen, ruta, simularApi } from "./datos";

// Uso de ejemplo: <TablaFilasCorregibles diagnosticoId={id} filas={erroneas} onFilaRevalidada={(f, capturada) => ...} />
// deja editar cada fila errónea; 300 ms después de la última tecla la valida en pantalla con fieldRules y, si pasa, la
// revalida con POST /diagnosticos/{id}/carga/validar-filas y pinta su nuevo estado.

afterEach(() => vi.unstubAllGlobals());

const valida = (cambios: Partial<FilaResumen> = {}) => fila({ fila: 3, identificador: "TST124", identificador_norm: "TST124", ...cambios });

function montar(filas: FilaResumen[] = [FILA_ERRONEA], onFilaRevalidada = vi.fn()) {
  render(<TablaFilasCorregibles diagnosticoId={ID} filas={filas} onFilaRevalidada={onFilaRevalidada} />);
  return onFilaRevalidada;
}

const identificador = (n = 3) => screen.getByRole("textbox", { name: `Identificador de la fila ${n}` });
const filaDe = (n = 3) => identificador(n).closest("tr") as HTMLElement;
/** Región viva de la TablaEditable (HU #14562 · N3: sin aria-label, se ubica junto a la tabla). */
const regionDeLaTabla = () => within(screen.getByRole("table", { name: "Filas con errores" }).parentElement as HTMLElement).getByRole("status");

describe("TablaFilasCorregibles — corregir ahora (AC 4)", () => {
  it("editar una fila con un valor válido la revalida al vuelo y pasa a válida", async () => {
    const api = simularApi({ [`POST ${ruta("/carga/validar-filas")}`]: () => Response.json(resumen({ filas: [valida()] })) });
    const onFilaRevalidada = montar();
    expect(within(filaDe()).getByText("Inválido")).toBeInTheDocument();

    fireEvent.change(identificador(), { target: { value: "tst124" } });

    await waitFor(() => expect(within(filaDe()).getByText("Corregida")).toBeInTheDocument());
    const [llamada] = llamadas(api, "POST", ruta("/carga/validar-filas"));
    expect(cuerpoDe(llamada)).toEqual({ filas: [{ fila: 3, identificador: "tst124", tipo_documento_locatario: null, documento_locatario: null }] });
    expect(onFilaRevalidada).toHaveBeenCalledWith(
      expect.objectContaining({ fila: 3, valida: true }),
      expect.objectContaining({ fila: 3, identificador: "tst124" }),
    );
  });

  it("espera 300 ms tras la última tecla: dos cambios seguidos hacen una sola revalidación con el último valor", async () => {
    const api = simularApi({ [`POST ${ruta("/carga/validar-filas")}`]: () => Response.json(resumen({ filas: [valida()] })) });
    montar();
    fireEvent.change(identificador(), { target: { value: "TST12" } });
    fireEvent.change(identificador(), { target: { value: "TST124" } });
    await waitFor(() => expect(within(filaDe()).getByText("Corregida")).toBeInTheDocument());
    const revalidaciones = llamadas(api, "POST", ruta("/carga/validar-filas"));
    expect(revalidaciones).toHaveLength(1);
    expect(cuerpoDe(revalidaciones[0])).toMatchObject({ filas: [{ identificador: "TST124" }] });
  });

  it("un identificador que excede 8 caracteres de placa se rechaza en pantalla sin llamar a la API", async () => {
    const api = simularApi({ [`POST ${ruta("/carga/validar-filas")}`]: () => Response.json(resumen({ filas: [valida()] })) });
    montar();
    fireEvent.change(identificador(), { target: { value: "AB1234567" } });
    await waitFor(() => expect(identificador()).toHaveAccessibleDescription(/la placa tiene 9 caracteres/i));
    expect(identificador()).toHaveAttribute("aria-invalid", "true");
    await new Promise((r) => setTimeout(r, 350));
    expect(llamadas(api, "POST", ruta("/carga/validar-filas"))).toHaveLength(0);
  });

  it("un valor que no es placa ni VIN se marca en pantalla sin llamar a la API", async () => {
    const api = simularApi({});
    montar();
    fireEvent.change(identificador(), { target: { value: "%%" } });
    await waitFor(() => expect(identificador()).toHaveAccessibleDescription(/ni una placa ni un vin/i));
    expect(llamadas(api, "POST", ruta("/carga/validar-filas"))).toHaveLength(0);
  });

  it("si el servidor la mantiene errónea, pinta su mensaje", async () => {
    simularApi({
      [`POST ${ruta("/carga/validar-filas")}`]: () =>
        Response.json(
          resumen({
            filas: [valida({ valida: false, errores: [{ campo: "Identificador_Vehiculo", code: "IDENTIFICADOR_DEMASIADO_LARGO", mensaje: "El identificador es demasiado largo." }] })],
          }),
        ),
    });
    montar();
    fireEvent.change(identificador(), { target: { value: "TST124" } });
    await waitFor(() => expect(identificador()).toHaveAccessibleDescription("El identificador es demasiado largo."));
    expect(within(filaDe()).getByText("Inválido")).toBeInTheDocument();
  });

  it("un documento enmascarado no se reenvía: el usuario lo vuelve a escribir y viaja solo en la petición", async () => {
    const conDoc = fila({
      fila: 6,
      identificador: "TST125",
      valida: false,
      tipo_documento_locatario: "CC",
      documento_locatario: "****12AB",
      errores: [{ campo: "Documento_Locatario", code: "DOCUMENTO_INVALIDO", mensaje: "El documento solo admite dígitos." }],
    });
    const api = simularApi({ [`POST ${ruta("/carga/validar-filas")}`]: () => Response.json(resumen({ filas: [fila({ fila: 6, identificador: "TST125", tipo_documento_locatario: "CC", documento_locatario: "****4050" })] })) });
    montar([conDoc]);
    expect(within(filaDe(6)).getByText("CC ****12AB")).toBeInTheDocument();
    const documento = screen.getByLabelText("Documento del locatario de la fila 6") as HTMLInputElement;
    expect(documento.value).toBe("");

    fireEvent.change(documento, { target: { value: "1020304050" } });

    await waitFor(() => expect(within(filaDe(6)).getByText("Corregida")).toBeInTheDocument());
    const [llamada] = llamadas(api, "POST", ruta("/carga/validar-filas"));
    expect(cuerpoDe(llamada)).toEqual({ filas: [{ fila: 6, identificador: "TST125", tipo_documento_locatario: "CC", documento_locatario: "1020304050" }] });
    expect(localStorage.length + sessionStorage.length).toBe(0);
  });
});

describe("TablaFilasCorregibles — vacío", () => {
  it("sin filas erróneas muestra el estado vacío", () => {
    simularApi({});
    montar([]);
    expect(screen.getByText(/no hay filas con errores/i)).toBeInTheDocument();
  });
});

describe("TablaFilasCorregibles — estados por fila y paginación (AC21, AC22)", () => {
  it("AC22: mientras el API revalida la fila dice «Revalidando» y al aceptarla queda «Corregida»", async () => {
    let soltar: (r: Response) => void = () => {};
    simularApi({ [`POST ${ruta("/carga/validar-filas")}`]: () => new Promise<Response>((r) => (soltar = r)) });
    montar();
    fireEvent.change(identificador(), { target: { value: "TST124" } });
    expect(await within(filaDe()).findByText("Revalidando")).toBeInTheDocument();
    soltar(Response.json(resumen({ filas: [valida()] })));
    expect(await within(filaDe()).findByText("Corregida")).toBeInTheDocument();
  });

  it("AC22 (negativo): si el API la vuelve a rechazar queda «Inválido» con su mensaje y los valores digitados", async () => {
    simularApi({
      [`POST ${ruta("/carga/validar-filas")}`]: () =>
        Response.json(resumen({ filas: [valida({ valida: false, errores: [{ campo: "Identificador_Vehiculo", code: "NO_EXISTE", mensaje: "La placa no tiene el formato esperado." }] })] })),
    });
    montar();
    fireEvent.change(identificador(), { target: { value: "TST124" } });
    await waitFor(() => expect(identificador()).toHaveAccessibleDescription("La placa no tiene el formato esperado."));
    expect(within(filaDe()).getByText("Inválido")).toBeInTheDocument();
    expect(identificador()).toHaveValue("TST124");
  });

  it("AC21: con más de 10 filas con error las pagina y permite verlas todas", () => {
    simularApi({});
    const filas = Array.from({ length: 12 }, (_, i) => fila({ ...FILA_ERRONEA, fila: i + 2 }));
    montar(filas);
    expect(screen.getAllByRole("textbox", { name: /^Identificador de la fila/ })).toHaveLength(10);
    expect(screen.getByText("Página 1 de 2")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Página siguiente" }));
    expect(screen.getByText("Página 2 de 2")).toBeInTheDocument();
    expect(screen.getAllByRole("textbox", { name: /^Identificador de la fila/ }).map((c) => c.getAttribute("aria-label"))).toEqual([
      "Identificador de la fila 12",
      "Identificador de la fila 13",
    ]);
    expect(screen.getByRole("button", { name: "Página siguiente" })).toBeDisabled();
  });
});

// HU #14562 · M12 (auditoría): Carga reutiliza `TablaEditable` del kit en modo controlado (`estadoFila`), sin una tabla
// editable propia. Se conservan AC21/AC22 y además se gana la región viva y la asociación del error a «Reintentar».
describe("TablaFilasCorregibles sobre TablaEditable (HU #14562 · M12)", () => {
  it("M12: la tabla es la TablaEditable del kit: nombre propio, columna Estado y región viva de estado", () => {
    simularApi({});
    montar();
    expect(screen.getByRole("table", { name: "Filas con errores" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Estado" })).toBeInTheDocument();
    expect(screen.getByRole("rowheader", { name: "3" })).toBeInTheDocument();
    expect(regionDeLaTabla()).toBeEmptyDOMElement();
    // HU #14562 · N3: la fila se nombra «Fila N»; la clave interna no es su nombre accesible.
    expect(screen.getByRole("row", { name: "Fila 3" })).toBe(filaDe());
  });

  it("M12 · AC22: la región viva anuncia «Revalidando» y luego «Corregida» con el número de fila", async () => {
    simularApi({ [`POST ${ruta("/carga/validar-filas")}`]: () => Response.json(resumen({ filas: [valida()] })) });
    montar();
    fireEvent.change(identificador(), { target: { value: "TST124" } });
    const region = regionDeLaTabla();
    expect(region).not.toHaveAttribute("aria-label");
    await waitFor(() => expect(region).toHaveTextContent("Fila 3: Corregida"));
  });

  it("M12 · AC22 (negativo): si la revalidación falla, «Reintentar Fila 3» lleva el motivo como descripción y no roba el foco", async () => {
    simularApi({ [`POST ${ruta("/carga/validar-filas")}`]: () => Response.json({ error: "Servicio no disponible" }, { status: 503 }) });
    montar();
    const campo = identificador();
    campo.focus();
    fireEvent.change(campo, { target: { value: "TST124" } });
    const reintentar = await within(filaDe()).findByRole("button", { name: "Reintentar Fila 3" });
    expect(reintentar).toHaveAccessibleDescription(/.+/);
    expect(within(filaDe()).getByText("Inválido")).toBeInTheDocument();
    expect(campo).toHaveFocus();
    expect(campo).toHaveValue("TST124");
  });
});

// HU #14562 · M6 (AC56): la tabla de Carga es la TablaEditable del kit, así que por debajo de 900 px pasa a tarjetas y cada
// celda lleva el nombre de su columna en data-label.
describe("TablaFilasCorregibles: tarjetas por debajo de 900 px (HU #14562 · M6)", () => {
  it("cada celda de la fila lleva su data-label y la tabla no impone un ancho mínimo", () => {
    simularApi({});
    montar();
    const tabla = screen.getByRole("table", { name: "Filas con errores" });
    expect(tabla.className).not.toMatch(/min-w-\[/);
    expect(Array.from(filaDe().children).map((c) => c.getAttribute("data-label"))).toEqual([
      "Fila",
      "Identificador",
      "Tipo de documento",
      "Documento del locatario",
      "Estado",
      "Acciones",
    ]);
  });
});
