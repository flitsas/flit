import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, within } from "@testing-library/react";
import { VistaDetalleCelda } from "@/components/tablero/DetalleCelda";
import type { AuditoriaConsulta, DetalleCelda, ResultadoSimitDetalle } from "@/lib/tablero/detalle";
import { persona } from "@/lib/tablero/__tests__/datosDetalle";

// Uso de ejemplo: <VistaDetalleCelda detalle={detalleSimit({ paz_y_salvo: true })} /> pinta el resumen SIMIT tipado (B4)
// y, si llega `auditoria` (B5), la auditoría con la lista de intentos. Datos sintéticos.

const simit = (extra: Partial<ResultadoSimitDetalle> = {}): ResultadoSimitDetalle => ({
  semaforo: null,
  cantidad_comparendos: 0,
  comparendos: [],
  comparendos_truncados: false,
  ...extra,
});

const detalleSimit = (resultado: ResultadoSimitDetalle, auditoria?: AuditoriaConsulta | null): DetalleCelda => ({
  id: "celda-1",
  vehiculo_id: "vehiculo-1",
  identificador: "TST001",
  nombre_diagnostico: "Lote de prueba",
  fuente: "SIMIT_VEHICULO",
  estado: "COMPLETADO",
  motivo_na: null,
  tipo_error: null,
  dato_obtenido_en: "2026-11-01T15:00:00Z",
  reutilizada: false,
  resultado,
  personas: null,
  respuesta_fuente: null,
  ...(auditoria !== undefined ? { auditoria } : {}),
});

const resumen = () => screen.getByRole("region", { name: "Resumen SIMIT" });

describe("HU14562 AC40 — SIMIT tipado sin valores inferidos", () => {
  it("muestra total adeudado, paz y salvo, acuerdos, resoluciones e infracción código · descripción", () => {
    render(
      <VistaDetalleCelda
        detalle={detalleSimit(
          simit({
            total_por_pagar: "750000",
            paz_y_salvo: false,
            acuerdos_pago: [{ estado: "VIGENTE", cuotas_abonadas: "2", cuotas_pendientes: "4", pendiente: "300000", fecha: "2026-01-02", organismo: "Org A" }],
            resoluciones: [{ numero: "R-1", fecha: "2026-02-03", estado: "ACTIVA", valor: "100000", organismo: "Org B" }],
            cantidad_resoluciones: 1,
            multas: [
              { numero: "M-1", infracciones: [{ codigo: "C24", descripcion: "Estacionar en sitio prohibido" }] },
              { numero: "M-2", infracciones: [{ codigo: "D12", descripcion: null }] },
            ],
          }),
        )}
      />,
    );
    const r = resumen();
    expect(within(r).getByText("$ 750.000")).toBeInTheDocument();
    expect(within(r).getByText("No está a paz y salvo")).toBeInTheDocument();
    expect(within(r).getByRole("table", { name: "Acuerdos de pago" })).toBeInTheDocument();
    const tablaRes = within(r).getByRole("table", { name: "Resoluciones" });
    expect(within(tablaRes).getByText("R-1")).toBeInTheDocument();
    expect(within(r).getByText("1 resolución")).toBeInTheDocument();
    expect(within(r).getByText("C24 · Estacionar en sitio prohibido")).toBeInTheDocument();
    expect(within(r).getByText("D12")).toBeInTheDocument();
  });

  it("negativo: sin paz y salvo, total ni resoluciones informados no infiere nada", () => {
    render(<VistaDetalleCelda detalle={detalleSimit(simit({ cantidad_resoluciones: null }))} />);
    const r = resumen();
    expect(within(r).getByText("La fuente no informó paz y salvo")).toBeInTheDocument();
    expect(within(r).getByText("Sin valor informado")).toBeInTheDocument();
    expect(within(r).getByText("La fuente no informó resoluciones")).toBeInTheDocument();
    expect(within(r).queryByText("$ 0")).not.toBeInTheDocument();
  });
});

describe("HU14562 AC41 — Auditoría de la consulta visible", () => {
  const auditoria: AuditoriaConsulta = {
    inicio: "2026-11-01T15:00:00Z",
    fin: "2026-11-01T15:00:04Z",
    duracion_ms: 4200,
    intentos: 2,
    intentos_detalle: [
      { n: 1, inicio: "2026-11-01T15:00:00Z", fin: "2026-11-01T15:00:01Z", duracion_ms: 1000, resultado: "ERROR", disparador: "AUTOMATICO", desde_cache: false, tipo_error: "TIMEOUT" },
      { n: 2, inicio: "2026-11-01T15:00:02Z", fin: "2026-11-01T15:00:04Z", duracion_ms: 2000, resultado: "EXITOSO", disparador: "REPROCESO", desde_cache: false },
    ],
  };

  it("muestra fuente, estado, inicio, fin, duración, intentos y la lista con resultado y disparador", () => {
    render(<VistaDetalleCelda detalle={detalleSimit(simit(), auditoria)} />);
    const a = screen.getByRole("region", { name: "Auditoría de consulta" });
    expect(within(a).getByText("SIMIT vehículo")).toBeInTheDocument();
    const valor = (termino: string) => within(a).getByText(termino, { selector: "dt" }).nextElementSibling?.textContent;
    expect(valor("Inicio")).toBe("01/11/2026 10:00");
    expect(valor("Fin")).toBe("01/11/2026 10:00");
    expect(valor("Duración")).toBe("4,2 s");
    expect(valor("Intentos")).toBe("2");
    const tabla = within(a).getByRole("table", { name: "Intentos de la consulta" });
    const filas = within(tabla).getAllByRole("row");
    expect(filas).toHaveLength(3);
    expect(within(filas[1]).getByText("Error")).toBeInTheDocument();
    expect(within(filas[1]).getByText("Automático")).toBeInTheDocument();
    expect(within(filas[2]).getByText("Exitoso")).toBeInTheDocument();
    expect(within(filas[2]).getByText("Reproceso")).toBeInTheDocument();
  });

  // HU #14562 · D2 (axe scrollable-region-focusable): cada tabla con scroll horizontal vive en una región con nombre y
  // foco visible. O-4 (auditoría D1–D3): solo es parada de Tab cuando de verdad desborda (scrollWidth > clientWidth);
  // si la tabla cabe, no suma una parada vacía. jsdom no mide: se simulan los anchos y el ResizeObserver.
  describe("D2 / O-4: regiones desplazables", () => {
    const TABLAS = ["Intentos de la consulta", "Infracciones", "Personas consultadas"];
    const conTodo = (): DetalleCelda => ({
      ...detalleSimit(
        simit({ multas: [{ numero: "M-1", infracciones: [{ codigo: "C24", descripcion: "Estacionar en sitio prohibido" }] }] }),
        auditoria,
      ),
      personas: [persona(1)],
    });

    let anchoContenido = 300;
    const observadores: Array<() => void> = [];
    beforeEach(() => {
      anchoContenido = 300;
      observadores.length = 0;
      vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockImplementation(function (this: HTMLElement) {
        return this.classList.contains("overflow-x-auto") ? 300 : 0;
      });
      vi.spyOn(HTMLElement.prototype, "scrollWidth", "get").mockImplementation(function (this: HTMLElement) {
        return this.classList.contains("overflow-x-auto") ? anchoContenido : 0;
      });
      vi.stubGlobal(
        "ResizeObserver",
        class {
          constructor(private cb: ResizeObserverCallback) {
            observadores.push(() => this.cb([], this as unknown as ResizeObserver));
          }
          observe() {}
          unobserve() {}
          disconnect() {}
        },
      );
    });
    afterEach(() => {
      vi.restoreAllMocks();
      vi.unstubAllGlobals();
    });

    it("con la tabla más ancha que su contenedor, la región es enfocable, con nombre y foco --di-action", () => {
      anchoContenido = 720;
      render(<VistaDetalleCelda detalle={conTodo()} />);
      for (const nombre of TABLAS) {
        const region = screen.getByRole("region", { name: `${nombre}, desplazable` });
        expect(region).toHaveAttribute("tabindex", "0");
        expect(region).toHaveClass("overflow-x-auto");
        expect(region.className).toMatch(/focus-visible:outline-\[var\(--di-action\)\]/);
        expect(within(region).getByRole("table", { name: nombre })).toBeInTheDocument();
      }
    });

    it("negativo: si la tabla cabe, la región conserva rol y nombre pero no es parada de Tab", () => {
      anchoContenido = 300;
      render(<VistaDetalleCelda detalle={conTodo()} />);
      for (const nombre of TABLAS) {
        const region = screen.getByRole("region", { name: `${nombre}, desplazable` });
        expect(region).not.toHaveAttribute("tabindex");
        expect(within(region).getByRole("table", { name: nombre })).toBeInTheDocument();
      }
    });

    it("al cambiar el ancho (ResizeObserver), la parada de Tab aparece y desaparece", () => {
      anchoContenido = 300;
      render(<VistaDetalleCelda detalle={conTodo()} />);
      const region = () => screen.getByRole("region", { name: "Personas consultadas, desplazable" });
      expect(region()).not.toHaveAttribute("tabindex");

      anchoContenido = 720;
      act(() => observadores.forEach((avisar) => avisar()));
      expect(region()).toHaveAttribute("tabindex", "0");

      anchoContenido = 300;
      act(() => observadores.forEach((avisar) => avisar()));
      expect(region()).not.toHaveAttribute("tabindex");
    });
  });

  it("sin auditoria (API anterior) la lista de intentos no aparece y el detalle funciona", () => {
    render(<VistaDetalleCelda detalle={detalleSimit(simit())} />);
    expect(screen.queryByRole("table", { name: "Intentos de la consulta" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Respuesta de la fuente" })).toBeInTheDocument();
  });
});
