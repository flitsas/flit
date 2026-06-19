// AC3 — Whitelist con textarea y validación inline por línea. Los inválidos
// muestran error con el número de línea y la textarea NO se limpia.
//
// Uso de ejemplo:
//   render(<WhitelistTextarea entries={[]} onSave={spy} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { WhitelistTextarea } from "../WhitelistTextarea";
import { ApiValidationError } from "@/lib/api/types";

describe("WhitelistTextarea (AC3)", () => {
  it("muestra error inline con el número de línea y no limpia la textarea", async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(undefined);

    render(<WhitelistTextarea entries={[]} onSave={onSave} />);

    const textarea = screen.getByLabelText(/correos de la lista blanca/i);
    await user.type(textarea, "valido@dominio.com\nesto-no-es-correo");
    await user.click(screen.getByRole("button", { name: /guardar lista blanca/i }));

    const errors = screen.getByTestId("whitelist-errors");
    expect(errors).toHaveTextContent(/línea 2/i);
    expect(onSave).not.toHaveBeenCalled(); // no se persiste con líneas inválidas
    expect(textarea).toHaveValue("valido@dominio.com\nesto-no-es-correo"); // no se limpia
  });

  it("persiste correos válidos y limpia la textarea en éxito", async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(undefined);

    render(<WhitelistTextarea entries={[]} onSave={onSave} />);

    const textarea = screen.getByLabelText(/correos de la lista blanca/i);
    await user.type(textarea, "uno@dominio.com, dos@dominio.com");
    await user.click(screen.getByRole("button", { name: /guardar lista blanca/i }));

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith(["uno@dominio.com", "dos@dominio.com"]),
    );
    expect(await screen.findByText(/lista blanca actualizada correctamente/i)).toBeInTheDocument();
    expect(textarea).toHaveValue("");
  });

  it("mapea los errores 422 del backend a su línea", async () => {
    const user = userEvent.setup();
    const onSave = vi
      .fn()
      .mockRejectedValue(
        new ApiValidationError(
          [{ field: "emails", message: "Correo ya existente.", value: "dup@dominio.com" }],
          422,
        ),
      );

    render(<WhitelistTextarea entries={[]} onSave={onSave} />);

    const textarea = screen.getByLabelText(/correos de la lista blanca/i);
    await user.type(textarea, "ok@dominio.com\ndup@dominio.com");
    await user.click(screen.getByRole("button", { name: /guardar lista blanca/i }));

    const errors = await screen.findByTestId("whitelist-errors");
    expect(errors).toHaveTextContent(/línea 2/i);
    expect(errors).toHaveTextContent(/correo ya existente/i);
    expect(textarea).toHaveValue("ok@dominio.com\ndup@dominio.com"); // no se limpia
  });

  it("muestra los correos exentos actuales", () => {
    render(
      <WhitelistTextarea
        entries={[{ email: "existente@dominio.com", createdAt: "2026-01-01T00:00:00Z", addedBy: null }]}
        onSave={vi.fn()}
      />,
    );
    expect(screen.getByText("existente@dominio.com")).toBeInTheDocument();
  });
});
