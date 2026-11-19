"use client";

// Renderer único de las declaraciones del tipo (color/combustible/carrocería nuevos, nivel de blindaje, causal de cancelación,
// trámites simultáneos…). No conoce ningún tipo: pinta cada `DeclaracionTipo` de la definición según su `control` y, por cada
// valor elegido, los soportes que esa opción pide. El estado y el guardado viven en FormularioRequisitos; este componente
// solo dibuja. Lo monta ese paso, así que el canal del punto lo hereda sin montaje propio.
import { AlertTriangle } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Drop, type EstadoArchivo } from "@/components/tramite/Drop";
import { Btn, Card, Field } from "@/components/tramite/piezas";
import { SelectorBusqueda } from "@/components/tramite/SelectorBusqueda";
import type { Adjunto, ClientePortal, DeclaracionTipo, DefinicionTipo } from "@/lib/api/cliente";
import { mensajeDe } from "@/lib/api/errores";
import { soportesDe, valorActualEnRunt, type SoporteDeclaracion, type ValoresRunt } from "@/lib/tramite/declaraciones";
import { adjuntoDe, AYUDA_ARCHIVOS } from "@/lib/tramite/requisitos";

const CINCO_MINUTOS = 5 * 60 * 1000;
const ESPERA_BUSQUEDA_MS = 300;

export interface SubidaSoporte {
  estado: "cargando" | "error";
  nombre: string;
  error?: string;
  archivo?: File;
}

interface Props {
  cliente: ClientePortal;
  definicion: DefinicionTipo;
  /** Las declaraciones que se piden ahora (ya filtradas). */
  declaraciones: readonly DeclaracionTipo[];
  campos: Readonly<Record<string, string>>;
  /** Lo que consta hoy en el RUNT; se informa bajo el selector y el valor nuevo no puede ser el mismo. */
  valoresRunt?: ValoresRunt | null | undefined;
  /** Borrador: las carrocerías las filtra el servidor por la clase RUNT de su vehículo. */
  solicitudId: string;
  adjuntos: readonly Adjunto[];
  subidas: Readonly<Record<string, SubidaSoporte>>;
  intentoAvance: number;
  /** Error a mostrar bajo una llave (`declaracion.<clave>`); ya decide si es momento de mostrarlo. */
  error: (llave: string) => string | undefined;
  /** Error de un soporte faltante (`documento.<docCode>`), solo tras intentar avanzar. */
  errorSoporte: (docCode: string) => string | undefined;
  onCambiar: (declaracion: DeclaracionTipo, valor: string) => void;
  onTocar: (llave: string) => void;
  onSubir: (soporte: { docCode: string }, archivo: File) => void;
  onQuitar: (soporte: { docCode: string }) => void;
}

export function DeclaracionesTipo({ cliente, definicion, declaraciones, campos, valoresRunt, solicitudId, adjuntos, subidas, error, errorSoporte, onCambiar, onTocar, onSubir, onQuitar }: Props) {
  if (declaraciones.length === 0) return null;
  const soportes = declaraciones.flatMap((decl) => soportesDe(decl, (campos[decl.fieldKey] ?? "").trim(), definicion).map((s) => ({ decl, s })));
  return (
    <Card title="Declaraciones del trámite" id="requisitos-declaraciones">
      <div className="grid gap-4 md:grid-cols-3">
        {declaraciones.map((decl) => (
          <ControlDeclaracion
            key={decl.clave}
            cliente={cliente}
            decl={decl}
            solicitudId={solicitudId}
            valor={campos[decl.fieldKey] ?? ""}
            ayuda={valorActualEnRunt(decl, valoresRunt)?.texto}
            error={error(`declaracion.${decl.clave}`)}
            onCambiar={(v) => onCambiar(decl, v)}
            onTocar={() => onTocar(`declaracion.${decl.clave}`)}
          />
        ))}
      </div>
      {soportes.length > 0 && (
        <div className="mt-6 grid gap-4 md:grid-cols-3" aria-label="Soportes de las declaraciones" role="group">
          {soportes.map(({ decl, s }) => (
            <Soporte key={`${decl.clave}:${s.docCode}`} decl={decl} soporte={s} valor={campos[decl.fieldKey] ?? ""} adjuntos={adjuntos} subida={subidas[s.docCode]} error={errorSoporte(s.docCode)} onSubir={onSubir} onQuitar={onQuitar} />
          ))}
        </div>
      )}
    </Card>
  );
}

export function Soporte({
  decl,
  soporte,
  valor,
  adjuntos,
  subida,
  error,
  onSubir,
  onQuitar,
}: {
  decl: DeclaracionTipo;
  soporte: SoporteDeclaracion;
  valor: string;
  adjuntos: readonly Adjunto[];
  subida: SubidaSoporte | undefined;
  error: string | undefined;
  onSubir: Props["onSubir"];
  onQuitar: Props["onQuitar"];
}) {
  const cargado = adjuntoDe(adjuntos, soporte.docCode);
  const estado: EstadoArchivo = subida?.estado === "cargando" ? "cargando" : subida?.estado === "error" && !cargado ? "error" : cargado ? "ok" : "vacio";
  const elegida = decl.opciones.find((o) => o.codigo === valor)?.etiqueta;
  const hint = [`Respalda: ${decl.etiqueta}${elegida ? ` (${elegida})` : ""}`, AYUDA_ARCHIVOS].join(". ");
  return (
    <div className="space-y-2">
      <Drop
        title={`${soporte.nombre}${soporte.obligatorio ? " *" : ""}`}
        hint={hint}
        optional={!soporte.obligatorio}
        estado={estado}
        nombre={subida?.nombre ?? cargado?.nombreArchivo}
        error={subida?.estado === "error" ? subida.error : error}
        onFile={(archivo) => onSubir(soporte, archivo)}
        onQuitar={cargado ? () => onQuitar(soporte) : undefined}
      />
      {subida?.estado === "error" && subida.archivo && (
        <Btn tone="secondary" onClick={() => onSubir(soporte, subida.archivo as File)}>
          Reintentar subida
        </Btn>
      )}
    </div>
  );
}

function AyudaRunt({ texto }: { texto: string | undefined }) {
  return texto ? <p className="mt-1 text-xs font-light text-muted-foreground">{texto}</p> : null;
}

export function ControlDeclaracion({ cliente, decl, solicitudId, valor, ayuda, error, requerida, onCambiar, onTocar }: { requerida?: boolean; cliente: ClientePortal; decl: DeclaracionTipo; solicitudId: string; valor: string; ayuda: string | undefined; error: string | undefined; onCambiar: (v: string) => void; onTocar: () => void }) {
  const etiqueta = `${decl.etiqueta}${decl.obligatoria || requerida ? " *" : ""}`;
  // Con opciones en línea (blindaje, causal, combustibles) no hace falta el catálogo; sin ellas, las opciones vienen del servidor.
  if (decl.control === "seleccion_catalogo" && decl.catalogo !== "" && decl.opciones.length === 0) {
    return <ControlCatalogo cliente={cliente} decl={decl} solicitudId={solicitudId} etiqueta={etiqueta} valor={valor} ayuda={ayuda} error={error} onCambiar={onCambiar} onTocar={onTocar} />;
  }
  return (
    <div>
      <Field label={etiqueta} error={error}>
        <select className="field" aria-invalid={error ? true : undefined} value={valor} onChange={(e) => onCambiar(e.target.value)} onBlur={onTocar}>
          <option value="">Selecciona</option>
          {decl.opciones.map((o) => (
            <option key={o.codigo} value={o.codigo}>
              {o.etiqueta}
            </option>
          ))}
        </select>
      </Field>
      <AyudaRunt texto={ayuda} />
    </div>
  );
}

/** Opciones de un catálogo del vehículo (colores, combustibles, carrocerías) con búsqueda y los cuatro estados. */
function ControlCatalogo({ cliente, decl, solicitudId, etiqueta, valor, ayuda, error, onCambiar, onTocar }: { cliente: ClientePortal; decl: DeclaracionTipo; solicitudId: string; etiqueta: string; valor: string; ayuda: string | undefined; error: string | undefined; onCambiar: (v: string) => void; onTocar: () => void }) {
  // El catálogo llega por pedazos (hasta 50): la búsqueda por texto la hace el servidor, sin una petición por tecla.
  const [texto, setTexto] = useState("");
  const [busqueda, setBusqueda] = useState("");
  useEffect(() => {
    const id = window.setTimeout(() => setBusqueda(texto.trim()), ESPERA_BUSQUEDA_MS);
    return () => window.clearTimeout(id);
  }, [texto]);
  const porClase = decl.catalogo === "carrocerias";
  const catalogo = useQuery({
    queryKey: ["pn", "catalogo-vehiculo", decl.catalogo, porClase ? solicitudId : null, busqueda],
    queryFn: ({ signal }) => cliente.catalogos.catalogoVehiculo(decl.catalogo, { texto: busqueda, ...(porClase ? { solicitudId } : {}) }, { signal }),
    staleTime: CINCO_MINUTOS,
    placeholderData: (previa) => previa,
  });
  const opciones = (catalogo.data ?? []).map((o) => ({ codigo: o.codigo, etiqueta: o.etiqueta }));
  const vacio = catalogo.isSuccess && opciones.length === 0 && busqueda === "";
  return (
    <div aria-busy={catalogo.isPending}>
      <Field label={etiqueta} error={error}>
        <SelectorBusqueda
          etiqueta={decl.etiqueta}
          opciones={opciones}
          valor={valor}
          etiquetaElegida={valor}
          invalido={Boolean(error)}
          disabled={(!catalogo.isSuccess && !catalogo.isPlaceholderData) || vacio}
          cargando={catalogo.isFetching}
          onBuscar={setTexto}
          marcador={catalogo.isPending ? "Cargando..." : "Selecciona"}
          onChange={(o) => onCambiar(o.codigo)}
          onBlur={onTocar}
        />
      </Field>
      <AyudaRunt texto={ayuda} />
      <div aria-live="polite" className="mt-1 text-xs">
        {catalogo.isError && (
          <p role="alert" className="flex flex-wrap items-center gap-2 text-alert">
            <AlertTriangle aria-hidden className="h-3.5 w-3.5" />
            {mensajeDe(catalogo.error)}
            <Btn tone="secondary" onClick={() => void catalogo.refetch()}>
              Reintentar
            </Btn>
          </p>
        )}
        {vacio && (
          <p className="font-light text-muted-foreground">
            {porClase ? "No hay carrocerías disponibles para la clase de este vehículo." : "No hay opciones disponibles por ahora."}
          </p>
        )}
      </div>
    </div>
  );
}
