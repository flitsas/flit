import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ActoresPunto } from "@/components/punto/ActoresPunto";
import type { ClientePortal, EstadoIdentidad, Solicitud } from "@/lib/api/cliente";
import { omitirIdentidadActiva } from "@/lib/configuracion";
import { abrirResumen } from "./fixtures-envio";
import { json, renderConQuery } from "./utilidades";

const AVISO = /Modo de pruebas: la validación de identidad no es obligatoria en este entorno\./;

describe("omitirIdentidadActiva", () => {
  it("solo es true con pruebas.omitirIdentidad === true", () => {
    const base = { version: "1", preguntas: [], banners: [], enlaces: [], soporte: {} } as never;
    expect(omitirIdentidadActiva(undefined)).toBe(false);
    expect(omitirIdentidadActiva(base)).toBe(false);
    expect(omitirIdentidadActiva({ ...(base as object), pruebas: { omitirIdentidad: false } } as never)).toBe(false);
    expect(omitirIdentidadActiva({ ...(base as object), pruebas: { omitirIdentidad: "true" } } as never)).toBe(false);
    expect(omitirIdentidadActiva({ ...(base as object), pruebas: { omitirIdentidad: true } } as never)).toBe(true);
  });
});

const SIN_SOLICITAR: EstadoIdentidad = {
  solicitudId: "sol-1",
  completa: false,
  partes: [{ parte: "vendedor", ordinal: 1, estado: "sin_solicitar", puedeReenviar: false, reenviosRestantes: 5, esperaSegundos: 0 }],
};
const rol = (r: string, etiqueta: string) => ({ rol: r, etiqueta, min: 1, max: 1, biometria: true, firma: false, personaNaturalPermitida: true });
const SOLICITUD = {
  solicitudId: "sol-1",
  version: 1,
  estado: "borrador",
  actores: [{ rol: "vendedor", ordinal: 1, validacionEstado: "sin_solicitar", tipoDocumento: "CC", numeroDocumento: "1037669356", nombre: "DUEÑO", correo: "a@b.co", celular: "3001234567", direccion: "Calle 1", ciudad: "Medellin" }],
  campos: {},
  adjuntos: [],
  pendientes: [],
} as unknown as Solicitud;

function cliente(pruebas: unknown): ClientePortal {
  let llamadas = 0;
  return {
    configuracion: { obtener: vi.fn(async () => ({ version: "1", preguntas: [], banners: [], enlaces: [], soporte: {}, ...(pruebas ? { pruebas } : {}) })) },
    catalogos: { definicion: vi.fn(async () => ({ actores: [rol("vendedor", "Propietario (vendedor)"), rol("comprador", "Comprador")] })) },
    punto: {
      actoresAbrir: vi.fn(async () => ({ solicitudId: "sol-1", puedeSerRadicante: true })),
      actoresIdentidadRadicante: vi.fn(async () => ({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: null })),
      actoresIdentidadEstado: vi.fn(async () => {
        llamadas += 1;
        if (llamadas === 1) throw new Error("sin sesión");
        return SIN_SOLICITAR;
      }),
      actoresInvitar: vi.fn(),
    },
    solicitudes: { obtener: vi.fn(async () => SOLICITUD), guardarActores: vi.fn() },
    consultas: { personaDetalle: vi.fn() },
    identidad: { solicitar: vi.fn(), reenviar: vi.fn(), estado: vi.fn() },
  } as unknown as ClientePortal;
}

function montar(c: ClientePortal) {
  renderConQuery(
    <ActoresPunto cliente={c} placa="XJI21G" documento="1037669356" tipoCodigo="TRASPASO" onAtras={() => undefined} onContinuarRequisitos={() => undefined} onAviso={() => undefined} />,
  );
}

describe("Punto — modo de pruebas de identidad", () => {
  it("con omitirIdentidad: Continuar habilitado sin radicante identificado, partes visibles y aviso", async () => {
    const c = cliente({ omitirIdentidad: true });
    montar(c);
    expect(await screen.findByText(AVISO)).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("button", { name: /Continuar/i })).toBeEnabled());
    expect(screen.getByRole("region", { name: /^Comprador$/i })).toBeInTheDocument();
    await waitFor(() => expect(c.punto.actoresIdentidadRadicante).toHaveBeenCalledTimes(1)); // el lanzamiento sigue ocurriendo
  });

  it("sin el flag: todo como hoy (Continuar bloqueado, sin comprador, sin aviso)", async () => {
    montar(cliente(null));
    await screen.findByRole("button", { name: /Continuar/i });
    expect(screen.getByRole("button", { name: /Continuar/i })).toBeDisabled();
    expect(screen.queryByRole("region", { name: /^Comprador$/i })).toBeNull();
    expect(screen.queryByText(AVISO)).toBeNull();
  });

  it("con pruebas.omitirIdentidad=false tampoco cambia nada", async () => {
    montar(cliente({ omitirIdentidad: false }));
    await screen.findByRole("button", { name: /Continuar/i });
    expect(screen.getByRole("button", { name: /Continuar/i })).toBeDisabled();
    expect(screen.queryByText(AVISO)).toBeNull();
  });
});

const CONFIG = (pruebas?: unknown) => () => json({ version: "1", preguntas: [], banners: [], enlaces: [], soporte: {}, ...(pruebas ? { pruebas } : {}) });

describe("Portal web — Resumen en modo de pruebas", () => {
  it("con omitirIdentidad muestra el aviso", async () => {
    await abrirResumen({ "GET /configuracion": CONFIG({ omitirIdentidad: true }) });
    await waitFor(() => expect(document.getElementById("resumen-modo-pruebas")).toHaveTextContent(AVISO));
  });

  it("sin el flag no hay aviso", async () => {
    await abrirResumen({ "GET /configuracion": CONFIG() });
    await new Promise((r) => setTimeout(r, 50));
    expect(screen.queryAllByText(AVISO)).toHaveLength(0);
  });
});
