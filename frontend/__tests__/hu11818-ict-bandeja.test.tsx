// HU #11818 (Feature #11814) — bandeja de Trazabilidad ICT: una fila por trámite (AC1), los siete
// estados con contador y color propios (AC2), la tira como filtro (AC3), búsqueda por varias placas
// (AC4), exportación con los filtros aplicados (AC5), sin resultados (AC6) y error de carga (AC7).
// La capa de datos se mockea.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { PaginaTramitesIct, TramiteIct } from "@/lib/api/ict-trazabilidad";

const mocks = vi.hoisted(() => ({ fetchTramitesIct: vi.fn() }));
vi.mock("@/lib/api/ict-trazabilidad", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ict-trazabilidad")>();
  return { ...actual, fetchTramitesIct: mocks.fetchTramitesIct };
});

// La exportación construye un Blob y dispara una descarga; en jsdom no hay ninguna de las dos.
vi.stubGlobal("URL", {
  ...URL,
  createObjectURL: () => "blob:prueba",
  revokeObjectURL: () => undefined,
});

import { IctTrazabilidad } from "@/components/atom/modules/IctTrazabilidad";

const TENANT = "0ad1c0de-0000-4000-8000-000000000001";

function tramite(over: Partial<TramiteIct> = {}): TramiteIct {
  return {
    id: crypto.randomUUID(),
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
    ...over,
  };
}

function pagina(over: Partial<PaginaTramitesIct> = {}): PaginaTramitesIct {
  return {
    items: [tramite()],
    total: 1,
    page: 1,
    pageSize: 25,
    conteoPorEstado: {
      recibido: 34,
      en_validacion_negocio: 61,
      en_validacion_externa: 88,
      procesado: 12,
      borrador_creado: 902,
      con_novedades: 138,
      anulado: 8,
    },
    ...over,
  };
}

/** Los contadores y el chip de la fila comparten etiqueta: las consultas se acotan a la tira. */
function tira() {
  return within(screen.getByRole("group", { name: /contadores por estado/i }));
}

describe("HU #11818 — bandeja de Trazabilidad ICT", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchTramitesIct.mockResolvedValue(pagina());
  });

  it("AC1: cada fila es un trámite, con su número, placa y estado", async () => {
    render(<IctTrazabilidad />);

    await waitFor(() => expect(screen.getByText("10461")).toBeInTheDocument());
    expect(screen.getByText("NPV523")).toBeInTheDocument();
    expect(screen.getByText("Traspaso")).toBeInTheDocument();
    expect(screen.getByText("Renting Colombia S.A.S.")).toBeInTheDocument();
    // Ninguna columna habla de peticiones HTTP: ese es el cambio de eje frente al Log ICT.
    expect(screen.queryByText(/ruta|método|correlación/i)).not.toBeInTheDocument();
  });

  it("AC2: los siete estados tienen contador propio y ninguno comparte color", async () => {
    render(<IctTrazabilidad />);
    await waitFor(() => expect(mocks.fetchTramitesIct).toHaveBeenCalled());

    const botones = tira().getAllByRole("button");
    expect(botones).toHaveLength(7);
    expect(tira().getByText("902")).toBeInTheDocument();
    expect(tira().getByText("138")).toBeInTheDocument();

    // Con siete estados, los cinco tonos semánticos de StatusBadge obligarían a repetir color y la
    // tira dejaría de distinguirse de un vistazo, que es su única razón de ser.
    const colores = botones
      .map((b) => b.querySelector("span[style]")?.getAttribute("style") ?? "")
      .filter(Boolean);
    expect(new Set(colores).size).toBe(colores.length);
  });

  it("AC3: pulsar un contador filtra la bandeja, y volver a pulsarlo retira el filtro", async () => {
    render(<IctTrazabilidad />);
    await waitFor(() => expect(mocks.fetchTramitesIct).toHaveBeenCalled());

    const conNovedades = tira().getByRole("button", { name: /con novedades/i });
    fireEvent.click(conNovedades);

    await waitFor(() =>
      expect(mocks.fetchTramitesIct).toHaveBeenLastCalledWith(
        expect.objectContaining({ estado: "con_novedades" }),
      ),
    );
    expect(conNovedades).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(conNovedades);
    await waitFor(() =>
      expect(mocks.fetchTramitesIct).toHaveBeenLastCalledWith(
        expect.objectContaining({ estado: undefined }),
      ),
    );
  });

  it("AC4: se pueden buscar varias placas o VIN en una sola consulta", async () => {
    render(<IctTrazabilidad />);
    await waitFor(() => expect(mocks.fetchTramitesIct).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText(/placas o vin/i), {
      target: { value: "NPT415, LTS304" },
    });
    fireEvent.click(screen.getByRole("button", { name: /buscar/i }));

    await waitFor(() =>
      expect(mocks.fetchTramitesIct).toHaveBeenLastCalledWith(
        expect.objectContaining({ placas: "NPT415, LTS304" }),
      ),
    );
  });

  it("descarta un número de trámite que no es un número, en vez de buscarlo", async () => {
    // El backend busca por igualdad exacta: mandarle «abc» solo produce una consulta vacía que el
    // analista lee como «no existe» cuando en realidad se equivocó de campo.
    render(<IctTrazabilidad />);
    await waitFor(() => expect(mocks.fetchTramitesIct).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText(/n\.º de trámite/i), { target: { value: "abc" } });
    fireEvent.click(screen.getByRole("button", { name: /buscar/i }));

    await waitFor(() =>
      expect(mocks.fetchTramitesIct).toHaveBeenLastCalledWith(
        expect.objectContaining({ numero: undefined }),
      ),
    );
  });

  it("AC5: la exportación se ofrece solo cuando hay filas que exportar", async () => {
    // Ofrecer «Exportar» sobre una bandeja vacía produce un archivo con cabeceras y nada más, que
    // el destinatario del correo lee como «no hay nada» sin saber si es que falló la búsqueda.
    mocks.fetchTramitesIct.mockResolvedValue(pagina({ items: [], total: 0 }));
    const vacia = render(<IctTrazabilidad />);
    await waitFor(() => expect(mocks.fetchTramitesIct).toHaveBeenCalled());
    expect(screen.queryByRole("button", { name: /exportar a excel/i })).not.toBeInTheDocument();
    vacia.unmount();

    mocks.fetchTramitesIct.mockResolvedValue(pagina());
    render(<IctTrazabilidad />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /exportar a excel/i })).toBeInTheDocument(),
    );
  });

  it("AC6: sin resultados se explica por qué, en vez de dejar una tabla vacía", async () => {
    mocks.fetchTramitesIct.mockResolvedValue(pagina({ items: [], total: 0 }));
    render(<IctTrazabilidad />);

    await waitFor(() =>
      expect(screen.getByText(/ningún trámite de la integración coincide/i)).toBeInTheDocument(),
    );
  });

  it("AC7: un fallo de carga se explica sin exponer el código HTTP ni la ruta", async () => {
    mocks.fetchTramitesIct.mockRejectedValue(new Error("Request failed with status 503 /api/v1/ict"));
    render(<IctTrazabilidad />);

    await waitFor(() =>
      expect(screen.getByText(/no se pudieron cargar los trámites/i)).toBeInTheDocument(),
    );
    expect(screen.queryByText(/503/)).not.toBeInTheDocument();
    expect(screen.queryByText(/api\/v1/)).not.toBeInTheDocument();
  });

  it("destaca la espera larga y deja vacía la de un trámite ya terminado", async () => {
    // Una hora es el umbral: la cadencia más lenta del pipeline es de 45 s, así que más de una hora
    // parado no es esperar turno, es estar atascado.
    mocks.fetchTramitesIct.mockResolvedValue(
      pagina({
        items: [
          tramite({ numero: 1, placa: "AAA111", estado: "con_novedades", minutosEsperando: 252 }),
          tramite({ numero: 2, placa: "BBB222", estado: "borrador_creado", minutosEsperando: null }),
        ],
        total: 2,
      }),
    );
    render(<IctTrazabilidad />);

    await waitFor(() => expect(screen.getByText("4 h 12 min")).toBeInTheDocument());
    expect(screen.getByText("4 h 12 min").className).toMatch(/text-\[#C2410C\]/);
    // El terminal no muestra cero: cero se leería como «acaba de moverse».
    expect(screen.getAllByText("—").length).toBeGreaterThan(0);
  });

  it("muestra las señales de pausado y sin adjuntos cuando el trámite las trae", async () => {
    mocks.fetchTramitesIct.mockResolvedValue(
      pagina({ items: [tramite({ pausado: true, sinAdjuntos: true })], total: 1 }),
    );
    render(<IctTrazabilidad />);

    await waitFor(() => expect(screen.getByText("Pausado")).toBeInTheDocument());
    expect(screen.getByText("Sin adjuntos")).toBeInTheDocument();
  });
});
