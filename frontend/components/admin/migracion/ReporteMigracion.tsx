"use client";

import { Icono } from "@flit/ui/Icono";
import {
  enlaceTramite,
  etiquetaConteo,
  etiquetaEstadoInstancia,
  etiquetaTramite,
  type MigracionRespuesta,
} from "@/lib/migracion/types";

import { formatFechaHora } from "@/lib/format/date";
import { ETIQUETA_PUNTO_CLS, etiquetaKit, type StatusTone } from "@flit/ui/StatusBadge";
/**

 * El reporte de una migración, en el mismo orden en que lo imprime la consola por SSH: de dónde
 * salió, si ya estaba, qué hizo cada instancia y a dónde fue a parar.
 *
 * Los estados y los conteos se TRADUCEN (ver `ETIQUETA_CONTEO` y `ETIQUETA_ESTADO_INSTANCIA`).
 * Llegan del motor en inglés y en camelCase porque son nombres de campo de C#; mostrarlos crudos
 * hace que la pantalla se lea como un volcado de JSON en vez de como una interfaz.
 */
export function ReporteMigracion({ respuesta }: { respuesta: MigracionRespuesta }) {
  const { origen, yaMigrado, instancias, destino, conProblemas } = respuesta;

  return (
    <div className="flex flex-col gap-3 text-sm">
      <Encabezado respuesta={respuesta} />

      <Bloque titulo="Origen">
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1.5 sm:grid-cols-[auto_minmax(0,1fr)_auto_minmax(0,1fr)]">
          <Dato etiqueta="Trámite" valor={`${etiquetaTramite(origen.tramite)} #${origen.v1Id}`} />
          <Dato etiqueta="Tipo en V2" valor={origen.tipoV2} />
          <Dato etiqueta="Tabla de V1" valor={origen.tablaV1} />
          <Dato etiqueta="Lote" valor={origen.lote} />
          <Dato etiqueta="Base de V1" valor={origen.baseV1} />
          <Dato etiqueta="Base de V2" valor={origen.baseV2} />
        </dl>
      </Bloque>

      {yaMigrado && (
        <Bloque titulo="Ya venía migrado">
          <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1.5 sm:grid-cols-[auto_minmax(0,1fr)_auto_minmax(0,1fr)]">
            <Dato etiqueta="Lote anterior" valor={yaMigrado.lote} />
            <Dato etiqueta="Estado" valor={yaMigrado.estadoFinal} />
            <Dato
              etiqueta="Fecha"
              valor={formatFechaHora(new Date(yaMigrado.migradoEl))}
            />
          </dl>
          <p className="mt-2 opacity-80">
            Esta ejecución no creó el trámite: ya existía en V2. Reintentar es inofensivo, y las
            instancias que aparecen abajo como omitidas son justamente las que no hicieron falta.
          </p>
        </Bloque>
      )}

      {instancias.map((instancia) => (
        <Bloque key={instancia.instancia} titulo={`Instancia: ${instancia.instancia}`}>
          <div className="flex flex-wrap items-center gap-2">
            <Insignia estado={instancia.estado} conProblemas={instancia.conProblemas} />
            {instancia.motivo && <span className="opacity-80">{instancia.motivo}</span>}
          </div>

          {Object.keys(instancia.conteos).length > 0 && (
            <dl className="mt-2 flex flex-wrap gap-x-5 gap-y-1">
              {Object.entries(instancia.conteos).map(([clave, valor]) => (
                <div key={clave} className="flex items-baseline gap-1.5">
                  <dt className="text-xs opacity-70">{etiquetaConteo(clave)}</dt>
                  <dd className="font-semibold tabular-nums">{valor}</dd>
                </div>
              ))}
            </dl>
          )}

          {instancia.avisos.length > 0 && (
            <ul className="mt-2 flex flex-col gap-1">
              {instancia.avisos.map((aviso, i) => (
                <li key={i} className="flex items-start gap-1.5">
                  <Icono nombre="alerta" className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[var(--badge-warning-fg)]" />
                  <span className="opacity-90">{aviso}</span>
                </li>
              ))}
            </ul>
          )}
        </Bloque>
      ))}

      {destino && (
        <a
          href={enlaceTramite(destino)}
          target="_blank"
          rel="noreferrer"
          className="flex w-fit items-center gap-1.5 rounded-lg px-3 py-2 text-xs font-semibold text-white"
          style={{ backgroundColor: "var(--color-flit-brand)" }}
        >
          Abrir el trámite en V2
          <Icono nombre="externo" className="h-3.5 w-3.5" />
        </a>
      )}

      {!destino && !origen.dryRun && !conProblemas && (
        <p className="text-xs opacity-70">
          No hay enlace porque este trámite no quedó registrado en la libreta de migración.
        </p>
      )}
    </div>
  );
}

function Encabezado({ respuesta }: { respuesta: MigracionRespuesta }) {
  const { conProblemas, origen, yaMigrado } = respuesta;

  if (conProblemas) {
    return (
      <Titular
        icono={<Icono nombre="cerrar" className="h-5 w-5 text-destructive" />}
        texto="La migración reportó problemas"
        detalle="Revisa abajo qué instancia falló y por qué. Reintentar es seguro."
      />
    );
  }

  if (origen.dryRun) {
    return (
      <Titular
        icono={<Icono nombre="exclamacion" className="h-5 w-5" style={{ color: "var(--color-flit-brand)" }} />}
        texto="Simulación completada"
        // Se nombra el control tal y como está rotulado en pantalla: la versión anterior decía
        // «vuelve a lanzarla sin simulación» y ese botón no existe con ese nombre en ningún sitio.
        detalle="Nada se escribió. Cambia el modo a «Migración» y vuelve a lanzarla para migrar de verdad."
      />
    );
  }

  return (
    <Titular
      icono={<Icono nombre="check" className="h-5 w-5 text-[var(--flit-success-ink)]" />}
      texto={yaMigrado ? "El trámite ya estaba migrado" : "Migración completada"}
      detalle={
        yaMigrado
          ? "No se creó nada nuevo; el trámite ya existía en V2."
          : "El trámite quedó creado en V2."
      }
    />
  );
}

function Titular({
  icono,
  texto,
  detalle,
}: {
  icono: React.ReactNode;
  texto: string;
  detalle: string;
}) {
  return (
    <div className="flex items-start gap-2">
      <span className="mt-0.5 shrink-0">{icono}</span>
      <div>
        <p className="font-semibold">{texto}</p>
        <p className="text-xs opacity-70">{detalle}</p>
      </div>
    </div>
  );
}

function Bloque({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <section className="rounded-xl border border-border p-3">
      <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide opacity-60">{titulo}</h3>
      {children}
    </section>
  );
}

/**
 * Etiqueta y valor en dos celdas contiguas de la rejilla (de ahí el fragmento: los hijos son <dt> y
 * <dd> directos, no un <div> que los envuelva).
 *
 * La versión anterior los separaba con `justify-between` y una línea de puntos. En una tarjeta
 * estrecha se leía bien; a 1440 px la línea medía media pantalla y costaba unir cada etiqueta con
 * su valor.
 */
function Dato({ etiqueta, valor }: { etiqueta: string; valor: string }) {
  return (
    <>
      <dt className="whitespace-nowrap text-xs opacity-70">{etiqueta}</dt>
      <dd className="truncate font-medium" title={valor}>
        {valor}
      </dd>
    </>
  );
}

/**
 * Estados de instancia que NO merecen el verde de «hecho», con el mismo color que les da la tabla
 * del lote (ver `TONO` en `TablaLote`).
 *
 * El verde es la señal de que el trámite quedó en V2, y aquí se leía sobre la palabra «Simulado»:
 * la misma fila decía «Simulado» en azul en la tabla y «Simulado» en verde tres centímetros más
 * abajo, en la pantalla donde el color es justo lo que la gente mira para saber si ya migró.
 */
const TONO_ESTADO: Record<string, StatusTone> = {
  Simulated: "info",
  Skipped: "neutral",
  NotMigrated: "neutral",
  NoAttachments: "neutral",
  NotFoundInV1: "warning",
};

function Insignia({ estado, conProblemas }: { estado: string; conProblemas: boolean }) {
  const tono: StatusTone = conProblemas ? "danger" : (TONO_ESTADO[estado] ?? "success");

  return (
    <span {...etiquetaKit(tono)}>
      <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
      {etiquetaEstadoInstancia(estado)}
    </span>
  );
}
