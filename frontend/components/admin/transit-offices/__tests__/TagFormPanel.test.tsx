// HU #12883 AC1/AC3 — TagFormPanel deja de ofrecer un `<input type="color">` de libre elección:
// paleta CERRADA de colores de marca FLIT como radiogroup accesible, sin texto bajo el piso
// tipográfico y sin el hex de default fuera de tokens (#FF0000).
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TagFormPanel } from "../TagFormPanel";
import type { OtDocumentTag } from "@/lib/api/types-ot";

function renderPanel(onCreate = vi.fn(), onSaved = vi.fn()) {
  const utils = render(
    <TagFormPanel open onClose={vi.fn()} onCreate={onCreate} onSaved={onSaved} />,
  );
  return { ...utils, onCreate, onSaved };
}

describe("TagFormPanel — HU #12883 AC1/AC3", () => {
  it("AC1 no usa clases bajo el piso tipográfico ni el hex #FF0000 como color", () => {
    const { container } = renderPanel();
    expect(container.innerHTML).not.toMatch(/text-\[1[01]px\]/);
    expect(container.innerHTML).not.toMatch(/#FF0000/i);
  });

  it("AC3 ofrece una paleta CERRADA de colores como radiogroup accesible", () => {
    renderPanel();
    const group = screen.getByRole("radiogroup", { name: "Color de la etiqueta" });
    expect(group).toBeInTheDocument();

    // Las 6 opciones de marca FLIT, cada una con nombre accesible.
    ["Azul", "Cian", "Verde", "Naranja", "Navy", "Ámbar"].forEach((name) => {
      expect(screen.getByRole("radio", { name })).toBeInTheDocument();
    });

    // Azul (#557EFF) es el color por defecto al abrir.
    expect(screen.getByRole("radio", { name: "Azul" })).toBeChecked();
  });

  it("AC3 elegir un swatch cambia el color enviado al crear", async () => {
    const onCreate = vi.fn().mockResolvedValue({
      id: "tag-1",
      code: "URGENTE",
      name: "Urgente",
      color: "#FF4E00",
    } satisfies OtDocumentTag);
    const user = userEvent.setup();
    renderPanel(onCreate);

    await user.type(screen.getByLabelText("Código"), "URGENTE");
    await user.type(screen.getByLabelText("Nombre"), "Urgente");
    await user.click(screen.getByRole("radio", { name: "Naranja" }));
    await user.click(screen.getByRole("button", { name: /^Guardar$/i }));

    await waitFor(() =>
      expect(onCreate).toHaveBeenCalledWith({
        code: "URGENTE",
        name: "Urgente",
        color: "#FF4E00",
      }),
    );
  });
});
