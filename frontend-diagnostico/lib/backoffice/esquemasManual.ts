// Esquemas del llenado manual (HU #13802, F06-H5) por tipo de resultado normalizado, según el contrato de HU #13800
// (PUT /backoffice/consultas/{cid}/manual, esquemas ResultadoManualSimit, ResultadoManualAvaluo, ResultadoManualRuntPersona y
// ResultadoManualRues). El formulario trabaja con borradores de texto; aquí se validan contra la forma del esquema y se
// convierten al cuerpo cerrado del PUT. Los errores usan la misma ruta que el `campo` del 422 de la API
// (`resultado.fuentes[0].valor`, `personas[1].resultado.licencias[0].categoria`), así ambos se pintan junto al mismo campo.
// La API vuelve a validar todo.
import type { Fuente } from "@/lib/configuracion";
import type { ConsultaPersona } from "@/lib/tablero/detalle";
import type { CuerpoLlenado, FormularioManual } from "./api";

export type TipoEsquema = "SIMIT" | "AVALUO" | ConsultaPersona;

export interface ComparendoBorrador {
  numero: string;
  fecha: string;
  valor: string;
  estado: string;
  organismo: string;
}
export interface SimitBorrador {
  tipo: "SIMIT";
  semaforo: "" | "VERDE" | "AMARILLO" | "ROJO";
  comparendos: ComparendoBorrador[];
}
export type FuenteAvaluoCodigo = "fasecolda" | "mercado_libre" | "base_gravable";
export interface FuenteAvaluoBorrador {
  fuente: "" | FuenteAvaluoCodigo;
  estado: "" | "OK" | "SIN_DATOS" | "ERROR";
  valor: string;
  muestras: string;
}
export interface AvaluoBorrador {
  tipo: "AVALUO";
  sin_valor_comercial: boolean;
  valor_sugerido: string;
  fuente_principal: "" | FuenteAvaluoCodigo;
  fuentes: FuenteAvaluoBorrador[];
}
export interface LicenciaBorrador {
  categoria: string;
  estado: string;
  vigente_hasta: string;
}
export interface RuntPersonaBorrador {
  tipo: "RUNT_PERSONA";
  inscrito: boolean;
  estado_conductor: string;
  licencias: LicenciaBorrador[];
}
export interface RuesBorrador {
  tipo: "RUES";
  encontrada: boolean;
  estado_matricula: string;
  ultima_renovacion: string;
}
export type Borrador = SimitBorrador | AvaluoBorrador | RuntPersonaBorrador | RuesBorrador;

/** Errores por ruta del cuerpo (la misma forma que el `campo` del 422 de la API). */
export type ErroresCampo = Record<string, string>;

/** Esquema del resultado: SIMIT vehículo y AVALUO por su fuente; en las de persona, la `consulta` de cada persona. */
export function esquemaDe(fuente: Fuente, consulta?: ConsultaPersona | null): TipoEsquema {
  if (fuente === "AVALUO") return "AVALUO";
  if (fuente === "SIMIT_VEHICULO") return "SIMIT";
  return consulta ?? (fuente.startsWith("SIMIT") ? "SIMIT" : "RUNT_PERSONA");
}

export const comparendoVacio = (): ComparendoBorrador => ({ numero: "", fecha: "", valor: "", estado: "", organismo: "" });
export const fuenteAvaluoVacia = (): FuenteAvaluoBorrador => ({ fuente: "", estado: "", valor: "", muestras: "" });
export const licenciaVacia = (): LicenciaBorrador => ({ categoria: "", estado: "", vigente_hasta: "" });

export function borradorVacio(tipo: TipoEsquema): Borrador {
  switch (tipo) {
    case "SIMIT":
      return { tipo, semaforo: "", comparendos: [] };
    case "AVALUO":
      return { tipo, sin_valor_comercial: false, valor_sugerido: "", fuente_principal: "", fuentes: [fuenteAvaluoVacia()] };
    case "RUNT_PERSONA":
      return { tipo, inscrito: false, estado_conductor: "", licencias: [] };
    case "RUES":
      return { tipo, encontrada: false, estado_matricula: "", ultima_renovacion: "" };
  }
}

/** Clave del borrador: «vehiculo» en las fuentes del vehículo; el persona_id en las de persona. */
export const CLAVE_VEHICULO = "vehiculo";

export const MAX_COMPARENDOS = 500;
export const MAX_FUENTES_AVALUO = 3;
export const MAX_LICENCIAS = 30;

const PESOS = /^\d{1,15}(\.\d{1,2})?$/;
const ENTERO = /^\d{1,15}$/;
const ANIO = /^\d{4}$/;

/** AAAA-MM-DD de una fecha real del calendario. */
function esFecha(valor: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(valor)) return false;
  const d = new Date(`${valor}T00:00:00Z`);
  return !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === valor;
}

class Validador {
  readonly errores: ErroresCampo = {};

  error(campo: string, mensaje: string) {
    if (!this.errores[campo]) this.errores[campo] = mensaje;
  }

  /** Texto opcional de como mucho `max` caracteres: lo pone en `destino` si no está vacío. */
  texto(destino: Record<string, unknown>, clave: string, valor: string, campo: string, max: number, requerido = false) {
    const v = valor.trim();
    if (!v) {
      if (requerido) this.error(campo, "Este campo es obligatorio.");
      return;
    }
    if (v.length > max) this.error(campo, `Como mucho ${max} caracteres.`);
    destino[clave] = v;
  }

  fecha(destino: Record<string, unknown>, clave: string, valor: string, campo: string, admiteAnio = false) {
    const v = valor.trim();
    if (!v) return;
    if (!(esFecha(v) || (admiteAnio && ANIO.test(v))))
      this.error(campo, admiteAnio ? "Usa AAAA-MM-DD o solo el año (AAAA)." : "Usa una fecha válida (AAAA-MM-DD).");
    destino[clave] = v;
  }

  /** Entero ≥ 1 escrito como texto; null si está vacío o no es válido. */
  entero(valor: string, campo: string, requerido: boolean): number | null {
    const v = valor.trim();
    if (!v) {
      if (requerido) this.error(campo, "Este campo es obligatorio.");
      return null;
    }
    if (!ENTERO.test(v) || Number(v) < 1) {
      this.error(campo, "Escribe un número entero mayor que cero, sin puntos ni signos.");
      return null;
    }
    return Number(v);
  }
}

function simit(b: SimitBorrador, raiz: string, val: Validador): Record<string, unknown> {
  const r: Record<string, unknown> = {};
  if (b.semaforo) r.semaforo = b.semaforo;
  if (b.comparendos.length > MAX_COMPARENDOS) val.error(`${raiz}.comparendos`, `Como mucho ${MAX_COMPARENDOS} comparendos.`);
  const vistos = new Set<string>();
  r.cantidad_comparendos = b.comparendos.length;
  r.comparendos = b.comparendos.map((c, i) => {
    const base = `${raiz}.comparendos[${i}]`;
    const o: Record<string, unknown> = {};
    val.texto(o, "numero", c.numero, `${base}.numero`, 60, true);
    const numero = c.numero.trim();
    if (numero && vistos.has(numero)) val.error(`${base}.numero`, "Este número de comparendo ya está en la lista.");
    vistos.add(numero);
    val.fecha(o, "fecha", c.fecha, `${base}.fecha`);
    const valor = c.valor.trim();
    if (valor) {
      if (!PESOS.test(valor)) val.error(`${base}.valor`, "Escribe el valor en pesos solo con dígitos (y hasta dos decimales tras un punto).");
      o.valor = valor;
    }
    val.texto(o, "estado", c.estado, `${base}.estado`, 60);
    val.texto(o, "organismo", c.organismo, `${base}.organismo`, 120);
    return o;
  });
  return r;
}

function avaluo(b: AvaluoBorrador, raiz: string, val: Validador): Record<string, unknown> {
  const r: Record<string, unknown> = { sin_valor_comercial: b.sin_valor_comercial };
  if (b.fuentes.length > MAX_FUENTES_AVALUO) val.error(`${raiz}.fuentes`, `Como mucho ${MAX_FUENTES_AVALUO} fuentes.`);
  const vistas = new Set<string>();
  const conValor = new Set<string>();
  const fuentes = b.fuentes.map((f, i) => {
    const base = `${raiz}.fuentes[${i}]`;
    const o: Record<string, unknown> = {};
    if (!f.fuente) val.error(`${base}.fuente`, "Elige la fuente.");
    else if (vistas.has(f.fuente)) val.error(`${base}.fuente`, "Esta fuente ya está en la lista.");
    else o.fuente = f.fuente;
    vistas.add(f.fuente);
    if (!f.estado) val.error(`${base}.estado`, "Elige el estado de la fuente.");
    else o.estado = f.estado;
    if (f.estado === "OK") {
      const valor = val.entero(f.valor, `${base}.valor`, true);
      if (valor !== null) o.valor = valor;
      if (f.fuente) conValor.add(f.fuente);
      if (b.sin_valor_comercial) val.error(`${base}.estado`, "Sin valor comercial, ninguna fuente puede tener valor.");
    } else if (f.valor.trim()) {
      val.error(`${base}.valor`, "El valor solo va con el estado «Con valor».");
    }
    const muestras = val.entero(f.muestras, `${base}.muestras`, false);
    if (muestras !== null) o.muestras = muestras;
    return o;
  });
  if (!b.sin_valor_comercial) {
    const sugerido = val.entero(b.valor_sugerido, `${raiz}.valor_sugerido`, true);
    if (sugerido !== null) r.valor_sugerido = sugerido;
    if (!b.fuente_principal) val.error(`${raiz}.fuente_principal`, "Elige la fuente principal.");
    else if (!conValor.has(b.fuente_principal)) val.error(`${raiz}.fuente_principal`, "La fuente principal debe ser una de las fuentes con valor.");
    else r.fuente_principal = b.fuente_principal;
  }
  r.fuentes = fuentes;
  return r;
}

function runtPersona(b: RuntPersonaBorrador, raiz: string, val: Validador): Record<string, unknown> {
  const r: Record<string, unknown> = { inscrito: b.inscrito };
  val.texto(r, "estado_conductor", b.estado_conductor, `${raiz}.estado_conductor`, 60);
  if (b.licencias.length > MAX_LICENCIAS) val.error(`${raiz}.licencias`, `Como mucho ${MAX_LICENCIAS} licencias.`);
  r.licencias = b.licencias.map((l, i) => {
    const base = `${raiz}.licencias[${i}]`;
    const o: Record<string, unknown> = {};
    val.texto(o, "categoria", l.categoria, `${base}.categoria`, 10, true);
    val.texto(o, "estado", l.estado, `${base}.estado`, 60);
    val.fecha(o, "vigente_hasta", l.vigente_hasta, `${base}.vigente_hasta`);
    return o;
  });
  return r;
}

function rues(b: RuesBorrador, raiz: string, val: Validador): Record<string, unknown> {
  const r: Record<string, unknown> = { encontrada: b.encontrada };
  val.texto(r, "estado_matricula", b.estado_matricula, `${raiz}.estado_matricula`, 60);
  val.fecha(r, "ultima_renovacion", b.ultima_renovacion, `${raiz}.ultima_renovacion`, true);
  return r;
}

function resultado(b: Borrador, raiz: string, val: Validador): Record<string, unknown> {
  switch (b.tipo) {
    case "SIMIT":
      return simit(b, raiz, val);
    case "AVALUO":
      return avaluo(b, raiz, val);
    case "RUNT_PERSONA":
      return runtPersona(b, raiz, val);
    case "RUES":
      return rues(b, raiz, val);
  }
}

/**
 * Cuerpo del PUT desde los borradores, o los errores por campo si algo está fuera del esquema (entonces no hay cuerpo y no
 * se manda nada). En las fuentes de persona van TODAS las personas en error, en el orden del formulario.
 */
export function construirCuerpo(
  formulario: FormularioManual,
  borradores: Record<string, Borrador>,
): { cuerpo: CuerpoLlenado | null; errores: ErroresCampo } {
  const val = new Validador();
  let cuerpo: CuerpoLlenado;
  if (formulario.personas) {
    cuerpo = {
      version: formulario.version,
      personas: formulario.personas.map((p, i) => ({
        persona_id: p.persona_id,
        resultado: resultado(borradores[p.persona_id] ?? borradorVacio(esquemaDe(formulario.fuente, p.consulta)), `personas[${i}].resultado`, val),
      })),
    };
  } else {
    cuerpo = {
      version: formulario.version,
      resultado: resultado(borradores[CLAVE_VEHICULO] ?? borradorVacio(esquemaDe(formulario.fuente)), "resultado", val),
    };
  }
  return Object.keys(val.errores).length > 0 ? { cuerpo: null, errores: val.errores } : { cuerpo, errores: {} };
}

/** id de DOM estable para el campo de una ruta del cuerpo. */
export function idDeCampo(campo: string): string {
  return `manual-${campo.replace(/[^a-zA-Z0-9]+/g, "-")}`;
}
