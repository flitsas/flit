// HU #11790 (Feature #11784) — pestaña «Log completo»: consultas ocultas de entrada (AC1),
// totalidad recuperable (AC2), filtro de solo errores (AC3), envío y respuesta lado a lado (AC4),
// códigos traducidos (AC5), evento sin payload (AC6), filtro sin coincidencias (AC7).
import { beforeEach, describe, expect, it, vi, type Mock } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { LogQxEvent, LogQxEventosPage } from "@/lib/api/admin-log-qx";

const mocks = vi.hoisted(() => ({ fetchLogQxEventos: vi.fn() }));
vi.mock("@/lib/api/admin-log-qx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-log-qx")>();
  return { ...actual, fetchLogQxEventos: mocks.fetchLogQxEventos };
});

import { LogCompleto } from "@/components/logqx/LogCompleto";

const SUB = "11111111-1111-1111-1111-111111111111";

const EVENTO_REGISTRO: LogQxEvent = {
  stage: "registro_enviado",
  outcome: "ok",
  detail: {
    documento: "TESLA_MI_20260811_1220_LRWYGCFJ3TC767907",
    consumidor: "1003",
    codigoDivipo: "17001",
    nombrePropietario: "••••••LLOS",
    codigo: 81,
    descripcion: "Los datos se almacenaron correctamente",
    origen: "quipux_register",
  },
  durationMs: 1240,
  origin: "quipux_register",
  responseCode: 81,
  correlationId: null,
  occurredAt: "2026-08-18T17:40:15Z",
};

const EVENTO_ERROR: LogQxEvent = {
  stage: "registro_error",
  outcome: "error_definitivo",
  detail: { codigo: 76, descripcion: "Error interno del organismo de tránsito" },
  durationMs: 850,
  origin: "quipux_register",
  responseCode: 76,
  correlationId: null,
  occurredAt: "2026-08-19T08:00:05Z",
};

const EVENTO_SIN_PAYLOAD: LogQxEvent = {
  stage: "reintento_manual",
  outcome: "ok",
  detail: null,
  durationMs: null,
  origin: "manual",
  responseCode: null,
  correlationId: null,
  occurredAt: "2026-08-19T09:00:00Z",
};

function page(over: Partial<LogQxEventosPage> = {}): LogQxEventosPage {
  return {
    data: [EVENTO_REGISTRO],
    totalCount: 5,
    page: 1,
    pageSize: 50,
    ocultosSinNovedad: 1060,
    totalEventos: 1065,
    ...over,
  };
}

describe("LOG QX — log completo (HU #11790)", () => {
  beforeEach(() => mocks.fetchLogQxEventos.mockReset());

  it("AC1: entra ocultando las consultas sin novedad e informa cuántas ocultó", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    render(<LogCompleto submissionId={SUB} />);

    await waitFor(() =>
      expect(mocks.fetchLogQxEventos).toHaveBeenCalledWith(
        SUB,
        expect.objectContaining({ ocultarSinNovedad: true }),
      ),
    );
    // Sin este número, 5 filas de una radicación de 1.065 eventos parecen pérdida de datos.
    expect(await screen.findByText(/1.060 consultas sin novedad ocultas/)).toBeInTheDocument();
  });

  it("AC2: desactivar el interruptor pide la totalidad al servidor", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    render(<LogCompleto submissionId={SUB} />);
    await waitFor(() => expect(mocks.fetchLogQxEventos).toHaveBeenCalled());

    fireEvent.click(screen.getByLabelText(/Ocultar consultas sin novedad/i));

    await waitFor(() =>
      expect(mocks.fetchLogQxEventos).toHaveBeenLastCalledWith(
        SUB,
        expect.objectContaining({ ocultarSinNovedad: false }),
      ),
    );
  });

  it("AC2: con el interruptor apagado no se anuncian ocultos", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page({ ocultosSinNovedad: 0, totalCount: 1065 }));

    render(<LogCompleto submissionId={SUB} />);
    await waitFor(() => expect(mocks.fetchLogQxEventos).toHaveBeenCalled());

    fireEvent.click(screen.getByLabelText(/Ocultar consultas sin novedad/i));

    await waitFor(() =>
      expect(screen.queryByText(/consultas sin novedad ocultas/)).not.toBeInTheDocument(),
    );
  });

  it("AC3: «Solo errores» se delega al servidor, no se filtra en el cliente", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    render(<LogCompleto submissionId={SUB} />);
    await waitFor(() => expect(mocks.fetchLogQxEventos).toHaveBeenCalled());

    fireEvent.click(screen.getByRole("button", { name: /Solo errores/i }));

    await waitFor(() =>
      expect(mocks.fetchLogQxEventos).toHaveBeenLastCalledWith(
        SUB,
        expect.objectContaining({ soloErrores: true }),
      ),
    );
  });

  it("AC4: abrir un evento muestra lo enviado y lo recibido lado a lado", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    render(<LogCompleto submissionId={SUB} />);
    fireEvent.click(await screen.findByRole("button", { name: /Radicación enviada/i }));

    expect(await screen.findByText("Lo que enviamos")).toBeInTheDocument();
    expect(screen.getByText("Lo que respondió Quipux")).toBeInTheDocument();
    // El envío lleva sus claves; la respuesta, las suyas.
    expect(screen.getByText("consumidor")).toBeInTheDocument();
    expect(screen.getByText("descripcion")).toBeInTheDocument();
  });

  it("AC4: el enmascarado del backend se muestra tal cual, sin revertirlo", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    render(<LogCompleto submissionId={SUB} />);
    fireEvent.click(await screen.findByRole("button", { name: /Radicación enviada/i }));

    expect(await screen.findByText("••••••LLOS")).toBeInTheDocument();
  });

  it("AC4: el JSON original queda accesible detrás de un control", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    render(<LogCompleto submissionId={SUB} />);
    fireEvent.click(await screen.findByRole("button", { name: /Radicación enviada/i }));
    fireEvent.click(await screen.findByRole("button", { name: /ver original/i }));

    expect(await screen.findByText(/"codigoDivipo": "17001"/)).toBeInTheDocument();
  });

  it("AC5: los códigos llevan su significado y NUNCA se rotulan como HTTP", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page({ data: [EVENTO_REGISTRO, EVENTO_ERROR] }));

    render(<LogCompleto submissionId={SUB} />);
    await screen.findByText(/Radicación enviada/);

    expect(screen.getByText(/81 · Almacenado correctamente/)).toBeInTheDocument();
    expect(screen.getByText(/76 · Error interno de la secretaría/)).toBeInTheDocument();
    // El defecto que arrastraba la pantalla anterior.
    expect(screen.queryByText(/HTTP 81/)).not.toBeInTheDocument();
    expect(screen.queryByText(/HTTP 76/)).not.toBeInTheDocument();
  });

  it("AC4: un evento que solo registra la respuesta no pinta una columna vacía", async () => {
    // registro_respuesta guarda solo el lado de Quipux; pintar "Lo que enviamos: sin datos"
    // se lee como un fallo cuando en realidad ese evento no tiene envío.
    const soloRespuesta: LogQxEvent = {
      ...EVENTO_REGISTRO,
      stage: "registro_respuesta",
      detail: { codigo: 81, mensaje: "Los datos se almacenaron correctamente", duration_ms: 1100 },
    };
    mocks.fetchLogQxEventos.mockResolvedValue(page({ data: [soloRespuesta] }));

    render(<LogCompleto submissionId={SUB} />);
    fireEvent.click(await screen.findByRole("button", { name: /Radicado en Quipux/i }));

    expect(await screen.findByText("Lo que respondió Quipux")).toBeInTheDocument();
    expect(screen.queryByText("Lo que enviamos")).not.toBeInTheDocument();
    expect(screen.queryByText(/Sin datos registrados/i)).not.toBeInTheDocument();
    expect(screen.getByText(/solo registra la respuesta de Quipux/i)).toBeInTheDocument();
  });

  it("AC4: el JSON original sigue accesible aunque solo haya un lado", async () => {
    const soloEnvio: LogQxEvent = {
      ...EVENTO_REGISTRO,
      stage: "claimed",
      detail: { batch: "BATCH-003" },
      responseCode: null,
    };
    mocks.fetchLogQxEventos.mockResolvedValue(page({ data: [soloEnvio] }));

    render(<LogCompleto submissionId={SUB} />);
    fireEvent.click(await screen.findByRole("button", { name: /Tomado de la cola/i }));
    fireEvent.click(await screen.findByRole("button", { name: /ver original/i }));

    expect(await screen.findByText(/"batch": "BATCH-003"/)).toBeInTheDocument();
  });

  it("AC6: un evento histórico sin payload lo dice, sin romper la pantalla", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page({ data: [EVENTO_SIN_PAYLOAD] }));

    render(<LogCompleto submissionId={SUB} />);
    fireEvent.click(await screen.findByRole("button", { name: /Reintento manual/i }));

    expect(await screen.findByText(/Sin payload disponible/i)).toBeInTheDocument();
  });

  it("AC7: sin resultados en «solo errores» se dice que no hubo errores", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page({ data: [], totalCount: 0 }));

    render(<LogCompleto submissionId={SUB} />);
    await waitFor(() => expect(mocks.fetchLogQxEventos).toHaveBeenCalled());

    fireEvent.click(screen.getByRole("button", { name: /Solo errores/i }));

    expect(
      await screen.findByText(/no registró ningún error/i),
    ).toBeInTheDocument();
  });

  it("AC7: sin resultados con el interruptor puesto se explica cómo ver la totalidad", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page({ data: [], totalCount: 0 }));

    render(<LogCompleto submissionId={SUB} />);

    expect(
      await screen.findByText(/Desactiva «ocultar consultas sin novedad» para ver la totalidad/i),
    ).toBeInTheDocument();
  });

  it("informa cuántos eventos se ven sobre el total de la radicación", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    render(<LogCompleto submissionId={SUB} />);

    // La cifra vive en el resumen de `PageNav`, la misma paginación del listado de trámites.
    expect(
      await screen.findByText(/1\.065 eventos en la radicación/),
    ).toBeInTheDocument();
  });

  it("el origen se muestra en español, no con el nombre del worker", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    render(<LogCompleto submissionId={SUB} />);
    const fila = (await screen.findByText(/Radicación enviada/)).closest("tr")!;

    expect(within(fila).getByText("Radicación")).toBeInTheDocument();
    expect(within(fila).queryByText("quipux_register")).not.toBeInTheDocument();
  });

  it("AC8: exportar entrega un XLSX con el nombre de la radicación", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    // jsdom no descarga: se observa el <a download> que el componente fabrica.
    const clicks: string[] = [];
    const original = document.createElement.bind(document);
    const spy = vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      const el = original(tag) as HTMLElement;
      if (tag === "a") {
        el.click = () => clicks.push((el as HTMLAnchorElement).download);
      }
      return el;
    });
    const objectUrl: Mock<(blob: Blob) => string> = vi.fn(() => "blob:log-qx");
    Object.assign(URL, { createObjectURL: objectUrl, revokeObjectURL: vi.fn() });

    render(<LogCompleto submissionId={SUB} />);
    await screen.findByText(/Radicación enviada/);
    fireEvent.click(screen.getByRole("button", { name: /Exportar/i }));

    expect(clicks).toEqual([`log-qx-${SUB}.xlsx`]);
    // El escritor propio del proyecto produce un binario, no texto plano.
    expect(objectUrl).toHaveBeenCalledTimes(1);
    expect(objectUrl.mock.calls[0]![0].type).toContain("spreadsheetml");
    spy.mockRestore();
  });

  it("cambiar un filtro vuelve a la primera página", async () => {
    mocks.fetchLogQxEventos.mockResolvedValue(page());

    render(<LogCompleto submissionId={SUB} />);
    await waitFor(() => expect(mocks.fetchLogQxEventos).toHaveBeenCalled());

    fireEvent.click(screen.getByRole("button", { name: /Solo errores/i }));

    await waitFor(() =>
      expect(mocks.fetchLogQxEventos).toHaveBeenLastCalledWith(
        SUB,
        expect.objectContaining({ page: 1 }),
      ),
    );
  });
});
