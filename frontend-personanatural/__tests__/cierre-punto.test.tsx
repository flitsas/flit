import { fireEvent, screen, waitFor } from "@testing-library/react";
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
  ...extra,
});

function montar(rutas: Record<string, Manejador>) {
  const llamadas: string[] = [];
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push(clave);
    return (rutas[clave] ?? (() => json({ code: "no_simulado" }, 404)))();
  }) as unknown as typeof fetch;
  const onTerminar = vi.fn();
  renderConQuery(<CierrePunto cliente={crearCliente({ fetchImpl })} solicitudId="s-1" tipoCodigo="TIPO_A" onTerminar={onTerminar} />);
  return { llamadas, onTerminar };
}

describe("Cierre del punto", () => {
  it("registrado: organismo, ID destacado, radicado FLIT 2.0, valor y medio", async () => {
    const { onTerminar } = montar({ [CIERRE]: () => json(registrado()) });

    expect(await screen.findByText("Tu trámite quedó. Recoge en este organismo.")).toBeInTheDocument();
    expect(screen.getByText("Tránsito de Prueba")).toBeInTheDocument();
    expect(screen.getByTestId("codigo-cierre")).toHaveTextContent("PN-2026-000123");
    expect(screen.getByTestId("radicado-flit2")).toHaveTextContent("TR-2026-000123");
    expect(screen.getByText("Efectivo")).toBeInTheDocument();
    expect(screen.getByText(/366\.000/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Copiar ID/ })).toHaveClass("bg-success");

    fireEvent.click(screen.getByRole("button", { name: "Volver al inicio" }));
    expect(onTerminar).toHaveBeenCalledTimes(1);
  });

  it("copiar ID escribe el código público en el portapapeles", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    montar({ [CIERRE]: () => json(registrado()) });

    fireEvent.click(await screen.findByRole("button", { name: /Copiar ID/ }));

    await waitFor(() => expect(writeText).toHaveBeenCalledWith("PN-2026-000123"));
    expect(await screen.findByRole("button", { name: /Copiado/ })).toBeInTheDocument();
  });

  it("sin radicado FLIT 2.0 y sin cobro: no muestra radicado y dice «Sin cobro»", async () => {
    montar({ [CIERRE]: () => json(registrado({ radicadoFlit2: undefined, medio: "sin_cobro", medioCaja: undefined, valorCop: 0 })) });

    await screen.findByTestId("codigo-cierre");
    expect(screen.queryByTestId("radicado-flit2")).toBeNull();
    expect(screen.getByText("Sin cobro")).toBeInTheDocument();
  });

  it("muestra «Registrando tu trámite…» mientras carga", async () => {
    montar({ [CIERRE]: () => new Promise<Response>(() => undefined) });
    expect(await screen.findByText("Registrando tu trámite…")).toBeInTheDocument();
  });

  it("en_registro: informa y sondea hasta que queda registrado", async () => {
    let n = 0;
    montar({ [CIERRE]: () => json(++n === 1 ? registrado({ estadoCierre: "en_registro", radicadoFlit2: undefined }) : registrado()) });

    expect(await screen.findByText(/Tu trámite está en proceso de registro/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Copiar ID/ })).not.toHaveClass("bg-success");
    expect(await screen.findByText("Tu trámite quedó. Recoge en este organismo.", {}, { timeout: 8000 })).toBeInTheDocument();
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

  it("«Consultar trámite» abre Consultar con el código precargado y usa /cierre/{codigo} sin pedir placa", async () => {
    const { llamadas } = montar({
      [CIERRE]: () => json(registrado()),
      "GET /punto/pago/cierre/PN-2026-000123": () => json(registrado()),
    });
    fireEvent.click(await screen.findByRole("button", { name: "Consultar trámite" }));

    const dialogo = await screen.findByRole("dialog");
    expect(dialogo).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Consultar" }));

    await waitFor(() => expect(llamadas).toContain("GET /punto/pago/cierre/PN-2026-000123"));
    expect(llamadas.some((l) => l.includes("/radicados/consulta"))).toBe(false);
  });

  it("Consultar sin pago (404 del cierre) cae a la consulta de siempre con la placa", async () => {
    const { llamadas } = montar({
      [CIERRE]: () => json(registrado()),
      "POST /radicados/consulta": () => json({ code: "x" }, 404),
    });
    fireEvent.click(await screen.findByRole("button", { name: "Consultar trámite" }));
    await screen.findByRole("dialog");
    fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "abc123" } });
    fireEvent.click(screen.getByRole("button", { name: "Consultar" }));

    await waitFor(() => expect(llamadas).toContain("POST /radicados/consulta"));
  });
});
