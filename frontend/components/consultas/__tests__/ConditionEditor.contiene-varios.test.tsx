import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ConditionEditor } from "@/components/consultas/QueryFilterBar";
import type { QueryField } from "@/lib/api/queries";

const CAMPO = {
  id: "placa",
  label: "Placa",
  kind: "texto",
  operators: ["es_alguno", "contiene"],
  options: [],
} as unknown as QueryField;

function montar(contieneAdmiteVarios?: boolean) {
  render(
    <ConditionEditor
      field={CAMPO}
      value={null}
      onApply={() => {}}
      onRemove={() => {}}
      testIdPrefix="t"
      anchored={false}
      contieneAdmiteVarios={contieneAdmiteVarios}
    />,
  );
}

describe("ConditionEditor · «contiene» con varios valores", () => {
  it("con true anuncia varios por coma y cuenta", async () => {
    montar(true);
    await userEvent.selectOptions(screen.getByLabelText("Operador"), "contiene");
    const caja = screen.getByTestId("t-valores-placa");
    expect(caja).toHaveAttribute("placeholder", "Parte del texto · varios separados por coma");
    await userEvent.type(caja, "AB, CD");
    expect(screen.getByText("2 valores")).toBeInTheDocument();
  });

  it("con false (por defecto) vuelve al texto de un valor y no cuenta, pero «es» sí admite varios", async () => {
    montar();
    const caja = screen.getByTestId("t-valores-placa");
    expect(caja).toHaveAttribute("placeholder", "Varios valores separados por coma, o uno por línea");
    await userEvent.selectOptions(screen.getByLabelText("Operador"), "contiene");
    expect(caja).toHaveAttribute("placeholder", "Parte del texto…");
    await userEvent.type(caja, "AB, CD");
    expect(screen.queryByText("2 valores")).not.toBeInTheDocument();
  });
});
