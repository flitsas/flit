import { fireEvent, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { EnvioPunto, type PasoEnvioPunto } from "@/components/punto/EnvioPunto";
import { crearCliente } from "@/lib/api/cliente";
import { ACEPTADA, definicionCompleta, RUTA_DEFINICION, solicitudCompleta } from "./fixtures-envio";
import { routerPush } from "./router";
import { json, renderConQuery } from "./utilidades";

type Manejador = (init: RequestInit) => Response | Promise<Response>;

const RADICAR = "POST /solicitudes/s-1/radicar";
const enCola = () => solicitudCompleta({ estado: "en_cola", version: 8 });

type PropsAnfitrion = { cliente: ReturnType<typeof crearCliente>; onTerminar: () => void; onAtras: () => void; onPaso: (p: PasoEnvioPunto) => void; inicial: PasoEnvioPunto };

function Anfitrion({ cliente, onTerminar, onAtras, onPaso, inicial }: PropsAnfitrion) {
  const [paso, setPaso] = useState<PasoEnvioPunto>(inicial);
  const alPaso = (p: PasoEnvioPunto) => {
    onPaso(p);
    if (p !== "pago") setPaso(p);
  };
  return <EnvioPunto cliente={cliente} solicitudId="s-1" tipoCodigo="TIPO_A" paso={paso} onPaso={alPaso} onAtras={onAtras} onIrActores={() => undefined} onTerminar={onTerminar} />;
}

function montar(rutas: Record<string, Manejador> = {}, inicial: PasoEnvioPunto = "resumen") {
  const fase = { enviada: false };
  const todas: Record<string, Manejador> = {
    "GET /solicitudes/s-1": () => json(fase.enviada ? enCola() : solicitudCompleta()),
    [RUTA_DEFINICION]: () => json(definicionCompleta()),
    "GET /solicitudes/s-1/estado": () => json({ solicitudId: "s-1", codigoPublico: "PN-2026-0042", estado: "en_cola", tipoTramiteCodigo: "TIPO_A", actualizadoEn: "2026-10-28T15:30:00Z" }),
    [RADICAR]: () => {
      fase.enviada = true;
      return json(ACEPTADA, 202);
    },
    ...rutas,
  };
  const llamadas: string[] = [];
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push(clave);
    return (todas[clave] ?? (() => json({ code: "no_simulado", message: clave }, 404)))(init);
  }) as unknown as typeof fetch;
  const onTerminar = vi.fn();
  const onAtras = vi.fn();
  const onPaso = vi.fn();
  renderConQuery(<Anfitrion cliente={crearCliente({ fetchImpl })} onTerminar={onTerminar} onAtras={onAtras} onPaso={onPaso} inicial={inicial} />);
  return { fase, llamadas, onTerminar, onAtras, onPaso };
}

describe("Envío del punto · Resumen y confirmación", () => {
  it("Resumen con lo declarado y sin valor de cotización; «Continuar al pago» lleva al paso Pago sin radicar", async () => {
    const { llamadas, onPaso } = montar();

    const resumen = await screen.findByRole("region", { name: "Resumen del trámite" });
    expect(resumen).toHaveTextContent("PlacaABC123");
    expect(resumen).toHaveTextContent("Tipo de trámiteTrámite A");
    expect(screen.queryByText("Valor del trámite")).toBeNull();
    expect(screen.queryByText(/Comprobante de pago/)).toBeNull();
    expect(screen.queryByRole("button", { name: "Enviar trámite" })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Continuar al pago" }));

    expect(onPaso).toHaveBeenCalledWith("pago");
    expect(llamadas.filter((l) => l === RADICAR)).toHaveLength(0);
  }, 20000);

  it("con la solicitud en pendiente_pago el Resumen es de solo lectura: sin «Editar» ni «Atrás»", async () => {
    montar({ "GET /solicitudes/s-1": () => json(solicitudCompleta({ estado: "pendiente_pago", version: 9 })) });
    await screen.findByRole("region", { name: "Resumen del trámite" });

    expect(screen.queryByRole("button", { name: /^Editar/ })).toBeNull();
    expect(screen.queryByRole("button", { name: "Atrás" })).toBeNull();
    expect(screen.getByRole("button", { name: "Continuar al pago" })).toBeEnabled();
  });

  it("el cierre (paso envío) muestra la confirmación del portal y «Ir al Dashboard» termina", async () => {
    const { onTerminar } = montar({ "GET /solicitudes/s-1": () => json(enCola()) }, "envio");

    expect(await screen.findByRole("heading", { level: 2, name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" })).toBeInTheDocument();
    expect(await screen.findByTestId("radicado-entregado")).toHaveTextContent("PN-2026-0042");
    expect(screen.getByRole("region", { name: "Confirmación del trámite" })).toHaveTextContent("Ciudad Uno");

    fireEvent.click(screen.getByRole("button", { name: "Ir al Dashboard" }));
    expect(onTerminar).toHaveBeenCalledTimes(1);
    expect(routerPush).not.toHaveBeenCalled();
  }, 20000);

  it("cotiza con el organismo del borrador y muestra el valor en el Resumen y en la confirmación", async () => {
    const cotizar = vi.fn(() => json({ lineas: [{ codigo: "TARIFA", nombre: "Tarifa", subtitulo: "", valorCop: 2000 }], totalCop: 2000, versionTarifa: "v1", adicionalesNoAplicados: [] }));
    const { llamadas } = montar({ "POST /cotizaciones": cotizar });

    const resumen = await screen.findByRole("region", { name: "Resumen del trámite" });
    await waitFor(() => expect(resumen).toHaveTextContent(/2.000/));
    expect(llamadas.filter((l) => l === "POST /cotizaciones").length).toBeGreaterThan(0);
    expect(JSON.parse(String((cotizar.mock.calls[0] as unknown as [RequestInit])[0].body))).toMatchObject({ organismoId: "o-1", tipoCodigo: "TIPO_A" });
  });

  it("«Atrás» vuelve a Requisitos", async () => {
    const { onAtras } = montar();
    await screen.findByRole("region", { name: "Resumen del trámite" });

    fireEvent.click(screen.getByRole("button", { name: "Atrás" }));

    expect(onAtras).toHaveBeenCalledTimes(1);
  });

  it("si no carga el borrador ofrece reintentar", async () => {
    let falla = true;
    montar({ "GET /solicitudes/s-1": () => (falla ? json({ code: "servicio_no_disponible" }, 503) : json(solicitudCompleta())) });

    expect(await screen.findByRole("alert")).toHaveTextContent("El servicio no está disponible por ahora");
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("region", { name: "Resumen del trámite" })).toBeInTheDocument();
  });
});
