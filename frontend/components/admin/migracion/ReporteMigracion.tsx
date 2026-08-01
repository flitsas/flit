"use client";

import { AlertTriangle, ArrowUpRight, CheckCircle2, Info, XCircle } from "lucide-react";
import { enlaceTramite, type MigracionRespuesta } from "@/lib/migracion/types";

/**
 * El reporte de una migración, en el mismo orden en que lo imprime la consola por SSH: de dónde
 * salió, si ya estaba, qué hizo cada instancia y a dónde fue a parar.
 *
 * Los conteos se muestran TAL CUAL vienen del host, sin traducir las claves. Son las mismas
 * palabras que aparecen en el reporte de consola (`copiados`, `yaMigrados`, `excluidos`…), y quien
 * opera esta consola es quien ya lee esos reportes: renombrarlas aquí obligaría a mantener una
 * tabla de equivalencias en la cabeza para comparar una migración por UI con una por SSH.
 */
export function ReporteMigracion({ respuesta }: { respuesta: MigracionRespuesta }) {
  const { origen, yaMigrado, instancias, destino, conProblemas } = respuesta;

  return (
    <div className="flex flex-col gap-3 text-sm">
      <Encabezado respuesta={respuesta} />

      <Bloque titulo="Origen">
        <dl className="grid grid-cols-1 gap-x-6 gap-y-1 sm:grid-cols-2">
          <Dato etiqueta="Trámite" valor={`${origen.tramite} #${origen.v1Id}`} />
          <Dato etiqueta="Tipo en V2" valor={origen.tipoV2} />
          <Dato etiqueta="Tabla de V1" valor={origen.tablaV1} />
          <Dato etiqueta="Lote" valor={origen.lote} />
          <Dato etiqueta="Base de V1" valor={origen.baseV1} />
          <Dato etiqueta="Base de V2" valor={origen.baseV2} />
        </dl>
        {origen.dryRun && (
          <p className="mt-2 flex items-start gap-1.5 opacity-80">
            <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            Simulación: se leyó todo y no se escribió nada. Ningún trámite quedó creado.
          </p>
        )}
      </Bloque>

      {yaMigrado && (
        <Bloque titulo="Ya venía migrado">
          <dl className="grid grid-cols-1 gap-x-6 gap-y-1 sm:grid-cols-2">
            <Dato etiqueta="Lote anterior" valor={yaMigrado.lote} />
            <Dato etiqueta="Estado" valor={yaMigrado.estadoFinal} />
            <Dato
              etiqueta="Fecha"
              valor={new Date(yaMigrado.migradoEl).toLocaleString("es-CO")}
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
            <Insignia
              tono={instancia.conProblemas ? "malo" : "bueno"}
              texto={instancia.estado}
            />
            {instancia.motivo && <span className="opacity-80">{instancia.motivo}</span>}
          </div>

          {Object.keys(instancia.conteos).length > 0 && (
            <dl className="mt-2 flex flex-wrap gap-x-5 gap-y-1">
              {Object.entries(instancia.conteos).map(([clave, valor]) => (
                <div key={clave} className="flex items-baseline gap-1.5">
                  <dt className="text-xs opacity-70">{clave}</dt>
                  <dd className="font-semibold tabular-nums">{valor}</dd>
                </div>
              ))}
            </dl>
          )}

          {instancia.avisos.length > 0 && (
            <ul className="mt-2 flex flex-col gap-1">
              {instancia.avisos.map((aviso, i) => (
                <li key={i} className="flex items-start gap-1.5">
                  <AlertTriangle
                    className="mt-0.5 h-3.5 w-3.5 shrink-0 text-amber-500"
                    aria-hidden="true"
                  />
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
          style={{ backgroundColor: "#557EFF" }}
        >
          Abrir el trámite en V2
          <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
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
        icono={<XCircle className="h-5 w-5 text-red-500" aria-hidden="true" />}
        texto="La migración reportó problemas"
        detalle="Revisa abajo qué instancia falló y por qué. Reintentar es seguro."
      />
    );
  }

  if (origen.dryRun) {
    return (
      <Titular
        icono={<Info className="h-5 w-5" aria-hidden="true" style={{ color: "#557EFF" }} />}
        texto="Simulación completada"
        detalle="Nada se escribió. Vuelve a lanzarla sin simulación para migrar de verdad."
      />
    );
  }

  return (
    <Titular
      icono={<CheckCircle2 className="h-5 w-5 text-emerald-500" aria-hidden="true" />}
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
    <section className="rounded-xl border border-[#DFE5ED] p-3 dark:border-white/10">
      <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide opacity-60">{titulo}</h3>
      {children}
    </section>
  );
}

function Dato({ etiqueta, valor }: { etiqueta: string; valor: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 border-b border-dashed border-[#DFE5ED]/60 py-0.5 dark:border-white/5">
      <dt className="text-xs opacity-70">{etiqueta}</dt>
      <dd className="truncate font-medium" title={valor}>
        {valor}
      </dd>
    </div>
  );
}

function Insignia({ tono, texto }: { tono: "bueno" | "malo"; texto: string }) {
  const clases =
    tono === "bueno"
      ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
      : "bg-red-500/10 text-red-600 dark:text-red-400";

  return (
    <span className={`rounded-md px-2 py-0.5 text-xs font-semibold ${clases}`}>{texto}</span>
  );
}
