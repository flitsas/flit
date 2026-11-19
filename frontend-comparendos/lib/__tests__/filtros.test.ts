import { describe, expect, it } from "vitest";
import { aQuery, etiquetasAplicadas, FILTROS_VACIOS, filtrosAParams, filtrosDeParams, leerLista, quitarEtiqueta, VISTA_POR_DEFECTO, type FiltrosBandeja, type VistaBandeja } from "@/lib/filtros";

const f = (cambios: Partial<FiltrosBandeja>): FiltrosBandeja => ({ ...FILTROS_VACIOS, ...cambios });
const textos = (x: FiltrosBandeja, nombres: Record<string, string> = {}) => etiquetasAplicadas(x, nombres).map((e) => e.texto);

describe("menú «Filtros» y etiquetas (HU #13868)", () => {
  it("AC3: «no es» viaja solo para las listas que tienen valores", () => {
    expect(aQuery(f({ organismos: ["o1"], fuente: [], negar: ["organismos", "fuente"] })).negar).toBe("organismos");
    expect(aQuery(f({ negar: ["fuente"] })).negar).toBeUndefined();
    expect(aQuery(f({ placas: ["ABC123", "XYZ987"], numeros: ["A1"] }))).toMatchObject({ placas: "ABC123,XYZ987", numeros: "A1" });
  });

  it("AC3: lee una lista pegada desde Excel, sin repetidos y en mayúsculas", () => {
    expect(leerLista("abc123\r\nXYZ987\n\nabc123")).toEqual(["ABC123", "XYZ987"]);
    expect(leerLista("A1, B2;C3\tD4")).toEqual(["A1", "B2", "C3", "D4"]);
    expect(leerLista("   ")).toEqual([]);
  });

  it("AC4: «Activos» es la etiqueta por defecto y la bandeja vacía no muestra otras", () => {
    expect(etiquetasAplicadas(FILTROS_VACIOS)).toEqual([{ clave: "activos", texto: "Activos", porDefecto: true }]);
    expect(textos(f({ activos: "inactivos" }))).toEqual(["Solo inactivos"]);
    expect(textos(f({ activos: "todos" }))).toEqual([]);
  });

  it("AC4: cada filtro aplicado tiene su etiqueta legible, con «es / no es» y nombres", () => {
    expect(
      textos(
        f({
          rapido: "Radar",
          estadoLegal: ["Vencido"],
          organismos: ["o1"],
          negar: ["organismos"],
          placas: ["A", "B", "C"],
          imposicionDesde: "2026-01-01",
          pagado: "false",
          resultadoDp: "SinRespuesta",
        }),
        { o1: "Bogotá" },
      ),
    ).toEqual([
      "Radar de vencimientos (7 días hábiles)",
      "Estado legal: Vencido",
      "Activos",
      "Organismo de tránsito no es Bogotá",
      "Placa es 3 valores",
      "Imposición: desde 01 de ene de 2026",
      "Resultado del DP: Enviado, sin respuesta",
      "Sin pago",
    ]);
    expect(textos(f({ estadoProceso: ["DerechoPeticion"], activos: "todos" }))).toEqual(["Estado del proceso es Derecho de petición"]);
  });

  it("AC4: la × quita solo ese filtro; quitar «Activos» deja ver activos e inactivos", () => {
    const x = f({ organismos: ["o1"], negar: ["organismos", "fuente"], fuente: ["Simit"], imposicionDesde: "2026-01-01", imposicionHasta: "2026-02-01" });
    expect(quitarEtiqueta(x, "organismos")).toMatchObject({ organismos: [], negar: ["fuente"], fuente: ["Simit"] });
    expect(quitarEtiqueta(x, "imposicion")).toMatchObject({ imposicionDesde: "", imposicionHasta: "" });
    expect(quitarEtiqueta(x, "activos").activos).toBe("todos");
    expect(quitarEtiqueta(f({ rapido: "Radar" }), "rapido").rapido).toBeNull();
  });
});

describe("filtros de la bandeja en la URL (rediseño S1)", () => {
  const ida = (x: FiltrosBandeja, vista?: Partial<VistaBandeja>) => filtrosDeParams(filtrosAParams(x, { ...VISTA_POR_DEFECTO, ...vista }));

  it("la bandeja de fábrica deja la URL limpia", () => {
    expect(filtrosAParams(FILTROS_VACIOS).toString()).toBe("");
    expect(filtrosDeParams(new URLSearchParams())).toEqual({ filtros: FILTROS_VACIOS, vista: VISTA_POR_DEFECTO });
  });

  it("ida y vuelta: listas, «no es», fechas, sí/no, atajos, orden, página y empresa", () => {
    const x = f({
      buscar: "abc",
      placas: ["ABC123", "XYZ987"],
      organismos: ["o1"],
      negar: ["organismos"],
      imposicionDesde: "2026-01-01",
      imposicionHasta: "2026-02-01",
      estadoLegal: ["Vencido"],
      pagado: "false",
      conDp: "true",
      resultadoDp: "Favorable",
      activos: "todos",
      sinGestion: true,
      rapido: "ProximosAVencer",
      juridico: "C038",
    });
    const vista = { orden: "valor", descendente: true, pagina: 3, tamano: 20, empresa: "e1" };
    expect(ida(x, vista)).toEqual({ filtros: x, vista });
  });

  it("acepta lo que mandan el dashboard y Reportes: ?nit= suelto y ?rapido=Radar", () => {
    const { filtros, vista } = filtrosDeParams(new URLSearchParams("rapido=Radar&nit=n1&empresa=e9"));
    expect(filtros).toEqual(f({ rapido: "Radar", nits: ["n1"] }));
    expect(vista.empresa).toBe("e9");
  });

  it("acepta tal cual los enlaces del Inicio (S2), con empresa y NIT opcionales", () => {
    const casos: [string, Partial<FiltrosBandeja>][] = [
      ["rapido=ProximosAVencer", { rapido: "ProximosAVencer" }],
      ["rapido=SinContraventor", { rapido: "SinContraventor" }],
      ["rapido=RechazadosOt", { rapido: "RechazadosOt" }],
      ["rapido=Radar", { rapido: "Radar" }],
      ["sinGestion=true", { sinGestion: true }],
      ["estadoLegal=Vencido", { estadoLegal: ["Vencido"] }],
      ["cobroCoactivo=true", { cobroCoactivo: "true" }],
      ["juridico=CaducidadInmediata", { juridico: "CaducidadInmediata" }],
    ];
    for (const [q, esperado] of casos) {
      expect(filtrosDeParams(new URLSearchParams(q)).filtros).toEqual(f(esperado));
      const { filtros, vista } = filtrosDeParams(new URLSearchParams(`${q}&empresa=e1&nit=n1`));
      expect(filtros).toEqual(f({ ...esperado, nits: ["n1"] }));
      expect(vista.empresa).toBe("e1");
    }
  });

  it("ignora lo que no reconoce sin romper la bandeja", () => {
    const { filtros, vista } = filtrosDeParams(
      new URLSearchParams("rapido=Otro&pagado=quizas&estadoLegal=Vencido,Inventado&imposicionDesde=ayer&negar=nada&orden=hack&descendente=true&pagina=-2&tamano=999&activos=raro"),
    );
    expect(filtros).toEqual(f({ estadoLegal: ["Vencido"] }));
    expect(vista).toEqual(VISTA_POR_DEFECTO);
  });
});
