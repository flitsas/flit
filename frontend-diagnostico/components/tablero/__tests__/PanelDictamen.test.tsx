import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { PanelDictamen, type PanelDictamenProps } from "@/components/tablero/PanelDictamen";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";
import { EMPRESA_A } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: al pulsar «Dictamen» en la grilla se abre <PanelDictamen modo="compania" /> o, en el backoffice,
// modo="backoffice" con ?empresa=. Pide el GET al abrir. Vacío, carga, error y lleno. El override solo con la capacidad.

const LOTE = "00000000-0000-7000-8000-00000000e001";
const VEHICULO = "vehiculo-1";
const RUTA = `/diagnosticos/${LOTE}/vehiculos/${VEHICULO}/dictamen`;
const RUTA_BO = `/backoffice/diagnosticos/${LOTE}/vehiculos/${VEHICULO}/dictamen`;

const LLENO = {
  version: 1,
  veredicto: "BLOQUEADO",
  estado_redaccion: "PENDIENTE",
  alertamientos: [
    { codigo: "SOAT_NO_VIGENTE", texto: "El SOAT no está vigente.", efecto: "BLOQUEA", fuente: "RUNT" },
    { codigo: "IMPUESTO", texto: "Hay impuesto pendiente.", efecto: "ADVIERTE", fuente: "IMPUESTO_DEPARTAMENTAL" },
  ],
  catalogo_version: 1,
  creado_en: "2026-11-03T15:00:00+00:00",
};

const pintar = (props: Partial<PanelDictamenProps> = {}) =>
  render(
    <PanelDictamen
      diagnosticoId={LOTE}
      vehiculoId={VEHICULO}
      identificador="TST001"
      modo="compania"
      puedeCorregir={false}
      onClose={() => {}}
      {...props}
    />,
  );

afterEach(() => vi.unstubAllGlobals());

describe("PanelDictamen — vacío, carga, error y lleno", () => {
  it("muestra la carga y luego el veredicto, los alertamientos y la redacción pendiente", async () => {
    let soltar: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      soltar = resolver;
    });
    simularApi({ [`GET ${RUTA}`]: () => pendiente });
    pintar();
    expect(screen.getByRole("status", { name: /cargando el dictamen/i })).toBeInTheDocument();

    await act(async () => {
      soltar(Response.json(LLENO));
    });
    expect(await screen.findByText("Bloqueado")).toBeInTheDocument();
    expect(screen.getByText("redacción IA pendiente")).toBeInTheDocument();
    const lista = screen.getByRole("list");
    expect(lista).toHaveTextContent("SOAT_NO_VIGENTE");
    expect(lista).toHaveTextContent("Efecto: BLOQUEA");
    expect(lista).toHaveTextContent("Fuente: RUNT");
    expect(lista).toHaveTextContent("El SOAT no está vigente.");
    expect(lista.textContent?.indexOf("SOAT_NO_VIGENTE")).toBeLessThan(lista.textContent?.indexOf("IMPUESTO") ?? 0);
    expect(screen.queryByRole("button", { name: "Reemplazar veredicto" })).not.toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: "Confirmo el cambio de veredicto" })).not.toBeInTheDocument();
  });

  it("SIN_DICTAMEN es vacío, no un error con reintentar", async () => {
    simularApi({
      [`GET ${RUTA}`]: () => Response.json({ code: "SIN_DICTAMEN", error: "Este vehículo no tiene un dictamen vigente." }, { status: 404 }),
    });
    pintar();
    expect(await screen.findByText("Este vehículo no tiene un dictamen vigente.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
  });

  it("otro fallo ofrece reintentar y la segunda lectura llena el panel", async () => {
    let veces = 0;
    simularApi({
      [`GET ${RUTA}`]: () => {
        veces += 1;
        return veces === 1 ? Response.json({ error: "no" }, { status: 500 }) : Response.json({ ...LLENO, estado_redaccion: "COMPLETADA" });
      },
    });
    pintar();
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Bloqueado")).toBeInTheDocument();
    expect(screen.queryByText("redacción IA pendiente")).not.toBeInTheDocument();
    expect(veces).toBe(2);
  });
});

describe("PanelDictamen — override solo en el backoffice con la capacidad", () => {
  it("sin la capacidad no hay veredictos ni confirmación", async () => {
    simularApi({ [`GET ${RUTA_BO}`]: () => Response.json(LLENO) });
    pintar({ modo: "backoffice", empresa: EMPRESA_A, puedeCorregir: false });
    expect(await screen.findByText("Bloqueado")).toBeInTheDocument();
    expect(screen.queryByRole("radio", { name: "Viable" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reemplazar veredicto" })).not.toBeInTheDocument();
  });

  it("la compañía no ofrece override aunque la capacidad llegue en true", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json(LLENO) });
    pintar({ puedeCorregir: true });
    expect(await screen.findByText("Bloqueado")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reemplazar veredicto" })).not.toBeInTheDocument();
  });

  it("solo la confirmación manda confirmado true y el foco vuelve al botón que abrió", async () => {
    const api = simularApi({
      [`GET ${RUTA_BO}`]: () => Response.json(LLENO),
      [`PUT ${RUTA_BO}`]: () => Response.json({ ...LLENO, version: 2, veredicto: "VIABLE", estado_redaccion: "PENDIENTE" }),
    });
    render(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: "Ver dictamen de TST001" }));
    expect(await screen.findByText(/Veredicto:/)).toHaveTextContent("Bloqueado");
    fireEvent.click(screen.getByRole("radio", { name: "Viable" }));
    expect(llamadas(api, "PUT", RUTA_BO)).toHaveLength(0);
    expect(screen.getByRole("button", { name: "Reemplazar veredicto" })).toBeDisabled();

    fireEvent.click(screen.getByRole("checkbox", { name: "Confirmo el cambio de veredicto" }));
    fireEvent.click(screen.getByRole("button", { name: "Reemplazar veredicto" }));
    expect(await screen.findByText(/Veredicto:/)).toHaveTextContent("Viable");
    const cuerpo = JSON.parse(String((llamadas(api, "PUT", RUTA_BO)[0][1] as RequestInit).body));
    expect(cuerpo).toEqual({ veredicto: "VIABLE", confirmado: true });

    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    expect(screen.getByRole("button", { name: "Ver dictamen de TST001" })).toHaveFocus();
  });
});

function Harness() {
  const [abierto, setAbierto] = useState(false);
  const [disparador, setDisparador] = useState<HTMLElement | null>(null);
  return (
    <>
      <button
        type="button"
        onClick={(e) => {
          setDisparador(e.currentTarget);
          setAbierto(true);
        }}
      >
        Ver dictamen de TST001
      </button>
      {abierto && (
        <PanelDictamen
          diagnosticoId={LOTE}
          vehiculoId={VEHICULO}
          identificador="TST001"
          modo="backoffice"
          empresa={EMPRESA_A}
          puedeCorregir
          onClose={() => setAbierto(false)}
          devolverFocoA={disparador}
        />
      )}
    </>
  );
}
