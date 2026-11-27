import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EditorColumna } from "@/components/atom/FiltroColumna";

describe("Filtro de columna de texto: varios valores", () => {
  it("explica que se aceptan varios separados por coma", async () => {
    render(
      <EditorColumna
        columna={{ id: "placa", label: "Placa", tipo: "texto" }}
        onAplicar={() => {}}
        onQuitar={() => {}}
      />,
    );
    const caja = screen.getByLabelText("Placa contiene");
    expect(caja).toHaveAttribute("placeholder", "Varios separados por coma");
    expect(caja).toHaveAccessibleDescription("Puedes poner varios valores separados por coma.");
    await userEvent.type(caja, "AAA111, BBB222");
    expect(caja).toHaveValue("AAA111, BBB222");
  });
});
