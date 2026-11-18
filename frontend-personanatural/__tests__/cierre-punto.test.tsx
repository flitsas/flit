import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CierrePunto } from "@/components/punto/CierrePunto";
import { crearCliente } from "@/lib/api/cliente";
import { json, renderConQuery } from "./utilidades";

type Manejador = () => Response | Promise<Response>;

const CIERRE = "GET /punto/pago/cierre";
const registrado = (extra: Record<string, unknown> = {}) => ({
  estadoCierre: "registrado",
  codigoPublico: "PN-2026-000123",
  radicadoFlit2: "TR-2026-000123",
  valorCop: 366000,
  medio: "caja",
  medioCaja: "efectivo",
  pagadoEn: "2026-10-27T15:00:00+00:00",
  recogeEn: { organismo: "Tránsito de Prueba" },
  tipoTramite: "Traspaso",
  placa: "ABC123",
  ...extra,
});

function montar(rutas: Record<string, Manejador>, extras: { onPagoAnulado?: () => void; onRadicado?: (r: string) => void } = {}) {
  const llamadas: string[] = [];
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push(clave);
    return (rutas[clave] ?? (() => json({ code: "no_simulado" }, 404)))();
  }) as unknown as typeof fetch;
  const onTerminar = vi.fn();
  renderConQuery(<CierrePunto cliente={crearCliente({ fetchImpl })} solicitudId="s-1" tipoCodigo="TIPO_A" placa="ABC123" tipoNombre="Traspaso" onTerminar={onTerminar} {...extras} />);
  return { llamadas, onTerminar };
}

describe("Cierre del punto", () => {
  it("registrado: título de éxito, tabla, seguimiento, tarjetas y botones del diseño", async () => {
    const resumen = {
      actores: [
        { parte: "Vendedor", nombre: "Juan Pérez" },
        { parte: "Comprador", nombre: "María Rodríguez" },
      ],
      valorVenta: 45000000,
      tienePrenda: true,
      decisionPrenda: "levantar",
      documentos: [{ nombre: "paz-y-salvo.pdf" }, { nombre: "impronta.png" }],
      desglose: [
        { nombre: "Derechos de tránsito", valorCop: 300000 },
        { nombre: "Servicio FLIT", valorCop: 66000 },
      ],
      correoEnviado: true,
    };
    const { onTerminar } = montar({ [CIERRE]: () => json(registrado({ resumen })) });

    const titulo = await screen.findByRole("heading", { name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });
    expect(titulo).toHaveClass("text-success");
    expect(screen.queryByText("¡Tu trámite quedó! Recoge en este organismo.")).toBeNull();
    expect(within(screen.getByRole("table")).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["ID del trámite", "Tipo", "Nº radicado", "Organismo"]);
    expect(within(screen.getByRole("table")).getByText("Tránsito de Prueba")).toBeInTheDocument();
    expect(screen.getByTestId("codigo-cierre")).toHaveTextContent("PN-2026-000123");
    expect(screen.getByTestId("tipo-cierre")).toHaveTextContent("Traspaso");
    expect(screen.getByTestId("radicado-flit2")).toHaveTextContent("TR-2026-000123");
    // Seguimiento de 5 pasos, «Enviado al OT» como actual.
    const pasos = within(screen.getByRole("list", { name: "Tracking del radicado" })).getAllByRole("listitem");
    expect(pasos.map((p) => p.getAttribute("data-status"))).toEqual(["done", "done", "current", "pending", "pending"]);
    expect(pasos[2]).toHaveTextContent("Enviado al OT");
    expect(screen.getByText(/utiliza tu ID de trámite o el número de radicado\. También hemos enviado una copia de esta confirmación a tu correo electrónico registrado\./)).toBeInTheDocument();
    // Sin «Copiar ID» ni «Consultar trámite».
    expect(screen.queryByRole("button", { name: /Copiar ID/ })).toBeNull();
    expect(screen.queryByRole("button", { name: "Consultar trámite" })).toBeNull();
    // Tarjetas con datos reales.
    expect(screen.getByRole("region", { name: "Vehículo" })).toHaveTextContent("ABC123");
    expect(screen.getByRole("region", { name: "Actores" })).toHaveTextContent("Vendedor: Juan Pérez");
    expect(screen.getByRole("region", { name: "Actores" })).toHaveTextContent("Comprador: María Rodríguez");
    const condiciones = screen.getByRole("region", { name: "Condiciones Comerciales" });
    expect(condiciones).toHaveTextContent(/Venta: .*45\.000\.000/);
    expect(condiciones).toHaveTextContent("Prenda: Sí · levantar");
    expect(condiciones).toHaveTextContent("paz-y-salvo.pdf, impronta.png");
    const total = screen.getByRole("region", { name: "Valor Total del Trámite" });
    expect(total).toHaveTextContent(/366\.000/);
    expect(total).toHaveTextContent(/Derechos de tránsito: .*300\.000/);
    expect(total).toHaveTextContent(/Servicio FLIT: .*66\.000/);
    const pago = screen.getByRole("region", { name: "Pago en caja" });
    expect(within(pago).getByTestId("medio-cierre")).toHaveTextContent("Efectivo");
    expect(within(pago).getByText("Pagado en caja")).toHaveClass("text-success");
    const docs = screen.getByRole("region", { name: "Documentos cargados del traspaso" });
    expect(within(docs).getAllByText("Cargado")).toHaveLength(2);

    expect(screen.getByRole("button", { name: "Descargar comprobante" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Volver al inicio" }));
    expect(onTerminar).toHaveBeenCalledTimes(1);
  });

  it("sin correo enviado omite la frase de la copia al correo; sin resumen no pinta tarjetas vacías", async () => {
    montar({ [CIERRE]: () => json(registrado()) });

    await screen.findByTestId("codigo-cierre");
    expect(screen.getByText(/utiliza tu ID de trámite o el número de radicado\./)).not.toHaveTextContent("correo electrónico");
    expect(screen.queryByRole("region", { name: "Actores" })).toBeNull();
    expect(screen.queryByRole("region", { name: "Condiciones Comerciales" })).toBeNull();
    expect(screen.queryByRole("region", { name: "Documentos cargados del traspaso" })).toBeNull();
  });

  it("«Descargar comprobante» genera un PDF en el navegador", async () => {
    const crear = vi.fn(() => "blob:comprobante");
    Object.defineProperty(URL, "createObjectURL", { value: crear, configurable: true });
    Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
    const clic = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
    montar({ [CIERRE]: () => json(registrado()) });

    fireEvent.click(await screen.findByRole("button", { name: "Descargar comprobante" }));

    expect(crear).toHaveBeenCalledTimes(1);
    const blob = (crear.mock.calls[0] as unknown as [Blob])[0];
    expect(blob.type).toBe("application/pdf");
    expect(clic).toHaveBeenCalled();
    clic.mockRestore();
  });

  it("sin radicado FLIT 2.0 y sin cobro: la columna dice «En registro» y el medio dice «Sin cobro»", async () => {
    montar({ [CIERRE]: () => json(registrado({ radicadoFlit2: undefined, medio: "sin_cobro", medioCaja: undefined, valorCop: 0 })) });

    await screen.findByTestId("codigo-cierre");
    expect(screen.getByTestId("radicado-flit2")).toHaveTextContent("En registro");
    expect(screen.getByTestId("medio-cierre")).toHaveTextContent("Sin cobro");
  });

  it("muestra «Registrando tu trámite…» mientras carga", async () => {
    montar({ [CIERRE]: () => new Promise<Response>(() => undefined) });
    expect(await screen.findByText("Registrando tu trámite…")).toBeInTheDocument();
  });

  it("en_registro: informa, mantiene «Enviado al OT» pendiente y sondea hasta que queda registrado", async () => {
    let n = 0;
    montar({ [CIERRE]: () => json(++n === 1 ? registrado({ estadoCierre: "en_registro", radicadoFlit2: undefined }) : registrado()) });

    const aviso = await screen.findByRole("heading", { name: "Tu trámite está en proceso de registro" });
    expect(aviso).toHaveClass("text-warning");
    expect(screen.getByTestId("radicado-flit2")).toHaveTextContent("En registro");
    const pasos = within(screen.getByRole("list", { name: "Tracking del radicado" })).getAllByRole("listitem");
    expect(pasos.map((p) => p.getAttribute("data-status"))).toEqual(["done", "done", "current", "pending", "pending"]);
    expect(await screen.findByRole("heading", { name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" }, { timeout: 8000 })).toBeInTheDocument();
  }, 15000);

  it("rechazado: motivo en rojo con el código para acercarse al organismo", async () => {
    montar({ [CIERRE]: () => json(registrado({ estadoCierre: "rechazado", motivo: "La placa ya tiene un trámite en curso.", radicadoFlit2: undefined })) });

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("La placa ya tiene un trámite en curso.");
    expect(alerta).toHaveTextContent("PN-2026-000123");
  });

  it("error: mensaje claro y «Reintentar» vuelve a pedir el cierre", async () => {
    let n = 0;
    const { llamadas } = montar({ [CIERRE]: () => (++n === 1 ? json({ code: "pago_pendiente", message: "El pago aún no se confirma." }, 409) : json(registrado())) });

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));

    expect(await screen.findByTestId("codigo-cierre")).toBeInTheDocument();
    expect(llamadas.filter((l) => l === CIERRE)).toHaveLength(2);
  });

});

describe("Cierre del punto: pago anulado y radicado", () => {
  it("avisa el radicado de FLIT 2.0 cuando el cierre lo trae", async () => {
    const onRadicado = vi.fn();
    montar({ [CIERRE]: () => json(registrado()) }, { onRadicado });
    await waitFor(() => expect(onRadicado).toHaveBeenCalledWith("TR-2026-000123"));
  });

  it("no avisa radicado mientras el trámite sigue en registro sin radicado", async () => {
    const onRadicado = vi.fn();
    montar({ [CIERRE]: () => json(registrado({ estadoCierre: "en_registro", radicadoFlit2: undefined })) }, { onRadicado });
    await screen.findByText(/Tu trámite está en proceso de registro/);
    expect(onRadicado).not.toHaveBeenCalled();
  });

  it("409 tras haber mostrado un pago: avisa onPagoAnulado y no deja el bloque viejo ni «Valor pagado»", async () => {
    let n = 0;
    const onPagoAnulado = vi.fn();
    montar(
      { [CIERRE]: () => (++n === 1 ? json(registrado({ estadoCierre: "en_registro", radicadoFlit2: undefined })) : json({ code: "pago_pendiente", message: "El trámite aún no tiene un pago confirmado." }, 409)) },
      { onPagoAnulado },
    );
    expect(await screen.findByText(/Tu trámite está en proceso de registro/)).toBeInTheDocument();
    await waitFor(() => expect(onPagoAnulado).toHaveBeenCalledTimes(1), { timeout: 8000 });
    expect(screen.queryByText(/Valor pagado/)).toBeNull();
    expect(screen.queryByText(/proceso de registro/)).toBeNull();
  }, 15000);

  it("409 en la primera carga (sin pago mostrado) conserva el error con «Reintentar» y no avisa anulación", async () => {
    const onPagoAnulado = vi.fn();
    montar({ [CIERRE]: () => json({ code: "pago_pendiente", message: "El pago aún no se confirma." }, 409) }, { onPagoAnulado });
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(onPagoAnulado).not.toHaveBeenCalled();
  });
});
