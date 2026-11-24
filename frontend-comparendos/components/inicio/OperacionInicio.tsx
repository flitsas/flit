"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { ChipFiltro, EMBUDO_CLS } from "@flit/ui/ChipsFiltro";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { BarrasSimples } from "@/components/inicio/BarrasSimples";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { formatoPesos } from "@/lib/format/date";
import { etiquetaMes, formatoTasa, hrefBandeja, hrefTramo, PASOS_GESTION, pesosCorto, type Cifra, type Operacion } from "@/lib/reportes";

const n = (x: number) => x.toLocaleString("es-CO");
const comparendos = (x: number) => `${n(x)} ${x === 1 ? "comparendo" : "comparendos"}`;
/** «· $ 3,8 M» solo si el usuario ve pesos. */
const conPesos = (v: number | null) => (v === null ? "" : ` · ${pesosCorto(v)}`);

function Evitable({ etiqueta, cifra, pie }: { etiqueta: string; cifra: Cifra; pie: string }) {
  return (
    <TarjetaMetrica
      etiqueta={etiqueta}
      cifra={n(cifra.cantidad)}
      colorCifra={cifra.cantidad === 0 ? "var(--di-text-tertiary)" : "var(--badge-warning-fg)"}
      title={cifra.valor === null ? undefined : `Valor de esos comparendos: ${formatoPesos(cifra.valor)}`}
      pie={`${pie}${conPesos(cifra.valor)}`}
    />
  );
}

/** Periodo de imposición y NIT de Gestión, en los parámetros de la bandeja: cada cifra abre la bandeja con su mismo corte. */
export interface CorteBandeja {
  imposicionDesde?: string;
  imposicionHasta?: string;
  nits?: string;
}

const conCorte = (query: Record<string, string>, corte: CorteBandeja): Record<string, string> => {
  const q: Record<string, string> = { ...query };
  for (const [k, v] of Object.entries(corte)) if (v) q[k] = v;
  return q;
};

/** Un tramo de antigüedad cortado además por el periodo de Gestión (la intersección de los dos rangos de imposición). */
function hrefTramoConCorte(t: { desde: string | null; hasta: string | null }, empresa: string | null, corte: CorteBandeja): string | null {
  if (!t.desde && !t.hasta) return null;
  const mayor = (a: string | null | undefined, b: string | null | undefined) => (!a ? (b ?? null) : !b ? a : a > b ? a : b);
  const menor = (a: string | null | undefined, b: string | null | undefined) => (!a ? (b ?? null) : !b ? a : a < b ? a : b);
  const desde = mayor(t.desde, corte.imposicionDesde);
  const hasta = menor(t.hasta, corte.imposicionHasta);
  if (!corte.imposicionDesde && !corte.imposicionHasta && !corte.nits) return hrefTramo(t, empresa);
  const q: Record<string, string> = { pagado: "false" };
  if (desde) q.imposicionDesde = desde;
  if (hasta) q.imposicionHasta = hasta;
  if (corte.nits) q.nits = corte.nits;
  return hrefBandeja(q, { empresa });
}

export interface OperacionInicioProps {
  o: Operacion;
  empresa: string | null;
  /**
   * Decisión 1 de Reportes 2.0: quien tiene el módulo de reportes ve en el Inicio solo el embudo y el calendario legal; lo
   * demás (vehículos, infracciones, evitables, antigüedad y DP por organismo) vive en Reportes › Gestión. Quien no tiene el
   * módulo no puede abrir Gestión y conserva todo aquí.
   */
  recorte?: boolean;
  /** Enlace «Ver en Reportes» del recorte (ya con la empresa del SuperAdmin). */
  hrefReportes?: string;
  corte?: CorteBandeja;
}

/**
 * La lente de operación de la gestora (rediseño R2, GET /comparendos/operacion; en Reportes › Gestión, GET /reportes/gestion):
 * gestión, vehículos que más repiten, lo que se pudo evitar, calendario legal, antigüedad y DP por organismo. Sin el módulo
 * no hay pesos. Cada cifra que tiene filtro en la bandeja lleva a ella.
 */
export function OperacionInicio({ o, empresa, recorte = false, hrefReportes, corte = {} }: OperacionInicioProps) {
  const router = useRouter();
  const v = o.vehiculos;
  const pendientes = o.resumen.pendientes;
  const evitables = o.evitables.soat.cantidad + o.evitables.tecnicoMecanica.cantidad;
  const bandeja = (query: Record<string, string>) => hrefBandeja(conCorte(query, corte), { empresa });
  const verEnReportes = hrefReportes ? (
    <Link
      href={hrefReportes}
      data-testid="ver-en-reportes"
      className="text-sm font-semibold text-[color:var(--kit-azul-texto)] underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--kit-azul-texto)]"
    >
      Ver en Reportes
    </Link>
  ) : undefined;
  const calendarioLegal = (
    <PanelInicio
      titulo="Calendario legal"
      subtitulo="Lo que caduca (un año sin decisión) o prescribe (tres años sin cobro) en los próximos seis meses: ahí se puede pedir que no se cobre."
      icono="calendario"
      data-testid="calendario"
    >
      <BarrasSimples
        color="var(--kit-amarillo)"
        filas={o.calendario.map((m) => ({
          clave: m.periodo,
          etiqueta: <span className="capitalize">{etiquetaMes(m.periodo)}</span>,
          valor: m.caducan + m.prescriben,
          cifra: n(m.caducan + m.prescriben),
          detalle: `${n(m.caducan)} caducan · ${n(m.prescriben)} prescriben`,
        }))}
      />
    </PanelInicio>
  );
  return (
    <>
      <PanelInicio
        titulo="Gestión de los comparendos"
        subtitulo={o.gestion.tasaExito === null ? "Cada paso abre la bandeja con ese estado." : `De lo que ya tuvo respuesta, ${formatoTasa(o.gestion.tasaExito)} salió favorable.`}
        icono="procesos"
        accion={verEnReportes}
        data-testid="gestion"
      >
        <div role="group" aria-label="Gestión de los comparendos" className={EMBUDO_CLS}>
          {o.gestion.pasos.map((p) => (
            <ChipFiltro
              key={p.estadoProceso}
              cifra={n(p.cantidad)}
              etiqueta={PASOS_GESTION[p.estadoProceso] ?? p.estadoProceso}
              activo={false}
              disabled={p.cantidad === 0}
              aria-label={`${PASOS_GESTION[p.estadoProceso] ?? p.estadoProceso}: ${comparendos(p.cantidad)}. Ver en la bandeja`}
              onClick={() => router.push(bandeja({ estadoProceso: p.estadoProceso }))}
            />
          ))}
        </div>
      </PanelInicio>

      {recorte ? (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
          {calendarioLegal}
          <PanelInicio
            titulo="El resto, en Reportes"
            subtitulo="Lo que antes estaba aquí abajo tiene ahora su propia pestaña, con periodo y NIT."
            icono="reportes"
            data-testid="ahora-en-reportes"
          >
            <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm text-[color:var(--di-text-secondary)]">
              <li>Vehículos que más repiten</li>
              <li>Infracciones más frecuentes y lo que se pudo evitar</li>
              <li>Antigüedad de lo pendiente</li>
              <li>Derechos de petición por organismo</li>
            </ul>
            {hrefReportes && (
              <Link
                href={hrefReportes}
                data-testid="ver-en-reportes-gestion"
                className="mt-4 inline-flex min-h-11 items-center self-start rounded-[9px] border border-[color:var(--di-border)] px-4 text-sm font-semibold text-[color:var(--kit-azul-texto)] no-underline hover:bg-[var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--kit-azul-texto)]"
              >
                Ver en Reportes › Gestión
              </Link>
            )}
          </PanelInicio>
        </div>
      ) : (
        <>
      <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
        <PanelInicio
          titulo="Vehículos que más repiten"
          subtitulo={
            v.top.length === 0
              ? "Ningún vehículo con comparendos pendientes."
              : `${n(v.conTresOMas)} ${v.conTresOMas === 1 ? "vehículo tiene" : "vehículos tienen"} tres o más pendientes${
                  v.concentracionDiezPorCiento === null ? "" : `; el 10 % de los vehículos suma el ${formatoTasa(v.concentracionDiezPorCiento)} de los comparendos`
                }.`
          }
          icono="carro"
          data-testid="vehiculos"
        >
          <BarrasSimples
            filas={v.top.map((x) => ({
              clave: x.placa,
              etiqueta: <span className="font-semibold tracking-wide">{x.placa}</span>,
              valor: x.cantidad,
              cifra: comparendos(x.cantidad),
              detalle: `${x.organismos === 0 ? "Organismo sin identificar" : `Ante ${n(x.organismos)} ${x.organismos === 1 ? "organismo" : "organismos"}`}${conPesos(x.valor)}`,
              // pagado=false: la cifra son sus pendientes, y la bandeja debe mostrar los mismos.
              href: bandeja({ placas: x.placa, pagado: "false" }),
              ariaLabel: `${x.placa}: ${comparendos(x.cantidad)}${x.organismos === 0 ? "" : ` ante ${n(x.organismos)} ${x.organismos === 1 ? "organismo" : "organismos"}`}${x.valor === null ? "" : `, por un valor de ${formatoPesos(x.valor)}`}. Ver en la bandeja`,
            }))}
          />
        </PanelInicio>

        <PanelInicio
          titulo="Lo que se pudo evitar"
          subtitulo={
            pendientes === 0 ? "Sin comparendos pendientes." : `${comparendos(evitables)} pendientes por SOAT o técnico-mecánica vencidos: se evitan con la flota al día.`
          }
          icono="escudo"
          data-testid="evitables"
        >
          <div className="flex flex-col gap-5">
            <div className={METRICAS_GRID_CLS}>
              <Evitable etiqueta="SOAT vencido (D02)" cifra={o.evitables.soat} pie="Seguro obligatorio" />
              <Evitable etiqueta="Técnico-mecánica (C35)" cifra={o.evitables.tecnicoMecanica} pie="Revisión vencida" />
            </div>
            <div>
              <h3 className="mb-2 text-sm font-semibold text-[color:var(--di-text-primary)]">Infracciones más frecuentes</h3>
              <BarrasSimples
                color="var(--kit-naranja)"
                vacio="Sin infracciones pendientes."
                filas={o.infracciones.map((i) => ({
                  clave: i.codigo,
                  etiqueta: (
                    <>
                      <span className="font-semibold">{i.codigo}</span> <span className="text-[color:var(--di-text-secondary)]">{i.descripcion}</span>
                    </>
                  ),
                  valor: i.cantidad,
                  titulo: `${i.codigo} ${i.descripcion}`,
                  cifra: `${n(i.cantidad)}${conPesos(i.valor)}`,
                }))}
              />
            </div>
          </div>
        </PanelInicio>
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
        {calendarioLegal}

        <PanelInicio titulo="Antigüedad de lo pendiente" subtitulo="Hace cuánto ocurrió la infracción. Cada tramo abre la bandeja." icono="reloj" data-testid="antiguedad">
          <BarrasSimples
            color="var(--kit-gris)"
            filas={o.antiguedad.map((t) => {
              // «Sin fecha de imposición» no tiene filtro en la bandeja: ni enlace ni «Ver en la bandeja».
              const href = t.cantidad > 0 ? (hrefTramoConCorte(t, empresa, corte) ?? undefined) : undefined;
              return {
                clave: t.tramo,
                etiqueta: t.tramo,
                valor: t.cantidad,
                cifra: `${n(t.cantidad)}${conPesos(t.valor)}`,
                href,
                ariaLabel: href ? `${t.tramo}: ${comparendos(t.cantidad)}${t.valor === null ? "" : `, por un valor de ${formatoPesos(t.valor)}`}. Ver en la bandeja` : undefined,
              };
            })}
          />
        </PanelInicio>
      </div>

      <PanelInicio
        titulo="Derechos de petición por organismo"
        subtitulo="Todo lo enviado, también de comparendos ya pagados o cerrados: cuántos salieron favorables y cuánto tardan en responder."
        icono="documento"
        data-testid="dp-organismos"
      >
        <BarrasSimples
          color="var(--kit-verde)"
          vacio="Todavía no se han enviado derechos de petición."
          filas={o.dpPorOrganismo.map((d) => ({
            clave: d.organismoId ?? "sin-organismo",
            etiqueta: d.nombre,
            valor: d.enviados,
            cifra: `${n(d.favorables)} de ${n(d.enviados)} favorables`,
            detalle:
              d.respondidos === 0
                ? "Sin respuestas todavía"
                : `${n(d.respondidos)} con respuesta${d.diasPromedio === null ? "" : ` · ${String(d.diasPromedio).replace(".", ",")} días en promedio`}`,
            href: d.organismoId ? bandeja({ organismos: d.organismoId, conDp: "true" }) : undefined,
            // La bandeja muestra sus comparendos activos con DP, no los mismos enviados (que incluyen los ya cerrados).
            ariaLabel: `${d.nombre}: ${n(d.favorables)} de ${n(d.enviados)} derechos de petición favorables. Ver sus comparendos con DP en la bandeja`,
          }))}
        />
      </PanelInicio>
        </>
      )}
    </>
  );
}
