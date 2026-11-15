import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { AccionesVehiculo, type AccionesVehiculoProps } from "@/components/tablero/AccionesVehiculo";
import type { FilaVehiculo } from "@/lib/tablero/api";
import { ID, celda, llamadas, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: en la columna «Acciones» del tablero, <AccionesVehiculo vehiculo={v} capacidades={{ sanear, derivar,
//   descargar, reprocesar }} onAbrirDeuda={(disparador) => setDeuda({ vehiculo: v, disparador })} … /> pinta el disparador
//   «Acciones» con los ítems en orden D-103 y los mensajes de resultado debajo. El estado del saneamiento lo informa la
//   columna Dictamen (EtiquetaSaneamiento), no esta celda (auditoría UI UA-1).
// Ola Diagnóstico UX 2026-11-15 · FE-2 (spec-ui §2.3–§2.5).

const navegacion = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => navegacion }));

afterEach(() => vi.unstubAllGlobals());

const TERMINADO = (cambios: Partial<FilaVehiculo> = {}) =>
  vehiculo(1, [celda(1, "RUNT", "COMPLETADO")], {
    terminado: true,
    pegada: true,
    saneamiento: { estado: null, elegible: true, motivo_no_elegible: null },
    elegible_tramite: true,
    ...cambios,
  });

const TODAS = { sanear: true, derivar: true, descargar: true, reprocesar: true };

const pintar = (props: Partial<AccionesVehiculoProps> = {}) => {
  const base: AccionesVehiculoProps = {
    diagnosticoId: ID,
    vehiculo: TERMINADO(),
    estadoLote: "COMPLETADO",
    modo: undefined,
    tramitesUrl: "https://tramites.example",
    empresaId: "empresa-1",
    capacidades: TODAS,
    conDatosSaneamiento: true,
    onAbrirDeuda: vi.fn(),
    onCambioSaneamiento: vi.fn(),
    onCreadoTramite: vi.fn(),
    onRefrescar: vi.fn(),
    onAnunciar: vi.fn(),
    reintentar: vi.fn(async () => ({ reencoladas: 1 })),
    onReintentado: vi.fn(),
    onErrorReintento: vi.fn(),
  };
  const todas = { ...base, ...props };
  return { ...render(<AccionesVehiculo {...todas} />), props: todas };
};

const disparador = () => screen.getByRole("button", { name: "Acciones de la placa TST001" });
const abrir = () => {
  fireEvent.click(disparador());
  return screen.getAllByRole("menuitem");
};

describe("AccionesVehiculo — orden y visibilidad (FE-2, spec-ui §2.3)", () => {
  it("con todas las capacidades: Solicitar saneamiento, Crear trámite, Ver deuda, Descargar PDF y Reintentar consultas, en ese orden", () => {
    pintar();
    expect(disparador()).toHaveTextContent("Acciones");
    expect(abrir().map((i) => i.textContent?.trim())).toEqual([
      "Solicitar saneamiento",
      "Crear trámite",
      "Ver deuda",
      "Descargar PDF",
      "Reintentar consultas",
    ]);
    expect(screen.getByRole("menuitem", { name: "Solicitar saneamiento de la placa TST001" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Crear trámite para la placa TST001" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Ver deuda de la placa TST001" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Descargar PDF de TST001" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Reintentar consultas de la placa TST001" })).toBeInTheDocument();
  });

  it("una acción que el rol no tiene no aparece", () => {
    pintar({ capacidades: { sanear: false, derivar: false, descargar: true, reprocesar: false } });
    expect(abrir().map((i) => i.textContent?.trim())).toEqual(["Ver deuda", "Descargar PDF"]);
  });

  it("una acción no permitida para la fila sale deshabilitada con su motivo visible", () => {
    pintar({
      estadoLote: "EN_PROCESO",
      vehiculo: TERMINADO({
        terminado: false,
        elegible_tramite: false,
        motivo_no_elegible: "DICTAMEN_BLOQUEADO",
        saneamiento: { estado: null, elegible: false, motivo_no_elegible: "NO_TERMINADO" },
        celdas: [celda(1, "RUNT", "PENDIENTE")],
      }),
    });
    abrir();
    const sanear = screen.getByRole("menuitem", { name: "Solicitar saneamiento de la placa TST001" });
    expect(sanear).toHaveAttribute("aria-disabled", "true");
    expect(sanear).toHaveAccessibleDescription("Faltan consultas por terminar");
    const tramite = screen.getByRole("menuitem", { name: "Crear trámite para la placa TST001" });
    expect(tramite).toHaveAttribute("aria-disabled", "true");
    expect(tramite.getAttribute("aria-describedby")).toBeTruthy();
    const pdf = screen.getByRole("menuitem", { name: "Descargar PDF de TST001" });
    expect(pdf).toHaveAttribute("aria-disabled", "true");
    expect(pdf).toHaveAccessibleDescription("Disponible cuando terminen las consultas de este vehículo");
    // «Ver deuda» solo en terminados.
    expect(screen.queryByRole("menuitem", { name: /^Ver deuda/ })).toBeNull();
  });

  it("con trámite: «Ver trámite #N» es un enlace a pestaña nueva y su nombre accesible lo dice", () => {
    pintar({ vehiculo: TERMINADO({ tramite: { id: "t-41", numero: 41 } as FilaVehiculo["tramite"], elegible_tramite: false }) });
    abrir();
    const ver = screen.getByRole("menuitem", { name: /^Ver trámite #41 de la placa TST001 \(se abre en una pestaña nueva\)$/ });
    expect(ver.tagName).toBe("A");
    expect(ver).toHaveAttribute("target", "_blank");
    expect(ver).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("fila sin ningún ítem: sin disparador, «—» y el texto para lectores", () => {
    pintar({
      capacidades: { sanear: false, derivar: false, descargar: false, reprocesar: false },
      conDatosSaneamiento: false,
      vehiculo: TERMINADO({ saneamiento: undefined }),
    });
    expect(screen.queryByRole("button", { name: /^Acciones de la placa/ })).toBeNull();
    expect(screen.getByText("Sin acciones para la placa TST001")).toHaveClass("sr-only");
  });
});

describe("AccionesVehiculo — saneamiento (spec-ui §2.4)", () => {
  it("con saneamiento en gestión: sin chip duplicado de la columna Dictamen (UA-1) y el ítem «Ver saneamiento» abre la deuda con el disparador", () => {
    const onAbrirDeuda = vi.fn();
    const { container } = pintar({
      onAbrirDeuda,
      vehiculo: TERMINADO({ saneamiento: { estado: "EN_GESTION", elegible: false, motivo_no_elegible: "YA_SOLICITADO" } }),
    });
    expect(container.querySelector("[data-parte='chip-saneamiento']")).toBeNull();
    expect(screen.queryByText("Saneamiento:", { exact: false })).toBeNull();
    expect(screen.queryByText("En saneamiento")).toBeNull();
    abrir();
    fireEvent.click(screen.getByRole("menuitem", { name: "Ver saneamiento de la placa TST001: En saneamiento" }));
    expect(onAbrirDeuda).toHaveBeenCalledWith(disparador());
  });

  it("SOLICITADO con la capacidad: «Quitar solicitud de saneamiento» (el estado lo dice el texto, sin aria-pressed) y DELETE optimista", async () => {
    const api = simularApi({ [`DELETE /diagnosticos/${ID}/vehiculos/vehiculo-1/saneamiento`]: () => new Response(null, { status: 204 }) });
    const onCambioSaneamiento = vi.fn();
    pintar({ onCambioSaneamiento, vehiculo: TERMINADO({ saneamiento: { estado: "SOLICITADO", elegible: false, motivo_no_elegible: "YA_SOLICITADO" } }) });
    abrir();
    const quitar = screen.getByRole("menuitem", { name: "Quitar solicitud de saneamiento de la placa TST001" });
    expect(quitar).not.toHaveAttribute("aria-pressed");
    await act(async () => {
      fireEvent.click(quitar);
    });
    expect(onCambioSaneamiento).toHaveBeenCalledWith({ estado: null, elegible: true, motivo_no_elegible: null });
    expect(llamadas(api, "DELETE", `/diagnosticos/${ID}/vehiculos/vehiculo-1/saneamiento`)).toHaveLength(1);
    expect(disparador()).toHaveFocus();
  });

  it("si el PUT falla, el error va bajo el disparador en role=alert y la fila vuelve a su estado", async () => {
    simularApi({ [`PUT /diagnosticos/${ID}/vehiculos/vehiculo-1/saneamiento`]: () => Response.json({ code: "X", error: "x" }, { status: 500 }) });
    const onCambioSaneamiento = vi.fn();
    pintar({ onCambioSaneamiento });
    abrir();
    await act(async () => {
      fireEvent.click(screen.getByRole("menuitem", { name: "Solicitar saneamiento de la placa TST001" }));
    });
    expect(onCambioSaneamiento).toHaveBeenLastCalledWith({ estado: null, elegible: true, motivo_no_elegible: null });
    const alerta = screen.getByRole("alert");
    expect(alerta.textContent).toBeTruthy();
    expect(disparador().compareDocumentPosition(alerta) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  // Ronda 2 de E2E (acciones-detalle.spec.ts:239): el éxito solo iba a la región viva de la pantalla; §2.5 lo pide
  // también bajo el disparador, en role=status, con el foco quieto en «Acciones».
  it("al confirmarse el PUT, el resultado va bajo el disparador en role=status y el foco no se mueve (§2.5)", async () => {
    simularApi({
      [`PUT /diagnosticos/${ID}/vehiculos/vehiculo-1/saneamiento`]: () =>
        Response.json({ id: "s1", version: 1, estado: "SOLICITADO", solicitado_en: "2026-11-14T15:00:00Z", solicitado_por_mi: true, renglones: [], historial: [] }, { status: 201 }),
    });
    pintar();
    abrir();
    await act(async () => {
      fireEvent.click(screen.getByRole("menuitem", { name: "Solicitar saneamiento de la placa TST001" }));
    });
    const estado = screen.getByRole("status");
    expect(estado).toHaveTextContent("Saneamiento solicitado para TST001.");
    expect(estado).toHaveStyle({ color: "var(--badge-success-fg)" });
    expect(disparador().compareDocumentPosition(estado) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(disparador()).toHaveFocus();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("al quitar la solicitud, el status dice que se quitó; si luego falla, el status desaparece y queda solo el alert", async () => {
    let falla = false;
    simularApi({
      [`DELETE /diagnosticos/${ID}/vehiculos/vehiculo-1/saneamiento`]: () =>
        falla ? Response.json({ code: "X", error: "x" }, { status: 500 }) : new Response(null, { status: 204 }),
    });
    pintar({ vehiculo: TERMINADO({ saneamiento: { estado: "SOLICITADO", elegible: false, motivo_no_elegible: "YA_SOLICITADO" } }) });
    abrir();
    await act(async () => {
      fireEvent.click(screen.getByRole("menuitem", { name: "Quitar solicitud de saneamiento de la placa TST001" }));
    });
    expect(screen.getByRole("status")).toHaveTextContent("Se quitó la solicitud de saneamiento de TST001.");
    falla = true;
    abrir();
    await act(async () => {
      fireEvent.click(screen.getByRole("menuitem", { name: "Quitar solicitud de saneamiento de la placa TST001" }));
    });
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.getByRole("alert").textContent).toBeTruthy();
  });
});

describe("AccionesVehiculo — resultados (spec-ui §2.5)", () => {
  it("«Reintentar consultas»: llama al POST, avisa a quien pinta y el foco se queda en el disparador", async () => {
    const reintentar = vi.fn(async () => ({ reencoladas: 2 }));
    const onReintentado = vi.fn();
    pintar({ reintentar, onReintentado });
    abrir();
    await act(async () => {
      fireEvent.click(screen.getByRole("menuitem", { name: "Reintentar consultas de la placa TST001" }));
    });
    expect(reintentar).toHaveBeenCalledTimes(1);
    expect(onReintentado).toHaveBeenCalledWith({ reencoladas: 2 });
    expect(disparador()).toHaveFocus();
  });

  it("«Ver deuda» llama a onAbrirDeuda con el disparador «Acciones» de la fila", () => {
    const onAbrirDeuda = vi.fn();
    pintar({ onAbrirDeuda });
    abrir();
    fireEvent.click(screen.getByRole("menuitem", { name: "Ver deuda de la placa TST001" }));
    expect(onAbrirDeuda).toHaveBeenCalledWith(disparador());
  });
});
