"use client";

import { useEffect, useId, useRef, useState } from "react";
import { ChevronDown, ChevronRight, SlidersHorizontal } from "lucide-react";
import { apiFetch } from "@/lib/api";
import {
  contarFiltros,
  etiquetasAplicadas,
  FILTROS_VACIOS,
  leerLista,
  NOMBRE_CAMPO,
  OPCIONES_LISTA,
  quitarEtiqueta,
  TEXTO_RESULTADO_DP,
  TEXTO_TRI,
  type CampoNegable,
  type ClaveEtiqueta,
  type FiltrosBandeja,
  type TriEstado,
} from "@/lib/filtros";
import type { Nit, Organismo, Pagina } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";
import { BOTON_PRIMARIO_CLS, controlCls, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";

/** El menú agrupado por tema que aprobó Samuel (tanda 4 de la Epic #13779). */
const GRUPOS: { titulo: string; campos: ClaveEtiqueta[] }[] = [
  { titulo: "Comparendo", campos: ["numeros", "organismos", "estadoProceso", "fuente", "cobroCoactivo", "activos"] },
  { titulo: "Vehículo y propietario", campos: ["placas", "nits"] },
  { titulo: "Fechas", campos: ["imposicion", "notificacion", "registro"] },
  { titulo: "Conductor y avisos", campos: ["conContraventor", "cambioContraventor", "ultimoAviso"] },
  { titulo: "Derecho de petición y pago", campos: ["conDp", "resultadoDp", "pagado"] },
];

const RANGOS = {
  imposicion: ["imposicionDesde", "imposicionHasta"],
  notificacion: ["notificacionDesde", "notificacionHasta"],
  registro: ["registroDesde", "registroHasta"],
} as const;

const OPCION_CLS = "flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-xs hover:bg-[#EFF6FF] dark:hover:bg-white/5";
const TITULO_GRUPO_CLS = "px-2 pb-1 pt-2 text-[10px] font-semibold uppercase tracking-wide text-[#5B6B82] dark:text-slate-400";

/**
 * HU #13868: el menú «Filtros» de la bandeja, de un nivel como el de Trámites: al abrirlo se ve qué se puede filtrar,
 * y elegir un campo cambia el mismo panel por su editor. Lo que se elige es un borrador: la bandeja solo consulta al
 * pulsar «Aplicar», y cerrar sin aplicar no cambia nada.
 */
export function MenuFiltros({
  aplicados,
  onAplicar,
  empresa,
  nombres,
  onNombres,
}: {
  aplicados: FiltrosBandeja;
  onAplicar: (f: FiltrosBandeja) => void;
  empresa: string | null;
  /** Textos de los NIT y organismos ya conocidos, para el resumen de cada campo. */
  nombres: Record<string, string>;
  onNombres: (n: Record<string, string>) => void;
}) {
  const [abierto, setAbierto] = useState(false);
  const [borrador, setBorrador] = useState<FiltrosBandeja>(aplicados);
  const [editando, setEditando] = useState<ClaveEtiqueta | null>(null);
  const boton = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const id = useId();
  const n = contarFiltros(aplicados);

  const cerrar = () => {
    setAbierto(false);
    setEditando(null);
  };
  const abrir = () => {
    setBorrador(aplicados);
    setAbierto(true);
  };

  useEffect(() => {
    if (!abierto) return;
    const tecla = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setAbierto(false);
        setEditando(null);
        boton.current?.focus();
      }
    };
    const fuera = (e: MouseEvent) => {
      const t = e.target as Node;
      if (panel.current?.contains(t) || boton.current?.contains(t)) return;
      setAbierto(false);
      setEditando(null);
    };
    document.addEventListener("keydown", tecla);
    document.addEventListener("mousedown", fuera);
    return () => {
      document.removeEventListener("keydown", tecla);
      document.removeEventListener("mousedown", fuera);
    };
  }, [abierto]);

  const resumen = (clave: ClaveEtiqueta) => {
    const e = etiquetasAplicadas(borrador, nombres).find((x) => x.clave === clave);
    if (!e || e.porDefecto) return null;
    return e.texto.startsWith(`${NOMBRE_CAMPO[clave]} `) ? e.texto.slice(NOMBRE_CAMPO[clave].length + 1) : e.texto;
  };

  const aplicar = () => {
    onAplicar(borrador);
    cerrar();
  };
  // «Empezar de cero» limpia solo lo del menú: la búsqueda, el estado legal y la búsqueda rápida tienen su propio control.
  const empezarDeCero = () => {
    onAplicar({ ...FILTROS_VACIOS, buscar: aplicados.buscar, estadoLegal: aplicados.estadoLegal, rapido: aplicados.rapido, juridico: aplicados.juridico, sinGestion: aplicados.sinGestion });
    cerrar();
  };

  return (
    <div className="relative">
      <button
        ref={boton}
        type="button"
        onClick={() => (abierto ? cerrar() : abrir())}
        aria-haspopup="dialog"
        aria-expanded={abierto}
        aria-controls={abierto ? id : undefined}
        className={controlCls(n > 0)}
      >
        <SlidersHorizontal className="h-3.5 w-3.5" aria-hidden />
        {n > 0 ? `Filtros (${n})` : "Filtros"}
        <ChevronDown className="h-3.5 w-3.5" aria-hidden />
      </button>
      {abierto && (
        <div
          ref={panel}
          id={id}
          role="dialog"
          aria-label="Filtros de la bandeja"
          className="absolute left-0 top-full z-40 mt-2 flex max-h-[min(34rem,calc(100dvh-19rem))] w-[24rem] max-w-[calc(100vw-2rem)] flex-col rounded-xl border border-[#DFE5ED] bg-white shadow-lg dark:border-white/10 dark:bg-[#162744]"
        >
          <div className="min-h-0 flex-1 overflow-y-auto p-3">
            {editando ? (
              <>
                <button type="button" onClick={() => setEditando(null)} className="mb-2 inline-flex items-center gap-1 text-xs font-semibold text-flit-brand hover:underline">
                  ← Volver a los filtros
                </button>
                <p className="mb-2 text-xs font-semibold">{NOMBRE_CAMPO[editando]}</p>
                <Editor key={editando} clave={editando} borrador={borrador} onCambio={setBorrador} empresa={empresa} nombres={nombres} onNombres={onNombres} />
                {editando !== "activos" && resumen(editando) && (
                  <button
                    type="button"
                    className="mt-3 text-xs font-semibold text-[#3B4FD6] hover:underline"
                    onClick={() => {
                      setBorrador(quitarEtiqueta(borrador, editando));
                      setEditando(null);
                    }}
                  >
                    Quitar este filtro
                  </button>
                )}
              </>
            ) : (
              GRUPOS.map((g) => (
                <div key={g.titulo}>
                  <p className={TITULO_GRUPO_CLS}>{g.titulo}</p>
                  {g.campos.map((clave) => {
                    const valor = resumen(clave);
                    return (
                      <button
                        key={clave}
                        type="button"
                        onClick={() => setEditando(clave)}
                        className="flex w-full items-center justify-between gap-2 rounded-lg px-2 py-1.5 text-left text-xs hover:bg-[#EFF6FF] dark:hover:bg-white/5"
                      >
                        <span className="min-w-0">
                          <span className="block font-medium">{NOMBRE_CAMPO[clave]}</span>
                          {valor && <span className="block truncate text-[11px] font-semibold text-[#3B4FD6] dark:text-[#8FA8FF]">{valor}</span>}
                        </span>
                        <ChevronRight className="h-3.5 w-3.5 shrink-0 opacity-50" aria-hidden />
                      </button>
                    );
                  })}
                </div>
              ))
            )}
          </div>
          <div className="flex shrink-0 items-center justify-end gap-2 border-t border-[#DFE5ED] p-3 dark:border-white/10">
            <button
              type="button"
              onClick={empezarDeCero}
              disabled={n === 0}
              className="rounded-xl border border-[#FF4E00] px-3 py-2 text-xs font-semibold text-[#C2410C] transition hover:bg-[#FF4E00]/10 disabled:cursor-not-allowed disabled:opacity-40"
            >
              Empezar de cero
            </button>
            <button type="button" onClick={aplicar} className={BOTON_PRIMARIO_CLS}>
              Aplicar
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

/** «es / no es» de los filtros de lista. */
function EsNoEs({ campo, borrador, onCambio }: { campo: CampoNegable; borrador: FiltrosBandeja; onCambio: (f: FiltrosBandeja) => void }) {
  const no = borrador.negar.includes(campo);
  const poner = (negar: boolean) => onCambio({ ...borrador, negar: negar ? [...borrador.negar.filter((c) => c !== campo), campo] : borrador.negar.filter((c) => c !== campo) });
  return (
    <div role="radiogroup" aria-label="Condición" className="mb-2 inline-flex rounded-lg border border-[#DFE5ED] p-0.5 dark:border-white/15">
      {[false, true].map((v) => (
        <button
          key={String(v)}
          type="button"
          role="radio"
          aria-checked={no === v}
          onClick={() => poner(v)}
          className={`rounded-md px-3 py-1 text-xs font-semibold transition ${no === v ? "bg-flit-brand text-white" : "text-[#5B6B82] hover:bg-[#EFF6FF] dark:text-slate-300 dark:hover:bg-white/5"}`}
        >
          {v ? "no es" : "es"}
        </button>
      ))}
    </div>
  );
}

function Casillas({ opciones, valores, onCambio }: { opciones: { valor: string; texto: string }[]; valores: string[]; onCambio: (v: string[]) => void }) {
  if (opciones.length === 0) return <p className="px-2 py-1 text-xs opacity-60">Sin opciones</p>;
  return (
    <div className="flex flex-col">
      {opciones.map((o) => (
        <label key={o.valor} className={OPCION_CLS}>
          <input
            type="checkbox"
            checked={valores.includes(o.valor)}
            onChange={() => onCambio(valores.includes(o.valor) ? valores.filter((v) => v !== o.valor) : [...valores, o.valor])}
          />
          <span className="truncate">{o.texto}</span>
        </label>
      ))}
    </div>
  );
}

function Opciones<T extends string>({ nombre, opciones, valor, onCambio }: { nombre: string; opciones: { valor: T; texto: string }[]; valor: T; onCambio: (v: T) => void }) {
  return (
    <div role="radiogroup" aria-label={nombre} className="flex flex-col">
      {opciones.map((o) => (
        <label key={o.valor} className={OPCION_CLS}>
          <input type="radio" name={nombre} checked={valor === o.valor} onChange={() => onCambio(o.valor)} />
          {o.texto}
        </label>
      ))}
    </div>
  );
}

function Editor({
  clave,
  borrador,
  onCambio,
  empresa,
  nombres,
  onNombres,
}: {
  clave: ClaveEtiqueta;
  borrador: FiltrosBandeja;
  onCambio: (f: FiltrosBandeja) => void;
  empresa: string | null;
  nombres: Record<string, string>;
  onNombres: (n: Record<string, string>) => void;
}) {
  switch (clave) {
    case "estadoProceso":
    case "ultimoAviso":
    case "cambioContraventor":
    case "fuente":
      return (
        <>
          <EsNoEs campo={clave} borrador={borrador} onCambio={onCambio} />
          <Casillas
            opciones={Object.entries(OPCIONES_LISTA[clave]).map(([valor, e]) => ({ valor, texto: e.texto }))}
            valores={borrador[clave]}
            onCambio={(v) => onCambio({ ...borrador, [clave]: v })}
          />
        </>
      );
    case "nits":
      return <EditorNits borrador={borrador} onCambio={onCambio} empresa={empresa} onNombres={onNombres} />;
    case "organismos":
      return <EditorOrganismos borrador={borrador} onCambio={onCambio} nombres={nombres} onNombres={onNombres} />;
    case "placas":
    case "numeros":
      return <EditorLista campo={clave} borrador={borrador} onCambio={onCambio} />;
    case "imposicion":
    case "notificacion":
    case "registro": {
      const [desde, hasta] = RANGOS[clave];
      return (
        <div className="grid grid-cols-2 gap-2">
          <label className="flex flex-col gap-1 text-[11px] font-semibold text-[#5B6B82] dark:text-slate-300">
            Desde
            <input type="date" className={INPUT_CLS} value={borrador[desde]} onChange={(e) => onCambio({ ...borrador, [desde]: e.target.value })} />
          </label>
          <label className="flex flex-col gap-1 text-[11px] font-semibold text-[#5B6B82] dark:text-slate-300">
            Hasta
            <input type="date" className={INPUT_CLS} value={borrador[hasta]} onChange={(e) => onCambio({ ...borrador, [hasta]: e.target.value })} />
          </label>
        </div>
      );
    }
    case "conContraventor":
    case "pagado":
    case "conDp":
    case "cobroCoactivo":
      return (
        <Opciones<TriEstado>
          nombre={NOMBRE_CAMPO[clave]}
          opciones={[
            { valor: "", texto: "Sin filtrar" },
            { valor: "true", texto: TEXTO_TRI[clave].si },
            { valor: "false", texto: TEXTO_TRI[clave].no },
          ]}
          valor={borrador[clave]}
          onCambio={(v) => onCambio({ ...borrador, [clave]: v })}
        />
      );
    case "resultadoDp":
      return (
        <Opciones<FiltrosBandeja["resultadoDp"]>
          nombre={NOMBRE_CAMPO.resultadoDp}
          opciones={[{ valor: "", texto: "Sin filtrar" }, ...(["Favorable", "Desfavorable", "SinRespuesta"] as const).map((v) => ({ valor: v, texto: TEXTO_RESULTADO_DP[v] }))]}
          valor={borrador.resultadoDp}
          onCambio={(v) => onCambio({ ...borrador, resultadoDp: v })}
        />
      );
    case "activos":
      return (
        <Opciones<FiltrosBandeja["activos"]>
          nombre={NOMBRE_CAMPO.activos}
          opciones={[
            { valor: "activos", texto: "Solo activos (por defecto)" },
            { valor: "inactivos", texto: "Solo inactivos" },
            { valor: "todos", texto: "Activos e inactivos" },
          ]}
          valor={borrador.activos}
          onCambio={(v) => onCambio({ ...borrador, activos: v })}
        />
      );
    default:
      return null;
  }
}

/** Placas o números: se escriben o se pegan desde Excel, uno por línea. */
function EditorLista({ campo, borrador, onCambio }: { campo: "placas" | "numeros"; borrador: FiltrosBandeja; onCambio: (f: FiltrosBandeja) => void }) {
  const [texto, setTexto] = useState(() => borrador[campo].join("\n"));
  const valores = leerLista(texto);
  return (
    <>
      <EsNoEs campo={campo} borrador={borrador} onCambio={onCambio} />
      <textarea
        className={`${TEXTAREA_CLS} h-32 font-mono`}
        value={texto}
        aria-label={NOMBRE_CAMPO[campo]}
        placeholder={campo === "placas" ? "ABC123\nXYZ987" : "11001000000051927483\n76001000000053340009"}
        onChange={(e) => {
          setTexto(e.target.value);
          onCambio({ ...borrador, [campo]: leerLista(e.target.value) });
        }}
      />
      <p className="mt-1 text-[11px] opacity-70">
        {valores.length === 0
          ? "Escribe o pega desde Excel, uno por línea. Busca el valor completo."
          : `${valores.length} ${campo === "placas" ? (valores.length === 1 ? "placa" : "placas") : valores.length === 1 ? "número" : "números"}`}
      </p>
    </>
  );
}

function EditorNits({ borrador, onCambio, empresa, onNombres }: { borrador: FiltrosBandeja; onCambio: (f: FiltrosBandeja) => void; empresa: string | null; onNombres: (n: Record<string, string>) => void }) {
  const [buscar, setBuscar] = useState("");
  const nits = useCarga(`nits:${empresa ?? ""}`, (signal) => apiFetch<Nit[]>("/monitoreo/nits", { signal, query: { empresa } }));
  const opciones = (nits.datos ?? []).map((x) => ({ valor: x.id, texto: `${x.numero}${x.nombreTitular ? ` · ${x.nombreTitular}` : ""}` }));
  const t = buscar.trim().toLowerCase();
  return (
    <>
      <EsNoEs campo="nits" borrador={borrador} onCambio={onCambio} />
      <input value={buscar} onChange={(e) => setBuscar(e.target.value)} placeholder="Buscar NIT o titular…" aria-label="Buscar NIT" className={`${INPUT_CLS} mb-2`} />
      {nits.estado === "loading" ? (
        <p className="px-2 py-1 text-xs opacity-60">Cargando…</p>
      ) : (
        <Casillas
          opciones={opciones.filter((o) => !t || o.texto.toLowerCase().includes(t))}
          valores={borrador.nits}
          onCambio={(v) => {
            onNombres(Object.fromEntries((nits.datos ?? []).map((x) => [x.id, x.numero])));
            onCambio({ ...borrador, nits: v });
          }}
        />
      )}
    </>
  );
}

function EditorOrganismos({ borrador, onCambio, nombres, onNombres }: { borrador: FiltrosBandeja; onCambio: (f: FiltrosBandeja) => void; nombres: Record<string, string>; onNombres: (n: Record<string, string>) => void }) {
  const [buscar, setBuscar] = useState("");
  const ots = useCarga(`ots:${buscar}`, (signal) => apiFetch<Pagina<Organismo>>("/organismos", { signal, query: { buscar, tamano: 30 } }));
  const encontrados = (ots.datos?.items ?? []).map((o) => ({ valor: o.id, texto: o.nombre }));
  // Los ya elegidos van primero aunque la búsqueda de ahora no los traiga.
  const elegidos = borrador.organismos.filter((id) => !encontrados.some((o) => o.valor === id)).map((id) => ({ valor: id, texto: nombres[id] ?? id }));
  return (
    <>
      <EsNoEs campo="organismos" borrador={borrador} onCambio={onCambio} />
      <input value={buscar} onChange={(e) => setBuscar(e.target.value)} placeholder="Buscar organismo…" aria-label="Buscar organismo" className={`${INPUT_CLS} mb-2`} />
      <Casillas
        opciones={[...elegidos, ...encontrados]}
        valores={borrador.organismos}
        onCambio={(v) => {
          onNombres(Object.fromEntries(encontrados.map((o) => [o.valor, o.texto])));
          onCambio({ ...borrador, organismos: v });
        }}
      />
    </>
  );
}
