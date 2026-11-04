import { act, renderHook, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PersonaActor } from "@/components/tramite/renderers/PersonaActor";
import type { ActorRol, ClientePortal } from "@/lib/api/cliente";
import { filaVacia, type FilaActor } from "@/lib/tramite/actores";
import { limpiarEnviosFallidos, marcarEnvioFallido, useEnviosFallidos } from "@/lib/tramite/envioIdentidad";
import { renderConQuery } from "./utilidades";

const ROL: ActorRol = { rol: "vendedor", etiqueta: "Vendedor", min: 1, max: 1, biometria: true, firma: false, personaNaturalPermitida: true };

const fila = (): FilaActor => ({
  ...filaVacia("vendedor", 1),
  numeroDocumento: "52123456",
  nombre: "ANA RUIZ",
  correo: "ana@correo.co",
  celular: "3001234567",
  ciudad: "Medellin",
  consultado: true,
  nombreConsultado: true,
  runt: { inscrito: true, multasPendientes: null, nroPazYSalvo: null, comparendos: null },
});

function montar(extra: { envioAutomatico?: boolean; fallaEnvio?: string; estado?: "sin_solicitar" | "pendiente" } = {}) {
  const { estado = "sin_solicitar", ...resto } = extra;
  return renderConQuery(
    <PersonaActor
      fila={fila()}
      rol={ROL}
      esPropietario
      solicitudId="s-1"
      cliente={{ consultas: { personaDetalle: vi.fn() } } as unknown as ClientePortal}
      mostrarErrores={false}
      estado={estado}
      versionEstado={0}
      onCambio={() => undefined}
      guardarRol={async () => undefined}
      refrescarIdentidad={() => undefined}
      reportarError={() => undefined}
      {...resto}
    />,
  );
}

afterEach(() => limpiarEnviosFallidos());

describe("validación de identidad: envío automático solo en el portal web", () => {
  it("el punto (sin envío automático) conserva su botón de validación", () => {
    montar();
    expect(screen.getByRole("button", { name: "Reenviar Validación" })).toBeEnabled();
  });

  it("el portal web no ofrece el primer envío como botón mientras no haya fallo", () => {
    montar({ envioAutomatico: true });
    expect(screen.queryByRole("button", { name: /validación/i })).toBeNull();
  });

  it("el portal web con envío fallido muestra el motivo en una alerta y el botón de alerta", () => {
    montar({ envioAutomatico: true, fallaEnvio: "revisa el correo y el celular." });
    expect(screen.getByRole("alert")).toHaveTextContent("revisa el correo y el celular.");
    expect(screen.getByRole("button", { name: "Reenviar validación" })).toBeEnabled();
  });

  it("una validación ya enviada no muestra el botón de alerta aunque quede un fallo anterior", () => {
    montar({ envioAutomatico: true, fallaEnvio: "motivo viejo", estado: "pendiente" });
    expect(screen.queryByText(/motivo viejo/)).toBeNull();
  });
});

describe("aviso de envíos de identidad pendientes (Resumen)", () => {
  it("cuenta las personas con envío fallido por solicitud y se limpia al enviarse", () => {
    const { result } = renderHook(() => useEnviosFallidos("s-1"));
    expect(result.current).toBe(0);
    act(() => marcarEnvioFallido("s-1", "a-1", true));
    act(() => marcarEnvioFallido("s-1", "a-1", true));
    act(() => marcarEnvioFallido("s-2", "a-9", true));
    expect(result.current).toBe(1);
    act(() => marcarEnvioFallido("s-1", "a-1", false));
    expect(result.current).toBe(0);
  });
});
