import { fireEvent, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { abrirResumen, DOC_COMPRADOR, DOC_VENDEDOR, resumenVisible } from "./fixtures-envio";
import { nodo } from "./montar-wizard";

const seccion = (nombre: string) => within(resumenVisible()).getByRole("region", { name: nombre });

describe("Paso 4 · Resumen", () => {
  it("reabre el borrador completo en Resumen, con la miga del diseño", async () => {
    await abrirResumen();

    expect(nodo(4)).toHaveAttribute("aria-current", "step");
    expect(screen.getByRole("navigation", { name: "Ruta de navegación" })).toHaveTextContent("Dashboard > Trámites > Trámite A > Resumen");
    expect(screen.getByRole("list", { name: "Pasos del trámite" })).toHaveTextContent(/Diagnóstico.*Actores.*Requisitos.*Resumen.*Envío/);
  });

  it("muestra placa, tipo y valor, y el botón que radica", async () => {
    await abrirResumen();

    const datos = seccion("Datos del trámite");
    expect(datos).toHaveTextContent("PlacaABC123");
    expect(datos).toHaveTextContent("Tipo de TrámiteTrámite A");
    expect(datos).toHaveTextContent("Valor del trámite");
    expect(screen.queryByRole("region", { name: "Elección de facturación" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Datos bancarios FLIT" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Comprobante de pago" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Enviar trámite" })).toBeEnabled();
    expect(screen.queryByRole("button", { name: "Continuar" })).toBeNull();
  });

  it("«Atrás» vuelve a Requisitos", async () => {
    await abrirResumen();

    fireEvent.click(screen.getByRole("button", { name: "Atrás" }));

    expect(nodo(3)).toHaveAttribute("aria-current", "step");
  });

  it("Habeas Data: nada del ciudadano en el almacenamiento, en la URL ni en la consola", async () => {
    const espias = (["log", "info", "warn", "error", "debug"] as const).map((n) => vi.spyOn(console, n).mockImplementation(() => undefined));
    await abrirResumen();

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(window.location.href).not.toMatch(new RegExp(`${DOC_VENDEDOR}|${DOC_COMPRADOR}|ABC123`));
    for (const e of espias) expect(e).not.toHaveBeenCalled();
    vi.restoreAllMocks();
  });
});
