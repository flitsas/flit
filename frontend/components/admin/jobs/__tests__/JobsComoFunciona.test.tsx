import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobsComoFunciona } from "../JobsComoFunciona";

describe("JobsComoFunciona — instructivo SuperAdmin", () => {
  it("es un botón compacto y no despliega el texto en la página", () => {
    render(<JobsComoFunciona />);
    expect(screen.getByRole("button", { name: "Cómo funciona" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.queryByText(/la cadencia ict se configura una sola vez/i)).not.toBeInTheDocument();
  });

  it("abre un modal con el instructivo y se cierra con Entendido", async () => {
    const user = userEvent.setup();
    render(<JobsComoFunciona />);
    await user.click(screen.getByRole("button", { name: "Cómo funciona" }));

    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAccessibleName("Cómo funciona");
    expect((dialog.firstElementChild as HTMLElement).className).toContain("max-w-4xl");
    expect(screen.queryByText(/job_settings/i)).not.toBeInTheDocument();
    expect(screen.getByText(/guardar aplica en menos de un minuto/i)).toBeInTheDocument();
    expect(screen.getByText(/quipux también tiene un solo formulario/i)).toBeInTheDocument();
    expect(screen.getByText(/consultas runt no es confirmación runt/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Entendido" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
