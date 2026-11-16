import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { ResumenValidacion } from "@/components/carga/ResumenValidacion";
import { EstimadoConsultas } from "@/components/carga/EstimadoConsultas";
import { resumen } from "./datos";

// Uso de ejemplo: <ResumenValidacion resumen={r} onCorregir={...} onContinuar={...} /> pinta los conteos del análisis,
// los avisos y el estimado (solo si llega) y deja «Continuar solo con los válidos» deshabilitado con su motivo.

const continuar = () => screen.getByRole("button", { name: /continuar solo con los válidos/i });

describe("ResumenValidacion — conteos (AC 3)", () => {
  it("muestra válidos, erróneos y duplicados", () => {
    render(<ResumenValidacion resumen={resumen({ validos: 7, erroneos: 2, duplicados: 3, total_filas: 12 })} onContinuar={vi.fn()} />);
    const region = screen.getByRole("region", { name: "Resumen de validación" });
    expect(within(region).getByText("Válidos").nextSibling).toHaveTextContent("7");
    expect(within(region).getByText("Erróneos").nextSibling).toHaveTextContent("2");
    expect(within(region).getByText("Duplicados").nextSibling).toHaveTextContent("3");
    expect(region).toHaveTextContent(/12 filas leídas/i);
  });

  it("Continuar habilitado llama a onContinuar y Corregir ahora a onCorregir", () => {
    const onContinuar = vi.fn();
    const onCorregir = vi.fn();
    render(<ResumenValidacion resumen={resumen()} onContinuar={onContinuar} onCorregir={onCorregir} />);
    fireEvent.click(continuar());
    fireEvent.click(screen.getByRole("button", { name: /corregir ahora/i }));
    expect(onContinuar).toHaveBeenCalledOnce();
    expect(onCorregir).toHaveBeenCalledOnce();
  });

  it("sin filas erróneas no ofrece Corregir ahora", () => {
    render(<ResumenValidacion resumen={resumen({ erroneos: 0 })} onContinuar={vi.fn()} onCorregir={vi.fn()} />);
    expect(screen.queryByRole("button", { name: /corregir ahora/i })).not.toBeInTheDocument();
  });
});

describe("ResumenValidacion — sin válidos o sobre el límite (AC 6)", () => {
  it("sin válidos la acción está deshabilitada con el motivo visible y asociado", () => {
    const onContinuar = vi.fn();
    render(
      <ResumenValidacion
        resumen={resumen({ validos: 0, erroneos: 3, duplicados: 0, puede_continuar: false, avisos: [{ code: "SIN_VALIDOS", mensaje: "El archivo no tiene vehículos válidos." }] })}
        onContinuar={onContinuar}
      />,
    );
    expect(continuar()).toBeDisabled();
    expect(continuar()).toHaveAccessibleDescription(/no tiene vehículos válidos/i);
    fireEvent.click(continuar());
    expect(onContinuar).not.toHaveBeenCalled();
  });

  it("por encima del límite la acción está deshabilitada y el motivo trae el límite", () => {
    render(
      <ResumenValidacion
        resumen={resumen({ validos: 600, puede_continuar: false, supera_limite: true, limite_vehiculos_lote: 500, avisos: [] })}
        onContinuar={vi.fn()}
      />,
    );
    expect(continuar()).toBeDisabled();
    expect(continuar()).toHaveAccessibleDescription(/600.*500/);
  });

  it("sin aviso de la API y sin válidos el motivo lo pone la pantalla", () => {
    render(<ResumenValidacion resumen={resumen({ validos: 0, puede_continuar: false, avisos: [] })} onContinuar={vi.fn()} />);
    expect(continuar()).toHaveAccessibleDescription(/no hay vehículos válidos/i);
  });
});

describe("EstimadoConsultas — antes de lanzar (AC 7)", () => {
  it("con estimado (ANTES_DE_LANZAR) aparece por fuente dentro del resumen", () => {
    render(
      <ResumenValidacion
        resumen={resumen({ estimado_consultas: [{ fuente: "RUNT", consultas: 2 }, { fuente: "SIMIT_LOCATARIO", consultas: 1 }] })}
        onContinuar={vi.fn()}
      />,
    );
    const estimado = screen.getByRole("region", { name: "Estimado de consultas" });
    expect(within(estimado).getByText("RUNT vehículo").nextSibling).toHaveTextContent("2");
    expect(within(estimado).getByText("SIMIT locatario").nextSibling).toHaveTextContent("1");
  });

  it("sin estimado (NO_MOSTRAR: la propiedad no viene) no aparece", () => {
    render(<ResumenValidacion resumen={resumen()} onContinuar={vi.fn()} />);
    expect(screen.queryByRole("region", { name: "Estimado de consultas" })).not.toBeInTheDocument();
  });

  it("EstimadoConsultas sin datos o vacío no pinta nada", () => {
    const { container, rerender } = render(<EstimadoConsultas />);
    expect(container).toBeEmptyDOMElement();
    rerender(<EstimadoConsultas estimado={[]} />);
    expect(container).toBeEmptyDOMElement();
  });
});
