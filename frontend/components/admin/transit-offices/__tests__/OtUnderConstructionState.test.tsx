import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { OtUnderConstructionState } from "../OtUnderConstructionState";

describe("OtUnderConstructionState", () => {
  it("expone estado accesible con título y descripción", () => {
    render(
      <OtUnderConstructionState
        testId="ot-uc"
        title="Reportes en construcción"
        description="Módulo aún no disponible."
      />,
    );
    const status = screen.getByTestId("ot-uc");
    expect(status).toHaveAttribute("role", "status");
    expect(screen.getByRole("heading", { name: "Reportes en construcción" })).toBeInTheDocument();
    expect(screen.getByText("Módulo aún no disponible.")).toBeInTheDocument();
  });
});
