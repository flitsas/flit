"use client";

import { createContext, memo, useCallback, useContext, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { ChevronRight } from "lucide-react";
import { FOCO_CLS } from "@/components/configuracion/estilos";
import {
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  FILA_TABLA_CLS,
} from "@/components/tablero/vistasDetalle/comun";
import { camposVisibles, etiquetaCampo, textoValor } from "@/lib/tablero/etiquetasRespuesta";

// Respuesta de la fuente (HU #14087, página del detalle en pestaña nueva): renderizador genérico de lo que devuelve el API de
// detalle de la celda (respuesta_fuente, proto-JSON del rpc, o el resultado resumido). La API no cambia; aquí se pintan SOLO
// los datos propios de la consulta y TODO en español (lib/tablero/etiquetasRespuesta): se oculta la auditoría del proveedor y
// los campos internos en cualquier nivel (proveedor, semaforo, desde_cache, consultado_en, respuesta_cruda, http_status,
// duracion_ms, errores, tipo_error, mensaje_error, serialVersionUID, id e id_*, `campos` de ResultadoConsulta y, en los
// chequeos, clave y fuente); el `resultado` objeto que envuelve a ResultadoConsulta se aplana y el `resultado` enum del RUNT
// Intempo se oculta. Nombres de campo con el diccionario (desconocidos humanizados); valores: enums en español, Sí/No, «—» para
// null o vacío, «Sin registros» para listas vacías; los textos de la fuente, tal cual. Objeto → tabla campo/valor; array de
// objetos → tabla con una columna por clave visible; array de primitivos → lista; objeto anidado → subsección. Las de primer
// nivel son plegables y abren por defecto, las de segundo nivel arrancan cerradas, cada lista lleva su número de elementos y
// hay un «Expandir todo / Contraer todo». El origen manual de la celda (R-20) no viaja en la respuesta.

type Objeto = Record<string, unknown>;

const esObjeto = (v: unknown): v is Objeto => v !== null && typeof v === "object" && !Array.isArray(v);
const esPrimitivo = (v: unknown) => v === null || typeof v !== "object";
/** Va en una fila de la tabla campo/valor: primitivo, array vacío o array de primitivos. */
const vaEnFila = (v: unknown) => esPrimitivo(v) || (Array.isArray(v) && v.every(esPrimitivo));

/** Los chequeos de ResultadoConsulta ocultan además su clave y su fuente. */
const CHEQUEOS = "chequeos";

/** Profundidades con subsección plegable: 0 (abierta por defecto) y 1 (cerrada). */
const PROFUNDIDAD_PLEGABLE = 1;

/** Avisa al contenedor de que un plegable cambió, para que el botón «Expandir/Contraer todo» diga lo correcto. */
const AlCambiarPlegable = createContext<() => void>(() => {});

const CAMPO_CLS = "px-3 py-2 align-top text-left text-xs font-medium text-[var(--nav-texto)] dark:text-slate-300";
const VALOR_CLS = `${CELDA_TABLA_CLS} whitespace-pre-wrap break-words`;
const CONTENEDOR_TABLA_CLS = "overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10";
const TITULO_CLS = "text-xs font-semibold text-flit-primary dark:text-white";
/** Botón secundario compacto (mismos tokens que BOTON_SECUNDARIO_CLS, a la escala de 12px del detalle). */
const BOTON_PLEGAR_CLS = `inline-flex items-center justify-center self-end rounded-full border border-[var(--color-flit-gray)] bg-white px-3 py-1 text-xs font-semibold text-flit-primary transition dark:border-white/15 dark:bg-transparent dark:text-white ${FOCO_CLS}`;
const SUMMARY_CLS = `flex cursor-pointer list-none items-center gap-1 rounded-lg py-0.5 [&::-webkit-details-marker]:hidden ${FOCO_CLS}`;

function Titulo({ nivel, id, children }: { nivel: number; id: string; children: ReactNode }) {
  const Etiqueta = `h${Math.min(Math.max(nivel, 2), 6)}` as "h2" | "h3" | "h4" | "h5" | "h6";
  return (
    <Etiqueta id={id} className={TITULO_CLS}>
      {children}
    </Etiqueta>
  );
}

function ListaPrimitivos({ items }: { items: unknown[] }) {
  return (
    <ul className="flex flex-col gap-0.5">
      {items.map((item, i) => (
        <li key={i}>{textoValor(item)}</li>
      ))}
    </ul>
  );
}

/** Valor dentro de una celda de tabla: primitivo, «Sin registros», lista o tabla anidada. `nombre` es la clave cruda. */
function Valor({ nombre, valor }: { nombre: string; valor: unknown }) {
  if (esPrimitivo(valor)) return <>{textoValor(valor)}</>;
  if (Array.isArray(valor)) {
    if (valor.length === 0) return <>{textoValor(valor)}</>;
    if (valor.every(esPrimitivo)) return <ListaPrimitivos items={valor} />;
    if (valor.every(esObjeto)) return <TablaArreglo nombre={nombre} items={valor} />;
    return <ListaMixta nombre={nombre} items={valor} />;
  }
  const campos = camposVisibles(valor as Objeto);
  return campos.length === 0 ? <>{textoValor({})}</> : <TablaCampos etiqueta={etiquetaCampo(nombre)} campos={campos} />;
}

function ListaMixta({ nombre, items }: { nombre: string; items: unknown[] }) {
  return (
    <ul className="flex flex-col gap-1">
      {items.map((item, i) => (
        <li key={i}>
          <Valor nombre={nombre} valor={item} />
        </li>
      ))}
    </ul>
  );
}

function TablaCampos({ etiqueta, campos }: { etiqueta: string; campos: [string, unknown][] }) {
  return (
    <div className={CONTENEDOR_TABLA_CLS}>
      <table aria-label={etiqueta} className="w-full border-collapse text-xs">
        <thead>
          <tr>
            <th scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
              Campo
            </th>
            <th scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
              Valor
            </th>
          </tr>
        </thead>
        <tbody>
          {campos.map(([clave, valor]) => (
            <tr key={clave} className={FILA_TABLA_CLS}>
              <th scope="row" className={CAMPO_CLS}>
                {etiquetaCampo(clave)}
              </th>
              <td className={VALOR_CLS}>
                <Valor nombre={clave} valor={valor} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Array de objetos → tabla con una columna por clave visible (unión, en orden de aparición). */
function TablaArreglo({ nombre, items }: { nombre: string; items: Objeto[] }) {
  const filas = items.map((item) => new Map(camposVisibles(item, nombre === CHEQUEOS)));
  const columnas: string[] = [];
  for (const fila of filas) for (const clave of fila.keys()) if (!columnas.includes(clave)) columnas.push(clave);
  return (
    <div className={CONTENEDOR_TABLA_CLS}>
      <table aria-label={etiquetaCampo(nombre)} className="w-full border-collapse text-xs">
        <thead>
          <tr>
            {columnas.map((c) => (
              <th key={c} scope="col" className={`${CABECERA_TABLA_CLS} normal-case tracking-normal`} style={CABECERA_TABLA_ESTILO}>
                {etiquetaCampo(c)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {filas.map((fila, i) => (
            <tr key={i} className={FILA_TABLA_CLS}>
              {columnas.map((c) => (
                <td key={c} className={VALOR_CLS}>
                  {fila.has(c) ? <Valor nombre={c} valor={fila.get(c)} /> : null}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Subseccion({ nombre, valor, nivel, profundidad }: { nombre: string; valor: unknown; nivel: number; profundidad: number }) {
  const id = useId();
  const alCambiar = useContext(AlCambiarPlegable);
  const etiqueta = etiquetaCampo(nombre);
  const titulo = Array.isArray(valor) ? `${etiqueta} (${valor.length})` : etiqueta;
  const contenido = esObjeto(valor) ? (
    <Bloque etiqueta={etiqueta} datos={valor} nivel={nivel + 1} profundidad={profundidad + 1} />
  ) : (
    <Valor nombre={nombre} valor={valor} />
  );
  if (profundidad > PROFUNDIDAD_PLEGABLE) {
    return (
      <section aria-labelledby={id} className="flex flex-col gap-2">
        <Titulo nivel={nivel} id={id}>
          {titulo}
        </Titulo>
        {contenido}
      </section>
    );
  }
  return (
    <section aria-labelledby={id}>
      <details open={profundidad === 0} onToggle={alCambiar} className="group flex flex-col">
        <summary className={SUMMARY_CLS}>
          <ChevronRight className="h-3.5 w-3.5 shrink-0 text-flit-primary transition-transform group-open:rotate-90 dark:text-white" aria-hidden="true" />
          <Titulo nivel={nivel} id={id}>
            {titulo}
          </Titulo>
        </summary>
        <div className="mt-2 flex flex-col gap-2">{contenido}</div>
      </details>
    </section>
  );
}

function Bloque({ etiqueta, datos, nivel, profundidad }: { etiqueta: string; datos: Objeto; nivel: number; profundidad: number }) {
  const campos = camposVisibles(datos);
  if (campos.length === 0) return <p className={VALOR_CLS}>{textoValor({})}</p>;
  const filas = campos.filter(([, v]) => vaEnFila(v));
  const secciones = campos.filter(([, v]) => !vaEnFila(v));
  return (
    <div className="flex flex-col gap-3">
      {filas.length > 0 && <TablaCampos etiqueta={etiqueta} campos={filas} />}
      {secciones.map(([clave, valor]) => (
        <Subseccion key={clave} nombre={clave} valor={valor} nivel={nivel} profundidad={profundidad} />
      ))}
    </div>
  );
}

export interface RespuestaFuenteProps {
  /** `respuesta_fuente` del detalle de la celda (o de la persona) o, si no la hay, su `resultado`, tal como llega del API. */
  datos: object;
  /** Nombre accesible de la tabla principal campo/valor. */
  etiqueta: string;
  /** Nivel de encabezado de las subsecciones (la página es h1 y sus secciones h2: por defecto, h3). */
  nivel?: number;
}

/**
 * Los datos de la consulta en la respuesta de la fuente, en español (HU #14087). El árbol se memoiza: abrir o cerrar
 * plegables, o pulsar «Expandir/Contraer todo», no lo vuelve a construir (los <details> se abren y cierran en el DOM).
 */
export const RespuestaFuente = memo(function RespuestaFuente({ datos, etiqueta, nivel = 3 }: RespuestaFuenteProps) {
  const contenedor = useRef<HTMLDivElement>(null);
  const [todoAbierto, setTodoAbierto] = useState(false);
  const hayPlegables = useMemo(() => camposVisibles(datos as Objeto).some(([, v]) => !vaEnFila(v)), [datos]);
  const plegables = () => Array.from(contenedor.current?.querySelectorAll("details") ?? []);
  const recalcular = useCallback(() => {
    const lista = Array.from(contenedor.current?.querySelectorAll("details") ?? []);
    setTodoAbierto(lista.length > 0 && lista.every((d) => d.open));
  }, []);
  const arbol = useMemo(() => <Bloque etiqueta={etiqueta} datos={datos as Objeto} nivel={nivel} profundidad={0} />, [datos, etiqueta, nivel]);

  function alternarTodo() {
    const abrir = !todoAbierto;
    for (const d of plegables()) d.open = abrir;
    setTodoAbierto(abrir);
  }

  return (
    <div ref={contenedor} className="flex flex-col gap-3">
      {hayPlegables && (
        <button type="button" onClick={alternarTodo} className={BOTON_PLEGAR_CLS}>
          {todoAbierto ? "Contraer todo" : "Expandir todo"}
        </button>
      )}
      <AlCambiarPlegable.Provider value={recalcular}>{arbol}</AlCambiarPlegable.Provider>
    </div>
  );
});
