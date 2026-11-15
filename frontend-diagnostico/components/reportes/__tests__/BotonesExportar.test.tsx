import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { BotonesExportar } from "@/components/reportes/BotonesExportar";
import { ApiError } from "@/lib/api";
import { ErrorDescargaReporte } from "@/lib/reportes/api";
import { CONSULTAS, VEHICULOS } from "@/lib/reportes/__tests__/datosReportes";

// Uso de ejemplo: <BotonesExportar reporte={reporte} total={pagina.total} cargando={false} avisoId="aviso-pii"
// onDescargar={(formato) => descargarReporte(ambito, tipo, consulta, formato)} />. Exporta TODO el filtro; los máximos
// salen del catálogo; 422/429 se explican bajo los botones; la tabla no cae.

const props = { reporte: VEHICULOS, total: 1830, cargando: false, avisoId: "aviso-pii", onDescargar: vi.fn(async () => {}) };
const excel = () => screen.getByRole("button", { name: /Exportar Excel/ });
const pdf = () => screen.getByRole("button", { name: /Exportar PDF/ });

describe("BotonesExportar — AC descargas", () => {
  it("dice cuántos registros exporta, avisa los datos personales y apunta al aviso con aria-describedby", () => {
    render(<BotonesExportar {...props} />);
    expect(screen.getByText("Se exportan los 1.830 registros del filtro")).toBeInTheDocument();
    expect(screen.getByText("Contiene datos personales")).toBeInTheDocument();
    expect(excel().getAttribute("aria-describedby")).toContain("aviso-pii");
  });

  it("sin datos personales no lleva la leyenda", () => {
    render(<BotonesExportar {...props} reporte={CONSULTAS} />);
    expect(screen.queryByText("Contiene datos personales")).not.toBeInTheDocument();
  });

  it("si el total supera el máximo de un formato, ese botón queda deshabilitado con el motivo y el otro no", () => {
    const onDescargar = vi.fn(async () => {});
    render(<BotonesExportar {...props} total={1830} onDescargar={onDescargar} />);
    expect(pdf()).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByText("Acota el filtro: máximo 1.000 registros en PDF")).toBeInTheDocument();
    expect(excel()).not.toHaveAttribute("aria-disabled", "true");
    fireEvent.click(pdf());
    expect(onDescargar).not.toHaveBeenCalled();
  });

  it("vacío o cargando: los dos deshabilitados con motivo visible", () => {
    const { rerender } = render(<BotonesExportar {...props} total={0} />);
    expect(excel()).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByText("No hay registros para exportar")).toBeInTheDocument();
    rerender(<BotonesExportar {...props} total={null} cargando />);
    expect(pdf()).toHaveAttribute("aria-disabled", "true");
  });

  it("en vuelo: «Generando…» con aria-busy y el otro formato sigue activo", async () => {
    let terminar: () => void = () => {};
    const onDescargar = vi.fn(() => new Promise<void>((r) => (terminar = r)));
    render(<BotonesExportar {...props} total={500} onDescargar={onDescargar} />);
    fireEvent.click(excel());
    expect(onDescargar).toHaveBeenCalledWith("excel");
    const enVuelo = screen.getByRole("button", { name: /Generando/ });
    expect(enVuelo).toHaveAttribute("aria-busy", "true");
    expect(screen.getByText("Puede tardar unos segundos")).toBeInTheDocument();
    expect(pdf()).not.toHaveAttribute("aria-disabled", "true");
    await act(async () => terminar());
    expect(screen.getByRole("button", { name: /Exportar Excel/ })).not.toHaveAttribute("aria-busy", "true");
  });

  it("422 REPORTE_DEMASIADO_GRANDE muestra filas y máximo; 429 pide esperar (role=alert)", async () => {
    const onDescargar = vi
      .fn()
      .mockRejectedValueOnce(new ErrorDescargaReporte(422, "x", "REPORTE_DEMASIADO_GRANDE", 6120, 5000))
      .mockRejectedValueOnce(new ErrorDescargaReporte(429, "x", "EXPORTACION_EN_CURSO", undefined, undefined, 10));
    render(<BotonesExportar {...props} total={500} onDescargar={onDescargar} />);
    fireEvent.click(excel());
    expect(await screen.findByRole("alert")).toHaveTextContent("El reporte tiene 6.120 registros y el Excel admite 5.000. Acota el rango o los filtros.");
    fireEvent.click(excel());
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Ya hay una exportación en curso. Espera 10 segundos e intenta de nuevo."));
  });

  it("un 403 se traduce a texto fijo, nunca el mensaje del servidor", async () => {
    const onDescargar = vi.fn().mockRejectedValue(new ApiError(403, "detalle interno", "SIN_PERMISO"));
    render(<BotonesExportar {...props} total={500} onDescargar={onDescargar} />);
    fireEvent.click(excel());
    expect(await screen.findByRole("alert")).toHaveTextContent("No tienes permiso para descargar reportes.");
  });

  it("CR-FE-5: el error se limpia cuando cambia la consulta (claveConsulta) y no con un rerender de la misma", async () => {
    const onDescargar = vi.fn().mockRejectedValue(new ErrorDescargaReporte(429, "x", "EXPORTACION_EN_CURSO", undefined, undefined, 10));
    const { rerender } = render(<BotonesExportar {...props} total={500} claveConsulta="a" onDescargar={onDescargar} />);
    fireEvent.click(excel());
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    rerender(<BotonesExportar {...props} total={500} claveConsulta="a" onDescargar={onDescargar} />);
    expect(screen.getByRole("alert")).toBeInTheDocument();
    rerender(<BotonesExportar {...props} total={480} claveConsulta="b" onDescargar={onDescargar} />);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
