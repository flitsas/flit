import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
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
    expect(screen.queryByRole("button", { name: "Aplicar" })).not.toBeInTheDocument();
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
    expect(screen.queryByRole("button", { name: "Aplicar" })).not.toBeInTheDocument();
  });

  it("la compañía no ofrece override aunque la capacidad llegue en true", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json(LLENO) });
    pintar({ puedeCorregir: true });
    expect(await screen.findByText("Bloqueado")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Aplicar" })).not.toBeInTheDocument();
  });

  it("HU #14562 AC45: el ajuste manda version_esperada, veredicto, alertamientos y motivo (sin el cuerpo legado) y el foco vuelve al botón", async () => {
    const api = simularApi({
      [`GET ${RUTA_BO}`]: () => Response.json(LLENO),
      [`PUT ${RUTA_BO}`]: () => Response.json({ ...LLENO, version: 2, estado_redaccion: "PENDIENTE" }),
    });
    render(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: "Ver dictamen de TST001" }));
    expect(await screen.findByText(/Veredicto:/)).toHaveTextContent("Bloqueado");
    fireEvent.click(screen.getByRole("radio", { name: "Bloqueado" }));
    expect(screen.getByRole("button", { name: "Aplicar" })).toBeDisabled();
    escribirMotivo(MOTIVO);
    expect(screen.getByRole("button", { name: "Aplicar" })).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: "Confirmo el cambio de veredicto" }));
    expect(llamadas(api, "PUT", RUTA_BO)).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));
    await act(async () => {});
    const cuerpo = JSON.parse(String((llamadas(api, "PUT", RUTA_BO)[0][1] as RequestInit).body));
    expect(cuerpo).toEqual({
      version_esperada: 1,
      veredicto: "BLOQUEADO",
      alertamientos: LLENO.alertamientos.map(({ codigo, texto, efecto, fuente }) => ({ codigo, texto, efecto, fuente })),
      motivo: MOTIVO,
    });
    expect(cuerpo).not.toHaveProperty("confirmado");

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

// HU #14562 AC45/AC46 (B-PR6 en el frontend): «Aplicar» con motivo válido y confirmación; errores 409/422 con lo digitado.
const MOTIVO = "Soporte verificó el SOAT con la aseguradora";

function escribirMotivo(texto: string) {
  fireEvent.change(screen.getByRole("textbox", { name: "Motivo del ajuste" }), { target: { value: texto } });
}

async function abrirAjuste() {
  pintar({ modo: "backoffice", empresa: EMPRESA_A, puedeCorregir: true });
  expect(await screen.findByText(/Veredicto:/)).toHaveTextContent("Bloqueado");
  fireEvent.click(screen.getByRole("radio", { name: "Bloqueado" }));
  escribirMotivo(MOTIVO);
  fireEvent.click(screen.getByRole("checkbox", { name: "Confirmo el cambio de veredicto" }));
}

const cuerpoPut = (api: Parameters<typeof llamadas>[0], n: number) =>
  JSON.parse(String((llamadas(api, "PUT", RUTA_BO)[n][1] as RequestInit).body));

describe("PanelDictamen — HU #14562 ajuste FLIT (AC45/AC46)", () => {
  it("AC45: con el motivo corto «Aplicar» queda deshabilitado y el campo explica el mínimo", async () => {
    simularApi({ [`GET ${RUTA_BO}`]: () => Response.json(LLENO) });
    pintar({ modo: "backoffice", empresa: EMPRESA_A, puedeCorregir: true });
    await screen.findByText(/Veredicto:/);
    fireEvent.click(screen.getByRole("radio", { name: "Bloqueado" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Confirmo el cambio de veredicto" }));
    escribirMotivo("corto");
    expect(screen.getByRole("button", { name: "Aplicar" })).toBeDisabled();
    expect(screen.getByText("Escribe al menos 10 caracteres.")).toBeInTheDocument();
    escribirMotivo(MOTIVO);
    expect(screen.getByRole("button", { name: "Aplicar" })).toBeEnabled();
  });

  it("AC46: 409 DICTAMEN_DESACTUALIZADO muestra el mensaje, conserva el motivo y «Recargar» trae la versión vigente para el reenvío", async () => {
    let lecturas = 0;
    let envios = 0;
    const api = simularApi({
      [`GET ${RUTA_BO}`]: () => Response.json({ ...LLENO, version: ++lecturas }),
      [`PUT ${RUTA_BO}`]: () =>
        ++envios === 1
          ? Response.json({ code: "DICTAMEN_DESACTUALIZADO", error: "x" }, { status: 409 })
          : Response.json({ ...LLENO, version: 3 }),
    });
    await abrirAjuste();
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(await screen.findByText("El dictamen cambió mientras editabas. Recarga para ver la versión vigente")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Motivo del ajuste" })).toHaveValue(MOTIVO);
    fireEvent.click(screen.getByRole("button", { name: "Recargar" }));
    await act(async () => {});
    expect(lecturas).toBe(2);
    expect(screen.getByRole("textbox", { name: "Motivo del ajuste" })).toHaveValue(MOTIVO);
    expect(screen.getByRole("checkbox", { name: "Confirmo el cambio de veredicto" })).toBeChecked();
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));
    await act(async () => {});
    expect(cuerpoPut(api, 1).version_esperada).toBe(2);
  });

  it("AC46: 422 DICTAMEN_INCOHERENTE se explica en lenguaje claro y no ofrece «Recargar»", async () => {
    simularApi({
      [`GET ${RUTA_BO}`]: () => Response.json(LLENO),
      [`PUT ${RUTA_BO}`]: () => Response.json({ code: "DICTAMEN_INCOHERENTE", error: "x" }, { status: 422 }),
    });
    await abrirAjuste();
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(await screen.findByText(/un dictamen viable no puede tener alertamientos que bloqueen/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Recargar" })).toBeNull();
  });

  it("AC46: 409 VEHICULO_CON_TRAMITE pide confirmar y reenvía con confirmar_con_tramite = true", async () => {
    let envios = 0;
    const api = simularApi({
      [`GET ${RUTA_BO}`]: () => Response.json(LLENO),
      [`PUT ${RUTA_BO}`]: () =>
        ++envios === 1 ? Response.json({ code: "VEHICULO_CON_TRAMITE", error: "x" }, { status: 409 }) : Response.json({ ...LLENO, version: 2 }),
    });
    await abrirAjuste();
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(await screen.findByText(/el vehículo ya tiene un trámite creado; el ajuste no lo modifica/i)).toBeInTheDocument();
    expect(cuerpoPut(api, 0)).not.toHaveProperty("confirmar_con_tramite");
    fireEvent.click(screen.getByRole("button", { name: "Confirmar y aplicar" }));
    await act(async () => {});
    expect(cuerpoPut(api, 1).confirmar_con_tramite).toBe(true);
    expect(cuerpoPut(api, 1).motivo).toBe(MOTIVO);
  });
});

// HU #14562 AC35 (consultas del vehículo y formato de alertamientos), AC37 y AC38.
describe("PanelDictamen — HU #14562 AC35, AC37 y AC38", () => {
  const CONSULTAS = [
    { fuente: "RUNT" as const, estado: "COMPLETADO" as const },
    { fuente: "SIMIT_PROPIETARIO" as const, estado: "ERROR" as const },
  ];

  it("AC35: lista las consultas del vehículo con su estado y cada alertamiento con título, efecto, detalle y fuente", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json(LLENO) });
    pintar({ consultas: CONSULTAS });
    const consultas = await screen.findByRole("list", { name: "Consultas del vehículo" });
    expect(within(consultas).getByRole("listitem", { name: /RUNT vehículo/ })).toHaveTextContent("Ejecutado");
    expect(within(consultas).getByRole("listitem", { name: /SIMIT propietario/ })).toHaveTextContent(/error/i);
    const alertas = screen.getByRole("list", { name: "Alertamientos del dictamen" });
    const primera = within(alertas).getAllByRole("listitem")[0];
    expect(within(primera).getByRole("heading", { name: "SOAT no vigente" })).toBeInTheDocument();
    expect(primera).toHaveTextContent("Bloquea el trámite");
    expect(primera).toHaveTextContent("El SOAT no está vigente.");
    expect(primera).toHaveTextContent("Fuente: RUNT vehículo");
  });

  it("AC37: en la compañía el panel no ofrece crear trámite ni override (las acciones de trámite viven en la fila)", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json({ ...LLENO, veredicto: "VIABLE" }) });
    pintar();
    await screen.findByText("Viable");
    expect(screen.queryByRole("button", { name: /crear trámite|crear traspaso|ir al wizard/i })).toBeNull();
  });

  it("AC38: con cualquier dictamen no muestra números de documento (6 o más dígitos seguidos)", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json({ ...LLENO, ajustado_por_flit: true, ajustado_en: "2026-11-05T15:00:00+00:00" }) });
    pintar({ tramiteAnalizado: "Traspaso", consultas: CONSULTAS });
    const panel = await screen.findByRole("dialog");
    await screen.findByText("Bloqueado");
    expect(panel.textContent).not.toMatch(/\d{6,}/);
  });
});

// HU #14562, auditoría de UI M8: lo que soporte escribe en un alertamiento viaja en el PUT de «Aplicar»; no hay un «Guardar»
// por fila que, olvidado, haga que el PUT salga con el texto anterior sin aviso.
describe("PanelDictamen — HU #14562 M8: «Aplicar» nunca pierde lo editado", () => {
  it("el texto editado de un alertamiento, sin ningún paso extra, es el que manda el PUT", async () => {
    const api = simularApi({
      [`GET ${RUTA_BO}`]: () => Response.json(LLENO),
      [`PUT ${RUTA_BO}`]: () => Response.json({ ...LLENO, version: 2 }),
    });
    await abrirAjuste();
    fireEvent.change(screen.getByRole("textbox", { name: "Texto de Alertamiento 1" }), { target: { value: "El SOAT venció el 01/11/2026." } });
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));
    await act(async () => {});
    expect(cuerpoPut(api, 0).alertamientos[0]).toEqual({ codigo: "SOAT_NO_VIGENTE", texto: "El SOAT venció el 01/11/2026.", efecto: "BLOQUEA", fuente: "RUNT" });
  });

  it("con un alertamiento inválido «Aplicar» queda deshabilitado y el campo dice por qué", async () => {
    simularApi({ [`GET ${RUTA_BO}`]: () => Response.json(LLENO) });
    await abrirAjuste();
    const texto = screen.getByRole("textbox", { name: "Texto de Alertamiento 1" });
    fireEvent.change(texto, { target: { value: "" } });
    fireEvent.blur(texto);
    expect(screen.getByRole("button", { name: "Aplicar" })).toBeDisabled();
    expect(texto).toHaveAttribute("aria-invalid", "true");
    expect(texto).toHaveAccessibleDescription(/Escribe el texto del alertamiento/);
  });
});

// HU #14562 F-05 (auditoría §4.4): atajo «Crear trámite» en el pie del panel para el vehículo VIABLE; quien lo pinta decide
// si la fila es elegible y abre el flujo de la fila (modales AC37). El BLOQUEADO nunca lo ofrece (AC36).
describe("PanelDictamen — HU #14562 F-05: atajo «Crear trámite» en el pie", () => {
  it("con onCrearTramite y veredicto VIABLE, el pie ofrece «Crear trámite» y lo avisa a quien lo usa", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json({ ...LLENO, veredicto: "VIABLE", alertamientos: [] }) });
    const onCrearTramite = vi.fn();
    pintar({ onCrearTramite });
    await screen.findByText("Viable");
    const panel = screen.getByRole("dialog", { name: "Dictamen · TST001" });
    fireEvent.click(within(panel).getByRole("button", { name: "Crear trámite" }));
    expect(onCrearTramite).toHaveBeenCalledTimes(1);
  });

  it("AC36: con veredicto BLOQUEADO no hay atajo aunque llegue onCrearTramite", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json(LLENO) });
    pintar({ onCrearTramite: vi.fn() });
    expect(await screen.findByText(/el vehículo está bloqueado/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /crear trámite/i })).toBeNull();
  });

  it("mientras carga o sin dictamen no hay atajo", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json({ code: "SIN_DICTAMEN", error: "Sin dictamen" }, { status: 404 }) });
    pintar({ onCrearTramite: vi.fn() });
    expect(screen.queryByRole("button", { name: /crear trámite/i })).toBeNull();
    await act(async () => {});
    expect(screen.queryByRole("button", { name: /crear trámite/i })).toBeNull();
  });
});
