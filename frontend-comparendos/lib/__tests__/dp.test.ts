import { describe, expect, it } from "vitest";
import { armarRespuestaDp, avanceLote, hojaLote, erroresRespuestaDp, ordenarTipologias, revisarLote, type FilaLote, type FormRespuestaDp } from "@/lib/dp";
import { diaColombia } from "@/lib/format/date";

const pdf = new File(["%PDF"], "respuesta.pdf", { type: "application/pdf" });
const R: FormRespuestaDp = { radicadoOt: " SDM-2026-123 ", fecha: "2026-10-08", sentido: "Favorable", archivo: pdf };

describe("respuesta del organismo al DP", () => {
  it("lista para enviar cuando está completa y dentro del rango", () => {
    expect(erroresRespuestaDp(R, "2026-10-01", "2026-10-09")).toEqual([]);
  });

  it("exige radicado, sentido y PDF", () => {
    const e = erroresRespuestaDp({ radicadoOt: " ", fecha: "2026-10-08", sentido: "", archivo: null }, null, "2026-10-09");
    expect(e).toHaveLength(3);
    expect(erroresRespuestaDp({ ...R, archivo: new File(["x"], "foto.png", { type: "image/png" }) }, null, "2026-10-09")).toEqual(["La respuesta debe ser un PDF."]);
  });

  it("la fecha va entre la radicación y hoy", () => {
    expect(erroresRespuestaDp({ ...R, fecha: "2026-10-10" }, "2026-10-01", "2026-10-09")).toEqual(["La fecha debe estar entre la radicación y hoy."]);
    expect(erroresRespuestaDp({ ...R, fecha: "2026-09-30" }, "2026-10-01T15:00:00Z", "2026-10-09")).toEqual(["La fecha debe estar entre la radicación y hoy."]);
  });

  it("arma el multipart con el radicado recortado", () => {
    const f = armarRespuestaDp(R);
    expect(f.get("radicadoOt")).toBe("SDM-2026-123");
    expect(f.get("sentido")).toBe("Favorable");
    expect(f.get("archivo")).toBeInstanceOf(File);
  });
});

describe("tipologías", () => {
  it("las que proceden van primero", () => {
    const orden = ordenarTipologias([
      { codigo: "B", procede: false },
      { codigo: "C", procede: true },
      { codigo: "A", procede: true },
    ]).map((t) => t.codigo);
    expect(orden).toEqual(["A", "C", "B"]);
  });
});

const fila = (n: string, extra: Partial<FilaLote> = {}): FilaLote => ({
  id: n,
  numero: n,
  organismoTransitoId: "ot-1",
  organismo: "Movilidad de Bogotá",
  activo: true,
  pagado: false,
  tieneDp: false,
  ...extra,
});

describe("revisión del lote de DP", () => {
  it("un lote de un solo organismo pasa y anticipa los que se omitirán", () => {
    const r = revisarLote([fila("1"), fila("2", { tieneDp: true }), fila("3", { pagado: true }), fila("4", { activo: false })]);
    expect(r.errores).toEqual([]);
    expect(r.organismo).toBe("Movilidad de Bogotá");
    expect(r.omitiran).toEqual([
      { numero: "2", motivo: "Ya tiene derecho de petición" },
      { numero: "3", motivo: "Está pagado" },
      { numero: "4", motivo: "Está inactivo" },
    ]);
  });

  it("rechaza organismos distintos, comparendos sin organismo, vacío y más de 500", () => {
    expect(revisarLote([fila("1"), fila("2", { organismoTransitoId: "ot-2" })]).errores).toEqual([
      "Todos los comparendos del lote deben ser del mismo organismo de tránsito.",
    ]);
    expect(revisarLote([fila("1", { organismoTransitoId: null })]).errores).toEqual(["Hay comparendos sin organismo de tránsito identificado."]);
    expect(revisarLote([]).errores).toEqual(["Selecciona al menos un comparendo."]);
    expect(revisarLote(Array.from({ length: 501 }, (_, i) => fila(String(i)))).errores).toEqual(["Selecciona como máximo 500 comparendos."]);
  });

  it("calcula el avance sobre lo ya resuelto", () => {
    expect(avanceLote({ total: 4, enviados: 1, omitidos: 1, fallidos: 0 })).toBe(50);
    expect(avanceLote({ total: 0, enviados: 0, omitidos: 0, fallidos: 0 })).toBe(100);
  });
});

describe("día de Colombia", () => {
  it("un instante de madrugada UTC sigue siendo el día anterior en Bogotá", () => {
    expect(diaColombia("2026-10-09T03:00:00Z")).toBe("2026-10-08");
    expect(diaColombia(new Date("2026-10-09T15:00:00Z"))).toBe("2026-10-09");
  });
});

describe("hojaLote", () => {
  it("una fila por comparendo y los totales como nota, en hora de Colombia", () => {
    const hoja = hojaLote({
      estado: "Completado", total: 2, enviados: 1, omitidos: 1, fallidos: 0, creadoEn: "2026-10-13T15:04:00Z", terminadoEn: null,
      items: [
        { numero: "11001000000045678901", estado: "Enviado", radicado: "DC-261013100400-123", motivo: null },
        { numero: "11001000000045678902", estado: "Omitido", radicado: null, motivo: "Ya tiene un DP" },
      ],
    });
    expect(hoja.rows).toHaveLength(2);
    expect(hoja.rows[0]?.[2]).toBe("DC-261013100400-123");
    expect(hoja.rows[1]?.[3]).toBe("Ya tiene un DP");
    expect(hoja.notes?.[0]).toContain("1 enviados, 1 omitidos y 0 fallidos de 2");
    expect(hoja.notes?.[1]).toContain("13/10/2026 10:04");
  });
});
