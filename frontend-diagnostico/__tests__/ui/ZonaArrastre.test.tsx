import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
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

  it("muestra el archivo elegido con «Quitar» que llama onArchivo(null), y el error externo", () => {
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
    expect(screen.getByRole("alert")).toHaveTextContent("El servidor rechazó el archivo");
  });
});
