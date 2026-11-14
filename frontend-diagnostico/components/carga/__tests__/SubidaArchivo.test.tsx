import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { SubidaArchivo } from "@/components/carga/SubidaArchivo";
import { ERRORES_CARGA } from "@/lib/carga/api";
import { ID, archivo, llamadas, resumen, ruta, simularApi } from "./datos";

/**
 * La alerta con texto. Observación 8 del code review (HU #14562): la ZonaArrastre del kit deja su región role="alert"
 * vacía desde el montaje, así que junto al InlineAlert del error de la API hay dos alertas; se busca la que tiene texto.
 */
const alertaConTexto = async () => {
  let alerta: HTMLElement | undefined;
  await waitFor(() => {
    alerta = screen.getAllByRole("alert").find((a) => (a.textContent ?? "").trim() !== "");
    expect(alerta).toBeDefined();
  });
  return alerta as HTMLElement;
};

// Uso de ejemplo: <SubidaArchivo diagnosticoId={id} onResumen={(r) => ...} /> ofrece la plantilla, acepta .xlsx y .csv y
// sube el archivo a POST /diagnosticos/{id}/carga; con el resumen llama a onResumen, y con un rechazo muestra el motivo.

afterEach(() => vi.unstubAllGlobals());

const SECRETO = "mensaje del servidor que no debe verse";
const MENSAJE_XLS = "El formato .xls no se admite: guarde el archivo como .xlsx y vuelva a subirlo";

function montar(onResumen = vi.fn(), onAccesoNegado = vi.fn()) {
  render(<SubidaArchivo diagnosticoId={ID} onResumen={onResumen} onAccesoNegado={onAccesoNegado} />);
  return { onResumen, onAccesoNegado };
}

const selector = () => screen.getByLabelText(/archivo de vehículos/i) as HTMLInputElement;
const subir = (f: File) => {
  fireEvent.change(selector(), { target: { files: [f] } });
  const validar = screen.getByRole("button", { name: "Validar archivo" });
  if (!(validar as HTMLButtonElement).disabled) fireEvent.click(validar);
};

describe("SubidaArchivo — selector y plantilla", () => {
  it("el selector filtra .xlsx y .csv y la plantilla se descarga por el BFF", () => {
    simularApi({});
    montar();
    expect(selector()).toHaveAttribute("type", "file");
    expect(selector()).toHaveAttribute("accept", ".xlsx,.csv");
    expect(screen.getByRole("link", { name: /descargar plantilla/i })).toHaveAttribute("href", "/api/v1/diagnostico/plantilla");
  });

  it("un .csv válido se sube como multipart en el campo archivo y entrega el resumen", async () => {
    const api = simularApi({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()) });
    const { onResumen } = montar();
    subir(archivo("flota.csv", "text/csv"));
    await waitFor(() => expect(onResumen).toHaveBeenCalledWith(expect.objectContaining({ validos: 2, erroneos: 1, duplicados: 1 }), expect.any(File)));
    const [llamada] = llamadas(api, "POST", ruta("/carga"));
    const body = (llamada[1] as RequestInit).body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect((body.get("archivo") as File).name).toBe("flota.csv");
  });

  it("entrega también el mismo archivo con el resumen, para reenviarlo al confirmar", async () => {
    simularApi({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()) });
    const { onResumen } = montar();
    const elegido = archivo("flota.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    subir(elegido);
    await waitFor(() => expect(onResumen).toHaveBeenCalled());
    expect(onResumen.mock.calls[0][1]).toBe(elegido);
  });
});

describe("SubidaArchivo — archivo .xls (AC 1)", () => {
  it("AC10/AC20 (HU #14562): la ZonaArrastre rechaza el .xls en pantalla, con el error asociado y sin llamar a la API", async () => {
    const api = simularApi({
      [`POST ${ruta("/carga")}`]: () => Response.json({ code: "FORMATO_XLS_NO_ADMITIDO", error: MENSAJE_XLS }, { status: 422 }),
    });
    const { onResumen } = montar();
    subir(archivo("flota.xls", "application/vnd.ms-excel"));
    expect(await screen.findByRole("alert")).toHaveTextContent(/formato no admitido.*\.xlsx.*\.csv/i);
    expect(screen.getByRole("button", { name: /elegir archivo/i })).toHaveAccessibleDescription(/formato no admitido/i);
    expect(llamadas(api, "POST", ruta("/carga"))).toHaveLength(0);
    expect(onResumen).not.toHaveBeenCalled();
  });

  it("AC20 (HU #14562): usa la ZonaArrastre del kit, con «Elegir archivo» operable por teclado", () => {
    simularApi({});
    montar();
    expect(screen.getByTestId("zona-arrastre")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /elegir archivo/i })).toBeInTheDocument();
  });
});

describe("SubidaArchivo — formato no admitido (AC 2)", () => {
  it("un .pdf se rechaza con un mensaje claro sin llamar a la API", async () => {
    const api = simularApi({ [`POST ${ruta("/carga")}`]: () => Response.json(resumen()) });
    const { onResumen } = montar();
    subir(archivo("acta.pdf", "application/pdf"));
    expect(await screen.findByRole("alert")).toHaveTextContent(/formato no admitido.*\.xlsx.*\.csv/i);
    expect(llamadas(api, "POST", ruta("/carga"))).toHaveLength(0);
    expect(onResumen).not.toHaveBeenCalled();
  });

  // AP-9 (SF-6): reescrito. Antes afirmaba el mensaje del servidor; ahora el texto fijo del code (ERRORES_CARGA).
  it("AP-9: el 415 ARCHIVO_ILEGIBLE muestra el texto fijo del code, nunca el mensaje del servidor", async () => {
    simularApi({
      [`POST ${ruta("/carga")}`]: () => Response.json({ code: "ARCHIVO_ILEGIBLE", error: SECRETO }, { status: 415 }),
    });
    montar();
    subir(archivo("flota.xlsx"));
    const alerta = await alertaConTexto();
    expect(alerta).toHaveTextContent(ERRORES_CARGA.ARCHIVO_ILEGIBLE);
    expect(alerta).not.toHaveTextContent(SECRETO);
  });

  // AP-9 (SF-6): reescrito. Antes afirmaba el mensaje del servidor.
  it("AP-9: el 413 ARCHIVO_DEMASIADO_GRANDE avisa con texto fijo que el archivo supera el tamaño máximo", async () => {
    simularApi({
      [`POST ${ruta("/carga")}`]: () => Response.json({ code: "ARCHIVO_DEMASIADO_GRANDE", error: SECRETO }, { status: 413 }),
    });
    montar();
    subir(archivo("flota.xlsx"));
    const alerta = await alertaConTexto();
    expect(alerta).toHaveTextContent(/supera el tamaño máximo/);
    expect(alerta).not.toHaveTextContent(SECRETO);
  });

  it.each(["COLUMNA_REQUERIDA", "ARCHIVO_REQUERIDO", "FORMATO_XLS_NO_ADMITIDO", "DIAGNOSTICO_NO_EDITABLE"] as const)(
    "AP-9: el rechazo %s muestra el texto fijo del code, sin Reintentar",
    async (code) => {
      simularApi({ [`POST ${ruta("/carga")}`]: () => Response.json({ code, error: SECRETO }, { status: code === "DIAGNOSTICO_NO_EDITABLE" ? 409 : 422 }) });
      montar();
      subir(archivo("flota.xlsx"));
      const alerta = await alertaConTexto();
      expect(alerta).toHaveTextContent(ERRORES_CARGA[code]);
      expect(alerta).not.toHaveTextContent(SECRETO);
      expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
    },
  );

  it("AP-9: un 422 con un code fuera del contrato cae en el respaldo fijo", async () => {
    simularApi({ [`POST ${ruta("/carga")}`]: () => Response.json({ code: "OTRO", error: SECRETO }, { status: 422 }) });
    montar();
    subir(archivo("flota.xlsx"));
    const alerta = await alertaConTexto();
    expect(alerta).toHaveTextContent("No se pudo analizar el archivo.");
    expect(alerta).not.toHaveTextContent(SECRETO);
  });

  it("un 500 se muestra como error genérico con Reintentar, que vuelve a subir el mismo archivo", async () => {
    let veces = 0;
    const api = simularApi({
      [`POST ${ruta("/carga")}`]: () => (++veces === 1 ? new Response("", { status: 500 }) : Response.json(resumen())),
    });
    const { onResumen } = montar();
    subir(archivo("flota.csv", "text/csv"));
    // AP-9 (SF-6): reescrito. Un 5xx cae en el respaldo fijo del sitio, no en el message del ApiError.
    expect(await alertaConTexto()).toHaveTextContent("No se pudo analizar el archivo.");
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(onResumen).toHaveBeenCalled());
    expect(llamadas(api, "POST", ruta("/carga"))).toHaveLength(2);
  });

  it("un 403 de sesión (PRODUCTO_NO_HABILITADO) se entrega a onAccesoNegado", async () => {
    simularApi({
      [`POST ${ruta("/carga")}`]: () => Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "Sin producto" }, { status: 403 }),
    });
    const { onAccesoNegado } = montar();
    subir(archivo("flota.csv", "text/csv"));
    await waitFor(() => expect(onAccesoNegado).toHaveBeenCalledWith("PRODUCTO_NO_HABILITADO"));
  });
});
