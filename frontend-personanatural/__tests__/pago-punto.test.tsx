import { act, fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DiagnosticoPunto } from "@/components/punto/DiagnosticoPunto";
import { PagoPunto } from "@/components/punto/PagoPunto";
import type { EstadoPagoPunto } from "@/lib/api/cliente";
import { crearCliente } from "@/lib/api/cliente";
import { definicionCompleta, problema, RUTA_DEFINICION, solicitudCompleta } from "./fixtures-envio";
import { json, renderConQuery } from "./utilidades";

type Manejador = (init: RequestInit) => Response | Promise<Response>;

const GET_PAGO = "GET /punto/pago";
const INICIAR = "POST /punto/pago/iniciar";
const CAJA = "POST /punto/pago/caja";
const CONFIRMAR = "POST /punto/pago/confirmar";

const DESGLOSE = [
  { codigo: "TARIFA", nombre: "Tarifa del organismo", subtitulo: "", valorCop: 60000 },
  { codigo: "FLIT", nombre: "Servicio FLIT", subtitulo: "", valorCop: 15000 },
];

function estado(extra: Partial<EstadoPagoPunto> = {}): EstadoPagoPunto {
  return {
    solicitudId: "s-1",
    codigoPublico: "PN-2026-0042",
    estado: "pendiente_pago",
    puedePagar: true,
    identidadesPendientes: [],
    desglose: DESGLOSE,
    totalCop: 75000,
    ...extra,
  };
}

const VIGENTE = { codigo: "PN-2026-0042", estado: "vigente" as const, habilitadoEn: "2026-11-09T15:00:00Z", venceEn: "2026-11-10T04:59:59Z" };
const PAGO = { valorCop: 75000, medio: "caja", medioCaja: "efectivo", pagadoEn: "2026-11-09T16:00:00Z" };

function montar(rutas: Record<string, Manejador> = {}) {
  const todas: Record<string, Manejador> = {
    [GET_PAGO]: () => json(estado()),
    [INICIAR]: () => json(estado()),
    "GET /solicitudes/s-1": () => json(solicitudCompleta({ estado: "pendiente_pago" })),
    [RUTA_DEFINICION]: () => json(definicionCompleta()),
    ...rutas,
  };
  const llamadas: string[] = [];
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push(clave);
    return (todas[clave] ?? (() => json({ code: "no_simulado", message: clave }, 404)))(init);
  }) as unknown as typeof fetch;
  const props = { onVolver: vi.fn(), onIrActores: vi.fn(), onPagado: vi.fn() };
  renderConQuery(<PagoPunto cliente={crearCliente({ fetchImpl })} solicitudId="s-1" tipoCodigo="TIPO_A" placa="ABC123" tipoNombre="Traspaso" {...props} />);
  return { llamadas, ...props };
}

describe("Paso Pago del punto · #14451", () => {
  beforeEach(() => {
    Object.assign(navigator, { clipboard: { writeText: vi.fn(async () => undefined) } });
  });
  afterEach(() => vi.useRealTimers());

  it("muestra cargando y luego el desglose del organismo y de FLIT con el total, sin envío ni domicilio", async () => {
    montar({ [GET_PAGO]: () => json(estado({ cobro: VIGENTE })) });
    expect(screen.getByText("Cargando el pago…")).toBeInTheDocument();

    expect(await screen.findByText("Tarifa del organismo")).toBeInTheDocument();
    expect(screen.getByText("Servicio FLIT")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Resumen del cobro" })).toBeInTheDocument();
    expect(screen.getByText("Placa").parentElement).toHaveTextContent("ABC123");
    expect(screen.getByText("Tipo de Trámite").parentElement).toHaveTextContent("Traspaso");
    expect(screen.getByText("Total a pagar").parentElement).toHaveTextContent("75.000");
    expect(screen.getByText("Total a pagar").parentElement).toHaveClass("border-t", "text-lg");
    expect(screen.queryByText(/envío|domicilio/i)).toBeNull();
    expect(screen.queryByRole("button", { name: /Generar pago/ })).toBeNull();
    expect(screen.queryByText(/pasarela|tarjeta|PSE/i)).toBeNull();
  });

  it("al entrar genera el pago solo (una llamada): muestra la carga, luego el ID con copiar, la vigencia y el estado pendiente", async () => {
    let habilitado = false;
    let liberar: (r: Response) => void = () => undefined;
    const { llamadas } = montar({
      [GET_PAGO]: () => json(estado(habilitado ? { cobro: VIGENTE } : {})),
      [CAJA]: () => new Promise<Response>((r) => (liberar = r)),
    });

    expect(await screen.findByText("Generando el pago en caja…")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Generar pago/ })).toBeNull();
    habilitado = true;
    await act(async () => liberar(json({ codigoCobro: "PN-2026-0042", habilitadoEn: VIGENTE.habilitadoEn, venceEn: VIGENTE.venceEn, desglose: DESGLOSE, totalCop: 75000 })));

    expect(await screen.findByTestId("codigo-cobro")).toHaveTextContent("PN-2026-0042");
    expect(screen.getByText("Vale hasta hoy a las 11:59 p. m.")).toBeInTheDocument();
    expect(screen.getByText("Vale hasta hoy a las 11:59 p. m.")).toHaveClass("bg-warning");
    expect(screen.getByText("Pendiente de pago")).toBeInTheDocument();
    expect(screen.getByText(/caja del organismo/)).toBeInTheDocument();
    expect(llamadas.filter((l) => l === CAJA)).toHaveLength(1);

    fireEvent.click(screen.getByRole("button", { name: /Copiar código/ }));
    await waitFor(() => expect(navigator.clipboard.writeText).toHaveBeenCalledWith("PN-2026-0042"));
    expect(await screen.findByRole("button", { name: /Copiado/ })).toBeInTheDocument();
  });

  it("con un cobro vigente (retomar o recargar) lo muestra sin regenerarlo", async () => {
    const { llamadas } = montar({ [GET_PAGO]: () => json(estado({ cobro: VIGENTE })) });
    expect(await screen.findByTestId("codigo-cobro")).toHaveTextContent("PN-2026-0042");
    expect(llamadas).not.toContain(CAJA);
    expect(screen.queryByRole("button", { name: /Generar pago|Reintentar/ })).toBeNull();
  });

  it("con el cobro vigente sondea y, cuando aparece el pago, refresca la solicitud y pasa al cierre", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let pagado = false;
    const { onPagado } = montar({
      [GET_PAGO]: () => json(pagado ? estado({ estado: "en_cola", puedePagar: false, cobro: null, pago: PAGO }) : estado({ cobro: VIGENTE })),
    });
    expect(await screen.findByTestId("codigo-cobro")).toBeInTheDocument();
    expect(onPagado).not.toHaveBeenCalled();

    pagado = true;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4500);
    });

    await waitFor(() => expect(onPagado).toHaveBeenCalledTimes(1));
  });

  it("si el cobro venció lo indica y permite generarlo de nuevo", async () => {
    const { llamadas } = montar({
      [GET_PAGO]: () => json(estado({ cobro: { ...VIGENTE, estado: "vencido" } })),
      [CAJA]: () => json({ codigoCobro: "PN-2026-0042", habilitadoEn: VIGENTE.habilitadoEn, venceEn: VIGENTE.venceEn, desglose: DESGLOSE, totalCop: 80000 }),
    });

    expect(await screen.findByText(/El pago en caja venció/)).toBeInTheDocument();
    expect(screen.queryByTestId("codigo-cobro")).toBeNull();
    expect(llamadas).not.toContain(CAJA);
    fireEvent.click(screen.getByRole("button", { name: "Generar pago en caja de nuevo" }));
    await waitFor(() => expect(llamadas.filter((l) => l === CAJA)).toHaveLength(1));
  });

  it("con total $0 ofrece «Confirmar» y al confirmar sigue al cierre", async () => {
    let confirmado = false;
    const { llamadas, onPagado } = montar({
      [GET_PAGO]: () =>
        json(confirmado ? estado({ estado: "pagada", totalCop: 0, pago: { valorCop: 0, medio: "sin_cobro", pagadoEn: "2026-11-09T16:00:00Z" } }) : estado({ totalCop: 0, desglose: [] })),
      [CONFIRMAR]: () => {
        confirmado = true;
        return json(estado({ estado: "pagada", totalCop: 0 }));
      },
    });
    fireEvent.click(await screen.findByRole("button", { name: "Confirmar" }));

    await waitFor(() => expect(onPagado).toHaveBeenCalledTimes(1));
    expect(llamadas).toContain(CONFIRMAR);
    expect(llamadas).not.toContain(CAJA);
  });

  it("sin tarifa o medio no disponible muestra el motivo exacto y no deja pagar", async () => {
    montar({
      [GET_PAGO]: () => json(estado({ puedePagar: false, desglose: null, totalCop: null, bloqueo: { code: "medio_no_disponible", message: "El punto no tiene habilitado el pago en caja." } })),
    });

    expect(await screen.findByRole("alert")).toHaveTextContent("El punto no tiene habilitado el pago en caja.");
    expect(screen.queryByRole("button", { name: /Generar pago/ })).toBeNull();
    expect(screen.queryByRole("button", { name: "Confirmar" })).toBeNull();
    expect(screen.getByRole("button", { name: "Atrás" })).toBeEnabled();
  });

  it("si la generación automática falla muestra el motivo del servidor y «Reintentar» vuelve a llamar", async () => {
    let falla = true;
    const { llamadas } = montar({
      [CAJA]: () => (falla ? problema(409, "sin_tarifa", { message: "El trámite no tiene tarifa vigente en este organismo." }) : json({ codigoCobro: "PN-2026-0042", habilitadoEn: VIGENTE.habilitadoEn, venceEn: VIGENTE.venceEn, desglose: DESGLOSE, totalCop: 75000 })),
    });

    expect(await screen.findByRole("alert")).toHaveTextContent("El trámite no tiene tarifa vigente en este organismo.");
    expect(llamadas.filter((l) => l === CAJA)).toHaveLength(1);
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(llamadas.filter((l) => l === CAJA)).toHaveLength(2));
  });

  it("con total $0 no genera pago en caja", async () => {
    const { llamadas } = montar({ [GET_PAGO]: () => json(estado({ totalCop: 0, desglose: [] })) });
    await screen.findByRole("button", { name: "Confirmar" });
    expect(llamadas).not.toContain(CAJA);
  });

  it("si la consulta falla muestra el error con «Reintentar» y «Atrás»", async () => {
    let falla = true;
    montar({ [GET_PAGO]: () => (falla ? json({ code: "servicio_no_disponible" }, 503) : json(estado())) });

    expect(await screen.findByRole("alert")).toHaveTextContent("El servicio no está disponible por ahora");
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Tarifa del organismo")).toBeInTheDocument();
  });

  it("«Atrás» regresa a Requisitos", async () => {
    const { onVolver } = montar();
    await screen.findByText("Tarifa del organismo");
    fireEvent.click(screen.getByRole("button", { name: "Atrás" }));
    expect(onVolver).toHaveBeenCalledTimes(1);
  });

  it("si faltan requisitos al iniciar lo informa y no ofrece pagar", async () => {
    montar({
      [GET_PAGO]: () => json(estado({ estado: "borrador", puedePagar: false, desglose: null, totalCop: null })),
      [INICIAR]: () => problema(409, "requisitos_incompletos", { pendientes: [] }),
    });
    expect(await screen.findByRole("alert")).toHaveTextContent("Falta información del trámite");
    expect(screen.queryByRole("button", { name: /Generar pago/ })).toBeNull();
  });
});

describe("Paso Pago del punto · esperar identidades #14452", () => {
  afterEach(() => vi.useRealTimers());

  const PENDIENTES = [{ parte: "comprador", ordinal: 1, estado: "pendiente" }];
  const enBorrador = (extra: Partial<EstadoPagoPunto> = {}) =>
    estado({ estado: "borrador", puedePagar: false, desglose: null, totalCop: null, motivoIdentidad: "identidad_pendiente", identidadesPendientes: PENDIENTES, ...extra });

  it("muestra quién falta, no muestra el botón de pago y no inicia el pago", async () => {
    const { llamadas } = montar({ [GET_PAGO]: () => json(enBorrador()) });

    const lista = await screen.findByRole("list", { name: "Personas pendientes de validar" });
    await waitFor(() => expect(lista).toHaveTextContent("Comprador: validación pendiente"));
    expect(screen.queryByRole("button", { name: /Generar pago|Confirmar/ })).toBeNull();
    expect(llamadas).not.toContain(INICIAR);
  });

  it("«Ir a Actores» lleva al paso Actores", async () => {
    const { onIrActores } = montar({ [GET_PAGO]: () => json(enBorrador()) });
    fireEvent.click(await screen.findByRole("button", { name: "Ir a Actores" }));
    expect(onIrActores).toHaveBeenCalledTimes(1);
  });

  it("al validar la última identidad inicia el pago y habilita el botón sin recargar", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let validada = false;
    const { llamadas } = montar({
      [GET_PAGO]: () => json(validada ? estado() : enBorrador()),
      [INICIAR]: () => json(estado()),
      [CAJA]: () => json({ codigoCobro: "PN-2026-0042", habilitadoEn: VIGENTE.habilitadoEn, venceEn: VIGENTE.venceEn, desglose: DESGLOSE, totalCop: 75000 }),
    });
    await screen.findByRole("list", { name: "Personas pendientes de validar" });

    validada = true;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4500);
    });

    await waitFor(() => expect(llamadas.filter((l) => l === CAJA)).toHaveLength(1));
    expect(screen.queryByRole("list", { name: "Personas pendientes de validar" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Generar pago/ })).toBeNull();
  });

  it("si el servidor rechaza iniciar por identidad pendiente, muestra a quien falta", async () => {
    montar({
      [GET_PAGO]: () => json(enBorrador({ identidadesPendientes: [], motivoIdentidad: null })),
      [INICIAR]: () => problema(409, "identity_pending", { motivo: "identidad_pendiente", partes: PENDIENTES }),
    });
    const lista = await screen.findByRole("list", { name: "Personas pendientes de validar" });
    await waitFor(() => expect(lista).toHaveTextContent("Comprador"));
  });
});

describe("Asistente del punto: pago retomado, cobro anulado y barra lateral", () => {
  const CIERRE = "GET /punto/pago/cierre";
  const cierre = (extra: Record<string, unknown> = {}) => ({
    estadoCierre: "en_registro",
    codigoPublico: "PN-2026-0042",
    valorCop: 150000,
    medio: "caja",
    medioCaja: "efectivo",
    pagadoEn: "2026-11-09T16:00:00Z",
    recogeEn: { organismo: "Tránsito de Prueba" },
    ...extra,
  });

  function montarAsistente(rutas: Record<string, Manejador>) {
    const todas: Record<string, Manejador> = {
      "GET /solicitudes/s-1": () => json(solicitudCompleta({ estado: "pendiente_pago" })),
      [RUTA_DEFINICION]: () => json(definicionCompleta()),
      ...rutas,
    };
    const llamadas: string[] = [];
    const fetchImpl = (async (url: string, init: RequestInit = {}) => {
      const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
      llamadas.push(clave);
      return (todas[clave] ?? (() => json({ code: "no_simulado", message: clave }, 404)))(init);
    }) as unknown as typeof fetch;
    renderConQuery(
      <DiagnosticoPunto
        cliente={crearCliente({ fetchImpl })}
        placa="ABC123"
        documento="1020456789"
        tipoCodigo="TIPO_A"
        organismoNombre="Tránsito de Prueba"
        reanudar={{ solicitudId: "s-1" }}
        onVolver={vi.fn()}
        onAviso={vi.fn()}
      />,
    );
    return llamadas;
  }

  it("con una solicitud reanudada arranca en Pago sin diagnóstico ni actores", async () => {
    const llamadas = montarAsistente({ [GET_PAGO]: () => json(estado({ cobro: VIGENTE })), [INICIAR]: () => json(estado()) });
    expect(await screen.findByTestId("codigo-cobro")).toHaveTextContent("PN-2026-0042");
    expect(screen.getByText("Paso 4 · Pago", { selector: "h2" })).toBeInTheDocument();
    expect(llamadas.some((l) => l.includes("diagnostico") || l.includes("actores"))).toBe(false);
  });

  it("barra lateral: «sin radicado aún» y, al registrarse, «Radicado {radicado}»", async () => {
    montarAsistente({ [GET_PAGO]: () => json(estado({ pago: PAGO })), [CIERRE]: () => json(cierre({ estadoCierre: "registrado", radicadoFlit2: "TR-2026-000123" })) });
    expect(await screen.findByText("Canal del punto · Radicado TR-2026-000123", {}, { timeout: 8000 })).toBeInTheDocument();
    expect(screen.queryByText(/sin radicado aún/)).toBeNull();
  }, 15000);

  it("cobro anulado en el cierre: vuelve a Pago con el aviso amarillo y sin el bloque viejo", async () => {
    let anulado = false;
    let n = 0;
    const llamadas = montarAsistente({
      [GET_PAGO]: () => json(anulado ? estado() : estado({ pago: PAGO })),
      [INICIAR]: () => json(estado()),
      [CIERRE]: () => {
        if (++n === 1) return json(cierre());
        anulado = true;
        return json({ code: "pago_pendiente", message: "El trámite aún no tiene un pago confirmado." }, 409);
      },
    });
    expect(await screen.findByText(/Tu trámite está en proceso de registro/, {}, { timeout: 8000 })).toBeInTheDocument();
    const aviso = await screen.findByText("La caja anuló el cobro. Genera el pago de nuevo.", {}, { timeout: 10000 });
    expect(aviso).toHaveClass("border-warning");
    expect(await screen.findByRole("button", { name: "Generar pago en caja de nuevo" })).toBeEnabled();
    expect(llamadas).not.toContain(CAJA);
    expect(screen.queryByText(/Valor pagado/)).toBeNull();
    expect(screen.queryByText(/aún no tiene un pago confirmado/)).toBeNull();
  }, 25000);
});
