// HU #11819 (Feature #11814) — detalle del trámite: cuatro pestañas (AC1), recorrido con tiempos y
// tramo lento destacado (AC2), la novedad en la etapa que la produjo (AC3), salto al trámite con el
// tenant de la fila (AC4), la consulta que bloquea (AC5), datos por secciones de negocio (AC6),
// log acotado con aviso de lote (AC7) y las pestañas sin datos explicadas (AC8).
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type {
  ConsultaFuenteIct,
  DatosTramiteIct,
  EventoLogTramiteIct,
  RecorridoTramiteIct,
  TramiteIct,
} from "@/lib/api/ict-trazabilidad";

vi.mock("next/link", () => ({
  default: (props: { href: string; children: ReactNode; className?: string }) => (
    <a href={props.href} className={props.className}>
      {props.children}
    </a>
  ),
}));

const mocks = vi.hoisted(() => ({
  fetchRecorridoIct: vi.fn(),
  fetchConsultasFuenteIct: vi.fn(),
  fetchDatosTramiteIct: vi.fn(),
  fetchLogTramiteIct: vi.fn(),
}));
vi.mock("@/lib/api/ict-trazabilidad", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ict-trazabilidad")>();
  return { ...actual, ...mocks };
});

import { DetalleTramiteIct } from "@/components/ict/DetalleTramiteIct";

const TENANT = "0ad1c0de-0000-4000-8000-000000000001";
const INSTANCIA = "7f3d2a10-0000-4000-8000-000000000009";

const tramite: TramiteIct = {
  id: "id-1",
  numero: 10461,
  referenciaCliente: "REF-1",
  placa: "NPV523",
  vin: null,
  tipoTramiteId: 3,
  tipoTramite: "Traspaso",
  operacionId: 1,
  operacion: "Crear",
  clientTenantId: TENANT,
  compania: "Renting Colombia S.A.S.",
  radicador: "Edson Madrid",
  estado: "borrador_creado",
  minutosEsperando: null,
  pausado: false,
  sinAdjuntos: false,
  tieneTramiteFlit: true,
  recibidoEn: "2026-08-24T20:18:04.955Z",
};

function recorrido(over: Partial<RecorridoTramiteIct> = {}): RecorridoTramiteIct {
  return {
    id: "id-1",
    numero: 10461,
    referenciaCliente: "REF-1",
    placa: "NPV523",
    vin: null,
    tipoTramite: "Traspaso",
    operacion: "Crear",
    clientTenantId: TENANT,
    compania: "Renting Colombia S.A.S.",
    estado: "borrador_creado",
    hitos: [
      {
        etapa: "recibido",
        titulo: "Recibido por la integración",
        ocurrido: "2026-08-24T20:18:04.955Z",
        segundosDesdeAnterior: null,
        resultado: "ok",
        esTramoMasLento: false,
        mensaje: null,
      },
      {
        etapa: "en_validacion_negocio",
        titulo: "Validación de negocio",
        ocurrido: "2026-08-24T20:19:20.811Z",
        segundosDesdeAnterior: 75,
        resultado: "ok",
        esTramoMasLento: false,
        mensaje: null,
      },
      {
        etapa: "en_validacion_externa",
        titulo: "Consulta a fuentes externas",
        ocurrido: "2026-08-24T20:21:31.112Z",
        segundosDesdeAnterior: 130,
        resultado: "ok",
        esTramoMasLento: true,
        mensaje: null,
      },
    ],
    tiempos: {
      segundosTotal: 265,
      segundosHastaActivar: 75,
      segundosHastaCrearBorrador: 265,
      segundosSinAvanzar: null,
    },
    mensajeNovedad: null,
    procedureInstanceId: INSTANCIA,
    codigoOrganismoTransito: "25286000",
    organismoTransito: "STRIA TTOyTTE MCPAL FUNZA",
    ...over,
  };
}

function consulta(over: Partial<ConsultaFuenteIct> = {}): ConsultaFuenteIct {
  return {
    id: crypto.randomUUID(),
    nivelActor: "MAIN",
    nivelActorEtiqueta: "Principal",
    tipoConsulta: "DRIVER",
    tipoConsultaEtiqueta: "Conductor",
    identificador: "NIT *****1779",
    consultada: true,
    valida: true,
    intentos: 1,
    bloquea: false,
    creadaEn: "2026-08-24T20:21:00.000Z",
    respuesta: null,
    ...over,
  };
}

describe("HU #11819 — detalle del trámite ICT", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchRecorridoIct.mockResolvedValue(recorrido());
    mocks.fetchConsultasFuenteIct.mockResolvedValue([consulta()]);
    mocks.fetchDatosTramiteIct.mockResolvedValue({
      numero: 10461,
      secciones: [
        {
          titulo: "Transacción",
          datos: [{ etiqueta: "Placa", valor: "NPV523", esSensible: false }],
        },
        {
          titulo: "Vendedor",
          datos: [{ etiqueta: "Nombre", valor: "**********CIA", esSensible: true }],
        },
      ],
    } satisfies DatosTramiteIct);
    mocks.fetchLogTramiteIct.mockResolvedValue([]);
  });

  it("AC1: muestra las cuatro pestañas, con Recorrido activa por defecto", async () => {
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);

    expect(screen.getByRole("tab", { name: "Recorrido" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Consultas al RUNT" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Datos recibidos" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Log técnico" })).toBeInTheDocument();
  });

  it("solo pide los datos de la pestaña que se abre, no los de las cuatro", async () => {
    // Cargar las cuatro al abrir una fila multiplicaría por cuatro las consultas del panel de
    // soporte, y tres de cada cuatro no se llegan a mirar.
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);
    await waitFor(() => expect(mocks.fetchRecorridoIct).toHaveBeenCalled());

    expect(mocks.fetchConsultasFuenteIct).not.toHaveBeenCalled();
    expect(mocks.fetchDatosTramiteIct).not.toHaveBeenCalled();
    expect(mocks.fetchLogTramiteIct).not.toHaveBeenCalled();
  });

  it("AC2: pinta los tiempos entre etapas y nombra el tramo más lento", async () => {
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);

    await waitFor(() => expect(screen.getByText(/\+1 min 15 s/)).toBeInTheDocument());
    // El tramo lento no se distingue solo por color: se dice con palabras.
    expect(screen.getByText(/\+2 min 10 s · el más lento/)).toBeInTheDocument();
    // «Tiempo total» y «Hasta crear el borrador» coinciden a propósito en un trámite terminado:
    // los tres agregados se miden DESDE LA RECEPCIÓN y no encadenados, que es la semántica de v1.
    expect(screen.getAllByText("4 min 25 s")).toHaveLength(2);
    expect(screen.getByText("1 min 15 s")).toBeInTheDocument();
  });

  it("AC3: la novedad se muestra en la etapa que la produjo", async () => {
    const mensaje = "El código de organismo de tránsito no tiene un valor válido o no está activo.";
    mocks.fetchRecorridoIct.mockResolvedValue(
      recorrido({
        estado: "con_novedades",
        procedureInstanceId: null,
        hitos: [
          {
            etapa: "recibido",
            titulo: "Recibido por la integración",
            ocurrido: "2026-08-24T20:18:04.955Z",
            segundosDesdeAnterior: null,
            resultado: "ok",
            esTramoMasLento: false,
            mensaje: null,
          },
          {
            etapa: "en_validacion_negocio",
            titulo: "Validación de negocio",
            ocurrido: "2026-08-24T20:19:20.811Z",
            segundosDesdeAnterior: 75,
            resultado: "error",
            esTramoMasLento: false,
            mensaje,
          },
        ],
      }),
    );
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);

    await waitFor(() => expect(screen.getByText(mensaje)).toBeInTheDocument());
  });

  it("AC4: el SuperAdmin salta al trámite llevando el tenant de la fila", async () => {
    // Sin el tenant, la pantalla de trámite responde «Falta header X-Tenant-Id». Es la misma
    // lección del LOG QX (Feature #11784).
    render(<DetalleTramiteIct tramite={tramite} esAdmin />);

    await waitFor(() =>
      expect(screen.getByRole("link", { name: /ver trámite en flit/i })).toHaveAttribute(
        "href",
        `/tramites/${INSTANCIA}?t=${encodeURIComponent(TENANT)}`,
      ),
    );
  });

  it("quien no es SuperAdmin no lleva tenant en el enlace: lo deriva su sesión", async () => {
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);

    await waitFor(() =>
      expect(screen.getByRole("link", { name: /ver trámite en flit/i })).toHaveAttribute(
        "href",
        `/tramites/${INSTANCIA}`,
      ),
    );
  });

  it("AC5: la consulta que bloquea se explica y se distingue de las demás", async () => {
    mocks.fetchConsultasFuenteIct.mockResolvedValue([
      consulta(),
      consulta({
        nivelActorEtiqueta: "Representante legal",
        tipoConsultaEtiqueta: "Validación de identidad",
        consultada: false,
        valida: false,
        intentos: 3,
        bloquea: true,
      }),
    ]);
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);
    fireEvent.click(screen.getByRole("tab", { name: "Consultas al RUNT" }));

    await waitFor(() =>
      expect(
        screen.getByText(/validación de identidad del representante legal lleva 3 intentos/i),
      ).toBeInTheDocument(),
    );
  });

  it("AC6: los datos se agrupan por secciones de negocio, no como volcado JSON", async () => {
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);
    fireEvent.click(screen.getByRole("tab", { name: "Datos recibidos" }));

    await waitFor(() => expect(screen.getByText("Transacción")).toBeInTheDocument());
    expect(screen.getByText("Vendedor")).toBeInTheDocument();
    expect(screen.getByText("Placa")).toBeInTheDocument();
    // El dato personal llega enmascarado del servidor y se avisa en pantalla.
    expect(screen.getByText("**********CIA")).toBeInTheDocument();
    expect(screen.getByText(/datos personales se muestran enmascarados/i)).toBeInTheDocument();
  });

  it("AC7: el log dice cuántos trámites viajaban en la misma petición", async () => {
    mocks.fetchLogTramiteIct.mockResolvedValue([
      {
        id: "log-1",
        ocurrido: "2026-08-24T20:18:04.955Z",
        tipo: "Transacción",
        direccion: "Entrante",
        metodo: "POST",
        ruta: "/api/v1/external-transaction/register",
        codigo: 200,
        duracionMs: 3059,
        tramitesEnLaPeticion: 20,
      },
    ] satisfies EventoLogTramiteIct[]);
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);
    fireEvent.click(screen.getByRole("tab", { name: "Log técnico" }));

    await waitFor(() =>
      expect(screen.getByText(/esta petición traía 20 trámites/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/el log completo de la plataforma sigue en el módulo log ict/i))
      .toBeInTheDocument();
  });

  it("AC8: una pestaña sin datos explica por qué, en vez de quedarse en blanco", async () => {
    mocks.fetchConsultasFuenteIct.mockResolvedValue([]);
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);
    fireEvent.click(screen.getByRole("tab", { name: "Consultas al RUNT" }));

    await waitFor(() =>
      expect(
        screen.getByText(/todavía no ha llegado a la etapa de consulta a fuentes/i),
      ).toBeInTheDocument(),
    );
  });

  it("un fallo en una pestaña se explica y se puede reintentar sin cerrar el detalle", async () => {
    mocks.fetchLogTramiteIct.mockRejectedValue(new Error("boom"));
    render(<DetalleTramiteIct tramite={tramite} esAdmin={false} />);
    fireEvent.click(screen.getByRole("tab", { name: "Log técnico" }));

    await waitFor(() =>
      expect(screen.getByText(/no se pudo cargar el log de este trámite/i)).toBeInTheDocument(),
    );
    // Las demás pestañas siguen accesibles: el fallo es de una capa, no del detalle entero.
    expect(screen.getByRole("tab", { name: "Recorrido" })).toBeInTheDocument();
  });
});
