import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { BotonPdfVehiculo } from "@/components/tablero/BotonPdfVehiculo";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import type { FilaVehiculo } from "@/lib/tablero/api";
import { ID, V1, V2, celda, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <BotonPdfVehiculo diagnosticoId={grilla.id} estadoLote={grilla.estado} vehiculo={fila} /> en la columna
// «Reporte» de la grilla: enlace «PDF» con `download` cuando el backend lo emitiría (vehículo terminado, o lote INCOMPLETO
// con todo cerrado); si no, botón deshabilitado con su ayuda. Con JS baja el blob y traduce el 409 del servidor.

const AYUDA_EN_CURSO = "Disponible cuando terminen las consultas de este vehículo";
const AYUDA_NO_DISPONIBLE = "Las consultas no disponibles aparecerán así en el PDF";

/** TST004 en un lote INCOMPLETO: RUNT en error y lo demás cerrado. */
const V4 = vehiculo(4, [
  celda(4, "RUNT", "COMPLETADO"),
  celda(4, "IMPUESTO_DEPARTAMENTAL", "NA", { motivo_na: "SERVICIO_PUBLICO" }),
  celda(4, "SIMIT_PROPIETARIO", "ERROR", { tipo_error: "TIMEOUT" }),
  celda(4, "RUNT_LOCATARIO", "NA", { motivo_na: "SIN_DOCUMENTO_LOCATARIO" }),
]);

const pintar = (estadoLote: EstadoDiagnostico, v: FilaVehiculo) => render(<BotonPdfVehiculo diagnosticoId={ID} estadoLote={estadoLote} vehiculo={v} />);
const enlace = (placa: string) => screen.getByRole("link", { name: new RegExp(`^Descargar PDF de ${placa}$`) });
const esperar = () => act(async () => {
  await new Promise((r) => setTimeout(r, 0));
});

let descargados: string[];
beforeEach(() => {
  descargados = [];
  Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:prueba"), configurable: true });
  Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
    descargados.push(this.download);
  });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("BotonPdfVehiculo — AC1: lote EN_PROCESO", () => {
  it("vehículo terminado: enlace «PDF» con download a la ruta del PDF del vehículo", () => {
    pintar("EN_PROCESO", V1);
    const a = enlace("TST001");
    expect(a).toHaveTextContent("PDF");
    expect(a).toHaveAttribute("href", `/api/v1/diagnostico/diagnosticos/${ID}/vehiculos/vehiculo-1/pdf`);
    expect(a).toHaveAttribute("download");
    expect(screen.queryByText(AYUDA_EN_CURSO)).not.toBeInTheDocument();
  });

  it("vehículo en curso: botón deshabilitado, sin enlace, con su texto de ayuda como descripción accesible", () => {
    pintar("EN_PROCESO", V2);
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    const boton = screen.getByRole("button", { name: "Descargar PDF de TST002" });
    expect(boton).toBeDisabled();
    expect(boton).toHaveTextContent("PDF");
    expect(boton).toHaveAccessibleDescription(AYUDA_EN_CURSO);
    expect(screen.getByText(AYUDA_EN_CURSO)).toBeVisible();
  });
});

describe("BotonPdfVehiculo — AC6: lote INCOMPLETO", () => {
  it("con celdas en error: habilitado y la ayuda dice que las no disponibles saldrán así en el PDF", () => {
    expect(V4.terminado).toBe(false);
    pintar("INCOMPLETO", V4);
    const a = enlace("TST004");
    expect(a).toHaveAttribute("download");
    expect(a).toHaveAccessibleDescription(AYUDA_NO_DISPONIBLE);
    expect(screen.queryByText(AYUDA_EN_CURSO)).not.toBeInTheDocument();
  });

  it("sin celdas en error: habilitado y sin esa ayuda", () => {
    pintar("INCOMPLETO", V1);
    expect(enlace("TST001")).toBeInTheDocument();
    expect(screen.queryByText(AYUDA_NO_DISPONIBLE)).not.toBeInTheDocument();
  });

  it("con una celda todavía abierta: deshabilitado con la ayuda de en curso", () => {
    pintar("INCOMPLETO", V2);
    expect(screen.getByRole("button", { name: "Descargar PDF de TST002" })).toBeDisabled();
  });
});

describe("BotonPdfVehiculo — AC2: clic descarga el PDF y el 409 del servidor no rompe la fila", () => {
  it("clic: pide el PDF por el BFF y lo descarga con el nombre del Content-Disposition", async () => {
    const api = simularApi({
      [`GET /diagnosticos/${ID}/vehiculos/vehiculo-1/pdf`]: () =>
        new Response(new Blob(["%PDF-1.7"], { type: "application/pdf" }), {
          headers: { "Content-Type": "application/pdf", "Content-Disposition": "attachment; filename=diagnostico-00000000-TST001.pdf" },
        }),
    });
    pintar("EN_PROCESO", V1);
    fireEvent.click(enlace("TST001"));
    await esperar();
    expect(api).toHaveBeenCalledTimes(1);
    expect(descargados).toEqual(["diagnostico-00000000-TST001.pdf"]);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("409 VEHICULO_NO_TERMINADO: aviso en texto claro, sin descarga y el enlace sigue ahí", async () => {
    simularApi({ [`GET /diagnosticos/${ID}/vehiculos/vehiculo-1/pdf`]: () => Response.json({ code: "VEHICULO_NO_TERMINADO", error: "x" }, { status: 409 }) });
    pintar("EN_PROCESO", V1);
    fireEvent.click(enlace("TST001"));
    await esperar();
    expect(screen.getByRole("alert")).toHaveTextContent("Este vehículo todavía tiene consultas en curso. El PDF estará disponible cuando terminen.");
    expect(descargados).toEqual([]);
    expect(enlace("TST001")).toBeInTheDocument();
  });
});
