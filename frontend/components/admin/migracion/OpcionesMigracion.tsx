"use client";

import { AlertTriangle } from "lucide-react";
import { DESCRIPCION_INSTANCIA, INSTANCIAS, type Instancia } from "@/lib/migracion/types";

/**
 * Las opciones que comparten la migración de uno y la masiva: qué instancias correr y si es
 * simulación. Un solo componente para que las dos pantallas no puedan ofrecer opciones distintas.
 *
 * Una selección VACÍA significa «las tres», que es exactamente lo que hace el host cuando no se le
 * manda el parámetro. Se dice con todas las letras en la interfaz en vez de dejar que quien opera
 * lo deduzca de que no hay ninguna casilla marcada.
 */
export function OpcionesMigracion({
  instancias,
  onInstancias,
  dryRun,
  onDryRun,
  deshabilitado = false,
  disposicion = "columna",
}: {
  instancias: Instancia[];
  onInstancias: (valor: Instancia[]) => void;
  dryRun: boolean;
  onDryRun: (valor: boolean) => void;
  deshabilitado?: boolean;
  /**
   * `columna` para el panel estrecho de «Un trámite»; `fila` para la barra a todo el ancho del
   * cargue masivo, donde apilarlo dejaba una columna altísima al lado de una tarjeta corta y el
   * hueco resultante se leía como si la página estuviera rota.
   */
  disposicion?: "columna" | "fila";
}) {
  const enFila = disposicion === "fila";
  /**
   * Lo que está marcado DE VERDAD. La lista vacía significa «las tres» —es lo que hace el host sin
   * el parámetro— y las tres casillas se pintan marcadas, así que hay que expandirla antes de
   * tocar nada.
   *
   * Sin expandir, alternar hacía lo contrario de lo que se veía: con la lista vacía, un clic sobre
   * Documentos —que se veía MARCADO— no lo encontraba en la lista, así que lo AÑADÍA y dejaba solo
   * esa instancia. Desmarcar una acababa desmarcando las otras dos. Visto usando la consola.
   */
  const marcadas: readonly Instancia[] = instancias.length === 0 ? INSTANCIAS : instancias;
  // No se puede correr ninguna instancia, y con la lista vacía queriendo decir «las tres», quitar
  // la última volvería a marcarlas todas. Se bloquea la casilla en vez de hacer eso.
  const ultima = marcadas.length === 1;

  const alternar = (instancia: Instancia) => {
    onInstancias(
      marcadas.includes(instancia)
        ? marcadas.filter((i) => i !== instancia)
        : // Se reordena al orden canónico para que la interfaz muestre lo mismo que va a correr.
          INSTANCIAS.filter((i) => i === instancia || marcadas.includes(i)),
    );
  };

  const todas = marcadas.length === INSTANCIAS.length;

  // Adjuntos y documentos SE CUELGAN de la data plana: el motor los busca en `migration_map` y, si
  // el trámite no está, responde `NotMigrated` con problemas. En una simulación la data plana no se
  // escribe, así que sobre un trámite todavía sin migrar esas dos instancias fallan SIEMPRE — y el
  // lote entero se pinta de rojo sin que nada esté mal. Verificado contra el host real con el
  // traspaso 26350. Se avisa antes en vez de decidir por quien opera: simular los adjuntos de un
  // trámite que YA está migrado es legítimo y funciona.
  const dependeDeDatos = marcadas.some((i) => i === "adjuntos" || i === "documentos");

  return (
    <div
      className={
        enFila
          ? "grid grid-cols-1 gap-x-8 gap-y-4 lg:grid-cols-[minmax(0,1fr)_auto]"
          : "flex flex-col gap-4"
      }
    >
      {/*
        Los dos bloques van con la MISMA estructura —leyenda, tarjetas, explicación— y por eso la
        explicación va debajo y no encima. Cuando «Qué migrar» la llevaba arriba y el modo la
        llevaba abajo, uno empujaba sus tarjetas un renglón y el otro no: puestos lado a lado en la
        barra ancha, las dos filas de tarjetas quedaban desalineadas sin motivo visible. Así se
        alinean solas, y además ninguna explicación puede descuadrar nada al ocupar dos líneas.
      */}
      <fieldset className="flex flex-col gap-2" disabled={deshabilitado}>
        <legend className="text-xs font-semibold uppercase tracking-wide opacity-60">
          Qué migrar
        </legend>

        <div className={enFila ? "grid gap-2 sm:grid-cols-3" : "flex flex-col gap-2"}>
          {INSTANCIAS.map((instancia) => (
            <label
              key={instancia}
              className="flex cursor-pointer items-start gap-2 rounded-lg border border-[#DFE5ED] p-2.5 dark:border-white/10"
            >
              <input
                type="checkbox"
                className="mt-0.5 h-4 w-4 shrink-0 accent-[#557EFF]"
                checked={marcadas.includes(instancia)}
                disabled={ultima && marcadas.includes(instancia)}
                onChange={() => alternar(instancia)}
              />
              <span className="min-w-0">
                <span className="block text-sm font-medium capitalize">{instancia}</span>
                <span className="block text-xs opacity-70">
                  {DESCRIPCION_INSTANCIA[instancia]}
                </span>
              </span>
            </label>
          ))}
        </div>

        <p className="text-xs opacity-70">
          {todas
            ? "Se correrán las tres instancias, en su orden obligatorio."
            : "Solo las marcadas. Recuerda que los adjuntos y los documentos necesitan que los datos ya existan."}
        </p>
      </fieldset>

      <ModoEjecucion
        dryRun={dryRun}
        onDryRun={onDryRun}
        deshabilitado={deshabilitado}
        anchoFijo={enFila}
        avisarDependencia={dryRun && dependeDeDatos}
      />
    </div>
  );
}

/**
 * El interruptor que separa «no pasa nada» de «esto escribe en producción».
 *
 * Va FUERA del bloque «Qué migrar» y con otra forma —dos botones, no una casilla— porque cuando era
 * una casilla más, con el mismo aspecto que Datos/Adjuntos/Documentos, se leía como una cuarta cosa
 * que migrar. Siendo la única opción de esta pantalla con consecuencias irreversibles, tiene que
 * ser imposible confundirla, y el modo activo tiene que verse sin buscarlo.
 */
function ModoEjecucion({
  dryRun,
  onDryRun,
  deshabilitado,
  anchoFijo,
  avisarDependencia,
}: {
  dryRun: boolean;
  onDryRun: (valor: boolean) => void;
  deshabilitado: boolean;
  anchoFijo: boolean;
  avisarDependencia: boolean;
}) {
  return (
    <fieldset
      // En la barra ancha no debe estirarse a media pantalla: son dos botones, y a 700 px de ancho
      // parecen dos paneles. Con ancho fijo quedan del tamaño de lo que dicen.
      className={`flex flex-col gap-2 ${anchoFijo ? "lg:w-[19rem]" : ""}`}
      disabled={deshabilitado}
    >
      <legend className="text-xs font-semibold uppercase tracking-wide opacity-60">
        Modo de ejecución
      </legend>

      <div
        role="radiogroup"
        aria-label="Modo de ejecución"
        className="grid grid-cols-2 gap-2"
      >
        <Modo
          activo={dryRun}
          onClick={() => onDryRun(true)}
          deshabilitado={deshabilitado}
          titulo="Simulación"
          detalle="No escribe nada"
          clasesActivo="border-[#557EFF] bg-[#557EFF]/10 text-[#557EFF]"
        />
        {/*
          «Migración» y no «Migrar de verdad»: el título largo partía en dos renglones y descuadraba
          las dos tarjetas. El par de sustantivos cabe en una línea cada uno, y lo que de verdad
          avisa —«Escribe en V2», el ámbar y el párrafo de abajo— no depende del título.
        */}
        <Modo
          activo={!dryRun}
          onClick={() => onDryRun(false)}
          deshabilitado={deshabilitado}
          titulo="Migración"
          detalle="Escribe en V2"
          clasesActivo="border-amber-500 bg-amber-500/10 text-amber-600 dark:text-amber-400"
        />
      </div>

      <p className="text-xs opacity-70">
        {dryRun
          ? "Lee todo y reporta qué haría, sin crear nada."
          : "Los trámites quedarán creados en V2. Reintentar sigue siendo seguro: no se duplican."}
      </p>

      {avisarDependencia && (
        <p className="flex items-start gap-1.5 text-xs text-amber-600 dark:text-amber-400">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          <span>
            Simulando, los adjuntos y los documentos solo se pueden comprobar en trámites que{" "}
            <strong className="font-semibold">ya estén migrados</strong>: se cuelgan de la data
            plana, y en simulación esa no se escribe. En los demás saldrán en rojo como «Sin
            migrar». No es un fallo. Para una primera pasada, simula solo «Datos».
          </span>
        </p>
      )}
    </fieldset>
  );
}

function Modo({
  activo,
  onClick,
  deshabilitado,
  titulo,
  detalle,
  clasesActivo,
}: {
  activo: boolean;
  onClick: () => void;
  deshabilitado: boolean;
  titulo: string;
  detalle: string;
  clasesActivo: string;
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={activo}
      onClick={onClick}
      disabled={deshabilitado}
      className={`flex flex-col items-start gap-1 rounded-lg border p-2.5 text-left transition-colors disabled:opacity-50 ${
        activo ? clasesActivo : "border-[#DFE5ED] opacity-70 dark:border-white/10"
      }`}
    >
      <span className="text-sm font-semibold">{titulo}</span>
      <span className="text-xs opacity-80">{detalle}</span>
    </button>
  );
}
