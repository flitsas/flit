// Selector de tipo de trámite, compartido por los filtros de reportes del organismo y de la empresa.
//
// Vive en `components/consultas` por la misma razón que `columns.ts` y `export.ts`: es la carpeta
// donde acaba lo que las dos consolas usan igual. Tenerlo una sola vez es lo que evita que un
// informe ofrezca «Blindaje» y el de al lado no.
//
// Por qué existe: ADR-0050 pasó de dos tipos de trámite a veintiuno repartidos en tres familias.
// Los filtros se quedaron ofreciendo las tres familias, así que un informe rotulaba «Matrículas»
// tanto una matrícula inicial como una de leasing. Poner los veintiuno en una lista plana resuelve
// eso y crea otro problema: quien consulta tiene que saberse de memoria a qué familia pertenece
// cada tipo. De ahí los dos niveles en un solo control.
import type { ProcedureFamily, ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";

/** Etiquetas de familia. En plural porque encabezan un grupo, no una fila. */
const FAMILIA_LABEL: Record<ProcedureFamily, string> = {
  MATRICULAS: "Matrículas",
  TRASPASO: "Traspasos",
  OTROS: "Otros trámites",
};

/**
 * Orden de presentación: el del negocio, no el alfabético. Matrículas y traspasos son el grueso de
 * la operación y «otros» es el cajón, así que va al final.
 */
const ORDEN: ProcedureFamily[] = ["MATRICULAS", "TRASPASO", "OTROS"];

export interface GrupoTiposTramite {
  familia: ProcedureFamily;
  label: string;
  tipos: { id: string; name: string }[];
}

/**
 * Un tipo de trámite con su familia, sea de donde sea.
 *
 * <p>El identificador es `string` y no el tipo del catálogo de FLIT a propósito: ICT tiene su PROPIO
 * catálogo —los tipos de transacción que manda la integración, numerados— y necesita agruparse
 * igual. Un `<select>` solo maneja strings, así que quien llame convierte y punto.</p>
 */
export interface TipoConFamilia {
  id: string;
  name: string;
  family: string | null | undefined;
}

/** Lo que el usuario eligió: una familia entera, un tipo concreto, o nada. */
export interface SeleccionTipoTramite {
  familia?: ProcedureFamily;
  tipoId?: string;
}

/**
 * Agrupa por familia cualquier catálogo de tipos. Una familia sin tipos no produce grupo: un
 * encabezado vacío ocupa sitio y no responde nada.
 */
export function agruparPorFamilia(tipos: TipoConFamilia[]): GrupoTiposTramite[] {
  // Una familia fuera del dominio se recoge en «otros» en vez de desaparecer: el tipo existe y hay
  // trámites suyos, así que tiene que poder filtrarse aunque su código venga sucio.
  const familiaDe = (valor: string | null | undefined): ProcedureFamily => {
    const code = (valor ?? "").trim().toUpperCase();
    return code === "MATRICULAS" || code === "TRASPASO" ? code : "OTROS";
  };

  return ORDEN.flatMap((familia) => {
    const delGrupo = tipos
      .filter((t) => familiaDe(t.family) === familia)
      .map((t) => ({ id: t.id, name: t.name }))
      .sort((a, b) => a.name.localeCompare(b.name, "es"));
    return delGrupo.length === 0
      ? []
      : [{ familia, label: FAMILIA_LABEL[familia], tipos: delGrupo }];
  });
}

/** Atajo para el catálogo de FLIT, que es el que consumen los reportes. */
export function agruparTiposTramite(tipos: ProcedureTypeSummary[]): GrupoTiposTramite[] {
  return agruparPorFamilia(tipos);
}

// Los dos niveles conviven en un `<select>`, que solo sabe de strings: el valor lleva delante de
// qué nivel se trata. Sin el prefijo habría que adivinar si «MATRICULAS» es un id o una familia.
const PREFIJO_FAMILIA = "fam:";
const PREFIJO_TIPO = "tipo:";

export function valorTipoTramite(seleccion: SeleccionTipoTramite): string {
  if (seleccion.tipoId) return `${PREFIJO_TIPO}${seleccion.tipoId}`;
  if (seleccion.familia) return `${PREFIJO_FAMILIA}${seleccion.familia}`;
  return "";
}

export function leerTipoTramite(valor: string): SeleccionTipoTramite {
  if (valor.startsWith(PREFIJO_TIPO)) return { tipoId: valor.slice(PREFIJO_TIPO.length) };
  if (valor.startsWith(PREFIJO_FAMILIA)) {
    return { familia: valor.slice(PREFIJO_FAMILIA.length) as ProcedureFamily };
  }
  return {};
}

export interface TipoTramiteSelectProps {
  grupos: GrupoTiposTramite[];
  value: SeleccionTipoTramite;
  onChange: (seleccion: SeleccionTipoTramite) => void;
  /** Clase del `<select>`, para que cada consola conserve su propio estilo de campo. */
  className?: string;
  id?: string;
  /**
   * Ofrecer «Toda la familia». Se apaga donde el backend solo sabe filtrar por un tipo concreto:
   * enseñar una opción que el servidor va a ignorar es peor que no enseñarla, porque el informe
   * saldría sin filtrar y con pinta de estar filtrado. Ahí los encabezados siguen agrupando.
   */
  permitirFamilia?: boolean;
}

export function TipoTramiteSelect({
  grupos,
  value,
  onChange,
  className,
  id,
  permitirFamilia = true,
}: TipoTramiteSelectProps) {
  return (
    <select
      id={id}
      className={className}
      aria-label="Tipo de trámite"
      value={valorTipoTramite(value)}
      onChange={(e) => onChange(leerTipoTramite(e.target.value))}
    >
      <option value="">Todos los tipos</option>
      {grupos.map((grupo) => (
        <optgroup key={grupo.familia} label={grupo.label}>
          {permitirFamilia && (
            <option value={`${PREFIJO_FAMILIA}${grupo.familia}`}>
              Toda la familia: {grupo.label}
            </option>
          )}
          {grupo.tipos.map((tipo) => (
            <option key={tipo.id} value={`${PREFIJO_TIPO}${tipo.id}`}>
              {tipo.name}
            </option>
          ))}
        </optgroup>
      ))}
    </select>
  );
}
