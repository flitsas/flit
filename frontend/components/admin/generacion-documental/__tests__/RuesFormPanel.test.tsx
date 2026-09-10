// HU-01 (Feature #12201) — CF-22: los cuatro estados de UI del panel de Certificado RUES.
// Uso de ejemplo: <RuesFormPanel status="error" onRetry={fn} />
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RuesFormPanel } from "../RuesFormPanel";
import { TransferenciaPanel } from "../TransferenciaPanel";

describe("RuesFormPanel — cuatro estados de UI (CF-22)", () => {
  it("estado vacío: explica que no hay consulta en curso (no un panel en blanco)", () => {
    render(<RuesFormPanel />);
    expect(screen.getByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByText(/no hay una consulta RUES en curso/i)).toBeInTheDocument();
  });

  it("estado cargando: se anuncia por rol status, no solo por color", () => {
    render(<RuesFormPanel status="loading" />);
    const loading = screen.getByTestId("ui-loading");
    expect(loading).toHaveAttribute("role", "status");
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(screen.getByText("Cargando…")).toBeInTheDocument();
  });

  it("estado error: role=alert con texto y acción de reintento alcanzable por teclado", async () => {
    const onRetry = vi.fn();
    render(<RuesFormPanel status="error" onRetry={onRetry} />);

    expect(screen.getByTestId("ui-error")).toHaveAttribute("role", "alert");
    expect(screen.getByText(/no se pudo consultar el RUES/i)).toBeInTheDocument();

    const retry = screen.getByRole("button", { name: /reintentar/i });
    retry.focus();
    expect(retry).toHaveFocus();
    await userEvent.keyboard("{Enter}");
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("estado lleno: renderiza el contenido recibido", () => {
    render(
      <RuesFormPanel status="ready">
        <p>Razón social consultada</p>
      </RuesFormPanel>,
    );
    expect(screen.getByText("Razón social consultada")).toBeInTheDocument();
    expect(screen.queryByTestId("ui-empty")).not.toBeInTheDocument();
  });

  it("el panel tiene encabezado accesible asociado a la sección", () => {
    render(<RuesFormPanel />);
    const heading = screen.getByRole("heading", { name: "Certificado RUES" });
    expect(heading).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Certificado RUES" })).toBeInTheDocument();
  });

  it("no lanza cuando onRetry es undefined en estado error", () => {
    expect(() => render(<RuesFormPanel status="error" />)).not.toThrow();
  });
});

describe("TransferenciaPanel — cuatro estados de UI (CF-22)", () => {
  it("estado vacío explicado", () => {
    render(<TransferenciaPanel />);
    expect(screen.getByTestId("ui-empty")).toBeInTheDocument();
  });

  it("estados cargando, error y lleno", () => {
    const { rerender } = render(<TransferenciaPanel status="loading" />);
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();

    rerender(<TransferenciaPanel status="error" onRetry={vi.fn()} />);
    expect(screen.getByTestId("ui-error")).toBeInTheDocument();

    rerender(
      <TransferenciaPanel status="ready">
        <p>Formulario</p>
      </TransferenciaPanel>,
    );
    expect(screen.getByText("Formulario")).toBeInTheDocument();
  });
});
