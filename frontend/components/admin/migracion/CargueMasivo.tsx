"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Loader2, Play, RotateCcw, Square } from "lucide-react";
import { descargarPlantilla, leerArchivo, type FilaInvalida } from "@/lib/migracion/archivo";
import { consultarEstado, migrarTramite } from "@/lib/migracion/client";
import {
  borrarLote,
  cargarLote,
  clasificar,
  estaTerminada,
  guardarLote,
  nuevoLote,
  type FilaLote,
  type Lote,
} from "@/lib/migracion/progreso";
import { claveFila, type Instancia, type TipoTramite } from "@/lib/migracion/types";
import { OpcionesMigracion } from "./OpcionesMigracion";
import { TablaLote } from "./TablaLote";
import { AvisoError } from "./AvisoError";

/**
 * Migración masiva: cargar un archivo, revisar lo que trae, elegir qué filas migrar y verlas
 * avanzar.
 *
 * Dos decisiones que gobiernan todo lo demás:
 *
 * 1. **La cola es SECUENCIAL.** El host solo admite dos migraciones a la vez y responde 429 a la
 *    tercera; lanzar veinte en paralelo produciría dieciocho errores que no son errores. De paso,
 *    de a una se ve avanzar de verdad.
 *
 * 2. **El progreso se guarda en cada paso, no al final.** Es lo que hace que un F5 a mitad de una
 *    ola no pierda nada. Y al cargar se RECONCILIA contra la libreta del servidor, porque lo
 *    guardado en el navegador es una creencia: si la conexión se cortó con una migración en vuelo,
 *    esa migración terminó en el servidor y aquí figuraría como pendiente.
 */
export function CargueMasivo() {
  const [lote, setLote] = useState<Lote | null>(null);
  const [invalidas, setInvalidas] = useState<FilaInvalida[]>([]);
  const [seleccion, setSeleccion] = useState<Set<string>>(new Set());
  const [instancias, setInstancias] = useState<Instancia[]>([]);
  const [dryRun, setDryRun] = useState(true);
  const [corriendo, setCorriendo] = useState(false);
  // Null = no se ha recuperado nada; true/false = si lo recuperado se pudo contrastar contra el
  // servidor. Se muestra en pantalla porque un lote sin confirmar puede estar desactualizado.
  const [reconciliado, setReconciliado] = useState<boolean | null>(null);
  const [error, setError] = useState<Error | null>(null);

  // Una ref y no un estado: la lee el bucle de migración, que no se vuelve a crear en cada render.
  // Con un estado, el bucle leería para siempre el valor que tenía al arrancar y el botón de
  // detener no haría nada.
  const detener = useRef(false);
  const entradaArchivo = useRef<HTMLInputElement>(null);

  // La restauración del arranque es ASÍNCRONA (consulta al servidor), así que puede terminar
  // después de que alguien haya cargado un archivo nuevo. Sin esta bandera, al volver pisaría el
  // lote recién cargado con el guardado, y —más traicionero— dejaría la selección del lote viejo
  // sobre las filas del nuevo: el contador diría «3 en el archivo · 1 por migrar» sin explicación.
  const loteReemplazado = useRef(false);

  const persistir = useCallback((siguiente: Lote) => {
    setLote(siguiente);
    guardarLote(siguiente);
  }, []);

  /**
   * Al montar: recuperar lo guardado y contrastarlo con la libreta del servidor ANTES de pintarlo.
   *
   * El orden importa. Mostrar primero lo guardado y corregirlo después haría que quien vuelve viera
   * durante un segundo filas en «pendiente» que el servidor ya tiene migradas; si le da a migrar en
   * ese segundo, encola trabajo que no hace falta. Se pinta una sola vez, ya reconciliado.
   *
   * Y por eso ninguna llamada a `setState` ocurre antes del primer `await`: lo que se lee de
   * localStorage no es estado de React hasta que se ha confirmado.
   */
  useEffect(() => {
    void restaurar();

    async function restaurar() {
      const guardado = cargarLote();
      if (!guardado) {
        return;
      }

      try {
        const migrados = await consultarMigrados(guardado);

        const filas = guardado.filas.map((fila): FilaLote => {
          const yaEsta = migrados.has(claveFila(fila));

          // Solo se CORRIGE hacia arriba: una fila que el servidor dice migrada y aquí figura
          // pendiente pasa a "ya estaba". Nunca al revés — si el servidor no la tiene y aquí
          // constaba como migrada, lo más probable es que fuera una simulación, y degradarla
          // borraría el reporte que quien opera tiene delante.
          if (yaEsta && !estaTerminada(fila)) {
            return { ...fila, estado: "ya_estaba" };
          }

          // "en_curso" al cargar es siempre mentira: no hay ninguna petición en vuelo en una
          // página que acaba de montarse. O terminó (y la rama de arriba ya lo arregló) o se
          // perdió con la pestaña.
          if (fila.estado === "en_curso") {
            return { ...fila, estado: yaEsta ? "ya_estaba" : "pendiente" };
          }

          return fila;
        });

        aplicar({ ...guardado, filas }, true);
      } catch (e) {
        // Si la reconciliación falla se muestra lo guardado igualmente: es peor perder el progreso
        // que mostrarlo sin confirmar. El aviso queda en pantalla para que no se confunda con un
        // estado verificado.
        aplicar(guardado, false);
        setError(e instanceof Error ? e : new Error(String(e)));
      }
    }

    /** Deja el lote recuperado como estado de la pantalla, salvo que ya no venga a cuento. */
    function aplicar(recuperado: Lote, confirmado: boolean) {
      // Quien está mirando ya cargó otro archivo (o descartó el lote) mientras esto consultaba al
      // servidor: lo recuperado es historia y pisarlo sería quitarle de delante lo que acaba de
      // hacer.
      if (loteReemplazado.current) {
        return;
      }

      setLote(recuperado);
      setInstancias(recuperado.instancias);
      setDryRun(recuperado.dryRun);
      setReconciliado(confirmado);
      // Lo que quedó por hacer, ya marcado: quien vuelve a una ola a medias quiere continuarla, y
      // hacerle marcar de nuevo diecisiete casillas es trabajo que la pantalla puede ahorrarle.
      setSeleccion(new Set(recuperado.filas.filter((f) => !estaTerminada(f)).map(claveFila)));
      if (confirmado) {
        guardarLote(recuperado);
      }
    }
  }, []);

  async function alCargarArchivo(evento: React.ChangeEvent<HTMLInputElement>) {
    const archivo = evento.target.files?.[0];
    if (!archivo) {
      return;
    }

    setError(null);
    // Desde este momento la restauración pendiente ya no debe aplicarse: el archivo nuevo manda.
    loteReemplazado.current = true;
    try {
      const { validas, invalidas: malas } = await leerArchivo(archivo);
      setInvalidas(malas);

      if (validas.length === 0) {
        setLote(null);
        borrarLote();
        return;
      }

      const creado = nuevoLote(archivo.name, validas, instancias, dryRun);
      persistir(creado);
      // Todo seleccionado al cargar: lo normal es querer migrarlo entero, y quitar lo que no se
      // quiere es menos trabajo que marcar veinte casillas.
      setSeleccion(new Set(validas.map(claveFila)));
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
      setLote(null);
    } finally {
      // Se limpia para que volver a elegir el MISMO archivo dispare el evento otra vez.
      evento.target.value = "";
    }
  }

  async function migrarSeleccionadas() {
    if (!lote || corriendo) {
      return;
    }

    const cola = lote.filas.filter((f) => seleccion.has(claveFila(f)) && !estaTerminada(f));
    if (cola.length === 0) {
      return;
    }

    setCorriendo(true);
    setError(null);
    detener.current = false;

    // Se trabaja sobre una copia local y se persiste en cada paso. Leer del estado dentro del
    // bucle daría el valor capturado al arrancar.
    let actual: Lote = { ...lote, instancias, dryRun };

    for (const objetivo of cola) {
      if (detener.current) {
        break;
      }

      actual = marcar(actual, objetivo, { estado: "en_curso" });
      persistir(actual);

      try {
        const respuesta = await migrarTramite({
          tramite: objetivo.tramite,
          v1Id: objetivo.v1Id,
          instancias: instancias.length > 0 ? instancias : undefined,
          dryRun,
        });

        actual = marcar(actual, objetivo, {
          estado: clasificar(respuesta),
          respuesta,
          error: undefined,
        });
      } catch (e) {
        // Un fallo NO detiene la cola: en una ola de veinte, que el tercero falle no es motivo
        // para no intentar los diecisiete restantes. Queda marcado y se reintenta después.
        actual = marcar(actual, objetivo, {
          estado: "fallido",
          error: e instanceof Error ? e.message : String(e),
        });
      }

      persistir(actual);
    }

    setCorriendo(false);
  }

  function limpiar() {
    loteReemplazado.current = true;
    borrarLote();
    setLote(null);
    setInvalidas([]);
    setSeleccion(new Set());
    setError(null);
  }

  // Lo que se va a correr al pulsar el botón: seleccionado Y no terminado. Es el número que manda
  // en toda la pantalla — el de casillas marcadas a secas engaña en cuanto hay filas ya migradas.
  const pendientes =
    lote?.filas.filter((f) => seleccion.has(claveFila(f)) && !estaTerminada(f)) ?? [];
  const listas = lote?.filas.filter(estaTerminada).length ?? 0;
  // Ni una fila por hacer. Se mira sobre TODAS las filas y no sobre las seleccionadas: con
  // `pendientes` bastaría con desmarcarlas para que la pantalla dijera que terminó.
  const loteTerminado = lote !== null && lote.filas.length > 0 && lote.filas.every(estaTerminada);
  const fallidas = lote?.filas.filter((f) => f.estado === "fallido").length ?? 0;

  return (
    <div className="flex flex-col gap-4">
      <section className="flex flex-col gap-3 rounded-2xl border border-[#DFE5ED] p-4 dark:border-white/10">
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={descargarPlantilla}
            className="rounded-lg border border-[#DFE5ED] px-3 py-2 text-xs font-semibold dark:border-white/10"
          >
            Descargar plantilla
          </button>

          <button
            type="button"
            onClick={() => entradaArchivo.current?.click()}
            disabled={corriendo}
            className="rounded-lg px-3 py-2 text-xs font-semibold text-white disabled:opacity-50"
            style={{ backgroundColor: "#557EFF" }}
          >
            Cargar archivo
          </button>

          <input
            ref={entradaArchivo}
            type="file"
            accept=".csv,.xlsx,.txt"
            onChange={alCargarArchivo}
            className="hidden"
          />

          {lote && (
            <button
              type="button"
              onClick={limpiar}
              disabled={corriendo}
              className="rounded-lg border border-[#DFE5ED] px-3 py-2 text-xs font-semibold disabled:opacity-50 dark:border-white/10"
            >
              Descartar el lote
            </button>
          )}
        </div>

        <p className="text-xs opacity-70">
          Dos columnas: <strong>tipo</strong> (traspaso o matricula) e <strong>id</strong> de V1.
          Se aceptan .csv y .xlsx.
        </p>
      </section>

      {error && <AvisoError error={error} />}

      {invalidas.length > 0 && (
        <section className="rounded-2xl border border-amber-500/40 bg-amber-500/5 p-4">
          <h3 className="text-sm font-semibold">
            {invalidas.length === 1
              ? "1 fila del archivo no se puede migrar"
              : `${invalidas.length} filas del archivo no se pueden migrar`}
          </h3>
          <p className="mt-0.5 text-xs opacity-70">
            El resto sí; corrige estas y vuelve a cargar el archivo si las necesitas.
          </p>
          <ul className="mt-2 flex flex-col gap-1 text-xs">
            {invalidas.map((f) => (
              <li key={f.fila} className="flex flex-wrap gap-x-2">
                <span className="font-semibold tabular-nums">Fila {f.fila}:</span>
                <span className="opacity-90">{f.motivo}</span>
                {f.contenido && <span className="opacity-50">({f.contenido})</span>}
              </li>
            ))}
          </ul>
        </section>
      )}

      {lote && (
        <>
          {/*
            Una sola barra a todo el ancho, no dos columnas.
            Partirlo en «opciones | lote» dejaba una columna de opciones muy alta junto a una
            tarjeta de tres líneas, y el vacío de 350 px que quedaba a su derecha se leía como si
            la página se hubiera roto al cargar. Además invertía la importancia: las opciones se
            tocan una vez, y lo que se mira todo el rato es el archivo y el botón.
          */}
          <section className="flex flex-col gap-4 rounded-2xl border border-[#DFE5ED] p-4 dark:border-white/10">
            <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
              <div className="min-w-0">
                <p className="text-sm font-semibold">{lote.archivo}</p>
                {/*
                  Se cuenta lo que se va a CORRER, no lo que está marcado. Antes decía «4
                  seleccionados» junto a un botón que decía «Simular 1»: ambos eran ciertos —tres
                  ya estaban migradas— pero juntos no había forma de entenderlos.
                */}
                <p className="text-xs opacity-70">
                  {lote.filas.length} en el archivo · {listas} ya migrado{listas === 1 ? "" : "s"} ·{" "}
                  <strong className="font-semibold opacity-100">
                    {pendientes.length} por {dryRun ? "simular" : "migrar"}
                  </strong>
                </p>

                {reconciliado === true && (
                  <p className="mt-1 text-xs opacity-70">
                    Contrastado con el servidor: lo que aparece como migrado, lo está.
                  </p>
                )}
                {reconciliado === false && (
                  <p className="mt-1 text-xs text-amber-600 dark:text-amber-400">
                    No se pudo contrastar con el servidor. Lo que ves es el avance guardado en este
                    navegador y puede estar desactualizado.
                  </p>
                )}

                {/*
                  Qué hacer DESPUÉS. Sin esto, terminada una ola la pantalla se queda con el lote
                  hecho y un botón deshabilitado, sin decir por dónde se empieza otra; lo natural
                  es suponer que hay que descartar primero, que es el camino largo y hace pensar
                  que se pierde algo. Cargar el archivo nuevo basta.
                */}
                {/*
                  El mismo problema que el lote terminado, pero al revés: la ola acabó con fallos y
                  la pantalla no dice qué se espera de quien mira. Las que fallaron siguen marcadas
                  —no son «terminadas»— así que el botón ya ofrece justo esas; lo que faltaba era
                  decirlo, y decir que reintentar no duplica nada.
                */}
                {fallidas > 0 && !corriendo && (
                  <p className="mt-1 text-xs opacity-70">
                    {fallidas === 1 ? "1 trámite falló" : `${fallidas} trámites fallaron`} y{" "}
                    {fallidas === 1 ? "sigue marcado" : "siguen marcados"} para reintentar: el motivo
                    está al lado de cada uno. Reintentar es seguro — lo que ya migró no se duplica.
                  </p>
                )}

                {loteTerminado && !corriendo && (
                  <p className="mt-1 text-xs opacity-70">
                    Lote terminado. Para empezar otro,{" "}
                    <strong className="font-semibold opacity-100">carga un archivo nuevo</strong>:
                    reemplaza a este. «Descartar el lote» solo hace falta si quieres dejar la
                    pantalla vacía.
                  </p>
                )}
              </div>

              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  onClick={migrarSeleccionadas}
                  disabled={corriendo || pendientes.length === 0}
                  className={`flex items-center gap-1.5 rounded-lg px-4 py-2.5 text-sm font-semibold text-white transition-colors disabled:opacity-50 ${
                    dryRun ? "bg-[#557EFF]" : "bg-amber-600 hover:bg-amber-700"
                  }`}
                >
                  {corriendo ? (
                    <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                  ) : (
                    <Play className="h-4 w-4" aria-hidden="true" />
                  )}
                  {corriendo
                    ? "Migrando…"
                    : `${dryRun ? "Simular" : "Migrar"} ${pendientes.length} trámite${
                        pendientes.length === 1 ? "" : "s"
                      }`}
                </button>

                {corriendo && (
                  <button
                    type="button"
                    onClick={() => {
                      detener.current = true;
                    }}
                    className="flex items-center gap-1.5 rounded-lg border border-[#DFE5ED] px-3 py-2 text-xs font-semibold dark:border-white/10"
                  >
                    <Square className="h-3.5 w-3.5" aria-hidden="true" />
                    Detener al terminar el actual
                  </button>
                )}

                {!corriendo && lote.filas.some((f) => f.estado === "fallido") && (
                  <button
                    type="button"
                    onClick={() => {
                      setSeleccion(
                        new Set(
                          lote.filas.filter((f) => f.estado === "fallido").map(claveFila),
                        ),
                      );
                    }}
                    className="flex items-center gap-1.5 rounded-lg border border-[#DFE5ED] px-3 py-2 text-xs font-semibold dark:border-white/10"
                  >
                    <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                    Seleccionar solo los que fallaron
                  </button>
                )}
              </div>
            </div>

            {corriendo && (
              <p className="text-xs opacity-70">
                Van de a uno: el migrador solo admite dos a la vez. Si cierras la pestaña, lo que
                ya se migró queda migrado y el progreso se recupera al volver.
              </p>
            )}

            <div className="border-t border-[#DFE5ED] pt-4 dark:border-white/10">
              <OpcionesMigracion
                instancias={instancias}
                onInstancias={setInstancias}
                dryRun={dryRun}
                onDryRun={setDryRun}
                deshabilitado={corriendo}
                disposicion="fila"
              />
            </div>
          </section>

          <TablaLote
            filas={lote.filas}
            seleccion={seleccion}
            onSeleccion={setSeleccion}
            bloqueada={corriendo}
          />
        </>
      )}
    </div>
  );
}

/**
 * Pregunta al servidor cuáles de los trámites del lote ya están migrados, agrupando por tipo
 * porque la consulta de estado es por tipo de trámite (los ids viven en tablas distintas).
 */
async function consultarMigrados(lote: Lote): Promise<Set<string>> {
  const porTipo = new Map<TipoTramite, number[]>();
  for (const fila of lote.filas) {
    porTipo.set(fila.tramite, [...(porTipo.get(fila.tramite) ?? []), fila.v1Id]);
  }

  const migrados = new Set<string>();
  for (const [tramite, ids] of porTipo) {
    const estado = await consultarEstado(tramite, ids);
    for (const item of estado.items) {
      if (item.migrado) {
        migrados.add(claveFila({ tramite, v1Id: item.v1Id }));
      }
    }
  }

  return migrados;
}

/**
 * Actualiza UNA fila. La identidad es tipo + id, nunca el id solo: un traspaso y una matrícula
 * pueden compartir el mismo id de V1 —son tablas distintas— y con la clave corta, migrar el
 * traspaso 26350 marcaría también la matrícula 26350 como hecha sin haberla tocado.
 */
function marcar(
  lote: Lote,
  objetivo: { tramite: TipoTramite; v1Id: number },
  cambios: Partial<FilaLote>,
): Lote {
  const clave = claveFila(objetivo);
  return {
    ...lote,
    filas: lote.filas.map((f) => (claveFila(f) === clave ? { ...f, ...cambios } : f)),
  };
}
