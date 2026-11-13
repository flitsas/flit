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
    // Punto 5: sin `etiqueta` en la respuesta, el título y la fuente salen del diccionario de respaldo, nunca el código.
    expect(lista).toHaveTextContent("SOAT no vigente");
    expect(lista).toHaveTextContent("Efecto: Bloquea el trámite");
    expect(lista).toHaveTextContent("Fuente: RUNT vehículo");
    expect(lista).toHaveTextContent("El SOAT no está vigente.");
    expect(lista).not.toHaveTextContent("SOAT_NO_VIGENTE");
    expect(lista).not.toHaveTextContent(/BLOQUEA|ADVIERTE|IMPUESTO_DEPARTAMENTAL/);
    expect(lista.textContent?.indexOf("SOAT no vigente")).toBeLessThan(lista.textContent?.indexOf("Impuesto vehicular pendiente") ?? 0);
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

describe("PanelDictamen — Punto 5: rótulos, nunca códigos", () => {
  it("titula cada alerta con la etiqueta de la API y muestra «Fuente: {fuente_etiqueta}»", async () => {
    simularApi({
      [`GET ${RUTA}`]: () =>
        Response.json({
          ...LLENO,
          alertamientos: [
            {
              codigo: "SIMIT_PROPIETARIO",
              etiqueta: "Multas del propietario",
              texto: "El propietario tiene multas.",
              efecto: "BLOQUEA",
              fuente: "SIMIT_PROPIETARIO",
              fuente_etiqueta: "SIMIT del propietario",
            },
          ],
        }),
    });
    pintar();
    const item = (await screen.findAllByRole("listitem"))[0];
    expect(item).toHaveTextContent("Multas del propietario");
    expect(item).toHaveTextContent("Fuente: SIMIT del propietario");
    expect(item).not.toHaveTextContent("SIMIT_PROPIETARIO");
  });

  it("un código sin etiqueta ni respaldo dice «Novedad sin clasificar» y deja el aviso técnico", async () => {
    const aviso = vi.spyOn(console, "warn").mockImplementation(() => {});
    simularApi({
      [`GET ${RUTA}`]: () =>
        Response.json({
          ...LLENO,
          alertamientos: [{ codigo: "REGLA_FUTURA_X", texto: "Texto de la regla.", efecto: "EFECTO_FUTURO", fuente: "FUENTE_FUTURA_Y" }],
        }),
    });
    pintar();
    const item = (await screen.findAllByRole("listitem"))[0];
    expect(item).toHaveTextContent("Novedad sin clasificar");
    expect(item).not.toHaveTextContent("REGLA_FUTURA_X");
    expect(item).not.toHaveTextContent("EFECTO_FUTURO");
    expect(item).not.toHaveTextContent("FUENTE_FUTURA_Y");
    expect(aviso).toHaveBeenCalled();
    aviso.mockRestore();
  });

  it("FUENTE_PENDIENTE sin etiqueta se rotula con el nombre de la fuente", async () => {
    simularApi({
      [`GET ${RUTA}`]: () =>
        Response.json({
          ...LLENO,
          alertamientos: [{ codigo: "FUENTE_PENDIENTE", texto: "La consulta no terminó.", efecto: "ADVIERTE", fuente: "AVALUO" }],
        }),
    });
    pintar();
    const item = (await screen.findAllByRole("listitem"))[0];
    expect(item).toHaveTextContent("Consulta pendiente: Avalúo comercial");
    expect(item).toHaveTextContent("Efecto: Advertencia");
    expect(item).not.toHaveTextContent("FUENTE_PENDIENTE");
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

// HU #14562 F-05 (AC35, AC36): el dictamen se lee en un PanelLateral de 640 px, con trámite analizado, fecha de emisión,
// versión del catálogo, marca «Ajustado por FLIT» (D6, sin autor ni motivo) y aviso de bloqueo.
describe("PanelDictamen — F-05 panel lateral (HU 14562)", () => {
  it("AC35: se abre como panel lateral de 640 px con «Cerrar», trámite, fecha de emisión y catálogo", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json({ ...LLENO, veredicto: "VIABLE" }) });
    pintar({ tramiteAnalizado: "Traspaso" });
    const panel = await screen.findByRole("dialog", { name: /dictamen · TST001/i });
    expect(panel).toHaveAttribute("aria-modal", "true");
    expect(panel.querySelector('[style*="max-width: 640px"]')).not.toBeNull();
    expect(screen.getByRole("button", { name: "Cerrar" })).toBeInTheDocument();
    expect(await screen.findByText("Viable")).toBeInTheDocument();
    expect(screen.getByText(/trámite analizado/i).nextElementSibling).toHaveTextContent("Traspaso");
    expect(screen.getByText(/emitido el/i).nextElementSibling).toHaveTextContent("03/11/2026");
    expect(screen.getByText(/versión del catálogo/i).nextElementSibling).toHaveTextContent("1");
    expect(screen.queryByText(/ajustado por flit/i)).toBeNull();
  });

  it("AC35: con ajustado_por_flit muestra la marca con la fecha, sin autor ni motivo", async () => {
    simularApi({
      [`GET ${RUTA}`]: () =>
        Response.json({ ...LLENO, ajustado_por_flit: true, ajustado_en: "2026-11-05T15:00:00+00:00" }),
    });
    pintar();
    const marca = await screen.findByText(/ajustado por flit/i);
    expect(marca.closest("p")).toHaveTextContent("05/11/2026");
    expect(screen.queryByText(/motivo/i)).toBeNull();
  });

  it("AC36: con veredicto BLOQUEADO avisa el bloqueo y no ofrece «Crear trámite»", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json(LLENO) });
    pintar();
    expect(await screen.findByText(/el vehículo está bloqueado/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /crear trámite/i })).toBeNull();
  });
});
