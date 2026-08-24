// HU #11789 (Feature #11784) — pantalla de trazabilidad: cabecera con la identificación (AC2),
// resumen en lenguaje natural (AC3), sondeo colapsado y visualmente distinto (AC4), tira de
// intentos (AC5), retorno conservando filtros (AC6) y radicación inexistente (AC7).
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { LogQxHito, LogQxHitosResult, LogQxRadicacion } from "@/lib/api/admin-log-qx";

vi.mock("next/link", () => ({
  default: (props: { href: string; children: ReactNode; className?: string }) => (
    <a href={props.href} className={props.className}>
      {props.children}
    </a>
  ),
}));

const mocks = vi.hoisted(() => ({ fetchLogQxHitos: vi.fn(), fetchLogQxEventos: vi.fn() }));
vi.mock("@/lib/api/admin-log-qx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-log-qx")>();
  return {
    ...actual,
    fetchLogQxHitos: mocks.fetchLogQxHitos,
    fetchLogQxEventos: mocks.fetchLogQxEventos,
  };
});

import { TrazabilidadScreen } from "@/components/logqx/TrazabilidadScreen";

const SUB = "11111111-1111-1111-1111-111111111111";
const INSTANCE = "22222222-2222-2222-2222-222222222222";

function radicacion(over: Partial<LogQxRadicacion> = {}): LogQxRadicacion {
  return {
    id: SUB,
    procedureInstanceId: INSTANCE,
    referenceNumber: "TRM-2026-000271",
    plate: "ABC123",
    procedureTypeName: "Matrícula inicial",
    clientTenantName: "AutoFlota Antioquia S.A.S",
    transitOfficeName: "Ibagué",
    divipoCode: "17001",
    documentoQx: "TESLA_MI_20260811_1220_LRWYGCFJ3TC767907",
    status: "registrado",
    attempts: 1,
    pollCount: 1065,
    qxRegisterCode: 81,
    qxProcedureCode: null,
    rejectionReason: null,
    createdAt: "2026-08-18T17:40:00Z",
    registeredAt: "2026-08-18T17:40:15Z",
    lastPolledAt: "2026-08-24T11:52:00Z",
    completedAt: null,
    updatedAt: "2026-08-24T11:52:00Z",
    esperandoDesde: "2026-08-18T17:40:00Z",
    horasEsperando: 142,
    intento: 1,
    totalIntentos: 1,
    hermanas: [{ id: SUB, intento: 1, status: "registrado", createdAt: "2026-08-18T17:40:00Z" }],
    ...over,
  };
}

const HITO_REGISTRO: LogQxHito = {
  tipo: "hito",
  stage: "registro_respuesta",
  outcome: "ok",
  occurredAt: "2026-08-18T17:40:15Z",
  hasta: null,
  durationMs: 1240,
  codigo: 81,
  estadoTramite: null,
  mensaje: "Los datos se almacenaron correctamente",
  correlationId: null,
  consultas: null,
  duracionMediaMs: null,
};

const BLOQUE_SONDEO: LogQxHito = {
  tipo: "sondeo",
  stage: "consulta_respuesta",
  outcome: "ok",
  occurredAt: "2026-08-18T17:50:00Z",
  hasta: "2026-08-24T11:52:00Z",
  durationMs: null,
  codigo: 81,
  estadoTramite: 1,
  mensaje: null,
  correlationId: null,
  consultas: 1065,
  duracionMediaMs: 430,
};

function result(over: Partial<LogQxHitosResult> = {}): LogQxHitosResult {
  return { radicacion: radicacion(), hitos: [HITO_REGISTRO, BLOQUE_SONDEO], ...over };
}

describe("LOG QX — pantalla de trazabilidad (HU #11789)", () => {
  beforeEach(() => {
    mocks.fetchLogQxHitos.mockReset();
    mocks.fetchLogQxEventos.mockReset();
  });

  it("AC2: la cabecera identifica el trámite y muestra el documento QX", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(result());

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    expect(await screen.findByText("TRM-2026-000271")).toBeInTheDocument();
    expect(screen.getByText("ABC123")).toBeInTheDocument();
    // Aparece en la cabecera, en el resumen y en el cierre de la línea: las tres son correctas.
    expect(screen.getAllByText(/Secretaría de Ibagué/).length).toBeGreaterThan(0);
    expect(screen.getByText("Documento QX")).toBeInTheDocument();
  });

  it("AC3: el resumen explica qué pasó sin obligar a leer códigos", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(result());

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    const resumen = await screen.findByText(/todavía no lo resuelve/i);
    expect(resumen).toBeInTheDocument();
    expect(resumen.textContent).toMatch(/1.065 consultas de estado/);
  });

  it("AC4: las 1.065 consultas ocupan un único bloque con su ventana y media", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(result());

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    const timeline = await screen.findByRole("list", {
      name: /Línea de hitos de la radicación/i,
    });
    // Dos entradas para 1.066 eventos: el hito de registro y el bloque de sondeo.
    // La tercera es el cierre "esperando decisión", que no es un evento.
    expect(within(timeline).getByText(/Consultando estado del trámite/i)).toBeInTheDocument();
    expect(within(timeline).getByText("1.065")).toBeInTheDocument();
    expect(within(timeline).getByText(/430 ms/)).toBeInTheDocument();
  });

  it("AC4: el bloque de sondeo se puede desplegar y remite al log completo", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(result());

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    fireEvent.click(await screen.findByRole("button", { name: /Qué hay dentro de este bloque/i }));

    expect(await screen.findByText(/no aportan información/i)).toBeInTheDocument();
    expect(screen.getByText(/ocultar consultas sin novedad/i)).toBeInTheDocument();
  });

  it("AC4: un hito real muestra su código traducido, nunca rotulado como HTTP", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(result());

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    const timeline = await screen.findByRole("list", {
      name: /Línea de hitos de la radicación/i,
    });
    // El código traducido sale tanto en la cabecera como en el hito; aquí se comprueba el hito.
    expect(within(timeline).getByText(/81 · Almacenado correctamente/)).toBeInTheDocument();
    expect(screen.queryByText(/HTTP 81/)).not.toBeInTheDocument();
  });

  it("AC5: un trámite con varias radicaciones muestra la tira y permite cambiar de intento", async () => {
    const OTRA = "33333333-3333-3333-3333-333333333333";
    mocks.fetchLogQxHitos.mockResolvedValue(
      result({
        radicacion: radicacion({
          intento: 2,
          totalIntentos: 2,
          hermanas: [
            { id: OTRA, intento: 1, status: "fallido", createdAt: "2026-08-17T08:00:00Z" },
            { id: SUB, intento: 2, status: "registrado", createdAt: "2026-08-18T17:40:00Z" },
          ],
        }),
      }),
    );

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    expect(await screen.findByText(/Este trámite tuvo 2 radicaciones/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Intento 1/i }));
    await waitFor(() => expect(mocks.fetchLogQxHitos).toHaveBeenLastCalledWith(OTRA));
  });

  it("AC5: sin reintentos no se muestra la tira, que sería ruido", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(result());

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);
    await screen.findByText("TRM-2026-000271");

    expect(screen.queryByText(/radicaciones\./i)).not.toBeInTheDocument();
  });

  it("AC6: el enlace de vuelta conserva los filtros con los que se llegó", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(result());

    render(
      <TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx&placa=ABC123&estado=en_tramite" />,
    );

    const volver = await screen.findByRole("link", { name: /Volver a LOG QX/i });
    expect(volver).toHaveAttribute("href", "/?m=log-qx&placa=ABC123&estado=en_tramite");
  });

  it("AC2: enlaza de vuelta al detalle del trámite", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(result());

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    expect(await screen.findByRole("link", { name: /Ver trámite/i })).toHaveAttribute(
      "href",
      `/tramites/${INSTANCE}`,
    );
  });

  it("AC7: una radicación inexistente muestra el error, no una pantalla rota", async () => {
    mocks.fetchLogQxHitos.mockRejectedValue(new Error("No se encontró la radicación"));

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    expect(await screen.findByText(/No se encontró la radicación/i)).toBeInTheDocument();
  });

  it("una radicación sin eventos lo explica en vez de dibujar una línea vacía", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(result({ hitos: [] }));

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    expect(
      await screen.findByText(/todavía no tiene eventos registrados/i),
    ).toBeInTheDocument();
  });

  it("no duplica el tipo cuando el nombre del catálogo ya empieza por «Secretaría»", async () => {
    // Los nombres reales vienen así: "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA".
    mocks.fetchLogQxHitos.mockResolvedValue(
      result({
        radicacion: radicacion({ transitOfficeName: "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA" }),
      }),
    );

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);
    await screen.findByText("TRM-2026-000271");

    expect(screen.queryByText(/Secretaría de SECRETARIA/i)).not.toBeInTheDocument();
    expect(screen.getAllByText(/La SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA/).length)
      .toBeGreaterThan(0);
  });

  it("antepone «Secretaría de» cuando el nombre NO lo trae", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(
      result({ radicacion: radicacion({ transitOfficeName: "Ibagué" }) }),
    );

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);
    await screen.findByText("TRM-2026-000271");

    expect(screen.getAllByText(/Secretaría de Ibagué/).length).toBeGreaterThan(0);
  });

  it("una radicación aprobada sin fecha de cierre no produce «lo aprobó el —»", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(
      result({
        radicacion: radicacion({
          status: "aprobado",
          completedAt: null,
          esperandoDesde: null,
          horasEsperando: null,
        }),
      }),
    );

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);

    const resumen = await screen.findByText(/lo aprobó/i);
    // El guión del formateador sirve en una tabla; en mitad de una frase la rompe.
    expect(resumen.textContent).not.toMatch(/aprobó el —/);
    expect(resumen.textContent).toMatch(/lo aprobó\./);
  });

  it("un trámite resuelto no muestra el cierre de «esperando decisión»", async () => {
    mocks.fetchLogQxHitos.mockResolvedValue(
      result({
        radicacion: radicacion({
          status: "aprobado",
          completedAt: "2026-08-21T09:03:00Z",
          esperandoDesde: null,
          horasEsperando: null,
        }),
      }),
    );

    render(<TrazabilidadScreen submissionId={SUB} volverHref="/?m=log-qx" />);
    await screen.findByText("TRM-2026-000271");

    expect(screen.queryByText(/Esperando la decisión/i)).not.toBeInTheDocument();
  });
});
