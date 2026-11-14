import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";
import {
  MAXIMO_DEUDA_VARIAS,
  MAXIMO_SOLICITUD_MASIVA,
  descargarExcelDeudaVarias,
  desmarcarSaneamiento,
  marcarSaneamiento,
  mensajeErrorSaneamiento,
  obtenerDeuda,
  obtenerSaneamiento,
  solicitarSaneamientos,
  urlDeudaExcel,
  urlDeudaPdf,
  urlEvidencia,
  type PanelDeuda,
  type Saneamiento,
} from "@/lib/saneamiento/api";

// Uso de ejemplo: await marcarSaneamiento(lote, vehiculo) → PUT …/saneamiento; await solicitarSaneamientos([v1, v2]) →
// { solicitados, omitidos }; await obtenerDeuda(lote, vehiculo) → panel de deuda (§6.5). Errores por `code` con ApiError.

afterEach(() => vi.unstubAllGlobals());

const LOTE = "00000000-0000-7000-8000-00000000b001";
const VEH = "00000000-0000-7000-8000-00000000c001";
const REN = "00000000-0000-7000-8000-00000000d001";
const RUTA = `/diagnosticos/${LOTE}/vehiculos/${VEH}/saneamiento`;

const saneamiento: Saneamiento = {
  id: "00000000-0000-7000-8000-00000000e001",
  version: 123,
  estado: "SOLICITADO",
  solicitado_en: "2026-11-14T15:00:00Z",
  solicitado_por_mi: true,
  tomado_en: null,
  cerrado_en: null,
  motivo_cierre: null,
  motivo_anulacion: null,
  observacion_flit: null,
  renglones: [
    {
      id: REN,
      version: 45,
      fuente: "SIMIT_PROPIETARIO",
      hallazgo: "DEUDA",
      descripcion: null,
      automatico: true,
      resultado: "PENDIENTE",
      nota: null,
      evidencia: { nombre: "pago.pdf", tamano: 123456, subida_en: "2026-11-14T16:00:00Z" },
    },
  ],
  historial: [{ evento: "SOLICITADO", ocurrido_en: "2026-11-14T15:00:00Z", actor: "COMPANIA", fuente: null, motivo: null }],
};

const deuda: PanelDeuda = {
  vehiculo: { id: VEH, placa: "ABC123" },
  modo: "CON_ANALISIS",
  dictamen: { veredicto: "BLOQUEADO", catalogo_version: 2, motivos: [{ codigo: "SIMIT_PROPIETARIO", efecto: "BLOQUEA", texto: "Multas" }] },
  fuentes: [
    {
      fuente: "SIMIT_PROPIETARIO",
      estado: "CON_DEUDA",
      consultado_en: "2026-11-14T15:00:00Z",
      persona: { tipo_documento: "CC", documento: "0000" },
      paz_y_salvo: false,
      comparendos: [{ numero: "N-1", fecha: "2026-01-01", valor: "450000.00", estado: "PENDIENTE" }],
      comparendos_truncados: false,
      acuerdos_pago: [{ saldo_pendiente: 300000 }],
    },
    { fuente: "IMPUESTO", estado: "CON_DEUDA", consultado_en: "2026-11-14T15:00:00Z", monto_total: 1250000, vigencias: [{ anio: 2025, valor: 1250000, pagada: false }] },
    { fuente: "SOAT", estado: "NO_VIGENTE", vence: "2026-10-01" },
    { fuente: "RTM", estado: "SIN_HALLAZGO" },
    { fuente: "SIMIT_LOCATARIO", estado: "NO_APLICA", motivo_na: "SIN_DOCUMENTO_LOCATARIO" },
  ],
  saneamiento: { estado: "EN_GESTION" },
};

const cuerpo = (fetchMock: { mock: { calls: unknown[][] } }, n = 0) => JSON.parse(String((fetchMock.mock.calls[n][1] as RequestInit).body));

describe("lib/saneamiento/api — marcar y desmarcar (§6.1)", () => {
  it("marcar es un PUT sin cuerpo y devuelve el saneamiento (201 o 200 idempotente)", async () => {
    const fetchMock = simularApi({ [`PUT ${RUTA}`]: () => Response.json(saneamiento, { status: 201 }) });
    const s = await marcarSaneamiento(LOTE, VEH);
    expect(llamadas(fetchMock, "PUT", RUTA)).toHaveLength(1);
    expect((fetchMock.mock.calls[0][1] as RequestInit).body).toBeUndefined();
    expect(s.estado).toBe("SOLICITADO");
  });

  it("marcar con 409 lanza ApiError con el code (VEHICULO_NO_TERMINADO, YA_SOLICITADO)", async () => {
    simularApi({ [`PUT ${RUTA}`]: () => Response.json({ code: "YA_SOLICITADO", error: "x" }, { status: 409 }) });
    await expect(marcarSaneamiento(LOTE, VEH)).rejects.toMatchObject({ status: 409, code: "YA_SOLICITADO" });
  });

  it("desmarcar: 204 resuelve y 404 SIN_SANEAMIENTO también (ya no estaba)", async () => {
    const fetchMock = simularApi({ [`DELETE ${RUTA}`]: () => new Response(null, { status: 204 }) });
    await expect(desmarcarSaneamiento(LOTE, VEH)).resolves.toBeUndefined();
    expect(llamadas(fetchMock, "DELETE", RUTA)).toHaveLength(1);
    simularApi({ [`DELETE ${RUTA}`]: () => Response.json({ code: "SIN_SANEAMIENTO" }, { status: 404 }) });
    await expect(desmarcarSaneamiento(LOTE, VEH)).resolves.toBeUndefined();
  });

  it("desmarcar con 409 SANEAMIENTO_EN_GESTION o un 404 de otro tipo lanza", async () => {
    simularApi({ [`DELETE ${RUTA}`]: () => Response.json({ code: "SANEAMIENTO_EN_GESTION" }, { status: 409 }) });
    await expect(desmarcarSaneamiento(LOTE, VEH)).rejects.toMatchObject({ code: "SANEAMIENTO_EN_GESTION" });
    simularApi({ [`DELETE ${RUTA}`]: () => Response.json({ code: "NO_ENCONTRADO" }, { status: 404 }) });
    await expect(desmarcarSaneamiento(LOTE, VEH)).rejects.toBeInstanceOf(ApiError);
  });

  it("codifica los ids en la ruta", async () => {
    const fetchMock = simularApi({});
    await marcarSaneamiento("a/b", "c d").catch(() => undefined);
    expect(String(fetchMock.mock.calls[0][0])).toBe("/api/v1/diagnostico/diagnosticos/a%2Fb/vehiculos/c%20d/saneamiento");
  });
});

describe("lib/saneamiento/api — masivo (§6.1)", () => {
  it("manda vehiculo_ids sin repetidos y devuelve solicitados y omitidos", async () => {
    const fetchMock = simularApi({
      "POST /saneamientos/solicitudes": () => Response.json({ solicitados: 1, omitidos: [{ vehiculo_id: "v2", motivo: "NO_TERMINADO" }] }),
    });
    const r = await solicitarSaneamientos(["v1", "v2", "v1"]);
    expect(cuerpo(fetchMock)).toEqual({ vehiculo_ids: ["v1", "v2"] });
    expect(r).toEqual({ solicitados: 1, omitidos: [{ vehiculo_id: "v2", motivo: "NO_TERMINADO" }] });
  });

  it("lista vacía o de más de 500 no sale a la red: ApiError 400 LISTA_INVALIDA", async () => {
    const fetchMock = simularApi({});
    expect(MAXIMO_SOLICITUD_MASIVA).toBe(500);
    await expect(solicitarSaneamientos([])).rejects.toMatchObject({ status: 400, code: "LISTA_INVALIDA" });
    const muchos = Array.from({ length: 501 }, (_, i) => `v${i}`);
    await expect(solicitarSaneamientos(muchos)).rejects.toMatchObject({ code: "LISTA_INVALIDA" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("respuesta inesperada lanza un error legible", async () => {
    simularApi({ "POST /saneamientos/solicitudes": () => Response.json({}) });
    await expect(solicitarSaneamientos(["v1"])).rejects.toMatchObject({ message: expect.stringContaining("respondió algo inesperado") });
  });
});

describe("lib/saneamiento/api — detalle, evidencia y deuda", () => {
  it("detalle 200 devuelve el saneamiento del cliente, sin usuario_id ni referencia de la evidencia (SP-15)", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json(saneamiento) });
    const s = await obtenerSaneamiento(LOTE, VEH);
    expect(s?.renglones[0].evidencia).toEqual({ nombre: "pago.pdf", tamano: 123456, subida_en: "2026-11-14T16:00:00Z" });
    expect(Object.keys(s!.historial[0]).sort()).toEqual(["actor", "evento", "fuente", "motivo", "ocurrido_en"]);
  });

  it("detalle 404 SIN_SANEAMIENTO devuelve null; otro error lanza", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json({ code: "SIN_SANEAMIENTO" }, { status: 404 }) });
    await expect(obtenerSaneamiento(LOTE, VEH)).resolves.toBeNull();
    simularApi({ [`GET ${RUTA}`]: () => Response.json({ code: "X" }, { status: 500 }) });
    await expect(obtenerSaneamiento(LOTE, VEH)).rejects.toBeInstanceOf(ApiError);
  });

  it("panel de deuda: GET …/deuda; 409 VEHICULO_NO_TERMINADO lanza con el code", async () => {
    const ruta = `/diagnosticos/${LOTE}/vehiculos/${VEH}/deuda`;
    simularApi({ [`GET ${ruta}`]: () => Response.json(deuda) });
    const panel = await obtenerDeuda(LOTE, VEH);
    expect(panel.fuentes.map((f) => f.fuente)).toEqual(["SIMIT_PROPIETARIO", "IMPUESTO", "SOAT", "RTM", "SIMIT_LOCATARIO"]);
    simularApi({ [`GET ${ruta}`]: () => Response.json({ code: "VEHICULO_NO_TERMINADO" }, { status: 409 }) });
    await expect(obtenerDeuda(LOTE, VEH)).rejects.toMatchObject({ code: "VEHICULO_NO_TERMINADO" });
  });

  it("URLs del BFF para PDF, Excel y evidencia", () => {
    expect(urlDeudaPdf(LOTE, VEH)).toBe(`/api/v1/diagnostico/diagnosticos/${LOTE}/vehiculos/${VEH}/deuda/pdf`);
    expect(urlDeudaExcel(LOTE, VEH)).toBe(`/api/v1/diagnostico/diagnosticos/${LOTE}/vehiculos/${VEH}/deuda/excel`);
    expect(urlEvidencia(LOTE, VEH, REN)).toBe(`/api/v1/diagnostico${RUTA}/renglones/${REN}/evidencia`);
  });
});

describe("lib/saneamiento/api — Excel de varias placas (POST /vehiculos/deuda/excel)", () => {
  it("manda los ids y guarda el archivo con el nombre del Content-Disposition", async () => {
    const creados: HTMLAnchorElement[] = [];
    const original = document.createElement.bind(document);
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      const el = original(tag);
      if (tag === "a") {
        vi.spyOn(el as HTMLAnchorElement, "click").mockImplementation(() => undefined);
        creados.push(el as HTMLAnchorElement);
      }
      return el;
    });
    vi.stubGlobal("URL", Object.assign(URL, { createObjectURL: vi.fn(() => "blob:x"), revokeObjectURL: vi.fn() }));
    const fetchMock = simularApi({
      "POST /vehiculos/deuda/excel": () =>
        new Response(new Blob(["xlsx"]), { status: 200, headers: { "Content-Disposition": 'attachment; filename="deuda-placas.xlsx"' } }),
    });
    const nombre = await descargarExcelDeudaVarias(["v1", "v2"]);
    expect(nombre).toBe("deuda-placas.xlsx");
    expect(cuerpo(fetchMock)).toEqual({ vehiculo_ids: ["v1", "v2"] });
    expect(creados[0].download).toBe("deuda-placas.xlsx");
    vi.restoreAllMocks();
  });

  it("más de 200 o ninguna no sale a la red; un error HTTP lanza con su code", async () => {
    const fetchMock = simularApi({ "POST /vehiculos/deuda/excel": () => Response.json({ code: "LISTA_INVALIDA" }, { status: 400 }) });
    expect(MAXIMO_DEUDA_VARIAS).toBe(200);
    await expect(descargarExcelDeudaVarias([])).rejects.toMatchObject({ code: "LISTA_INVALIDA" });
    await expect(descargarExcelDeudaVarias(Array.from({ length: 201 }, (_, i) => `v${i}`))).rejects.toMatchObject({ code: "LISTA_INVALIDA" });
    expect(fetchMock).not.toHaveBeenCalled();
    await expect(descargarExcelDeudaVarias(["v1"])).rejects.toMatchObject({ status: 400, code: "LISTA_INVALIDA" });
  });
});

describe("mensajeErrorSaneamiento — texto por code, nunca el código", () => {
  const error = (status: number, code?: string) => new ApiError(status, "texto del servidor", code);

  it("los códigos del cliente tienen su texto de la spec", () => {
    expect(mensajeErrorSaneamiento(error(409, "VEHICULO_NO_TERMINADO"))).toBe("Este vehículo aún tiene consultas por terminar.");
    expect(mensajeErrorSaneamiento(error(409, "YA_SOLICITADO"))).toBe("Este vehículo ya tiene un saneamiento.");
    expect(mensajeErrorSaneamiento(error(409, "SANEAMIENTO_EN_GESTION"))).toBe("FLIT ya empezó este saneamiento; ya no se puede quitar.");
  });

  it("los del backoffice también", () => {
    expect(mensajeErrorSaneamiento(error(409, "VERSION_DESACTUALIZADA"))).toBe("Otra persona cambió este saneamiento.");
    expect(mensajeErrorSaneamiento(error(409, "NO_EDITABLE"))).toBe("El saneamiento ya no está en gestión.");
    expect(mensajeErrorSaneamiento(error(409, "TRANSICION_INVALIDA"))).toBe("El estado cambió mientras tanto. Recarga para ver el estado actual.");
    expect(mensajeErrorSaneamiento(error(413, "ARCHIVO_MUY_GRANDE"))).toBe("El archivo supera 10 MB.");
    expect(mensajeErrorSaneamiento(error(422, "ARCHIVO_NO_ES_PDF"))).toBe("El archivo no es un PDF válido.");
    expect(mensajeErrorSaneamiento(error(400, "ARCHIVO_REQUERIDO"))).toBe("Elige un archivo PDF.");
    expect(mensajeErrorSaneamiento(error(422, "MOTIVO_REQUERIDO"))).toBe("Escribe al menos 10 caracteres.");
    expect(mensajeErrorSaneamiento(error(422, "DEMASIADAS_FILAS"))).toBe(
      "Hay más de 5.000 saneamientos con estos filtros. Acota las fechas o el estado para descargar.",
    );
  });

  it("sin code conocido: el respaldo dado, o el genérico; 5xx y red, el genérico", () => {
    expect(mensajeErrorSaneamiento(error(500))).toBe("No pudimos guardar el cambio. Intenta de nuevo.");
    expect(mensajeErrorSaneamiento(new TypeError("Failed to fetch"), "No pudimos solicitar el saneamiento. Intenta de nuevo.")).toBe(
      "No pudimos solicitar el saneamiento. Intenta de nuevo.",
    );
    expect(mensajeErrorSaneamiento(error(409, "CODIGO_NUEVO"))).not.toContain("CODIGO_NUEVO");
  });
});
