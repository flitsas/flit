// AC7 — Cuatro estados UI (cargando, vacío, error, lleno).
//
// Uso de ejemplo:
//   render(<UiStateBoundary status="loading">contenido</UiStateBoundary>);
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { UiStateBoundary } from "../UiStateBoundary";

describe("UiStateBoundary (AC7)", () => {
  it("muestra el skeleton en estado cargando", () => {
    render(
      <UiStateBoundary status="loading">
        <p>contenido</p>
      </UiStateBoundary>,
    );
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
    expect(screen.queryByText("contenido")).not.toBeInTheDocument();
  });

  it("muestra el estado vacío con CTA", () => {
    render(
      <UiStateBoundary status="empty" emptyMessage="Sin datos" emptyCta={<button>Crear</button>}>
        <p>contenido</p>
      </UiStateBoundary>,
    );
    expect(screen.getByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByText("Sin datos")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Crear" })).toBeInTheDocument();
  });

  it("muestra el estado de error y dispara onRetry", () => {
    const onRetry = vi.fn();
    render(
      <UiStateBoundary status="error" errorMessage="Falló" onRetry={onRetry}>
        <p>contenido</p>
      </UiStateBoundary>,
    );
    expect(screen.getByText("Falló")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("renderiza los hijos en estado lleno", () => {
    render(
      <UiStateBoundary status="ready">
        <p>contenido</p>
      </UiStateBoundary>,
    );
    expect(screen.getByText("contenido")).toBeInTheDocument();
  });
});
