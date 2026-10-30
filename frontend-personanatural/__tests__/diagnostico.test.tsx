import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { fijarContexto } from "@/lib/tramite/contexto";
import type { Diagnostico, VehiculoMinimo } from "@/lib/api/cliente";
import { solicitudDe } from "./fixtures-wizard";
import { CONTEXTO, cuerpo, DIAGNOSTICO_BLOQUEADO, DIAGNOSTICO_OK, guardado, montar, type Manejador, nodo, resolverCaptcha, VEHICULO, wizardAbierto } from "./montar-wizard";
import { json } from "./utilidades";

describe("paso 1: diagnóstico con consultas reales (F2)", () => {
  const rutasConsulta = (diagnostico: Diagnostico, vehiculo: VehiculoMinimo = VEHICULO): Record<string, Manejador> => {
    let consultado = false;
    return {
      "POST /solicitudes/s-1/consultas/vehiculo": () => json(vehiculo),
      "POST /solicitudes/s-1/diagnostico": () => {
        consultado = true;
        return json(diagnostico);
      },
      "GET /solicitudes/s-1": () => json(consultado ? guardado(diagnostico) : solicitudDe()),
    };
  };

  it("consulta el vehículo con placa y cédula del propietario, con consentimiento y captcha, y pinta lo que devuelve el back", async () => {
    const { llamadas } = await wizardAbierto(rutasConsulta(DIAGNOSTICO_OK));
    expect(screen.queryByText(/Consultado en RUNT/)).not.toBeInTheDocument();

    resolverCaptcha();

    expect(await screen.findByText("Consultado en RUNT el 28/10/2026 15:23")).toBeInTheDocument();
    expect(cuerpo(llamadas, "POST /solicitudes/s-1/consultas/vehiculo")).toEqual({ placa: "ABC123", propietario: { tipo: "CC", numero: "1020456789" }, consentimiento: true, consentimientoVersion: "v1" });
    expect(llamadas.find((l) => l.clave === "POST /solicitudes/s-1/consultas/vehiculo")?.init.headers).toMatchObject({ "x-captcha-token": "dev-captcha-token" });
    expect(cuerpo(llamadas, "POST /solicitudes/s-1/diagnostico")).toEqual({ propietario: { tipoDocumento: "CC", numeroDocumento: "1020456789" } });

    const indicadores = within(screen.getByRole("list", { name: "Indicadores del vehículo" }));
    expect(indicadores.getAllByRole("listitem")).toHaveLength(3);
    expect(indicadores.getByText("SOAT").closest("li")).toHaveTextContent("SOAT: OK");
    expect(indicadores.getByText("Multas asociadas").closest("li")).toHaveTextContent("Multas asociadas: OK");

    const tecnicos = screen.getByRole("region", { name: "Datos técnicos del vehículo" });
    expect(within(tecnicos).getByText("CAMIONETA")).toBeInTheDocument();
    expect(within(tecnicos).getByText("Propietario Actual")).toBeInTheDocument();
    expect(within(tecnicos).getByText("C.C. 1.020.456.789")).toBeInTheDocument();
  });

  it("sin bloqueos habilita «Continuar» y lleva al paso 2 con el foco en su título", async () => {
    await wizardAbierto(rutasConsulta(DIAGNOSTICO_OK));
    expect(screen.getByRole("button", { name: "Continuar" })).toBeDisabled();
    expect(screen.getByText("Verifica tu identidad y consulta el vehículo para continuar.")).toBeInTheDocument();

    resolverCaptcha();
    await screen.findByText(/Consultado en RUNT/);
    const continuar = screen.getByRole("button", { name: "Continuar" });
    await waitFor(() => expect(continuar).toBeEnabled());
    fireEvent.click(continuar);

    await waitFor(() => expect(nodo(2)).toHaveAttribute("aria-current", "step"));
    expect(screen.getByRole("navigation", { name: "Ruta de navegación" })).toHaveTextContent("Actores");
    expect(nodo(1)).not.toHaveAttribute("aria-current");
    expect(nodo(1)).toBeEnabled();
    await waitFor(() => expect(screen.getByRole("heading", { level: 2, name: "Paso 2 · Actores" })).toHaveFocus());
    expect(window.scrollTo).toHaveBeenCalled();
  });

  it("con pendientes muestra el aviso amarillo con role=alert y deja «Continuar» deshabilitado con su motivo", async () => {
    await wizardAbierto(rutasConsulta(DIAGNOSTICO_BLOQUEADO));
    resolverCaptcha();

    const aviso = await screen.findByText(/Para continuar con la radicación debes estar al día en pagos de SOAT, multas e impuestos vehiculares\./);
    expect(aviso.closest('[role="alert"]')).not.toBeNull();
    expect(screen.getByText("SOAT").closest("li")).toHaveTextContent("SOAT: POR REVISAR");
    const continuar = screen.getByRole("button", { name: "Continuar" });
    expect(continuar).toBeDisabled();
    expect(continuar).toHaveAccessibleDescription("Resuelve los pendientes del diagnóstico para continuar.");
  });

  it("mientras consulta el vehículo conserva el loader y muestra la frase de espera", async () => {
    await wizardAbierto({
      "POST /solicitudes/s-1/consultas/vehiculo": () => new Promise(() => {}),
    });
    resolverCaptcha();

    const frase = await screen.findByText("Consultando el vehículo…");
    const espera = frase.closest('[role="status"]');
    expect(espera).not.toBeNull();
    expect(espera?.querySelector(".mobility-orbit")).not.toBeNull();
  });

  it("si el vehículo no existe lo dice, no pide diagnóstico y no deja continuar", async () => {
    const { llamadas } = await wizardAbierto({ "POST /solicitudes/s-1/consultas/vehiculo": () => json({ ...VEHICULO, existe: false }) });
    resolverCaptcha();

    expect(await screen.findByText(/No encontramos el vehículo/)).toBeInTheDocument();
    expect(llamadas.some((l) => l.clave.endsWith("/diagnostico"))).toBe(false);
    expect(screen.getByRole("button", { name: "Continuar" })).toBeDisabled();
  });

  it("si el RUNT no responde avisa y «Reintentar» pide de nuevo la verificación para volver a consultar", async () => {
    let intentos = 0;
    const { llamadas } = await wizardAbierto({
      "POST /solicitudes/s-1/consultas/vehiculo": () => (++intentos === 1 ? json({ code: "proveedor_no_disponible", title: "Consulta no disponible" }, 503) : json(VEHICULO)),
      "POST /solicitudes/s-1/diagnostico": () => json(DIAGNOSTICO_OK),
      "GET /solicitudes/s-1": () => json(intentos > 1 ? guardado(DIAGNOSTICO_OK) : solicitudDe()),
    });
    resolverCaptcha();

    expect(await screen.findByText("No pudimos consultar el RUNT. Intenta de nuevo.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    resolverCaptcha();

    expect(await screen.findByText(/Consultado en RUNT/)).toBeInTheDocument();
    expect(llamadas.filter((l) => l.clave === "POST /solicitudes/s-1/consultas/vehiculo")).toHaveLength(2);
  });

  it("ante un 429 muestra la espera, no reintenta solo y deja «Reintentar» deshabilitado", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const { llamadas } = await wizardAbierto({
        "POST /solicitudes/s-1/consultas/vehiculo": () => new Response(JSON.stringify({ code: "cedula_rate_limited", title: "Tope" }), { status: 429, headers: { "content-type": "application/json", "retry-after": "30" } }),
      });
      resolverCaptcha();

      expect(await screen.findByText("Alcanzaste el máximo de consultas por hora para este documento. Intenta más tarde.")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Reintentar" })).toBeDisabled();
      expect(screen.getByText(/Podrás intentarlo de nuevo en \d+ s\./)).toBeInTheDocument();
      await act(async () => {
        await vi.advanceTimersByTimeAsync(5000);
      });
      expect(llamadas.filter((l) => l.clave === "POST /solicitudes/s-1/consultas/vehiculo")).toHaveLength(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it("con VIN consulta solo por VIN, sin la cédula del propietario", async () => {
    fijarContexto({ ...CONTEXTO, placa: undefined });
    const { llamadas } = montar({
      "GET /solicitudes/s-1": () => json(solicitudDe({ placa: null, vin: "9BWZZZ377VT004251" })),
      "POST /solicitudes/s-1/consultas/vehiculo": () => json(VEHICULO),
      "POST /solicitudes/s-1/diagnostico": () => json(DIAGNOSTICO_OK),
    });
    await screen.findByRole("heading", { level: 1, name: "Trámite de Trámite A" });
    resolverCaptcha();

    await screen.findByRole("list", { name: "Indicadores del vehículo" });
    expect(cuerpo(llamadas, "POST /solicitudes/s-1/consultas/vehiculo")).toEqual({ vin: "9BWZZZ377VT004251", consentimiento: true, consentimientoVersion: "v1" });
    const tecnicos = screen.getByRole("region", { name: "Datos técnicos del vehículo" });
    expect(within(tecnicos).getByText("VIN")).toBeInTheDocument();
    expect(within(tecnicos).getByText("9BWZZZ377VT004251")).toBeInTheDocument();
  });

  it("al reanudar un borrador con diagnóstico guardado sin bloqueos muestra sus indicadores y sigue en el paso que falta", async () => {
    await wizardAbierto({ "GET /solicitudes/s-1": () => json(guardado(DIAGNOSTICO_OK)) });

    expect(nodo(2)).toHaveAttribute("aria-current", "step");
    expect(nodo(1)).toBeEnabled();
    fireEvent.click(nodo(1));
    expect(await screen.findByText("Consultado en RUNT el 28/10/2026 15:23")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Continuar" })).toBeEnabled();
  });
});

