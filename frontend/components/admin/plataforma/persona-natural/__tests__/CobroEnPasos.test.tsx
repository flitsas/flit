// Mejora sobre la marcha — Tarifas/Cobro en dos pasos. Los pasos son dobles: aquí solo se prueba la coordinación.
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CobroEnPasos } from "../CobroEnPasos";

function montar(onCambio = vi.fn()) {
  render(
    <CobroEnPasos
      nombrePrecios="Precios"
      onCambio={onCambio}
      precios={(p) => (
        <div>
          <h2 id="pn-tarifas-titulo">Tabla de precios</h2>
          <p>{p.solicitudPrecio ? `Formulario para ${p.solicitudPrecio.tipoTramiteId}` : "Sin formulario"}</p>
          <button onClick={p.onSolicitudCerrada}>Cerrar formulario</button>
          <button onClick={p.onCambio}>Guardar precio</button>
        </div>
      )}
      medios={(m) => (
        <div>
          <p>Tabla de medios · recarga {m.recargar}</p>
          <button onClick={() => m.onDefinirPrecio("t1")}>Definir</button>
          <button onClick={m.onIrAPrecios}>Ir</button>
        </div>
      )}
    />,
  );
  return onCambio;
}

const paso = (n: RegExp) => screen.getByRole("tab", { name: n });

describe("CobroEnPasos", () => {
  it("empieza en el paso 1 y muestra los dos pasos numerados", () => {
    montar();
    expect(paso(/^1 · Precios$/)).toHaveAttribute("aria-selected", "true");
    expect(paso(/^2 · Medios de pago$/)).toHaveAttribute("aria-selected", "false");
    expect(screen.getByText("Tabla de precios")).toBeVisible();
    expect(screen.getByText(/Tabla de medios/)).not.toBeVisible();
  });

  it("«Definir precio» lleva al paso 1 con el trámite elegido y al cerrar vuelve a Medios de pago", async () => {
    montar();
    await userEvent.click(paso(/^2 · /));
    await userEvent.click(screen.getByRole("button", { name: "Definir" }));
    expect(paso(/^1 · /)).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Formulario para t1")).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Cerrar formulario" }));
    expect(paso(/^2 · /)).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Sin formulario")).toBeInTheDocument();
  });

  it("un cambio de precios recarga Medios de pago y avisa a quien monta la pantalla", async () => {
    const onCambio = montar();
    await userEvent.click(screen.getByRole("button", { name: "Guardar precio" }));
    expect(screen.getByText("Tabla de medios · recarga 1")).toBeInTheDocument();
    expect(onCambio).toHaveBeenCalledTimes(1);
  });

  it("«Ir a Precios» desde el paso 2 vuelve al paso 1", async () => {
    montar();
    await userEvent.click(paso(/^2 · /));
    await userEvent.click(screen.getByRole("button", { name: "Ir" }));
    expect(paso(/^1 · /)).toHaveAttribute("aria-selected", "true");
  });
});
