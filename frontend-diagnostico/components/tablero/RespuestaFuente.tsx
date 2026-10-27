import { useId, type ReactNode } from "react";
import {
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  FILA_TABLA_CLS,
} from "@/components/tablero/vistasDetalle/comun";

// Respuesta de la fuente (HU #14087 AC8): renderizador genérico y reutilizable de lo que devuelve el API de detalle de la celda
// (DTO cerrado, R-13/R-20). Pinta TODOS los campos tal como llegan: nombres verbatim (snake_case del JSON) y valores verbatim
// (null → «null», true/false, números sin formato, textos tal cual). Objeto → tabla campo/valor; array de objetos → tabla con
// una columna por clave (unión de claves, en orden de aparición); array de primitivos → lista; objeto anidado → subsección con
// el nombre del campo como título; array vacío → «[]». No omite, modifica ni inventa campos del contrato.
//
// Defensa en profundidad de R-20: el contrato nunca trae proveedor, transacción, origen, usuario ni departamento consultado; si
// por error llegara una de esas claves, no se pinta (ninguna clave del DTO cerrado coincide con este patrón).
const CLAVES_RESERVADAS_R20 = /proveedor|transacci|origen|usuario|departamento/i;

type Objeto = Record<string, unknown>;

const esObjeto = (v: unknown): v is Objeto => v !== null && typeof v === "object" && !Array.isArray(v);
const esPrimitivo = (v: unknown) => v === null || typeof v !== "object";
/** Va en una fila de la tabla campo/valor: primitivo, array vacío o array de primitivos. */
const vaEnFila = (v: unknown) => esPrimitivo(v) || (Array.isArray(v) && v.every(esPrimitivo));

const camposVisibles = (o: Objeto) => Object.entries(o).filter(([clave]) => !CLAVES_RESERVADAS_R20.test(clave));

/** Valor primitivo verbatim: null → «null», booleano → «true»/«false», número sin formato, texto tal cual. */
function textoPrimitivo(v: unknown): string {
  if (v === null) return "null";
  if (v === undefined) return "";
  return String(v);
}

const CAMPO_CLS = "px-3 py-2 align-top text-left font-mono text-xs font-medium text-[var(--nav-texto)] dark:text-slate-300";
const VALOR_CLS = `${CELDA_TABLA_CLS} whitespace-pre-wrap break-words font-mono`;
const CONTENEDOR_TABLA_CLS = "overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10";
const TITULO_CLS = "font-mono text-xs font-semibold text-flit-primary dark:text-white";

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
        <li key={i}>{textoPrimitivo(item)}</li>
      ))}
    </ul>
  );
}

/** Valor dentro de una celda de tabla: primitivo, «[]», lista o tabla anidada (sin perder campos). */
function Valor({ nombre, valor }: { nombre: string; valor: unknown }) {
  if (esPrimitivo(valor)) return <>{textoPrimitivo(valor)}</>;
  if (Array.isArray(valor)) {
    if (valor.length === 0) return <>[]</>;
    if (valor.every(esPrimitivo)) return <ListaPrimitivos items={valor} />;
    if (valor.every(esObjeto)) return <TablaArreglo nombre={nombre} items={valor} />;
    return <ListaMixta nombre={nombre} items={valor} />;
  }
  const campos = camposVisibles(valor as Objeto);
  return campos.length === 0 ? <>{"{}"}</> : <TablaCampos etiqueta={nombre} campos={campos} />;
}

function ListaMixta({ nombre, items }: { nombre: string; items: unknown[] }) {
  return (
    <ul className="flex flex-col gap-1">
      {items.map((item, i) => (
        <li key={i}>
          <Valor nombre={`${nombre}[${i}]`} valor={item} />
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
                {clave}
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

function TablaArreglo({ nombre, items }: { nombre: string; items: Objeto[] }) {
  const columnas: string[] = [];
  for (const item of items) for (const [clave] of camposVisibles(item)) if (!columnas.includes(clave)) columnas.push(clave);
  return (
    <div className={CONTENEDOR_TABLA_CLS}>
      <table aria-label={nombre} className="w-full border-collapse text-xs">
        <thead>
          <tr>
            {columnas.map((c) => (
              <th key={c} scope="col" className={`${CABECERA_TABLA_CLS} normal-case tracking-normal font-mono`} style={CABECERA_TABLA_ESTILO}>
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {items.map((item, i) => (
            <tr key={i} className={FILA_TABLA_CLS}>
              {columnas.map((c) => (
                <td key={c} className={VALOR_CLS}>
                  {Object.prototype.hasOwnProperty.call(item, c) ? <Valor nombre={c} valor={item[c]} /> : null}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Subseccion({ nombre, valor, nivel }: { nombre: string; valor: unknown; nivel: number }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2">
      <Titulo nivel={nivel} id={id}>
        {nombre}
      </Titulo>
      {esObjeto(valor) ? <Bloque etiqueta={nombre} datos={valor} nivel={nivel + 1} /> : <Valor nombre={nombre} valor={valor} />}
    </section>
  );
}

function Bloque({ etiqueta, datos, nivel }: { etiqueta: string; datos: Objeto; nivel: number }) {
  const campos = camposVisibles(datos);
  if (campos.length === 0) return <p className={VALOR_CLS}>{"{}"}</p>;
  const filas = campos.filter(([, v]) => vaEnFila(v));
  const secciones = campos.filter(([, v]) => !vaEnFila(v));
  return (
    <div className="flex flex-col gap-3">
      {filas.length > 0 && <TablaCampos etiqueta={etiqueta} campos={filas} />}
      {secciones.map(([clave, valor]) => (
        <Subseccion key={clave} nombre={clave} valor={valor} nivel={nivel} />
      ))}
    </div>
  );
}

export interface RespuestaFuenteProps {
  /** `resultado` del detalle de la celda (o `personas[i].resultado`), tal como llega del API. */
  datos: object;
  /** Nombre accesible de la tabla principal campo/valor. */
  etiqueta: string;
  /** Nivel de encabezado de las subsecciones (el diálogo es h2 y sus secciones h3: por defecto, h4). */
  nivel?: number;
}

/** Todos los campos de la respuesta de la fuente, verbatim (HU #14087 AC8). */
export function RespuestaFuente({ datos, etiqueta, nivel = 4 }: RespuestaFuenteProps) {
  return <Bloque etiqueta={etiqueta} datos={datos as Objeto} nivel={nivel} />;
}
