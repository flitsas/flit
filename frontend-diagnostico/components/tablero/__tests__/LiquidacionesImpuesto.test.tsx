import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { LiquidacionesImpuesto } from "@/components/tablero/vistasDetalle/LiquidacionesImpuesto";
import type { ResultadoImpuestoDetalle } from "@/lib/tablero/detalle";
import { ID, llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <LiquidacionesImpuesto diagnosticoId vehiculoId identificador="TST001" resultado={r} puedeDescargar />
// pinta la tabla «Vigencias pendientes» (año, valor, vencimiento, referencia de pago) y, con el permiso de descarga, un botón
// «Descargar liquidación <año>» por vigencia con archivo y «Descargar liquidación consolidada» si existe. Cada botón baja su
// PDF bajo demanda (vehículo + año, nunca el id del archivo), muestra su propio estado cargando y su error legible.

const RUTA = (v: string) => `GET /diagnosticos/${ID}/vehiculos/vehiculo-1/impuesto/liquidaciones/${v}`;
const pdf = () => new Response(new Blob(["%PDF-1.7 prueba"], { type: "application/pdf" }), { status: 200, headers: { "Content-Type": "application/pdf" } });

const resultado = (cambios: Partial<ResultadoImpuestoDetalle> = {}): ResultadoImpuestoDetalle => ({
  tiene_deuda: true,
  monto_total: 530000,
  liquidacion_consolidada_id: "archivo-secreto-consolidado",
  tiene_liquidacion_consolidada: true,
  vigencias: [
    { anio: 2023, valor: 90000, pagada: true, tiene_liquidacion: true, referencia_pago: "REF-PAGADA" },
    { anio: 2024, valor: 180000, pagada: false, tiene_liquidacion: true, referencia_pago: "REF-0001", fecha_vencimiento: "2024-07-31" },
    { anio: 2025, valor: 350000, pagada: false, tiene_liquidacion: false },
  ],
  ...cambios,
});

function pintar(r: ResultadoImpuestoDetalle = resultado(), puedeDescargar = true) {
  return render(<LiquidacionesImpuesto diagnosticoId={ID} vehiculoId="vehiculo-1" identificador="TST001" resultado={r} puedeDescargar={puedeDescargar} />);
}

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

const filas = (tabla: HTMLElement) =>
  within(tabla)
    .getAllByRole("row")
    .slice(1)
    .map((f) => within(f).getAllByRole("cell").map((c) => c.textContent?.replace(/\s/g, " ")));

describe("LiquidacionesImpuesto — tabla de vigencias pendientes", () => {
  it("lleno: solo las pendientes, con año, valor en pesos, vencimiento y referencia de pago", () => {
    pintar(resultado(), false);
    const tabla = screen.getByRole("table", { name: "Vigencias pendientes" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Año", "Valor", "Vencimiento", "Referencia de pago"]);
    expect(filas(tabla)).toEqual([
      ["2024", "$ 180.000", "31/07/2024", "REF-0001"],
      ["2025", "$ 350.000", "—", "—"],
    ]);
    expect(screen.queryByText("REF-PAGADA")).not.toBeInTheDocument();
  });

  it("vacío: sin vigencias pendientes lo dice y no pinta tabla", () => {
    pintar(resultado({ vigencias: [{ anio: 2023, valor: 90000, pagada: true }], tiene_liquidacion_consolidada: false }));
    expect(screen.getByText("No hay vigencias pendientes de pago.")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("sin el permiso de descarga: la tabla sí, pero ningún botón de liquidación", () => {
    pintar(resultado(), false);
    expect(screen.queryByRole("button", { name: /Descargar liquidación/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Liquidación" })).not.toBeInTheDocument();
  });

  it("con el permiso: un botón por vigencia pendiente con archivo, y la consolidada si existe", () => {
    pintar();
    expect(screen.getAllByRole("button").map((b) => b.textContent?.trim())).toEqual(["Descargar liquidación 2024", "Descargar liquidación consolidada"]);
    expect(screen.queryByRole("button", { name: "Descargar liquidación 2025" })).not.toBeInTheDocument();
  });

  it("sin consolidada (tiene_liquidacion_consolidada false o ausente): no hay botón consolidado aunque llegue el id viejo", () => {
    pintar(resultado({ tiene_liquidacion_consolidada: undefined }));
    expect(screen.queryByRole("button", { name: "Descargar liquidación consolidada" })).not.toBeInTheDocument();
  });

  it("nunca pinta el id del archivo", () => {
    const { container } = pintar();
    expect(container.innerHTML).not.toMatch(/archivo-secreto/);
  });
});

describe("LiquidacionesImpuesto — descarga bajo demanda", () => {
  it("clic: pide vehículo + año, guarda liquidacion-<placa>-<año>.pdf y el botón vuelve a quedar listo", async () => {
    const api = simularApi({ [RUTA("2024")]: () => pdf() });
    pintar();
    fireEvent.click(screen.getByRole("button", { name: "Descargar liquidación 2024" }));
    await waitFor(() => expect(descargados).toEqual(["liquidacion-TST001-2024.pdf"]));
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/vehiculos/vehiculo-1/impuesto/liquidaciones/2024`)).toHaveLength(1);
    expect(String(api.mock.calls[0][0])).not.toMatch(/archivo-secreto/);
    await waitFor(() => expect(screen.getByRole("button", { name: "Descargar liquidación 2024" })).not.toHaveAttribute("aria-busy"));
  });

  it("cargando: solo el botón pulsado queda ocupado y deshabilitado; el otro sigue disponible", async () => {
    let soltar: (r: Response) => void = () => {};
    simularApi({ [RUTA("consolidada")]: () => new Promise<Response>((r) => (soltar = r)) });
    pintar();
    fireEvent.click(screen.getByRole("button", { name: "Descargar liquidación consolidada" }));
    const ocupado = await screen.findByRole("button", { name: "Descargando liquidación consolidada…" });
    expect(ocupado).toHaveAttribute("aria-busy", "true");
    expect(ocupado).toBeDisabled();
    expect(screen.getByRole("button", { name: "Descargar liquidación 2024" })).toBeEnabled();
    soltar(pdf());
    await waitFor(() => expect(descargados).toEqual(["liquidacion-TST001-consolidada.pdf"]));
  });

  it("error 404: texto legible bajo el botón, enlazado con aria-describedby a un id que existe", async () => {
    simularApi({ [RUTA("2024")]: () => Response.json({ code: "LIQUIDACION_NO_ENCONTRADA", error: "x" }, { status: 404 }) });
    pintar();
    const boton = screen.getByRole("button", { name: "Descargar liquidación 2024" });
    fireEvent.click(boton);
    const aviso = await screen.findByText("La fuente ya no tiene este archivo.");
    expect(descargados).toEqual([]);
    const id = boton.getAttribute("aria-describedby");
    expect(id).toBeTruthy();
    expect(document.getElementById(id as string)).toBe(aviso);
    expect(screen.getByRole("button", { name: "Descargar liquidación consolidada" })).not.toHaveAttribute("aria-describedby");
  });

  it("error de credencial (409 CREDENCIAL_NO_CONFIGURADA): lo dice sin el código técnico", async () => {
    simularApi({ [RUTA("consolidada")]: () => Response.json({ code: "CREDENCIAL_NO_CONFIGURADA", error: "x" }, { status: 409 }) });
    const { container } = pintar();
    fireEvent.click(screen.getByRole("button", { name: "Descargar liquidación consolidada" }));
    expect(await screen.findByText(/no tiene configurada la credencial del impuesto/)).toBeInTheDocument();
    expect(container.textContent).not.toMatch(/CREDENCIAL_NO_CONFIGURADA/);
  });

  it("otro error: texto genérico; al reintentar con éxito el error desaparece", async () => {
    let falla = true;
    simularApi({ [RUTA("2024")]: () => (falla ? Response.json({ code: "FUENTE_NO_DISPONIBLE" }, { status: 503 }) : pdf()) });
    pintar();
    fireEvent.click(screen.getByRole("button", { name: "Descargar liquidación 2024" }));
    expect(await screen.findByText("No se pudo descargar la liquidación. Intenta de nuevo en un momento.")).toBeInTheDocument();
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Descargar liquidación 2024" }));
    await waitFor(() => expect(descargados).toEqual(["liquidacion-TST001-2024.pdf"]));
    expect(screen.queryByText(/No se pudo descargar la liquidación/)).not.toBeInTheDocument();
  });

  it("accesibilidad: botones nativos type=button, una parada de Tab por control y sin envoltorios enfocables", () => {
    const { container } = pintar();
    for (const b of screen.getAllByRole("button")) {
      expect(b.tagName).toBe("BUTTON");
      expect(b).toHaveAttribute("type", "button");
    }
    expect(container.querySelectorAll("[tabindex]:not(button)")).toHaveLength(0);
  });
});
