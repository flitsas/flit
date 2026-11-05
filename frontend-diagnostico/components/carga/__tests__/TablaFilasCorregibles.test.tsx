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

describe("TablaFilasCorregibles — corregir ahora (AC 4)", () => {
  it("editar una fila con un valor válido la revalida al vuelo y pasa a válida", async () => {
    const api = simularApi({ [`POST ${ruta("/carga/validar-filas")}`]: () => Response.json(resumen({ filas: [valida()] })) });
    const onFilaRevalidada = montar();
    expect(within(filaDe()).getByText("Errónea")).toBeInTheDocument();

    fireEvent.change(identificador(), { target: { value: "tst124" } });

    await waitFor(() => expect(within(filaDe()).getByText("Válida")).toBeInTheDocument());
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
    await waitFor(() => expect(within(filaDe()).getByText("Válida")).toBeInTheDocument());
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
    expect(within(filaDe()).getByText("Errónea")).toBeInTheDocument();
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

    await waitFor(() => expect(within(filaDe(6)).getByText("Válida")).toBeInTheDocument());
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
