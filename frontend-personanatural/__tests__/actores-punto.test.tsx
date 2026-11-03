import { screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ActoresPunto } from "@/components/punto/ActoresPunto";
import type { ClientePortal, EstadoIdentidad, Solicitud } from "@/lib/api/cliente";
import { renderConQuery } from "./utilidades";

const ESTADO_APROBADO: EstadoIdentidad = {
  solicitudId: "sol-1",
  completa: false,
  partes: [
    { parte: "vendedor", ordinal: 1, estado: "aprobada", puedeReenviar: false, reenviosRestantes: 5, esperaSegundos: 0 },
    { parte: "comprador", ordinal: 1, estado: "sin_solicitar", puedeReenviar: false, reenviosRestantes: 5, esperaSegundos: 0 },
  ],
};

const SOLICITUD: Solicitud = {
  solicitudId: "sol-1",
  codigoPublico: "P-1",
  version: 1,
  estado: "borrador",
  tipoTramiteCodigo: "TRASPASO",
  organismoId: "o-1",
  actores: [
    {
      rol: "vendedor",
      ordinal: 1,
      validacionEstado: "aprobada",
      tipoDocumento: "CC",
      numeroDocumento: "1037669356",
      nombre: "DUEÑO PRUEBA",
      correo: "dueno@correo.co",
      celular: "3001234567",
      direccion: "Calle 1",
      ciudad: "Medellin",
    },
  ],
  campos: {},
  adjuntos: [],
  diagnostico: null,
  comercial: null,
  requisitos: null,
  pasos: [],
  pendientes: [],
  completo: false,
  definicionCambio: null,
} as unknown as Solicitud;

const rol = (r: string, etiqueta: string) => ({ rol: r, etiqueta, min: 1, max: 1, biometria: true, firma: false, personaNaturalPermitida: true });
const ACTORES_TRASPASO = [rol("vendedor", "Propietario (vendedor)"), rol("comprador", "Comprador")];

function clienteMock(overrides: {
  actores?: ReturnType<typeof rol>[];
  estado?: () => Promise<EstadoIdentidad>;
  abrir?: () => Promise<unknown>;
  radicante?: () => Promise<unknown>;
  obtener?: () => Promise<Solicitud>;
}): ClientePortal {
  const abrir = overrides.abrir ?? vi.fn(async () => ({ solicitudId: "nueva", puedeSerRadicante: true }));
  const radicante = overrides.radicante ?? vi.fn(async () => ({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: "https://verify.example/x" }));
  const estado =
    overrides.estado ??
    vi.fn(async () => {
      throw new Error("sin sesión");
    });
  const obtener = overrides.obtener ?? vi.fn(async () => SOLICITUD);
  return {
    catalogos: { definicion: vi.fn(async () => ({ actores: overrides.actores ?? ACTORES_TRASPASO })) },
    punto: {
      actoresAbrir: abrir,
      actoresIdentidadRadicante: radicante,
      actoresIdentidadEstado: estado,
      actoresInvitar: vi.fn(),
    },
    solicitudes: { obtener, guardarActores: vi.fn() },
    consultas: { personaDetalle: vi.fn() },
    identidad: { solicitar: vi.fn(), reenviar: vi.fn(), estado: vi.fn() },
  } as unknown as ClientePortal;
}

describe("ActoresPunto — fichas portal + reanudación", () => {
  it("si el vendedor ya está aprobado, no vuelve a abrir ni a pedir identidad y muestra fichas", async () => {
    const abrir = vi.fn(async () => ({ solicitudId: "nueva", puedeSerRadicante: true }));
    const radicante = vi.fn(async () => ({ capturaUrl: "https://verify.example/x" }));
    const estado = vi.fn(async () => ESTADO_APROBADO);
    const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);

    renderConQuery(
      <ActoresPunto
        cliente={clienteMock({ estado, abrir, radicante })}
        placa="XJI21G"
        documento="1037669356"
        tipoCodigo="TRASPASO"
        onAtras={() => undefined}
        onContinuarRequisitos={() => undefined}
        onAviso={() => undefined}
      />,
    );

    expect(await screen.findByText(/Identidad validada/i)).toBeInTheDocument();
    const vendedor = screen.getByRole("region", { name: /Propietario \(vendedor\)/i });
    const comprador = screen.getByRole("region", { name: /^Comprador$/i });
    expect(within(vendedor).getByLabelText(/Teléfono/i)).toBeInTheDocument();
    expect(within(vendedor).getByLabelText(/Correo electrónico/i)).toBeInTheDocument();
    expect(within(vendedor).getByLabelText(/^Ciudad/i)).toBeInTheDocument();
    expect(within(vendedor).getByLabelText(/Dirección/i)).toBeInTheDocument();
    expect(within(comprador).getByLabelText(/Número de documento/i)).toBeInTheDocument();
    await waitFor(() => {
      expect(abrir).not.toHaveBeenCalled();
      expect(radicante).not.toHaveBeenCalled();
    });
    expect(openSpy).not.toHaveBeenCalled();
    openSpy.mockRestore();
  });

  it("tipo de un solo rol: una sola tarjeta (titular), sin comprador, con el documento fijo, y continúa al aprobarse", async () => {
    const estado = vi.fn(async () => ({
      ...ESTADO_APROBADO,
      partes: [{ parte: "titular", ordinal: 1, estado: "aprobada" as const, puedeReenviar: false, reenviosRestantes: 5, esperaSegundos: 0 }],
    }));
    const solicitud = {
      ...SOLICITUD,
      tipoTramiteCodigo: "CAMBIO_COLOR",
      actores: [{ ...SOLICITUD.actores[0], rol: "titular" }],
    } as unknown as Solicitud;
    const onContinuar = vi.fn();
    renderConQuery(
      <ActoresPunto
        cliente={clienteMock({ estado, actores: [rol("titular", "Titular")], obtener: async () => solicitud })}
        placa="XJI21G"
        documento="1037669356"
        tipoCodigo="CAMBIO_COLOR"
        onAtras={() => undefined}
        onContinuarRequisitos={onContinuar}
        onAviso={() => undefined}
      />,
    );
    const titular = await screen.findByRole("region", { name: /^Titular$/i });
    expect(screen.queryByRole("region", { name: /Comprador/i })).toBeNull();
    expect(screen.queryByRole("region", { name: /vendedor/i })).toBeNull();
    expect(within(titular).getByLabelText(/Número de documento/i)).toHaveValue("C.C. 1.037.669.356");
    expect(within(titular).getByLabelText(/Número de documento/i)).toHaveAttribute("readonly");
    const continuar = await screen.findByRole("button", { name: /Continuar/i });
    await waitFor(() => expect(continuar).toBeEnabled());
  });
});
