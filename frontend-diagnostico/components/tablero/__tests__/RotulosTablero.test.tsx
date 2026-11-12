import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CeldaSemaforo, ChipEstadoCelda } from "@/components/tablero/CeldaSemaforo";
import { EstadoGlobal } from "@/components/tablero/EstadoGlobal";
import { celda, grilla } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: el chip de la celda, la nota de la N/A y el estado del lote nunca pintan un código interno (Punto 5).
// Usan la etiqueta de la API cuando llega; si el código es desconocido y no hay etiqueta, «Novedad sin clasificar».

afterEach(() => vi.restoreAllMocks());

const enTabla = (nodo: React.ReactNode) =>
  render(
    <table>
      <tbody>
        <tr>{nodo}</tr>
      </tbody>
    </table>,
  );

describe("Tablero — Punto 5: estados y motivos sin códigos", () => {
  it("la nota de una N/A usa motivo_na_etiqueta cuando llega", () => {
    enTabla(
      <CeldaSemaforo
        fuente="SIMIT_LOCATARIO"
        celda={celda(1, "SIMIT_LOCATARIO", "NA", { motivo_na: "SIN_DOCUMENTO_LOCATARIO", motivo_na_etiqueta: "Falta el documento del locatario" } as never)}
        nombreFuente="SIMIT locatario"
        identificador="TST001"
        puedeReprocesar={false}
      />,
    );
    expect(screen.getByText("Falta el documento del locatario")).toBeInTheDocument();
  });

  it("un motivo de N/A desconocido sin etiqueta dice «Novedad sin clasificar», no el código", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    const { container } = enTabla(
      <CeldaSemaforo
        fuente="AVALUO"
        celda={celda(1, "AVALUO", "NA", { motivo_na: "MOTIVO_FUTURO" as never })}
        nombreFuente="Avalúo comercial"
        identificador="TST001"
        puedeReprocesar={false}
      />,
    );
    expect(screen.getByText("Novedad sin clasificar")).toBeInTheDocument();
    expect(container).not.toHaveTextContent("MOTIVO_FUTURO");
  });

  it("el chip con un estado desconocido no se rompe ni pinta el código", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    const { container } = render(<ChipEstadoCelda estado={"ESTADO_FUTURO" as never} />);
    expect(container).toHaveTextContent("Novedad sin clasificar");
    expect(container).not.toHaveTextContent("ESTADO_FUTURO");
  });

  it("el estado del lote usa estado_etiqueta y un estado desconocido nunca sale como código", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    const { unmount } = render(<EstadoGlobal grilla={{ ...grilla(), estado: "COMPLETADO", estado_etiqueta: "Terminado" } as never} sondeando={false} />);
    expect(screen.getByRole("status", { name: "Estado del lote: Terminado" })).toBeInTheDocument();
    unmount();
    const { container } = render(<EstadoGlobal grilla={{ ...grilla(), estado: "ESTADO_FUTURO" } as never} sondeando={false} />);
    expect(screen.getByRole("status", { name: "Estado del lote: Novedad sin clasificar" })).toBeInTheDocument();
    expect(container).not.toHaveTextContent("ESTADO_FUTURO");
  });
});
