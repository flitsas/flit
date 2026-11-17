import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { InlineAlert } from "@flit/ui/InlineAlert";

// Uso de ejemplo (UA-4, hueco H-7 de la auditoría UI de HU #14577):
//   <InlineAlert tone="success" title="Saneamiento solicitado" vivo={false}>…</InlineAlert>
// `vivo` es opcional y por defecto `true`: el aviso sigue siendo región viva como siempre. Con `vivo={false}` es contenido
// estático, para los avisos de resultado que ya se anuncian por la región viva de la pantalla o reciben el foco.

describe("InlineAlert — prop vivo (H-7)", () => {
  it("por defecto no cambia: error/aviso/pendiente son alert asertivo; info/éxito son status cortés", () => {
    const { rerender } = render(<InlineAlert tone="warning">Texto</InlineAlert>);
    expect(screen.getByRole("alert")).toHaveAttribute("aria-live", "assertive");
    rerender(<InlineAlert tone="error">Texto</InlineAlert>);
    expect(screen.getByRole("alert")).toHaveAttribute("aria-live", "assertive");
    rerender(<InlineAlert tone="pending">Texto</InlineAlert>);
    expect(screen.getByRole("alert")).toHaveAttribute("aria-live", "assertive");
    rerender(<InlineAlert tone="success">Texto</InlineAlert>);
    expect(screen.getByRole("status")).toHaveAttribute("aria-live", "polite");
    rerender(<InlineAlert tone="info">Texto</InlineAlert>);
    expect(screen.getByRole("status")).toHaveAttribute("aria-live", "polite");
  });

  it("vivo explícito en true se comporta igual que el valor por defecto", () => {
    render(
      <InlineAlert tone="success" vivo>
        Texto
      </InlineAlert>,
    );
    expect(screen.getByRole("status")).toHaveAttribute("aria-live", "polite");
  });

  it("vivo={false}: sin role ni aria-live, con el mismo contenido, título y estilo del tono", () => {
    const { container, rerender } = render(
      <InlineAlert tone="warning" title="Algunos no se tomaron" vivo={false}>
        Se tomó 1 saneamiento.
      </InlineAlert>,
    );
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
    const raiz = container.firstElementChild as HTMLElement;
    expect(raiz).not.toHaveAttribute("aria-live");
    expect(raiz).not.toHaveAttribute("role");
    expect(raiz).toHaveTextContent("Algunos no se tomaron");
    expect(raiz).toHaveTextContent("Se tomó 1 saneamiento.");
    expect(raiz.getAttribute("style")).toContain("var(--badge-warning-bg)");
    rerender(
      <InlineAlert tone="success" vivo={false}>
        Listo
      </InlineAlert>,
    );
    expect(screen.queryByRole("status")).toBeNull();
  });
});
