import { readFileSync } from "node:fs";
import { join } from "node:path";
import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { BotonSanear, type BotonSanearProps } from "@/components/saneamiento/BotonSanear";
import type { SaneamientoFila } from "@/lib/saneamiento/api";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo (HU #14577 AC5–AC8, spec-ui §1.3): bandera de saneamiento de una fila. Toggle de un clic, sin
// confirmación y optimista:
//   <BotonSanear diagnosticoId={lote} vehiculo={v} saneamiento={v.saneamiento} puedeSanear={puedeSanear(capacidades)}
//     onCambio={(estado, fila) => actualizarFila(v.id, fila)} onAbrirPanel={(boton) => abrirPanel(v, boton)}
//     onRefrescar={refrescar} onAnunciar={anunciar} />
// Con EN_GESTION, SANEADO o NO_PROCEDE (o sin la capacidad y con saneamiento) es «Ver saneamiento» y abre el panel.

const navegacion = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => navegacion }));

const LOTE = "00000000-0000-7000-8000-00000000b001";
const VEH = "vehiculo-1";
const RUTA = `/diagnosticos/${LOTE}/vehiculos/${VEH}/saneamiento`;
const PUT = `PUT ${RUTA}`;
const DELETE = `DELETE ${RUTA}`;
const NOMBRE = "Solicitar saneamiento de la placa ABC123";

const SIN: SaneamientoFila = { estado: null, elegible: true, motivo_no_elegible: null };
const SOLICITADO: SaneamientoFila = { estado: "SOLICITADO", elegible: false, motivo_no_elegible: "YA_SOLICITADO" };
const SANEAMIENTO_API = { id: "s1", version: 1, estado: "SOLICITADO", solicitado_en: "2026-11-14T15:00:00Z", solicitado_por_mi: true, renglones: [], historial: [] };

type Extra = Partial<Omit<BotonSanearProps, "saneamiento">>;

/** La fila guarda el saneamiento como lo hace el tablero: con lo que llega por `onCambio`. */
function Fila({ inicial, extra }: { inicial: SaneamientoFila | null | undefined; extra: Extra }) {
  const [saneamiento, setSaneamiento] = useState(inicial);
  return (
    <BotonSanear
      diagnosticoId={LOTE}
      vehiculo={{ id: VEH, placa: "ABC123", vin: null, identificador: "ABC123" }}
      saneamiento={saneamiento}
      puedeSanear
      onAbrirPanel={vi.fn()}
      {...extra}
      onCambio={(estado, fila) => {
        extra.onCambio?.(estado, fila);
        setSaneamiento(fila);
      }}
    />
  );
}

const montar = (inicial: SaneamientoFila | null | undefined, extra: Extra = {}) => render(<Fila inicial={inicial} extra={extra} />);
const esperar = () =>
  act(async () => {
    await new Promise((r) => setTimeout(r, 0));
  });
const conError = (status: number, code: string) => () => Response.json({ code, error: "x" }, { status });

beforeEach(() => {
  navegacion.replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

describe("AC5 — marcar con la bandera", () => {
  it("elegible y sin saneamiento: «Sanear», apagada, aria-pressed falso y el nombre «Solicitar saneamiento de la placa…»", () => {
    simularApi({});
    montar(SIN);
    const boton = screen.getByRole("button", { name: NOMBRE });
    expect(boton).toHaveAttribute("aria-pressed", "false");
    expect(boton).toHaveTextContent("Sanear");
    expect(boton).not.toHaveAttribute("aria-disabled");
    expect(boton.querySelector("svg path:nth-child(2)")).toHaveAttribute("fill", "none");
  });

  it("un clic la enciende de inmediato (optimista, sin confirmación) y llama PUT …/saneamiento", async () => {
    let soltar: (r: Response) => void = () => {};
    const api = simularApi({ [PUT]: () => new Promise<Response>((r) => (soltar = r)) });
    const onCambio = vi.fn();
    const onAnunciar = vi.fn();
    montar(SIN, { onCambio, onAnunciar });
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    const boton = screen.getByRole("button", { name: NOMBRE });
    expect(boton).toHaveAttribute("aria-pressed", "true");
    expect(boton).toHaveTextContent("Solicitado");
    expect(boton).toHaveAttribute("aria-busy", "true");
    expect(onCambio).toHaveBeenCalledWith("SOLICITADO", SOLICITADO);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(llamadas(api, "PUT", RUTA)).toHaveLength(1);
    await act(async () => {
      soltar(Response.json(SANEAMIENTO_API, { status: 201 }));
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(boton).toHaveAttribute("aria-pressed", "true");
    expect(boton).not.toHaveAttribute("aria-busy", "true");
    expect(onAnunciar).toHaveBeenCalledWith("Saneamiento solicitado para ABC123.");
  });

  it("en vuelo los clics se ignoran (una sola llamada) y el botón no se deshabilita (conserva el foco)", () => {
    const api = simularApi({ [PUT]: () => new Promise<Response>(() => {}) });
    montar(SIN);
    const boton = screen.getByRole("button", { name: NOMBRE });
    boton.focus();
    fireEvent.click(boton);
    fireEvent.click(boton);
    fireEvent.click(boton);
    expect(llamadas(api, "PUT", RUTA)).toHaveLength(1);
    expect(boton).toBeEnabled();
    expect(boton).toHaveFocus();
  });

  it("200 (ya estaba SOLICITADO, idempotente) también queda encendida", async () => {
    simularApi({ [PUT]: () => Response.json(SANEAMIENTO_API, { status: 200 }) });
    montar(SIN);
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    await esperar();
    expect(screen.getByRole("button", { name: NOMBRE })).toHaveAttribute("aria-pressed", "true");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it.each([
    ["VEHICULO_NO_TERMINADO", "Este vehículo aún tiene consultas por terminar.", false],
    ["YA_SOLICITADO", "Este vehículo ya tiene un saneamiento.", true],
  ])("409 %s: revierte, explica en español y refresca la fila solo si cambió en el servidor", async (code, texto, refresca) => {
    simularApi({ [PUT]: conError(409, code) });
    const onRefrescar = vi.fn();
    const onCambio = vi.fn();
    montar(SIN, { onRefrescar, onCambio });
    const boton = screen.getByRole("button", { name: NOMBRE });
    boton.focus();
    fireEvent.click(boton);
    await esperar();
    expect(boton).toHaveAttribute("aria-pressed", "false");
    expect(onCambio).toHaveBeenLastCalledWith(null, SIN);
    expect(screen.getByRole("alert")).toHaveTextContent(texto);
    expect(onRefrescar).toHaveBeenCalledTimes(refresca ? 1 : 0);
    expect(boton).toHaveFocus();
  });
});

describe("AC6 — desmarcar y reversión si falla", () => {
  it("SOLICITADO: encendida, «Solicitado», el mismo nombre y «clic para quitar» en title (D-02)", () => {
    simularApi({});
    montar(SOLICITADO);
    const boton = screen.getByRole("button", { name: NOMBRE });
    expect(boton).toHaveAttribute("aria-pressed", "true");
    expect(boton).toHaveTextContent("Solicitado");
    expect(boton).toHaveAttribute("title", "Clic para quitar la solicitud");
    expect(boton.querySelector("svg path:nth-child(2)")).toHaveAttribute("fill", "currentColor");
  });

  it("CR-2 (WCAG 1.4.3): encendida, el fondo es opaco (--di-accent-soft sobre --card), así el hover de la fila no baja el contraste", () => {
    simularApi({});
    montar(SOLICITADO);
    const clases = screen.getByRole("button", { name: NOMBRE }).className.split(/\s+/);
    expect(clases).toContain("bg-card");
    expect(clases).toContain("bg-[image:linear-gradient(var(--di-accent-soft),var(--di-accent-soft))]");
    expect(clases).not.toContain("bg-[var(--di-accent-soft)]");
  });

  it("un clic la apaga de inmediato y llama DELETE …/saneamiento; 204 deja sin saneamiento y lo anuncia", async () => {
    const api = simularApi({ [DELETE]: () => new Response(null, { status: 204 }) });
    const onAnunciar = vi.fn();
    const onCambio = vi.fn();
    montar(SOLICITADO, { onAnunciar, onCambio });
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    expect(screen.getByRole("button", { name: NOMBRE })).toHaveAttribute("aria-pressed", "false");
    expect(onCambio).toHaveBeenCalledWith(null, SIN);
    expect(llamadas(api, "DELETE", RUTA)).toHaveLength(1);
    await esperar();
    expect(screen.getByRole("button", { name: NOMBRE })).toHaveAttribute("aria-pressed", "false");
    expect(onAnunciar).toHaveBeenCalledWith("Se quitó la solicitud de saneamiento de ABC123.");
  });

  it("404 SIN_SANEAMIENTO cuenta como éxito: ya no estaba", async () => {
    simularApi({ [DELETE]: conError(404, "SIN_SANEAMIENTO") });
    montar(SOLICITADO);
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    await esperar();
    expect(screen.getByRole("button", { name: NOMBRE })).toHaveAttribute("aria-pressed", "false");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("409 SANEAMIENTO_EN_GESTION: vuelve a encendida, explica qué pasó y refresca la fila", async () => {
    simularApi({ [DELETE]: conError(409, "SANEAMIENTO_EN_GESTION") });
    const onRefrescar = vi.fn();
    montar(SOLICITADO, { onRefrescar });
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    await esperar();
    expect(screen.getByRole("button", { name: NOMBRE })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("alert")).toHaveTextContent("FLIT ya empezó este saneamiento; ya no se puede quitar.");
    expect(onRefrescar).toHaveBeenCalledTimes(1);
  });

  it("error de red: vuelve al estado anterior con el mensaje genérico; el siguiente clic borra el error", async () => {
    let n = 0;
    simularApi({
      [DELETE]: () => {
        if (n++ === 0) throw new TypeError("Failed to fetch");
        return new Response(null, { status: 204 });
      },
    });
    montar(SOLICITADO);
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    await esperar();
    expect(screen.getByRole("button", { name: NOMBRE })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("alert")).toHaveTextContent("No pudimos guardar el cambio. Intenta de nuevo.");
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    expect(screen.queryByRole("alert")).toBeNull();
    await esperar();
    expect(screen.getByRole("button", { name: NOMBRE })).toHaveAttribute("aria-pressed", "false");
  });

  it("403: revierte y lleva a /403 con el código", async () => {
    simularApi({ [DELETE]: conError(403, "SIN_PERMISO") });
    montar(SOLICITADO);
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    await esperar();
    expect(navegacion.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO");
    expect(screen.getByRole("button", { name: NOMBRE })).toHaveAttribute("aria-pressed", "true");
  });
});

describe("AC7 — saneamiento en gestión o cerrado: indicador que abre el panel", () => {
  it.each([
    ["EN_GESTION", "En saneamiento"],
    ["SANEADO", "Saneado"],
    ["NO_PROCEDE", "Saneamiento no procede"],
  ] as const)("%s: «Ver saneamiento», sin aria-pressed, su nombre dice el estado y el clic abre el panel", (estado, texto) => {
    const api = simularApi({});
    const onAbrirPanel = vi.fn();
    montar({ estado, elegible: false, motivo_no_elegible: "YA_SOLICITADO" }, { onAbrirPanel });
    const boton = screen.getByRole("button", { name: `Ver saneamiento de la placa ABC123: ${texto}` });
    expect(boton).not.toHaveAttribute("aria-pressed");
    expect(boton).toHaveAttribute("aria-haspopup", "dialog");
    expect(boton).toHaveTextContent("Ver saneamiento");
    expect(boton.querySelector("svg path:nth-child(2)")).toHaveAttribute("fill", "currentColor");
    fireEvent.click(boton);
    expect(onAbrirPanel).toHaveBeenCalledWith(boton);
    expect(api).not.toHaveBeenCalled();
  });
});

describe("AC8 — no elegible y sin capacidad (negativo)", () => {
  it("no terminado: aria-disabled (no disabled), motivo «Faltan consultas por terminar» por aria-describedby y sin llamada", () => {
    const api = simularApi({});
    montar({ estado: null, elegible: false, motivo_no_elegible: "NO_TERMINADO" });
    const boton = screen.getByRole("button", { name: NOMBRE });
    expect(boton).toHaveAttribute("aria-disabled", "true");
    expect(boton).toBeEnabled();
    expect(boton).toHaveAttribute("aria-pressed", "false");
    expect(boton).toHaveAccessibleDescription("Faltan consultas por terminar");
    expect(boton).toHaveAttribute("title", "Faltan consultas por terminar");
    fireEvent.click(boton);
    expect(api).not.toHaveBeenCalled();
    expect(boton).toHaveAttribute("aria-pressed", "false");
  });

  it("sin la capacidad sanear y sin saneamiento: no hay bandera", () => {
    simularApi({});
    const { container } = montar(SIN, { puedeSanear: false });
    expect(container.innerHTML).toBe("");
  });

  it("sin la capacidad y SOLICITADO: solo indicador («Ver saneamiento») que abre el panel, sin desmarcar", () => {
    const api = simularApi({});
    const onAbrirPanel = vi.fn();
    montar(SOLICITADO, { puedeSanear: false, onAbrirPanel });
    const boton = screen.getByRole("button", { name: "Ver saneamiento de la placa ABC123: Saneamiento solicitado" });
    expect(boton).not.toHaveAttribute("aria-pressed");
    fireEvent.click(boton);
    expect(onAbrirPanel).toHaveBeenCalledTimes(1);
    expect(api).not.toHaveBeenCalled();
  });

  it("fila sin la clave saneamiento (backend previo): no se pinta, ni con la capacidad (spec §2.6)", () => {
    simularApi({});
    expect(montar(undefined).container.innerHTML).toBe("");
    expect(montar(null).container.innerHTML).toBe("");
  });
});

describe("Contrato visual y de props (spec-ui §1.3)", () => {
  it("variante «fila» usa la pastilla de las acciones de fila; «cabecera», el botón secundario del kit", () => {
    simularApi({});
    const { unmount } = montar(SIN);
    const fila = screen.getByRole("button", { name: NOMBRE });
    expect(fila.className).toMatch(/rounded-full/);
    expect(fila.className).toMatch(/whitespace-nowrap/);
    expect(fila.className).not.toContain("var(--kit-tinta)");
    unmount();
    montar(SIN, { variante: "cabecera" });
    expect(screen.getByRole("button", { name: NOMBRE }).className).toContain("var(--kit-tinta)");
  });

  it("encendida agrega el tinte --di-accent-soft (sobre --card, opaco: CR-2); el archivo no tiene colores sueltos", () => {
    simularApi({});
    montar(SOLICITADO);
    expect(screen.getByRole("button", { name: NOMBRE }).className).toContain("var(--di-accent-soft)");
    const fuente = readFileSync(join(__dirname, "..", "BotonSanear.tsx"), "utf8");
    expect(fuente).not.toMatch(/#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b/);
  });
});

// Uso de ejemplo (HU #14577 FE-C, cableado-fe-c.md §1): en la cabecera de PS-2 la bandera avisa al panel solo cuando la
// API confirmó, para que el panel recargue GET …/saneamiento sin correr contra el PUT o el DELETE en vuelo:
//   <BotonSanear variante="cabecera" … onCambio={ctx.onCambio} onConfirmado={(estado) => ctx.onCambioSaneamiento(estado)} />
describe("FE-C — onConfirmado: aviso cuando la API confirma", () => {
  it("PUT 201: onConfirmado llega después de la respuesta, no en el clic optimista", async () => {
    let soltar: (r: Response) => void = () => {};
    simularApi({ [PUT]: () => new Promise<Response>((r) => (soltar = r)) });
    const onConfirmado = vi.fn();
    montar(SIN, { onConfirmado });
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    expect(onConfirmado).not.toHaveBeenCalled();
    await act(async () => {
      soltar(Response.json(SANEAMIENTO_API, { status: 201 }));
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(onConfirmado).toHaveBeenCalledTimes(1);
    expect(onConfirmado).toHaveBeenCalledWith("SOLICITADO", SOLICITADO);
  });

  it.each([
    ["204", () => new Response(null, { status: 204 })],
    ["404 SIN_SANEAMIENTO", conError(404, "SIN_SANEAMIENTO")],
  ])("DELETE %s: onConfirmado(null) con la fila sin saneamiento", async (_caso, respuesta) => {
    simularApi({ [DELETE]: respuesta });
    const onConfirmado = vi.fn();
    montar(SOLICITADO, { onConfirmado });
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    await esperar();
    expect(onConfirmado).toHaveBeenCalledWith(null, { estado: null, elegible: true, motivo_no_elegible: null });
  });

  it("si la API falla (409 o red) no se llama onConfirmado; solo la reversión por onCambio", async () => {
    simularApi({ [PUT]: conError(409, "VEHICULO_NO_TERMINADO") });
    const onConfirmado = vi.fn();
    const onCambio = vi.fn();
    montar(SIN, { onConfirmado, onCambio });
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    await esperar();
    expect(onConfirmado).not.toHaveBeenCalled();
    expect(onCambio).toHaveBeenLastCalledWith(null, SIN);
  });

  it("sin onConfirmado el botón sigue funcionando como antes (opcional)", async () => {
    simularApi({ [PUT]: () => Response.json(SANEAMIENTO_API, { status: 201 }) });
    montar(SIN);
    fireEvent.click(screen.getByRole("button", { name: NOMBRE }));
    await esperar();
    expect(screen.getByRole("button", { name: NOMBRE })).toHaveAttribute("aria-pressed", "true");
  });
});
