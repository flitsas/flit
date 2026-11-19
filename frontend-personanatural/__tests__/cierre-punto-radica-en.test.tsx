// Cierre del punto: mientras el trámite sigue en registro se dice a qué hora se envía al organismo (`pago.radicaEn`).
import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CierrePunto } from "@/components/punto/CierrePunto";
import { crearCliente } from "@/lib/api/cliente";
import { formatearHora12 } from "@/lib/formato";
import { json, renderConQuery } from "./utilidades";

type Manejador = () => Response | Promise<Response>;

const CIERRE = "GET /punto/pago/cierre";
const ESTADO = "GET /punto/pago";
const enRegistro = {
  estadoCierre: "en_registro",
  codigoPublico: "PN-2026-000123",
  valorCop: 366000,
  medio: "caja",
  medioCaja: "efectivo",
  pagadoEn: "2026-11-18T02:30:00+00:00",
  recogeEn: { organismo: "Tránsito de Prueba" },
};
const estadoPago = (pago: Record<string, unknown>) => ({
  solicitudId: "s-1",
  codigoPublico: "PN-2026-000123",
  estado: "pagada",
  puedePagar: false,
  identidadesPendientes: [],
  pago: { valorCop: 366000, medio: "caja", medioCaja: "efectivo", pagadoEn: "2026-11-18T02:30:00+00:00", ...pago },
});

function montar(rutas: Record<string, Manejador>) {
  const llamadas: string[] = [];
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push(clave);
    return (rutas[clave] ?? (() => json({ code: "no_simulado" }, 404)))();
  }) as unknown as typeof fetch;
  renderConQuery(<CierrePunto cliente={crearCliente({ fetchImpl })} solicitudId="s-1" tipoCodigo="TIPO_A" onTerminar={vi.fn()} />);
  return llamadas;
}

describe("formatearHora12", () => {
  it("12 horas en hora de Colombia", () => {
    expect(formatearHora12("2026-11-18T02:41:00Z")).toBe("9:41 p. m.");
    expect(formatearHora12("2026-11-18T15:05:00Z")).toBe("10:05 a. m.");
    expect(formatearHora12("2026-11-18T05:00:00Z")).toBe("12:00 a. m.");
    expect(formatearHora12("2026-11-18T17:00:00Z")).toBe("12:00 p. m.");
    expect(formatearHora12(null)).toBeNull();
    expect(formatearHora12("no es fecha")).toBeNull();
  });
});

describe("Cierre del punto · hora de envío al organismo", () => {
  it("en registro con pago.enRegistro y radicaEn: «Se envía al organismo a las 9:41 p. m.»", async () => {
    montar({ [CIERRE]: () => json(enRegistro), [ESTADO]: () => json(estadoPago({ enRegistro: true, radicaEn: "2026-11-18T02:41:00Z" })) });

    expect(await screen.findByText(/Tu trámite está en proceso de registro/)).toBeInTheDocument();
    expect(await screen.findByTestId("radica-en")).toHaveTextContent("Se envía al organismo a las 9:41 p. m.");
  });

  it("sin radicaEn (o fuera de registro) no se muestra la hora", async () => {
    const llamadas = montar({ [CIERRE]: () => json(enRegistro), [ESTADO]: () => json(estadoPago({ enRegistro: true })) });

    await screen.findByText(/Tu trámite está en proceso de registro/);
    await waitFor(() => expect(llamadas).toContain(ESTADO));
    expect(screen.queryByTestId("radica-en")).not.toBeInTheDocument();
  });

  it("si el estado del pago falla, el cierre sigue sin la hora", async () => {
    montar({ [CIERRE]: () => json(enRegistro), [ESTADO]: () => json({ code: "x" }, 500) });

    expect(await screen.findByText(/Tu trámite está en proceso de registro/)).toBeInTheDocument();
    expect(screen.queryByTestId("radica-en")).not.toBeInTheDocument();
  });

  it("ya registrado no pide el estado ni muestra la hora", async () => {
    const llamadas = montar({ [CIERRE]: () => json({ ...enRegistro, estadoCierre: "registrado", radicadoFlit2: "TR-1" }) });

    await screen.findByRole("heading", { name: "¡Muchas gracias, tu trámite se ha enviado con éxito!" });
    expect(llamadas).not.toContain(ESTADO);
    expect(screen.queryByTestId("radica-en")).not.toBeInTheDocument();
  });
});
