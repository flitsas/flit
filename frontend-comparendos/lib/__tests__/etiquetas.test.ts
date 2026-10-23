import { describe, expect, it } from "vitest";
import { cambioContraventor, estadoProceso, nombreActor, textoParaGestora, textoSemaforo, tonoSemaforo, ultimoHito } from "@/lib/etiquetas";
import { aQuery, contarFiltros, describirFiltros, FILTROS_VACIOS, resumenPagina } from "@/lib/filtros";

describe("semáforo legal", () => {
  it("cuenta los días hábiles en tiempo y dice «Vence hoy» el último día", () => {
    expect(textoSemaforo("EnTiempo", 5)).toBe("5 días hábiles");
    expect(textoSemaforo("EnTiempo", 1)).toBe("1 día hábil");
    expect(textoSemaforo("EnTiempo", 0)).toBe("Vence hoy");
    expect(textoSemaforo("Vencido", null)).toBe("Vencido");
    expect(textoSemaforo("SinNotificar", null)).toBe("Sin notificación");
  });

  it("pinta en ámbar lo próximo a vencer y en rojo lo vencido", () => {
    expect(tonoSemaforo("EnTiempo", true)).toBe("warning");
    expect(tonoSemaforo("EnTiempo", false)).toBe("success");
    expect(tonoSemaforo("Vencido", false)).toBe("danger");
  });
});

describe("etiquetas §4.9", () => {
  it("«Último hito de notificación» nombra el DP y el aviso 4 (matriz 0.28)", () => {
    expect(ultimoHito("DpEnviado").texto).toBe("Notificado a secretaría — derecho de petición");
    expect(ultimoHito("Aviso4").texto).toBe("Notificación 4 — solicitud de cambio de contraventor");
    expect(ultimoHito("SinProcesar").tono).toBe("neutral");
  });

  it("usa los colores acordados", () => {
    expect(estadoProceso("RespuestaFavorable").tono).toBe("success");
    expect(estadoProceso("DerechoPeticion").tono).toBe("info");
    expect(cambioContraventor("RechazadoOt")).toEqual({ texto: "Rechazado OT", tono: "danger" });
  });

  it("muestra un valor desconocido tal cual, en gris", () => {
    expect(estadoProceso("Otro")).toEqual({ texto: "Otro", tono: "neutral" });
    expect(estadoProceso(null).texto).toBe("—");
  });
});

describe("filtros de la bandeja", () => {
  it("solo envía lo que tiene valor", () => {
    const q = aQuery({ ...FILTROS_VACIOS, estadoLegal: ["EnTiempo", "Vencido"], buscar: " abc ", sinGestion: true, rapido: "ProximosAVencer" });
    expect(q).toMatchObject({ estadoLegal: "EnTiempo,Vencido", buscar: "abc", sinGestion: true, rapido: "ProximosAVencer" });
    expect(q.activos).toBeUndefined();
    expect(q.nits).toBeUndefined();
  });

  it("cuenta solo lo del menú «Filtros»: ni la búsqueda, ni el estado legal, ni los atajos", () => {
    expect(contarFiltros(FILTROS_VACIOS)).toBe(0);
    expect(aQuery({ ...FILTROS_VACIOS, resultadoDp: "SinRespuesta" }).resultadoDp).toBe("SinRespuesta");
    expect(contarFiltros({ ...FILTROS_VACIOS, resultadoDp: "Favorable" })).toBe(1);
    expect(contarFiltros({ ...FILTROS_VACIOS, placas: ["X"], imposicionDesde: "2026-01-01", activos: "inactivos" })).toBe(3);
    expect(contarFiltros({ ...FILTROS_VACIOS, activos: "todos" })).toBe(0);
    expect(contarFiltros({ ...FILTROS_VACIOS, buscar: "abc", estadoLegal: ["Vencido"], rapido: "Radar" })).toBe(0);
  });

  it("resume la página del pie", () => {
    expect(resumenPagina(2, 25, 25, 300)).toBe("Mostrando 26–50 de 300");
    expect(resumenPagina(1, 25, 0, 0, "comparendos")).toBe("Sin comparendos que mostrar");
  });
});

describe("describirFiltros (Mis exportaciones, §7.26)", () => {
  it("traduce valores, nombres y rangos", () => {
    const texto = describirFiltros(
      { rapido: "Radar", nits: ["n1"], placa: " abc123 ", estadoLegal: ["Vencido"], imposicionDesde: "2026-01-01", pagado: false, activos: "todos" },
      { n1: "800197268" },
      { estadoLegal: { Vencido: { texto: "Vencido" } } },
    );
    expect(texto).toBe("Radar de vencimientos · NIT: 800197268 · Placa: ABC123 · Imposición: 2026-01-01 a … · Estado legal: Vencido · Sin pagar · Activos e inactivos");
  });

  it("sin filtros lo dice", () => {
    expect(describirFiltros({ activos: "activos" }, {}, {})).toBe("Sin filtros");
  });
});

describe("colores del contraventor (Epic GDC-4)", () => {
  it("naranja si falta asignarlo a mano, verde si lo asignó la API", async () => {
    const { consultaContraventor } = await import("@/lib/etiquetas");
    expect(consultaContraventor("NoAplica").tono).toBe("warning");
    expect(consultaContraventor("NoEncontrado").tono).toBe("warning");
    expect(consultaContraventor("Encontrado").tono).toBe("success");
  });
});

describe("textos sin tecnología para la gestora", () => {
  it("traduce los eventos guardados con los textos viejos", () => {
    expect(textoParaGestora("La API de contratos encontró el conductor.")).toBe("La consulta automática encontró el conductor.");
    expect(textoParaGestora("La consulta a la API de contratos falló: API de contratos simulada no disponible.\n{\"placa\":\"X\"}")).toBe(
      "No se pudo consultar el conductor: se reintenta más tarde o se asigna a mano.",
    );
    expect(textoParaGestora("Conductor asignado (API): JUAN, CC ****0655.")).toBe("Conductor asignado (automático): JUAN, CC ****0655.");
    expect(textoParaGestora("Aviso 1 enviado por el scheduler a x@y.co (sin descuento).")).toBe("Aviso 1 enviado automáticamente a x@y.co (sin descuento).");
    expect(textoParaGestora("Pago registrado.")).toBe("Pago registrado.");
  });

  it("los autores técnicos se leen como automáticos", () => {
    expect(nombreActor("Scheduler")).toBe("Automático");
    expect(nombreActor("API de contratos")).toBe("Consulta automática");
    expect(nombreActor("admin@empresa.local")).toBe("admin@empresa.local");
  });
});
