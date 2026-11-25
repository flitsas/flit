import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { FranjaLote } from "@/components/ocr/FranjaLote";

// GDC-EVID §10: si el lote sigue en curso y no avanza hace más de 5 minutos, la franja lo avisa (el servidor lo retoma solo)
// sin quitar la barra. Sin `ultimoAvanceEn`, nada.
const AHORA = new Date("2026-11-25T15:30:00Z");
const hace = (min: number) => new Date(AHORA.getTime() - min * 60000).toISOString();
const LOTE = {
  loteId: "l1", porAsociar: 36, total: 48, asociados: 12, noAsociados: [], correosEnCamino: 11, sinCorreo: [],
  solicitadoPor: "admin@empresa.local", solicitadoEn: "2026-11-25T15:00:00Z", terminadoEn: null,
};
const ver = (lote: Record<string, unknown>) => render(<FranjaLote lote={{ ...LOTE, ...lote }} onCerrar={() => {}} onVer={() => {}} />);

describe("franja del lote: sin avance", () => {
  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it("4 minutos sin avance: nada", () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(AHORA);
    ver({ ultimoAvanceEn: hace(4) });
    expect(screen.queryByText(/Sin avance/)).toBeNull();
    expect(screen.getByRole("progressbar")).toBeTruthy();
  });

  it("6 minutos sin avance: lo avisa sin quitar la barra", () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(AHORA);
    ver({ ultimoAvanceEn: hace(6) });
    expect(screen.getByText("Sin avance desde hace 6 minutos; se retomará automáticamente.")).toBeTruthy();
    expect(screen.getByRole("progressbar")).toBeTruthy();
    expect(screen.getByText("Asociando 12 de 48…")).toBeTruthy();
  });

  it("sin el campo, o con el lote terminado: nada", () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(AHORA);
    ver({});
    expect(screen.queryByText(/Sin avance/)).toBeNull();
    cleanup();
    ver({ ultimoAvanceEn: null });
    expect(screen.queryByText(/Sin avance/)).toBeNull();
    cleanup();
    ver({ porAsociar: 0, asociados: 48, ultimoAvanceEn: hace(30), terminadoEn: hace(1) });
    expect(screen.getByText("Asociación terminada")).toBeTruthy();
    expect(screen.queryByText(/Sin avance/)).toBeNull();
  });
});

describe("franja del lote: motivos que llegan mal", () => {
  afterEach(cleanup);
  it("motivo nulo o vacío → «Sin motivo registrado» (no asociados y sin correo)", () => {
    ver({
      porAsociar: 0, asociados: 46, terminadoEn: "2026-11-25T15:10:00Z",
      sinCorreo: [{ motivo: null, cantidad: 2 }, { motivo: "  ", cantidad: 1 }],
      noAsociados: [{ documentoId: "a", nombreArchivo: "a.pdf", motivo: null }, { documentoId: "b", nombreArchivo: "b.pdf", motivo: "" }],
    });
    expect(screen.getByText("Sin motivo registrado (2)")).toBeTruthy();
    expect(screen.getByText("Sin motivo registrado (1)")).toBeTruthy();
    expect(screen.getByText("a.pdf: Sin motivo registrado")).toBeTruthy();
    expect(screen.getByText("b.pdf: Sin motivo registrado")).toBeTruthy();
    expect(screen.queryByText(/null/)).toBeNull();
  });
});
