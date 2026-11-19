"use client";

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

/**
 * La lente de operación del Inicio de la gestora (rediseño R2, GET /comparendos/operacion): gestión, vehículos que más
 * repiten, lo que se pudo evitar, calendario legal, antigüedad y DP por organismo. Se ve con o sin el módulo de reportes;
 * sin él no hay pesos. Cada cifra que tiene filtro en la bandeja lleva a ella.
 */
export function OperacionInicio({ o, empresa }: { o: Operacion; empresa: string | null }) {
  const router = useRouter();
  const v = o.vehiculos;
  const pendientes = o.resumen.pendientes;
  const evitables = o.evitables.soat.cantidad + o.evitables.tecnicoMecanica.cantidad;
  return (
    <>
      <PanelInicio
        titulo="Gestión de los comparendos"
        subtitulo={o.gestion.tasaExito === null ? "Cada paso abre la bandeja con ese estado." : `De lo que ya tuvo respuesta, ${formatoTasa(o.gestion.tasaExito)} salió favorable.`}
        icono="procesos"
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
              onClick={() => router.push(hrefBandeja({ estadoProceso: p.estadoProceso }, { empresa }))}
            />
          ))}
        </div>
      </PanelInicio>

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
              href: hrefBandeja({ placas: x.placa }, { empresa }),
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

        <PanelInicio titulo="Antigüedad de lo pendiente" subtitulo="Hace cuánto ocurrió la infracción. Cada tramo abre la bandeja." icono="reloj" data-testid="antiguedad">
          <BarrasSimples
            color="var(--kit-gris)"
            filas={o.antiguedad.map((t) => ({
              clave: t.tramo,
              etiqueta: t.tramo,
              valor: t.cantidad,
              cifra: `${n(t.cantidad)}${conPesos(t.valor)}`,
              href: t.cantidad > 0 ? hrefTramo(t, empresa) : undefined,
              ariaLabel: `${t.tramo}: ${comparendos(t.cantidad)}${t.valor === null ? "" : `, por un valor de ${formatoPesos(t.valor)}`}. Ver en la bandeja`,
            }))}
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
            href: d.organismoId ? hrefBandeja({ organismos: d.organismoId, conDp: "true" }, { empresa }) : undefined,
            ariaLabel: `${d.nombre}: ${n(d.favorables)} de ${n(d.enviados)} derechos de petición favorables. Ver en la bandeja`,
          }))}
        />
      </PanelInicio>
    </>
  );
}
