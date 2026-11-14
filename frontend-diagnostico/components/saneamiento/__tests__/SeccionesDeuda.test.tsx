import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { SeccionesDeuda } from "@/components/saneamiento/SeccionesDeuda";
import type { FuenteDeudaSimit } from "@/lib/saneamiento/api";
import { deudaCompleta, deudaSinPendientes } from "./datosDeuda";

// Uso de ejemplo: <SeccionesDeuda deuda={panelDeuda} /> pinta las secciones «Dictamen» y «Deuda por fuente» de PS-2 con los
// datos que recibe por props (no pide nada). La reutiliza la gestión del backoffice (PS-5) en modo lectura.

const tarjeta = (nombre: RegExp) => screen.getByRole("button", { name: nombre });
const contenidoDe = (boton: HTMLElement) => document.getElementById(boton.getAttribute("aria-controls") ?? "") as HTMLElement;

describe("SeccionesDeuda — AC14: dictamen", () => {
  it("muestra el veredicto, la versión del catálogo y los motivos: primero los que bloquean, después las advertencias", () => {
    render(<SeccionesDeuda deuda={deudaCompleta()} />);
    expect(screen.getByRole("heading", { level: 3, name: "Dictamen" })).toBeInTheDocument();
    expect(screen.getByText("Bloqueado")).toBeInTheDocument();
    expect(screen.getByText("Versión del catálogo 2")).toBeInTheDocument();
    const motivos = within(screen.getByRole("list", { name: "Motivos del dictamen" })).getAllByRole("listitem");
    expect(motivos).toHaveLength(2);
    expect(motivos[0]).toHaveTextContent("Bloquea:");
    expect(motivos[0]).toHaveTextContent("El propietario tiene comparendos pendientes.");
    expect(motivos[1]).toHaveTextContent("Advertencia:");
    expect(motivos[1]).toHaveTextContent("Hay impuesto pendiente de una vigencia.");
    // Nunca el código del motivo.
    expect(screen.getByRole("list", { name: "Motivos del dictamen" })).not.toHaveTextContent("SIMIT_PROPIETARIO");
  });

  it("modo Simple (dictamen null): la sección Dictamen no se muestra", () => {
    render(<SeccionesDeuda deuda={deudaSinPendientes()} />);
    expect(screen.queryByRole("heading", { name: "Dictamen" })).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 3, name: "Deuda por fuente" })).toBeInTheDocument();
  });
});

describe("SeccionesDeuda — AC15: deuda por fuente", () => {
  it("una TarjetaPlegable por fuente en el orden fijo, con el estado en texto y abiertas solo las que tienen hallazgo", () => {
    render(<SeccionesDeuda deuda={deudaCompleta()} />);
    // El nombre accesible separa la fuente de su estado («SIMIT del vehículo: Sin verificar»).
    const nombres = screen.getAllByRole("button").map((b) => b.textContent ?? "");
    expect(nombres).toEqual([
      "SIMIT del vehículo: Sin verificar",
      "SIMIT del propietario: Con deuda",
      "SIMIT del locatario: No aplica",
      "Impuesto vehicular: Con deuda",
      "SOAT: No vigente",
      "Revisión técnico-mecánica: Sin pendientes",
    ]);
    // Encabezado de nivel 4 bajo el h3 de la sección.
    expect(screen.getAllByRole("heading", { level: 4 })).toHaveLength(6);
    expect(tarjeta(/^SIMIT del propietario/)).toHaveAttribute("aria-expanded", "true");
    expect(tarjeta(/^SIMIT del vehículo/)).toHaveAttribute("aria-expanded", "true");
    expect(tarjeta(/^SOAT/)).toHaveAttribute("aria-expanded", "true");
    expect(tarjeta(/^Revisión técnico-mecánica/)).toHaveAttribute("aria-expanded", "false");
    expect(tarjeta(/^SIMIT del locatario/)).toHaveAttribute("aria-expanded", "false");
  });

  it("SIMIT: documento sin nombre, paz y salvo, comparendos con el valor tal como llega, sin total, y aviso de recorte", () => {
    render(<SeccionesDeuda deuda={deudaCompleta()} />);
    const simit = contenidoDe(tarjeta(/^SIMIT del propietario/));
    expect(simit).toHaveTextContent("Consultado el 03/11/2026");
    expect(simit).toHaveTextContent("CC 900000001");
    expect(simit).toHaveTextContent("Paz y salvo");
    expect(within(simit).getByText("No")).toBeInTheDocument();
    const tabla = within(simit).getByRole("table", { name: "Comparendos de SIMIT del propietario" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Número", "Fecha", "Valor", "Estado"]);
    const filas = within(tabla).getAllByRole("row").slice(1);
    expect(filas).toHaveLength(2);
    expect(filas[0]).toHaveTextContent("C-0001");
    expect(filas[0]).toHaveTextContent("15/01/2026");
    // S17: texto tal como llega, sin convertir ni sumar.
    expect(within(filas[0]).getByText("450000.00")).toBeInTheDocument();
    expect(within(filas[1]).getByText("$ 120.000")).toBeInTheDocument();
    expect(simit).not.toHaveTextContent(/total/i);
    expect(within(simit).getByText("Se muestran los primeros 100 comparendos. El PDF y el Excel llevan los mismos.")).toBeInTheDocument();
    expect(simit).toHaveTextContent("Acuerdo de pago con saldo pendiente de $ 300.000");
  });

  it("impuesto: total pendiente con separador de miles y la tabla de vigencias", () => {
    render(<SeccionesDeuda deuda={deudaCompleta()} />);
    const impuesto = contenidoDe(tarjeta(/^Impuesto vehicular/));
    expect(impuesto).toHaveTextContent("Total pendiente: $ 1.250.000");
    const tabla = within(impuesto).getByRole("table", { name: "Vigencias del impuesto" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Año", "Valor", "Pagada"]);
    const filas = within(tabla).getAllByRole("row").slice(1);
    expect(filas[0]).toHaveTextContent("2025");
    expect(filas[0]).toHaveTextContent("No");
    expect(filas[1]).toHaveTextContent("2024");
    expect(filas[1]).toHaveTextContent("Sí");
  });

  it("SOAT vencido, RTM vigente y locatario no aplica con el motivo legible", () => {
    render(<SeccionesDeuda deuda={deudaCompleta()} />);
    expect(contenidoDe(tarjeta(/^SOAT/))).toHaveTextContent("Vencido el 01/10/2026");
    const rtm = tarjeta(/^Revisión técnico-mecánica/);
    fireEvent.click(rtm);
    expect(contenidoDe(rtm)).toHaveTextContent("Vence el 01/05/2027");
    expect(contenidoDe(tarjeta(/^SIMIT del locatario/))).toHaveTextContent("No se consultó: falta el documento del locatario.");
  });

  it("sin hallazgos en ninguna fuente: «Sin pendientes» y nunca queda vacío", () => {
    render(<SeccionesDeuda deuda={deudaSinPendientes()} />);
    const aviso = screen.getByText("No encontramos deudas ni pendientes en las fuentes consultadas.").closest("[role='status']") as HTMLElement;
    expect(within(aviso).getByText("Sin pendientes")).toBeInTheDocument();
    expect(contenidoDe(tarjeta(/^Revisión técnico-mecánica/))).toHaveTextContent("No aplica para este vehículo.");
  });

  it("con hallazgos no muestra «Sin pendientes»", () => {
    render(<SeccionesDeuda deuda={deudaCompleta()} />);
    expect(screen.queryByText("No encontramos deudas ni pendientes en las fuentes consultadas.")).not.toBeInTheDocument();
  });

  it("UA-6: un saldo del acuerdo de pago que no es numérico se muestra tal como llega, nunca «$ NaN»", () => {
    const deuda = deudaCompleta();
    const simit = deuda.fuentes.find((f) => f.fuente === "SIMIT_PROPIETARIO") as FuenteDeudaSimit;
    simit.acuerdos_pago = [{ saldo_pendiente: "$1.250.000" }, { saldo_pendiente: "1.250.000,00" }, { saldo_pendiente: "450000" }];
    render(<SeccionesDeuda deuda={deuda} />);
    const acuerdos = within(screen.getByRole("list", { name: "Acuerdos de pago de SIMIT del propietario" })).getAllByRole("listitem");
    expect(acuerdos.map((a) => a.textContent)).toEqual([
      "Acuerdo de pago con saldo pendiente de $1.250.000",
      "Acuerdo de pago con saldo pendiente de 1.250.000,00",
      "Acuerdo de pago con saldo pendiente de $ 450.000",
    ]);
    expect(screen.queryByText(/NaN/)).not.toBeInTheDocument();
  });

  it("UA-3: dos entradas SIMIT_PROPIETARIO: tarjetas con nombre propio por ordinal, sin documento, y abrir/cerrar independiente", () => {
    const deuda = deudaCompleta();
    const primera = deuda.fuentes.find((f) => f.fuente === "SIMIT_PROPIETARIO") as FuenteDeudaSimit;
    const segunda: FuenteDeudaSimit = { ...primera, persona: { tipo_documento: "CC", documento: "900000002" }, comparendos: [], comparendos_truncados: false, acuerdos_pago: [] };
    deuda.fuentes = [...deuda.fuentes, segunda];
    const avisos: unknown[] = [];
    const error = vi.spyOn(console, "error").mockImplementation((...args) => void avisos.push(args));
    render(<SeccionesDeuda deuda={deuda} />);
    const nombres = screen.getAllByRole("button").map((b) => b.textContent ?? "");
    expect(nombres.filter((n) => n.startsWith("SIMIT del propietario"))).toEqual([
      "SIMIT del propietario 1 de 2: Con deuda",
      "SIMIT del propietario 2 de 2: Con deuda",
    ]);
    // El nombre del botón no lleva el número de documento.
    expect(nombres.join(" ")).not.toMatch(/900000001|900000002/);
    // Sin aviso de React por keys repetidas.
    expect(avisos.some((a) => String((a as unknown[])[0]).includes("same key"))).toBe(false);
    error.mockRestore();
    const p1 = tarjeta(/^SIMIT del propietario 1 de 2/);
    const p2 = tarjeta(/^SIMIT del propietario 2 de 2/);
    expect(p1).toHaveAttribute("aria-expanded", "true");
    expect(p2).toHaveAttribute("aria-expanded", "true");
    fireEvent.click(p2);
    expect(p2).toHaveAttribute("aria-expanded", "false");
    expect(p1).toHaveAttribute("aria-expanded", "true");
    // La tabla interna también se distingue por el ordinal.
    expect(within(contenidoDe(p1)).getByRole("table", { name: "Comparendos de SIMIT del propietario 1 de 2" })).toBeInTheDocument();
  });

  it("SP-11: aunque la respuesta trajera un nombre, nunca se pinta", () => {
    const deuda = deudaCompleta();
    const simit = deuda.fuentes.find((f) => f.fuente === "SIMIT_PROPIETARIO") as unknown as { persona: Record<string, string> };
    simit.persona.nombre = "Persona De Prueba";
    render(<SeccionesDeuda deuda={deuda} />);
    expect(screen.queryByText(/Persona De Prueba/)).not.toBeInTheDocument();
  });
});
