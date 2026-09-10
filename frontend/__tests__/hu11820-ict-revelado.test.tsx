// HU #11820 (Feature #11814) — revelado auditado de datos personales: enmascarado por defecto
// (AC1), el revelado exige una acción explícita y no se extiende a otros trámites (AC2), se avisa
// de que queda registrado (AC3), sin permiso no se revela y se explica por qué (AC4), y la consulta
// normal del detalle sigue enmascarando pase lo que pase (AC5).
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { DatosTramiteIct, TramiteIct } from "@/lib/api/ict-trazabilidad";

vi.mock("next/link", () => ({
  default: (props: { href: string; children: ReactNode }) => <a href={props.href}>{props.children}</a>,
}));

const mocks = vi.hoisted(() => ({
  fetchRecorridoIct: vi.fn(),
  fetchConsultasFuenteIct: vi.fn(),
  fetchDatosTramiteIct: vi.fn(),
  fetchLogTramiteIct: vi.fn(),
  revelarDatosPersonalesIct: vi.fn(),
}));
vi.mock("@/lib/api/ict-trazabilidad", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ict-trazabilidad")>();
  return { ...actual, ...mocks };
});

import { DetalleTramiteIct } from "@/components/ict/DetalleTramiteIct";

const tramite: TramiteIct = {
  id: "id-1",
  numero: 10461,
  referenciaCliente: null,
  placa: "NPV523",
  vin: null,
  tipoTramiteId: 3,
  tipoTramite: "Traspaso",
  operacionId: 1,
  operacion: "Crear",
  clientTenantId: "0ad1c0de-0000-4000-8000-000000000001",
  compania: "Renting Colombia S.A.S.",
  radicador: "Edson Madrid",
  estado: "con_novedades",
  minutosEsperando: 252,
  pausado: false,
  sinAdjuntos: false,
  tieneTramiteFlit: false,
  recibidoEn: "2026-08-24T20:18:04.955Z",
};

const ENMASCARADO: DatosTramiteIct = {
  numero: 10461,
  secciones: [
    { titulo: "Transacción", datos: [{ etiqueta: "Placa", valor: "NPV523", esSensible: false }] },
    {
      titulo: "Comprador",
      datos: [
        { etiqueta: "Nombre", valor: "********************CHOA", esSensible: true },
        { etiqueta: "Documento", valor: "CC ****8877", esSensible: true },
        { etiqueta: "Ciudad", valor: "VALLEDUPAR", esSensible: false },
      ],
    },
  ],
};

const EN_CLARO = {
  numero: 10461,
  auditado: true,
  secciones: [
    {
      titulo: "Comprador",
      datos: [
        { etiqueta: "Nombre", valor: "ANA MARIA RESTREPO OCHOA", esSensible: true },
        { etiqueta: "Documento", valor: "CC 43128877", esSensible: true },
        { etiqueta: "Ciudad", valor: "VALLEDUPAR", esSensible: false },
      ],
    },
  ],
};

async function abrirDatos() {
  render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);
  fireEvent.click(screen.getByRole("tab", { name: "Datos recibidos" }));
  await waitFor(() => expect(mocks.fetchDatosTramiteIct).toHaveBeenCalled());
}

describe("HU #11820 — revelado auditado de datos personales", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchRecorridoIct.mockResolvedValue({ hitos: [], tiempos: {}, procedureInstanceId: null });
    mocks.fetchConsultasFuenteIct.mockResolvedValue([]);
    mocks.fetchLogTramiteIct.mockResolvedValue([]);
    mocks.fetchDatosTramiteIct.mockResolvedValue(ENMASCARADO);
    mocks.revelarDatosPersonalesIct.mockResolvedValue(EN_CLARO);
  });

  it("AC1: al abrir, los datos personales llegan enmascarados y no se pide nada más", async () => {
    await abrirDatos();

    expect(screen.getByText("********************CHOA")).toBeInTheDocument();
    expect(screen.getByText("CC ****8877")).toBeInTheDocument();
    // Abrir la pestaña NO revela: hacerlo convertiría el control en un adorno.
    expect(mocks.revelarDatosPersonalesIct).not.toHaveBeenCalled();
  });

  it("AC2: el revelado exige pulsar, y solo pide el trámite que se está mirando", async () => {
    await abrirDatos();
    fireEvent.click(screen.getByRole("button", { name: /revelar datos personales/i }));

    await waitFor(() => expect(screen.getByText("ANA MARIA RESTREPO OCHOA")).toBeInTheDocument());
    expect(screen.getByText("CC 43128877")).toBeInTheDocument();
    expect(mocks.revelarDatosPersonalesIct).toHaveBeenCalledTimes(1);
    expect(mocks.revelarDatosPersonalesIct).toHaveBeenCalledWith(10461);
  });

  it("AC3: antes de revelar se avisa de que deja constancia, y después se confirma", async () => {
    // Saber que el acceso queda registrado cambia la decisión de pedirlo: ese es el control.
    await abrirDatos();
    expect(screen.getByText(/deja constancia de quién lo hizo/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /revelar datos personales/i }));
    await waitFor(() =>
      expect(screen.getByText(/quedó registrado con tu usuario y la fecha/i)).toBeInTheDocument(),
    );
  });

  it("volver a ocultar no repite la petición ni añade otro registro de auditoría", async () => {
    await abrirDatos();
    fireEvent.click(screen.getByRole("button", { name: /revelar datos personales/i }));
    await waitFor(() => expect(screen.getByText("ANA MARIA RESTREPO OCHOA")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: /volver a ocultar/i }));

    await waitFor(() => expect(screen.getByText("********************CHOA")).toBeInTheDocument());
    expect(screen.queryByText("ANA MARIA RESTREPO OCHOA")).not.toBeInTheDocument();
    expect(mocks.revelarDatosPersonalesIct).toHaveBeenCalledTimes(1);
  });

  it("AC4: sin permiso no se revela nada y se explica qué hacer", async () => {
    mocks.revelarDatosPersonalesIct.mockRejectedValue(new Error("403 forbidden"));
    await abrirDatos();
    fireEvent.click(screen.getByRole("button", { name: /revelar datos personales/i }));

    await waitFor(() =>
      expect(screen.getByText(/no tienes permiso para ver los datos personales/i)).toBeInTheDocument(),
    );
    // El dato sigue tapado y el mensaje no habla de códigos HTTP.
    expect(screen.getByText("********************CHOA")).toBeInTheDocument();
    expect(screen.queryByText(/403/)).not.toBeInTheDocument();
  });

  it("AC5: el revelado no toca las secciones sin datos personales", async () => {
    // La transacción no viene en la respuesta del revelado; debe seguir mostrándose igual en vez de
    // desaparecer al sustituir las secciones.
    await abrirDatos();
    fireEvent.click(screen.getByRole("button", { name: /revelar datos personales/i }));

    await waitFor(() => expect(screen.getByText("ANA MARIA RESTREPO OCHOA")).toBeInTheDocument());
    expect(screen.getByText("Transacción")).toBeInTheDocument();
    expect(screen.getByText("NPV523")).toBeInTheDocument();
  });
});
