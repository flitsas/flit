// Expediente — trazabilidad cronológica: los estados del historial real N 03 se pintan
// en orden ascendente (fecha/hora) con labels en español, aunque lleguen desordenados.
import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import ExpedienteTimeline from "@/components/operacion/ExpedienteTimeline";
import type { StatusHistory } from "@/lib/api/types/procedure-runtime";

const desordenado: StatusHistory[] = [
  { fromStatus: "entregado", toStatus: "aprobado", changedAt: "2026-07-06T12:00:00Z", reason: null },
  { fromStatus: null, toStatus: "borrador", changedAt: "2026-07-06T09:00:00Z", reason: null },
  { fromStatus: "preparado", toStatus: "entregado", changedAt: "2026-07-06T11:00:00Z", reason: null },
  { fromStatus: "borrador", toStatus: "preparado", changedAt: "2026-07-06T10:00:00Z", reason: "Gates OK" },
];

describe("ExpedienteTimeline — trazabilidad cronológica", () => {
  it("ordena los estados por fecha/hora ascendente con labels en español", () => {
    render(<ExpedienteTimeline statusHistory={desordenado} />);

    const items = screen.getAllByRole("listitem");
    const labels = items.map((li) => within(li).getAllByText(/.+/)[0].textContent);
    expect(labels[0]).toContain("Borrador");
    expect(labels[1]).toContain("Preparado");
    expect(labels[2]).toContain("Entregado");
    expect(labels[3]).toContain("Aprobado");
  });

  it("muestra el estado origen y el motivo cuando existen", () => {
    render(<ExpedienteTimeline statusHistory={desordenado} />);
    // El hito se pinta como UNA sola frase —«Preparado desde Borrador (Gates OK)»—, así que el
    // motivo se busca como parte de ella y no como nodo suelto: es lo mismo que se comprobaba
    // antes de que el componente adoptara el formato literal de la propuesta.
    expect(screen.getByText(/desde Borrador/i)).toBeInTheDocument();
    expect(screen.getByText(/\(Gates OK\)/)).toBeInTheDocument();
  });

  it("estado vacío", () => {
    render(<ExpedienteTimeline statusHistory={[]} />);
    expect(screen.getByText(/Sin eventos registrados/i)).toBeInTheDocument();
  });
});
