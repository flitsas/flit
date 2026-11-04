import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactElement } from "react";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ActoresPunto } from "@/components/punto/ActoresPunto";
import type { ClientePortal, EstadoIdentidad, Solicitud } from "@/lib/api/cliente";
import { ApiError } from "@/lib/api/errores";
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
  placa: "XJI21G",
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
  persona?: () => Promise<unknown>;
  guardar?: (...args: unknown[]) => Promise<unknown>;
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
    solicitudes: { obtener, guardarActores: overrides.guardar ?? vi.fn(async () => ({ ...SOLICITUD, version: 2 })) },
    consultas: { personaDetalle: overrides.persona ?? vi.fn() },
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

const SIN_SOLICITAR: EstadoIdentidad = {
  solicitudId: "sol-1",
  completa: false,
  partes: [{ parte: "vendedor", ordinal: 1, estado: "sin_solicitar", puedeReenviar: false, reenviosRestantes: 5, esperaSegundos: 0 }],
};

/** Sin sesión previa al abrir; después, el estado de la identidad del radicante. */
function estadoTrasAbrir(despues: EstadoIdentidad) {
  let llamadas = 0;
  return vi.fn(async () => {
    llamadas += 1;
    if (llamadas === 1) throw new Error("sin sesión");
    return despues;
  });
}

function montarPunto(cliente: ClientePortal, onAviso = vi.fn(), onContinuar: () => void = () => undefined) {
  renderConQuery(
    <ActoresPunto
      cliente={cliente}
      placa="XJI21G"
      documento="1037669356"
      tipoCodigo="TRASPASO"
      onAtras={() => undefined}
      onContinuarRequisitos={onContinuar}
      onAviso={onAviso}
    />,
  );
  return onAviso;
}

describe("ActoresPunto — épica #14185: identidad de quien radica", () => {
  it("al entrar lanza sola la identidad in-situ, sin botón de validar ni invitaciones", async () => {
    const radicante = vi.fn(async () => ({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: "https://verify.example/x" }));
    const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);
    montarPunto(clienteMock({ estado: estadoTrasAbrir(SIN_SOLICITAR), radicante }));
    await waitFor(() => expect(radicante).toHaveBeenCalledTimes(1));
    expect(openSpy).toHaveBeenCalledWith("https://verify.example/x", "_blank", "noopener,noreferrer");
    expect(await screen.findByText(/Validación: en curso/i)).toBeInTheDocument();
    expect(screen.getByText(/Completa la validación en la ventana que se abrió/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Abrir validación/i })).toHaveAttribute("href", "https://verify.example/x");
    expect(screen.queryByText(/Validando tu identidad/i)).toBeNull(); // sin cargador colgado
    expect(screen.queryByText(/Identidad validada/i)).toBeNull(); // sin aprobación real no hay alerta de validada
    expect(screen.queryByRole("button", { name: /Validar identidad en el punto|Enviar validación|Reenviar/i })).toBeNull();
    expect(screen.queryByRole("region", { name: /^Comprador$/i })).toBeNull();
    expect(screen.getByRole("button", { name: /Continuar/i })).toBeDisabled();
    openSpy.mockRestore();
  });

  it("al pasar a aprobada muestra «¡Identidad validada!» con el nombre y la placa, sin el documento ni jerga", async () => {
    const radicante = vi.fn(async () => ({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: null }));
    let n = 0;
    const estado = vi.fn(async () => {
      n += 1;
      if (n === 1) throw new Error("sin sesión");
      return n < 3 ? SIN_SOLICITAR : ESTADO_APROBADO;
    });
    montarPunto(clienteMock({ estado, radicante }));
    const titulo = await screen.findByText("¡Identidad validada!");
    expect(titulo).toHaveAttribute("role", "status");
    const alerta = document.getElementById("punto-actores-dueno-ok")!;
    expect(alerta.textContent).toContain("Dueño Prueba, confirmamos que eres el propietario de la placa XJI21G. Ya puedes continuar con tu trámite.");
    expect(alerta.textContent).not.toContain("1037669356");
    expect(screen.queryByText(/RUNT|radica en el punto/i)).toBeNull();
    expect(screen.queryByText("Identificado")).toBeNull();
  });

  it("no dueño: informa, sin invitaciones, sin lanzar identidad ni Continuar", async () => {
    const radicante = vi.fn();
    const abrir = vi.fn(async () => ({ solicitudId: "x", puedeSerRadicante: false, message: "No puedes realizar este trámite porque no eres el propietario del vehículo." }));
    montarPunto(clienteMock({ abrir, radicante }));
    expect(await screen.findByText(/no eres el propietario del vehículo/i)).toBeInTheDocument();
    expect(radicante).not.toHaveBeenCalled();
    expect(screen.queryByRole("region", { name: /^Comprador$/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /^Continuar$/i })).toBeNull();
  });

  it("si la identidad falla informa el motivo (aviso) y permite Reintentar, sin invitaciones", async () => {
    const radicante = vi
      .fn()
      .mockRejectedValueOnce(new ApiError("proveedor_rechazo", "El proveedor rechazó la validación.", 422))
      .mockResolvedValue({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: null });
    const onAviso = montarPunto(clienteMock({ estado: estadoTrasAbrir(SIN_SOLICITAR), radicante }));
    expect(await screen.findByText(/El proveedor rechazó la validación/i)).toBeInTheDocument();
    expect(onAviso).toHaveBeenCalledWith("No pudimos validar tu identidad", expect.stringMatching(/rechaz/i), "alerta");
    expect(screen.queryByRole("region", { name: /^Comprador$/i })).toBeNull();
    expect(screen.queryByText(/Validando tu identidad/i)).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /Reintentar/i }));
    await waitFor(() => expect(radicante).toHaveBeenCalledTimes(2));
  });

  it("el radicante no necesita correo ni celular: un correo con ñ avisa pero no bloquea ni se envía", async () => {
    const radicante = vi.fn(async () => ({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: null }));
    const conCorreo = {
      ...SOLICITUD,
      actores: [{ ...SOLICITUD.actores[0], correo: "wiññyn@flitsas.com", celular: "" }],
    } as unknown as Solicitud;
    montarPunto(clienteMock({ estado: estadoTrasAbrir(SIN_SOLICITAR), radicante, obtener: async () => conCorreo }));
    await waitFor(() => expect(radicante).toHaveBeenCalled());
    const llamada = (radicante.mock.calls[0] as unknown[])[0] as { correo?: string };
    expect(llamada.correo).toBeUndefined();
  });

  it("el lanzamiento es una sola solicitud por entrada, aunque el padre re-renderice con otro onAviso", async () => {
    const radicante = vi.fn(async () => ({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: null }));
    const cliente = clienteMock({ estado: estadoTrasAbrir(SIN_SOLICITAR), radicante });
    const props = { cliente, placa: "XJI21G", documento: "1037669356", tipoCodigo: "TRASPASO", onAtras: () => undefined, onContinuarRequisitos: () => undefined };
    const qc = new QueryClient();
    const envolver = (ui: ReactElement) => <QueryClientProvider client={qc}>{ui}</QueryClientProvider>;
    const { rerender } = render(envolver(<ActoresPunto {...props} onAviso={() => undefined} />));
    await waitFor(() => expect(radicante).toHaveBeenCalledTimes(1));
    rerender(envolver(<ActoresPunto {...props} onAviso={() => undefined} />));
    rerender(envolver(<ActoresPunto {...props} onAviso={() => undefined} />));
    await new Promise((r) => setTimeout(r, 50));
    expect(radicante).toHaveBeenCalledTimes(1);
  });

  it("no_es_propietario muestra el mensaje de no propietario sin Reintentar ni invitaciones", async () => {
    const radicante = vi.fn(async () => {
      throw new ApiError("no_es_propietario", "No puedes realizar este trámite porque no eres el propietario del vehículo.", 422);
    });
    montarPunto(clienteMock({ estado: estadoTrasAbrir(SIN_SOLICITAR), radicante }));
    expect(await screen.findByText(/no eres el propietario del vehículo/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Reintentar/i })).toBeNull();
    expect(screen.queryByRole("region", { name: /^Comprador$/i })).toBeNull();
    expect(radicante).toHaveBeenCalledTimes(1);
  });

  it("el lanzamiento espera a la consulta automática y manda el nombre del RUNT", async () => {
    const radicante = vi.fn(async () => ({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: null }));
    let resolver: (v: unknown) => void = () => undefined;
    const persona = vi.fn(() => new Promise((res) => { resolver = res; }));
    const vacia = { ...SOLICITUD, actores: [] } as unknown as Solicitud;
    montarPunto(clienteMock({ estado: estadoTrasAbrir(SIN_SOLICITAR), radicante, persona, obtener: async () => vacia }));
    await waitFor(() => expect(persona).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 100));
    expect(radicante).not.toHaveBeenCalled();
    resolver({ existe: true, nombres: "ANA", apellidos: "RUNT", nombreCompleto: "ANA RUNT", ciudad: null });
    await waitFor(() => expect(radicante).toHaveBeenCalledTimes(1));
    expect((radicante.mock.calls[0] as unknown[])[0]).toMatchObject({ nombre: "ANA RUNT", documento: "1037669356" });
  });

  it("Continuar guarda las fichas editadas antes de avanzar", async () => {
    const guardar = vi.fn(async () => ({ ...SOLICITUD, version: 2 }));
    const onContinuar = vi.fn();
    montarPunto(clienteMock({ estado: async () => ESTADO_APROBADO, guardar }), vi.fn(), onContinuar);
    const vendedor = await screen.findByRole("region", { name: /Propietario \(vendedor\)/i });
    fireEvent.change(within(vendedor).getByLabelText(/Teléfono/i), { target: { value: "3109998877" } });
    fireEvent.change(within(vendedor).getByLabelText(/Correo electrónico/i), { target: { value: "nuevo@correo.co" } });
    fireEvent.click(screen.getByRole("button", { name: /^Continuar$/i }));
    await waitFor(() => expect(onContinuar).toHaveBeenCalledTimes(1));
    const llamada = (guardar.mock.calls[0] as unknown[])[1] as { actores: { rol: string; celular: string; correo: string }[] };
    expect(llamada.actores[0]).toMatchObject({ rol: "vendedor", celular: "3109998877", correo: "nuevo@correo.co" });
  });

  it("si el guardado falla no avanza y muestra el motivo junto a la tarjeta", async () => {
    const guardar = vi.fn(async () => {
      throw new ApiError("actor_incompleto", "Falta el celular del titular.", 422);
    });
    const onContinuar = vi.fn();
    const onAviso = montarPunto(clienteMock({ estado: async () => ESTADO_APROBADO, guardar }), vi.fn(), onContinuar);
    await screen.findByRole("region", { name: /Propietario \(vendedor\)/i });
    fireEvent.click(screen.getByRole("button", { name: /^Continuar$/i }));
    expect(await screen.findByText(/Falta el celular del titular/i)).toBeInTheDocument();
    expect(onContinuar).not.toHaveBeenCalled();
    expect(onAviso).toHaveBeenCalledWith("No pudimos guardar los datos", expect.any(String), "alerta");
  });

  describe("reanudación de la sesión del punto", () => {
    function estadoConSesion() {
      let n = 0;
      return vi.fn(async () => (++n === 1 ? ESTADO_APROBADO : SIN_SOLICITAR));
    }
    const abrirNuevo = () => vi.fn(async () => ({ solicitudId: "nueva", puedeSerRadicante: true }));

    it("un borrador ya radicado no se reutiliza: abre uno nuevo", async () => {
      const abrir = abrirNuevo();
      const obtener = vi.fn().mockResolvedValueOnce({ ...SOLICITUD, estado: "radicada" }).mockResolvedValue(SOLICITUD);
      montarPunto(clienteMock({ estado: estadoConSesion(), abrir, obtener }));
      await waitFor(() => expect(abrir).toHaveBeenCalledTimes(1));
    });

    it("un borrador de otra placa o de otro documento no se reutiliza", async () => {
      const abrirPlaca = abrirNuevo();
      const { unmount } = renderConQuery(
        <ActoresPunto cliente={clienteMock({ estado: estadoConSesion(), abrir: abrirPlaca })} placa="AAA111" documento="1037669356" tipoCodigo="TRASPASO" onAtras={() => undefined} onContinuarRequisitos={() => undefined} onAviso={() => undefined} />,
      );
      await waitFor(() => expect(abrirPlaca).toHaveBeenCalledTimes(1));
      unmount();
      const abrirDoc = abrirNuevo();
      renderConQuery(
        <ActoresPunto cliente={clienteMock({ estado: estadoConSesion(), abrir: abrirDoc })} placa="XJI21G" documento="79555444" tipoCodigo="TRASPASO" onAtras={() => undefined} onContinuarRequisitos={() => undefined} onAviso={() => undefined} />,
      );
      await waitFor(() => expect(abrirDoc).toHaveBeenCalledTimes(1));
    });

    it("al reanudar con la validación en curso ofrece «Abrir validación» y la vuelve a pedir al pulsarlo", async () => {
      const pendiente: EstadoIdentidad = { solicitudId: "sol-1", completa: false, partes: [{ parte: "vendedor", ordinal: 1, estado: "pendiente", puedeReenviar: false, reenviosRestantes: 5, esperaSegundos: 0 }] };
      const radicante = vi.fn(async () => ({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: "https://verify.example/de-nuevo" }));
      const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);
      const abrir = abrirNuevo();
      montarPunto(clienteMock({ estado: vi.fn(async () => pendiente), abrir, radicante }));

      expect(await screen.findByText(/Validación: en curso/i)).toBeInTheDocument();
      expect(radicante).not.toHaveBeenCalled();
      fireEvent.click(screen.getByRole("button", { name: /Abrir validación/i }));

      await waitFor(() => expect(openSpy).toHaveBeenCalledWith("https://verify.example/de-nuevo", "_blank", "noopener,noreferrer"));
      expect(radicante).toHaveBeenCalledTimes(1);
      expect(abrir).not.toHaveBeenCalled();
      openSpy.mockRestore();
    });

    it("el mismo borrador editable (misma placa, tipo y documento) se reanuda sin abrir otro", async () => {
      const abrir = abrirNuevo();
      montarPunto(clienteMock({ estado: estadoConSesion(), abrir }));
      expect(await screen.findByText(/Identidad validada/i)).toBeInTheDocument();
      expect(abrir).not.toHaveBeenCalled();
    });
  });

  it("el correo técnico interno del radicante no se muestra al reanudar", async () => {
    const tecnico = {
      ...SOLICITUD,
      actores: [{ ...SOLICITUD.actores[0], correo: "validaciones+punto-abc123@flitsas.com" }],
    } as unknown as Solicitud;
    montarPunto(clienteMock({ estado: async () => ESTADO_APROBADO, obtener: async () => tecnico }));
    const vendedor = await screen.findByRole("region", { name: /Propietario \(vendedor\)/i });
    expect(within(vendedor).getByLabelText(/Correo electrónico/i)).toHaveValue("");
    expect(screen.queryByDisplayValue(/validaciones\+punto-/)).toBeNull();
  });

  describe("versión del borrador", () => {
    it("lanzar la identidad sube la versión y Continuar guarda con la versión nueva", async () => {
      const guardar = vi.fn(async () => ({ ...SOLICITUD, version: 6 }));
      const radicante = vi.fn(async () => ({ parte: "vendedor", ordinal: 1, estado: "pendiente", capturaUrl: null }));
      const obtener = vi.fn().mockResolvedValueOnce(SOLICITUD).mockResolvedValue({ ...SOLICITUD, version: 5 });
      const onContinuar = vi.fn();
      let n = 0;
      const estado = vi.fn(async () => {
        n += 1;
        if (n === 1) throw new Error("sin sesión");
        return n < 4 ? SIN_SOLICITAR : ESTADO_APROBADO;
      });
      montarPunto(clienteMock({ estado, radicante, obtener, guardar }), vi.fn(), onContinuar);
      await waitFor(() => expect(radicante).toHaveBeenCalled());
      await waitFor(() => expect(obtener.mock.calls.length).toBeGreaterThanOrEqual(2));
      const continuar = await screen.findByRole("button", { name: /^Continuar$/i }, { timeout: 4000 });
      await waitFor(() => expect(continuar).toBeEnabled(), { timeout: 4000 });
      fireEvent.click(continuar);
      await waitFor(() => expect(onContinuar).toHaveBeenCalled());
      expect((guardar.mock.calls[0] as unknown[])[1]).toMatchObject({ version: 5 });
    });

    it("un 409 de versión se reintenta una sola vez y avanza", async () => {
      const guardar = vi
        .fn()
        .mockRejectedValueOnce(new ApiError("conflicto_version", "Cambió", 409))
        .mockResolvedValue({ ...SOLICITUD, version: 9 });
      const obtener = vi.fn().mockResolvedValueOnce(SOLICITUD).mockResolvedValue({ ...SOLICITUD, version: 8 });
      const onContinuar = vi.fn();
      montarPunto(clienteMock({ estado: async () => ESTADO_APROBADO, guardar, obtener }), vi.fn(), onContinuar);
      await screen.findByRole("region", { name: /Propietario \(vendedor\)/i });
      fireEvent.click(screen.getByRole("button", { name: /^Continuar$/i }));
      await waitFor(() => expect(onContinuar).toHaveBeenCalledTimes(1));
      expect(guardar).toHaveBeenCalledTimes(2);
      expect((guardar.mock.calls[1] as unknown[])[1]).toMatchObject({ version: 8 });
    });

    it("un segundo 409 no avanza y muestra el motivo", async () => {
      const guardar = vi.fn(async () => {
        throw new ApiError("conflicto_version", "Cambió", 409);
      });
      const onContinuar = vi.fn();
      const onAviso = montarPunto(clienteMock({ estado: async () => ESTADO_APROBADO, guardar }), vi.fn(), onContinuar);
      await screen.findByRole("region", { name: /Propietario \(vendedor\)/i });
      fireEvent.click(screen.getByRole("button", { name: /^Continuar$/i }));
      await waitFor(() => expect(onAviso).toHaveBeenCalledWith("No pudimos guardar los datos", expect.any(String), "alerta"));
      expect(guardar).toHaveBeenCalledTimes(2);
      expect(onContinuar).not.toHaveBeenCalled();
    });
  });
});
