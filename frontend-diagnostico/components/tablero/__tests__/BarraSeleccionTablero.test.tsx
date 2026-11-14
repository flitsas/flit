import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { Icono } from "@flit/ui/Icono";
import { IconoDxBandera } from "@/components/dx/iconos/IconoDxBandera";
import { IconoDxCrearTramite } from "@/components/dx/iconos/IconoDxCrearTramite";
import {
  BarraSeleccionTablero,
  CasillaTablero,
  mensajeSolicitudSaneamiento,
  seleccionDeFila,
  type BarraSeleccionTableroProps,
} from "@/components/tablero/BarraSeleccionTablero";
import type { FilaVehiculo } from "@/lib/tablero/api";
import { V1 } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo (HU #14577 AC10–AC11, spec-ui §2.4): la barra del tablero con dos acciones en bloque, cada una con su
// cuenta de seleccionados elegibles y solo si el usuario tiene la capacidad:
//   <BarraSeleccionTablero elegibles={4} seleccionadas={3} onSeleccionarTodas={…} onQuitar={…}
//     crear={{ cantidad: 2, enviando: false, onCrear }} sanear={{ cantidad: 1, enviando: false, onSanear }} />
// `seleccionDeFila(v, { derivar, sanear })` dice si la casilla se habilita y con qué motivo; `mensajeSolicitudSaneamiento`
// arma el texto del resultado de POST /saneamientos/solicitudes.

const DIR = join(__dirname, "..");
const trazos = (icono: React.ReactElement) => {
  const { container, unmount } = render(icono);
  const html = container.querySelector("svg")!.innerHTML;
  unmount();
  return html;
};

const base = (cambios: Partial<BarraSeleccionTableroProps> = {}): BarraSeleccionTableroProps => ({
  elegibles: 4,
  seleccionadas: 3,
  onSeleccionarTodas: vi.fn(),
  onQuitar: vi.fn(),
  crear: { cantidad: 2, enviando: false, onCrear: vi.fn() },
  sanear: { cantidad: 1, enviando: false, onSanear: vi.fn() },
  ...cambios,
});

const region = () => screen.getByRole("region", { name: "Selección" });

const fila = (cambios: Partial<FilaVehiculo>): FilaVehiculo => ({ ...V1, ...cambios });
const DERIVABLE = { derivacion: { elegible: true, motivo_no_elegible: null, estado: null, tramite: null } };
const NO_VIABLE = { derivacion: { elegible: false, motivo_no_elegible: "NO_VIABLE", estado: null, tramite: null } };
const SANEABLE = { saneamiento: { estado: null, elegible: true, motivo_no_elegible: null } };
const NO_TERMINADO = { saneamiento: { estado: null, elegible: false, motivo_no_elegible: "NO_TERMINADO" as const } };

describe("AC10 — dos acciones, cada una cuenta solo sus elegibles", () => {
  it("«Crear trámites (N₁)» primaria y «Sanear (N₂)» secundaria, en ese orden, con sus íconos", () => {
    render(<BarraSeleccionTablero {...base()} />);
    const botones = within(region()).getAllByRole("button").filter((b) => b.textContent !== "Quitar selección");
    expect(botones.map((b) => b.textContent)).toEqual(["Crear trámites (2)", "Sanear (1)"]);
    expect(botones[0].className).toContain("var(--kit-azul-relleno)");
    expect(botones[1].className).toContain("var(--kit-tinta)");
    expect(botones[0].querySelector("svg")!.innerHTML).toBe(trazos(<IconoDxCrearTramite tamano={16} />));
    expect(botones[1].querySelector("svg")!.innerHTML).toBe(trazos(<IconoDxBandera tamano={16} />));
    expect(region()).toHaveTextContent("3 vehículos seleccionados");
    expect(region()).toHaveTextContent("Cada acción cuenta solo los vehículos que cumplen su regla.");
  });

  it("sin la capacidad derivar no hay «Crear trámites» y «Sanear» pasa a primaria", () => {
    render(<BarraSeleccionTablero {...base({ crear: undefined })} />);
    expect(screen.queryByRole("button", { name: /crear trámites/i })).toBeNull();
    expect(screen.getByRole("button", { name: "Sanear (1)" }).className).toContain("var(--kit-azul-relleno)");
    expect(screen.getByRole("group", { name: "Selección para sanear" })).toBeInTheDocument();
  });

  it("sin la capacidad sanear no hay «Sanear» y la barra queda como antes (texto y ayuda de crear trámites)", () => {
    render(<BarraSeleccionTablero {...base({ sanear: undefined })} />);
    expect(screen.queryByRole("button", { name: /sanear/i })).toBeNull();
    expect(screen.getByRole("group", { name: "Selección para crear trámites" })).toBeInTheDocument();
    expect(region()).toHaveTextContent(/solo se seleccionan vehículos viables y sin trámite/i);
  });

  it("con las dos, el grupo se nombra por las dos acciones", () => {
    render(<BarraSeleccionTablero {...base()} />);
    expect(screen.getByRole("group", { name: "Selección para crear trámites o sanear" })).toBeInTheDocument();
  });

  it("N = 0: el botón queda aria-disabled con el motivo como descripción y el clic no hace nada", () => {
    const onCrear = vi.fn();
    const onSanear = vi.fn();
    render(<BarraSeleccionTablero {...base({ crear: { cantidad: 0, enviando: false, onCrear }, sanear: { cantidad: 0, enviando: false, onSanear } })} />);
    const crear = screen.getByRole("button", { name: "Crear trámites (0)" });
    const sanear = screen.getByRole("button", { name: "Sanear (0)" });
    expect(crear).toHaveAttribute("aria-disabled", "true");
    expect(crear).toHaveAccessibleDescription("Ninguno de los seleccionados se puede crear");
    expect(sanear).toHaveAttribute("aria-disabled", "true");
    expect(sanear).toHaveAccessibleDescription("Ninguno de los seleccionados se puede sanear");
    fireEvent.click(crear);
    fireEvent.click(sanear);
    expect(onCrear).not.toHaveBeenCalled();
    expect(onSanear).not.toHaveBeenCalled();
  });

  it("más de 500 para sanear: aria-disabled con «Máximo 500 vehículos por solicitud» (D-11)", () => {
    const onSanear = vi.fn();
    render(<BarraSeleccionTablero {...base({ seleccionadas: 501, sanear: { cantidad: 501, enviando: false, onSanear } })} />);
    const sanear = screen.getByRole("button", { name: "Sanear (501)" });
    expect(sanear).toHaveAttribute("aria-disabled", "true");
    expect(sanear).toHaveAccessibleDescription("Máximo 500 vehículos por solicitud");
    fireEvent.click(sanear);
    expect(onSanear).not.toHaveBeenCalled();
  });

  it("con N > 0 cada botón llama a su acción", () => {
    const props = base();
    render(<BarraSeleccionTablero {...props} />);
    fireEvent.click(screen.getByRole("button", { name: "Crear trámites (2)" }));
    fireEvent.click(screen.getByRole("button", { name: "Sanear (1)" }));
    expect(props.crear!.onCrear).toHaveBeenCalledTimes(1);
    expect(props.sanear!.onSanear).toHaveBeenCalledTimes(1);
  });

  it("en vuelo: «Creando trámites…» y «Solicitando…» con aria-busy; no se puede quitar la selección", () => {
    render(
      <BarraSeleccionTablero
        {...base({ crear: { cantidad: 2, enviando: true, onCrear: vi.fn() }, sanear: { cantidad: 1, enviando: true, onSanear: vi.fn() } })}
      />,
    );
    expect(screen.getByRole("button", { name: /creando trámites/i })).toHaveAttribute("aria-busy", "true");
    expect(screen.getByRole("button", { name: /solicitando/i })).toHaveAttribute("aria-busy", "true");
    expect(screen.queryByRole("button", { name: /quitar selección/i })).toBeNull();
  });

  it("«Seleccionar todos los elegibles» con el check del kit; deshabilitado sin elegibles", () => {
    const props = base();
    const { rerender } = render(<BarraSeleccionTablero {...props} />);
    const todos = screen.getByRole("button", { name: "Seleccionar todos los elegibles" });
    expect(todos.querySelector("svg")!.innerHTML).toBe(trazos(<Icono nombre="check" className="h-4 w-4" />));
    fireEvent.click(todos);
    expect(props.onSeleccionarTodas).toHaveBeenCalledTimes(1);
    rerender(<BarraSeleccionTablero {...base({ elegibles: 0, seleccionadas: 0 })} />);
    expect(screen.getByRole("button", { name: "Seleccionar todos los elegibles" })).toBeDisabled();
  });

  it("el archivo no usa lucide ni colores sueltos y BarraSeleccion del kit no cambia de API", () => {
    const fuente = readFileSync(join(DIR, "BarraSeleccionTablero.tsx"), "utf8");
    expect(fuente).not.toMatch(/lucide-react/);
    expect(fuente).not.toMatch(/#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b/);
    expect(fuente).toMatch(/from "@flit\/ui\/BarraSeleccion"/);
  });
});

describe("AC10 — casilla de la fila: elegible para alguna de las dos acciones", () => {
  it.each([
    ["solo derivable, con las dos capacidades", { ...DERIVABLE, ...NO_TERMINADO }, { derivar: true, sanear: true }, true, true, false],
    ["solo saneable, con las dos capacidades", { ...NO_VIABLE, ...SANEABLE }, { derivar: true, sanear: true }, true, false, true],
    ["ambas", { ...DERIVABLE, ...SANEABLE }, { derivar: true, sanear: true }, true, true, true],
    ["ninguna", { ...NO_VIABLE, ...NO_TERMINADO }, { derivar: true, sanear: true }, false, false, false],
    ["saneable sin la capacidad sanear", { ...NO_VIABLE, ...SANEABLE }, { derivar: true, sanear: false }, false, false, false],
    ["derivable sin la capacidad derivar", { ...DERIVABLE, ...NO_TERMINADO }, { derivar: false, sanear: true }, false, false, false],
  ])("%s", (_caso, cambios, capacidades, habilitada, derivar, sanear) => {
    const s = seleccionDeFila(fila(cambios as Partial<FilaVehiculo>), capacidades);
    expect(s.visible).toBe(true);
    expect(s.habilitada).toBe(habilitada);
    expect(s.derivar).toBe(derivar);
    expect(s.sanear).toBe(sanear);
  });

  it("el motivo de la deshabilitada une con « · » los de las acciones que el usuario tiene", () => {
    const s = seleccionDeFila(fila({ ...NO_VIABLE, ...NO_TERMINADO } as Partial<FilaVehiculo>), { derivar: true, sanear: true });
    expect(s.motivo).toMatch(/^.+ · Faltan consultas por terminar$/);
    expect(s.motivo).toContain("viable");
    const soloSanear = seleccionDeFila(fila({ ...NO_VIABLE, ...NO_TERMINADO } as Partial<FilaVehiculo>), { derivar: false, sanear: true });
    expect(soloSanear.motivo).toBe("Faltan consultas por terminar");
  });

  it("sin datos de ninguna acción que el usuario tenga, la casilla no existe", () => {
    expect(seleccionDeFila(fila({}), { derivar: true, sanear: true }).visible).toBe(false);
    expect(seleccionDeFila(fila(SANEABLE as Partial<FilaVehiculo>), { derivar: true, sanear: false }).visible).toBe(false);
  });

  it("CasillaTablero deshabilitada: «Seleccionar la placa…», motivo por aria-describedby y title", () => {
    const v = fila({ ...NO_VIABLE, ...NO_TERMINADO } as Partial<FilaVehiculo>);
    render(<CasillaTablero vehiculo={v} seleccion={seleccionDeFila(v, { derivar: false, sanear: true })} marcada={false} onCambiar={vi.fn()} />);
    const casilla = screen.getByRole("checkbox", { name: `Seleccionar la placa ${V1.placa}` });
    expect(casilla).toBeDisabled();
    expect(casilla).toHaveAccessibleDescription("Faltan consultas por terminar");
    expect(casilla).toHaveAttribute("title", "Faltan consultas por terminar");
  });

  it("CasillaTablero habilitada marca y desmarca por id, sin descripción de motivo", () => {
    const v = fila(SANEABLE as Partial<FilaVehiculo>);
    const onCambiar = vi.fn();
    render(<CasillaTablero vehiculo={v} seleccion={seleccionDeFila(v, { derivar: true, sanear: true })} marcada={false} onCambiar={onCambiar} />);
    const casilla = screen.getByRole("checkbox");
    expect(casilla).toBeEnabled();
    expect(casilla).not.toHaveAttribute("aria-describedby");
    fireEvent.click(casilla);
    expect(onCambiar).toHaveBeenCalledWith(V1.id, true);
  });
});

describe("AC11 — texto del resultado de «Sanear (N)»", () => {
  it("sin omitidos: éxito, singular y plural", () => {
    expect(mensajeSolicitudSaneamiento({ solicitados: 1, omitidos: [] })).toEqual({ tono: "success", texto: "Se solicitó el saneamiento de 1 vehículo." });
    expect(mensajeSolicitudSaneamiento({ solicitados: 8, omitidos: [] })).toEqual({ tono: "success", texto: "Se solicitó el saneamiento de 8 vehículos." });
  });

  it("6 solicitados y 2 omitidos (YA_SOLICITADO y NO_TERMINADO): aviso con el motivo de cada uno en español", () => {
    const r = mensajeSolicitudSaneamiento({
      solicitados: 6,
      omitidos: [
        { vehiculo_id: "a", motivo: "YA_SOLICITADO" },
        { vehiculo_id: "b", motivo: "NO_TERMINADO" },
      ],
    });
    expect(r.tono).toBe("warning");
    expect(r.texto).toBe("Se solicitó el saneamiento de 6 vehículos. 2 se omitieron: 1 con consultas por terminar y 1 que ya tenía saneamiento.");
  });

  it("los tres motivos, con plurales y unión con comas y «y»; solo las partes con cuenta", () => {
    const omitidos = [
      ...["a", "b"].map((id) => ({ vehiculo_id: id, motivo: "NO_TERMINADO" as const })),
      ...["c", "d", "e"].map((id) => ({ vehiculo_id: id, motivo: "YA_SOLICITADO" as const })),
      { vehiculo_id: "f", motivo: "NO_ENCONTRADO" as const },
    ];
    expect(mensajeSolicitudSaneamiento({ solicitados: 2, omitidos }).texto).toBe(
      "Se solicitó el saneamiento de 2 vehículos. 6 se omitieron: 2 con consultas por terminar, 3 que ya tenían saneamiento y 1 no disponible.",
    );
    expect(mensajeSolicitudSaneamiento({ solicitados: 3, omitidos: [{ vehiculo_id: "x", motivo: "NO_ENCONTRADO" }] }).texto).toBe(
      "Se solicitó el saneamiento de 3 vehículos. 1 se omitió: 1 no disponible.",
    );
  });

  it("ninguno solicitado: lo dice sin «0 vehículos» (D-29)", () => {
    expect(mensajeSolicitudSaneamiento({ solicitados: 0, omitidos: [{ vehiculo_id: "x", motivo: "YA_SOLICITADO" }] })).toEqual({
      tono: "warning",
      texto: "No se solicitó el saneamiento de ningún vehículo. 1 se omitió: 1 que ya tenía saneamiento.",
    });
  });

  it("la barra muestra el resultado bajo la selección con el título «Saneamiento solicitado» y el error con role=alert", () => {
    const { rerender } = render(
      <BarraSeleccionTablero {...base({ sanear: { cantidad: 1, enviando: false, onSanear: vi.fn(), resultado: { n: 1, tono: "success", texto: "Se solicitó el saneamiento de 1 vehículo." } } })} />,
    );
    const aviso = screen.getByRole("status");
    expect(aviso).toHaveTextContent("Saneamiento solicitado");
    expect(aviso).toHaveTextContent("Se solicitó el saneamiento de 1 vehículo.");
    rerender(<BarraSeleccionTablero {...base({ sanear: { cantidad: 1, enviando: false, onSanear: vi.fn(), error: "No pudimos solicitar el saneamiento. Intenta de nuevo." } })} />);
    expect(screen.getByRole("alert")).toHaveTextContent("No pudimos solicitar el saneamiento. Intenta de nuevo.");
  });

  it("si la selección quedó vacía, el foco va al resultado (el botón «Sanear» desapareció)", () => {
    render(
      <BarraSeleccionTablero
        {...base({ seleccionadas: 0, sanear: { cantidad: 0, enviando: false, onSanear: vi.fn(), resultado: { n: 1, tono: "success", texto: "Se solicitó el saneamiento de 2 vehículos." } } })}
      />,
    );
    expect(screen.getByText("Se solicitó el saneamiento de 2 vehículos.").closest("[tabindex='-1']")).toHaveFocus();
  });
});
