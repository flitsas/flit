import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { DescargasLote } from "@/components/tablero/DescargasLote";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import { INTERVALO_SONDEO_ZIP_MS, type EstadoEvidencia } from "@/lib/reportes/api";
import { ID, llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <DescargasLote diagnosticoId={grilla.id} estadoLote={grilla.estado} puedeDescargar={capacidades.descargarReportes} />
// en el tablero: «Descargar Excel» (enlace con download por el BFF) y «Descargar PDF (.zip)», que pide el ZIP en segundo
// plano, anuncia «Preparando N PDF…» (aria-live) sondeando cada 3 s y, en LISTA, muestra el enlace del .zip; en ERROR, el
// aviso con Reintentar. Sin la capacidad no pinta nada.

const EID = "00000000-0000-7000-8000-0000000ee001";
const EID2 = "00000000-0000-7000-8000-0000000ee002";
const PEDIR = `POST /diagnosticos/${ID}/evidencias`;
const ESTADO = `GET /diagnosticos/${ID}/evidencias/${EID}`;
const URL_ARCHIVO = `/api/v1/diagnostico/diagnosticos/${ID}/evidencias/${EID}/archivo`;

const zip = (cambios: Partial<EstadoEvidencia> = {}): EstadoEvidencia => ({
  id: EID,
  tipo: "ZIP",
  estado: "PENDIENTE",
  vehiculos_incluidos: 2,
  vehiculos_pendientes: 1,
  creado_en: "2026-10-22T19:30:00+00:00",
  generado_en: null,
  tipo_error: null,
  ...cambios,
});

/** Responde en orden; la última se repite. */
const enOrden = (...estados: EstadoEvidencia[]) => {
  let n = 0;
  return () => Response.json(estados[Math.min(n++, estados.length - 1)]);
};

const avanzar = (ms = 0) => act(async () => {
  await vi.advanceTimersByTimeAsync(ms);
});

const pintar = (estadoLote: EstadoDiagnostico = "EN_PROCESO", puedeDescargar = true) =>
  render(<DescargasLote diagnosticoId={ID} estadoLote={estadoLote} puedeDescargar={puedeDescargar} />);

const pedirZip = async () => {
  fireEvent.click(screen.getByRole("button", { name: "Descargar PDF (.zip)" }));
  await avanzar();
};

const estadoZip = () => screen.getByRole("status", { name: /estado del zip/i });

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval"] });
  Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:prueba"), configurable: true });
  Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("DescargasLote — AC4: sin la capacidad descargarReportes", () => {
  it("no pinta ningún control de descarga", () => {
    const { container } = pintar("EN_PROCESO", false);
    expect(container).toBeEmptyDOMElement();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
});

describe("DescargasLote — Excel del lote", () => {
  it("enlace «Descargar Excel» con download a la ruta del Excel del lote", () => {
    pintar();
    const excel = screen.getByRole("link", { name: "Descargar Excel" });
    expect(excel).toHaveAttribute("href", `/api/v1/diagnostico/diagnosticos/${ID}/excel`);
    expect(excel).toHaveAttribute("download");
  });

  it("409 LOTE_SIN_LANZAR al descargar: aviso en texto claro, sin romper la barra", async () => {
    simularApi({ [`GET /diagnosticos/${ID}/excel`]: () => Response.json({ code: "LOTE_SIN_LANZAR", error: "x" }, { status: 409 }) });
    pintar();
    fireEvent.click(screen.getByRole("link", { name: "Descargar Excel" }));
    await avanzar();
    expect(screen.getByRole("alert")).toHaveTextContent("El lote todavía no se ha lanzado: aún no hay nada que descargar.");
    expect(screen.getByRole("link", { name: "Descargar Excel" })).toBeInTheDocument();
  });
});

describe("DescargasLote — AC3: el ZIP pasa de «Preparando» al enlace de descarga", () => {
  it("la región del estado del ZIP es aria-live polite y arranca vacía", () => {
    pintar();
    expect(estadoZip()).toHaveAttribute("aria-live", "polite");
    expect(estadoZip()).toHaveTextContent("");
  });

  it("202: anuncia «Preparando 2 PDF…», sondea cada 3 s y en LISTA muestra el enlace del .zip; luego deja de sondear", async () => {
    const api = simularApi({
      [PEDIR]: () => Response.json(zip(), { status: 202 }),
      [ESTADO]: enOrden(zip({ estado: "GENERANDO" }), zip({ estado: "LISTA", generado_en: "2026-10-22T19:31:00+00:00" })),
    });
    pintar();
    await pedirZip();
    expect(estadoZip()).toHaveTextContent("Preparando 2 PDF…");
    expect(screen.queryByRole("link", { name: /descargar el zip/i })).not.toBeInTheDocument();

    await avanzar(INTERVALO_SONDEO_ZIP_MS);
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/evidencias/${EID}`)).toHaveLength(1);
    expect(estadoZip()).toHaveTextContent("Preparando 2 PDF…");

    await avanzar(INTERVALO_SONDEO_ZIP_MS);
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/evidencias/${EID}`)).toHaveLength(2);
    const enlace = screen.getByRole("link", { name: /descargar el zip/i });
    expect(enlace).toHaveAttribute("href", URL_ARCHIVO);
    expect(enlace).toHaveAttribute("download");
    expect(estadoZip()).toHaveTextContent(/el zip está listo/i);

    await avanzar(INTERVALO_SONDEO_ZIP_MS * 3);
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/evidencias/${EID}`)).toHaveLength(2);
  });

  it("menciona los vehículos sin terminar que quedan fuera del ZIP", async () => {
    simularApi({ [PEDIR]: () => Response.json(zip({ vehiculos_pendientes: 1 }), { status: 202 }), [ESTADO]: enOrden(zip()) });
    pintar();
    await pedirZip();
    expect(estadoZip()).toHaveTextContent("1 vehículo sin terminar no se incluye.");
  });

  it("200 (ya había uno listo): el enlace aparece de una vez, sin sondear", async () => {
    const api = simularApi({ [PEDIR]: () => Response.json(zip({ estado: "LISTA", generado_en: "2026-10-22T19:31:00+00:00" })) });
    pintar();
    await pedirZip();
    expect(screen.getByRole("link", { name: /descargar el zip/i })).toHaveAttribute("href", URL_ARCHIVO);
    await avanzar(INTERVALO_SONDEO_ZIP_MS * 2);
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/evidencias/${EID}`)).toHaveLength(0);
  });

  it("al desmontar se detiene el sondeo", async () => {
    const api = simularApi({ [PEDIR]: () => Response.json(zip(), { status: 202 }), [ESTADO]: enOrden(zip({ estado: "GENERANDO" })) });
    const { unmount } = pintar();
    await pedirZip();
    unmount();
    await avanzar(INTERVALO_SONDEO_ZIP_MS * 3);
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/evidencias/${EID}`)).toHaveLength(0);
  });

  it("mientras se prepara, el botón del ZIP queda ocupado y no duplica el pedido", async () => {
    const api = simularApi({ [PEDIR]: () => Response.json(zip(), { status: 202 }), [ESTADO]: enOrden(zip({ estado: "GENERANDO" })) });
    pintar();
    await pedirZip();
    const boton = screen.getByRole("button", { name: "Descargar PDF (.zip)" });
    expect(boton).toBeDisabled();
    expect(boton).toHaveAttribute("aria-busy", "true");
    expect(llamadas(api, "POST", `/diagnosticos/${ID}/evidencias`)).toHaveLength(1);
  });
});

describe("DescargasLote — ZIP en ERROR: aviso con Reintentar", () => {
  it("ERROR: aviso «No se pudo generar el ZIP; vuelve a pedirlo», deja de sondear y Reintentar pide otro", async () => {
    let pedidos = 0;
    const api = simularApi({
      [PEDIR]: () => Response.json(pedidos++ === 0 ? zip() : zip({ id: EID2 }), { status: 202 }),
      [ESTADO]: enOrden(zip({ estado: "ERROR", tipo_error: "GENERACION_FALLIDA" })),
      [`GET /diagnosticos/${ID}/evidencias/${EID2}`]: enOrden(zip({ id: EID2, estado: "GENERANDO" })),
    });
    pintar();
    await pedirZip();
    await avanzar(INTERVALO_SONDEO_ZIP_MS);
    const alerta = screen.getByRole("alert");
    expect(alerta).toHaveTextContent("No se pudo generar el ZIP; vuelve a pedirlo");
    expect(alerta).not.toHaveTextContent("GENERACION_FALLIDA");
    expect(screen.queryByRole("link", { name: /descargar el zip/i })).not.toBeInTheDocument();

    await avanzar(INTERVALO_SONDEO_ZIP_MS * 2);
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/evidencias/${EID}`)).toHaveLength(1);

    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    await avanzar();
    expect(llamadas(api, "POST", `/diagnosticos/${ID}/evidencias`)).toHaveLength(2);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(estadoZip()).toHaveTextContent("Preparando 2 PDF…");
  });

  it("si el sondeo falla, aviso con Reintentar en lugar de seguir sondeando", async () => {
    const api = simularApi({ [PEDIR]: () => Response.json(zip(), { status: 202 }), [ESTADO]: () => Response.json({ error: "x" }, { status: 500 }) });
    pintar();
    await pedirZip();
    await avanzar(INTERVALO_SONDEO_ZIP_MS);
    expect(within(screen.getByRole("alert")).getByRole("button", { name: "Reintentar" })).toBeInTheDocument();
    await avanzar(INTERVALO_SONDEO_ZIP_MS * 2);
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/evidencias/${EID}`)).toHaveLength(1);
  });
});

describe("DescargasLote — AC5: 409 SIN_VEHICULOS_TERMINADOS", () => {
  it("muestra que aún no hay vehículos terminados y ningún enlace del ZIP", async () => {
    const api = simularApi({ [PEDIR]: () => Response.json({ code: "SIN_VEHICULOS_TERMINADOS", error: "x" }, { status: 409 }) });
    pintar();
    await pedirZip();
    expect(screen.getByRole("alert")).toHaveTextContent("Aún no hay vehículos terminados para armar el ZIP. Inténtalo cuando termine alguno.");
    expect(screen.queryByRole("link", { name: /descargar el zip/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
    await avanzar(INTERVALO_SONDEO_ZIP_MS * 2);
    expect(api).toHaveBeenCalledTimes(1);
  });
});
