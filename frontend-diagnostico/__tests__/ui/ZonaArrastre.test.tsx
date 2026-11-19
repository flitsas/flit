import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { REGION_VIVA_RETARDO_MS } from "@flit/ui/useRegionViva";
import { ZonaArrastre } from "@flit/ui/ZonaArrastre";

// Uso de ejemplo: <ZonaArrastre etiqueta="Archivo de vehículos" aceptar={[".xlsx", ".csv"]} maxBytes={5_000_000}
//   archivo={archivo} onArchivo={setArchivo} ayuda="Excel o CSV, hasta 5 MB" />

function archivo(nombre: string, bytes = 10) {
  return new File(["x".repeat(bytes)], nombre);
}

function base(onArchivo = vi.fn()) {
  return render(
    <ZonaArrastre etiqueta="Archivo de vehículos" aceptar={[".xlsx", ".csv"]} maxBytes={100} onArchivo={onArchivo} />,
  );
}

describe("ZonaArrastre (HU #14562 · AC10)", () => {
  it("AC10: el botón «Elegir archivo» es alcanzable por teclado y abre el selector sin arrastrar", () => {
    base();
    const boton = screen.getByRole("button", { name: /Elegir archivo/ });
    boton.focus();
    expect(document.activeElement).toBe(boton);
    const input = screen.getByLabelText("Archivo de vehículos") as HTMLInputElement;
    expect(input).toHaveAttribute("type", "file");
    expect(input).toHaveAttribute("accept", ".xlsx,.csv");
    const clic = vi.spyOn(input, "click");
    fireEvent.click(boton);
    expect(clic).toHaveBeenCalled();
  });

  it("AC10: un archivo válido elegido llama onArchivo", () => {
    const onArchivo = vi.fn();
    base(onArchivo);
    const f = archivo("lote.CSV");
    fireEvent.change(screen.getByLabelText("Archivo de vehículos"), { target: { files: [f] } });
    expect(onArchivo).toHaveBeenCalledWith(f);
  });

  it("AC10 negativo: otra extensión se rechaza con error asociado por aria-describedby, sin llamar onArchivo", () => {
    const onArchivo = vi.fn();
    base(onArchivo);
    fireEvent.change(screen.getByLabelText("Archivo de vehículos"), { target: { files: [archivo("foto.png")] } });
    expect(onArchivo).not.toHaveBeenCalled();
    const alerta = screen.getByRole("alert");
    expect(alerta).toHaveTextContent(/\.xlsx, \.csv/);
    const boton = screen.getByRole("button", { name: /Elegir archivo/ });
    expect(boton.getAttribute("aria-describedby")).toContain(alerta.id);
  });

  it("AC10 negativo: un archivo que supera maxBytes se rechaza sin llamar onArchivo", () => {
    const onArchivo = vi.fn();
    base(onArchivo);
    fireEvent.change(screen.getByLabelText("Archivo de vehículos"), { target: { files: [archivo("lote.xlsx", 101)] } });
    expect(onArchivo).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent(/tamaño máximo/);
  });

  it("AC10 negativo: soltar un archivo inválido por arrastre también se rechaza", () => {
    const onArchivo = vi.fn();
    base(onArchivo);
    const zona = screen.getByTestId("zona-arrastre");
    fireEvent.drop(zona, { dataTransfer: { files: [archivo("x.pdf")] } });
    expect(onArchivo).not.toHaveBeenCalled();
    fireEvent.drop(zona, { dataTransfer: { files: [archivo("ok.xlsx")] } });
    expect(onArchivo).toHaveBeenCalledTimes(1);
  });

  it("muestra el archivo elegido con «Quitar» que llama onArchivo(null), y el error externo", async () => {
    const onArchivo = vi.fn();
    render(
      <ZonaArrastre
        etiqueta="Archivo de vehículos"
        aceptar={[".xlsx"]}
        maxBytes={100}
        archivo={archivo("lote.xlsx")}
        onArchivo={onArchivo}
        error="El servidor rechazó el archivo"
        ayuda="Excel hasta 100 bytes"
      />,
    );
    expect(screen.getByText("lote.xlsx")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Quitar archivo" }));
    expect(onArchivo).toHaveBeenCalledWith(null);
    // Observación 8 del code review: la región nace vacía y recibe el error un instante después (patrón de useRegionViva).
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("El servidor rechazó el archivo"));
  });
});

// Observación 8 del code review (HU #14562): la región `role="alert"` existe vacía desde el montaje, para que el lector
// de pantalla anuncie el primer error. Un error que ya viene al montar se escribe pasado el retardo de useRegionViva.
describe("ZonaArrastre: región de error estable (code review, observación 8)", () => {
  it("sin error, la región role=alert ya existe y está vacía", () => {
    base();
    expect(screen.getByRole("alert")).toBeEmptyDOMElement();
  });

  it("con un error al montar, la región nace vacía y lo recibe tras el retardo", () => {
    vi.useFakeTimers();
    try {
      render(<ZonaArrastre etiqueta="Archivo" aceptar={[".xlsx"]} maxBytes={100} onArchivo={vi.fn()} error="El servidor rechazó el archivo" />);
      expect(screen.getByRole("alert")).toBeEmptyDOMElement();
      act(() => {
        vi.advanceTimersByTime(REGION_VIVA_RETARDO_MS);
      });
      expect(screen.getByRole("alert")).toHaveTextContent("El servidor rechazó el archivo");
    } finally {
      vi.useRealTimers();
    }
  });

  it("un rechazo por la acción del usuario se escribe en la misma región, sin esperar", () => {
    base();
    const region = screen.getByRole("alert");
    fireEvent.change(screen.getByLabelText("Archivo de vehículos", { selector: "input" }), { target: { files: [archivo("x.pdf")] } });
    expect(screen.getByRole("alert")).toBe(region);
    expect(region).toHaveTextContent(/Formato no admitido/);
  });
});
