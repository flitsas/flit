import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { SubidaArchivo } from "@/components/carga/SubidaArchivo";
import { ID, archivo, llamadas, resumen, ruta, simularApi } from "./datos";

// Uso de ejemplo: <SubidaArchivo diagnosticoId={id} onResumen={(r) => ...} /> ofrece la plantilla, acepta .xlsx y .csv y
// sube el archivo a POST /diagnosticos/{id}/carga; con el resumen llama a onResumen, y con un rechazo muestra el motivo.

afterEach(() => vi.unstubAllGlobals());

const MENSAJE_XLS = "El formato .xls no se admite: guarde el archivo como .xlsx y vuelva a subirlo";

function montar(onResumen = vi.fn(), onAccesoNegado = vi.fn()) {
  render(<SubidaArchivo diagnosticoId={ID} onResumen={onResumen} onAccesoNegado={onAccesoNegado} />);
  return { onResumen, onAccesoNegado };
}

const selector = () => screen.getByLabelText(/archivo de vehículos/i) as HTMLInputElement;
const subir = (f: File) => fireEvent.change(selector(), { target: { files: [f] } });

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
  it("el 422 de un .xls muestra el mensaje de la API que pide guardarlo como .xlsx y no avanza", async () => {
    simularApi({
      [`POST ${ruta("/carga")}`]: () => Response.json({ code: "FORMATO_XLS_NO_ADMITIDO", error: MENSAJE_XLS }, { status: 422 }),
    });
    const { onResumen } = montar();
    subir(archivo("flota.xls", "application/vnd.ms-excel"));
    expect(await screen.findByRole("alert")).toHaveTextContent(MENSAJE_XLS);
    expect(onResumen).not.toHaveBeenCalled();
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

  it("el 415 de la API (archivo ilegible) muestra su mensaje", async () => {
    simularApi({
      [`POST ${ruta("/carga")}`]: () => Response.json({ code: "ARCHIVO_ILEGIBLE", error: "El archivo .xlsx está dañado." }, { status: 415 }),
    });
    montar();
    subir(archivo("flota.xlsx"));
    expect(await screen.findByRole("alert")).toHaveTextContent("El archivo .xlsx está dañado.");
  });

  it("el 413 avisa que el archivo supera el tamaño máximo", async () => {
    simularApi({
      [`POST ${ruta("/carga")}`]: () => Response.json({ code: "ARCHIVO_DEMASIADO_GRANDE", error: "El archivo supera 5 MB." }, { status: 413 }),
    });
    montar();
    subir(archivo("flota.xlsx"));
    expect(await screen.findByRole("alert")).toHaveTextContent("El archivo supera 5 MB.");
  });

  it("un 500 se muestra como error genérico con Reintentar, que vuelve a subir el mismo archivo", async () => {
    let veces = 0;
    const api = simularApi({
      [`POST ${ruta("/carga")}`]: () => (++veces === 1 ? new Response("", { status: 500 }) : Response.json(resumen())),
    });
    const { onResumen } = montar();
    subir(archivo("flota.csv", "text/csv"));
    expect(await screen.findByRole("alert")).toHaveTextContent(/intenta de nuevo/i);
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
