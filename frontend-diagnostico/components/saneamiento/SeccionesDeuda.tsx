import { useId, type ReactNode } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Icono } from "@flit/ui/Icono";
import { ListaDefinicion } from "@flit/ui/ListaDefinicion";
import { ETIQUETA_PUNTO_CLS, etiquetaKit, type StatusTone } from "@flit/ui/StatusBadge";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { TarjetaPlegable } from "@flit/ui/TarjetaPlegable";
import { AYUDA_CLS } from "@/components/configuracion/estilos";
import { RegionDesplazable } from "@/components/comun/RegionDesplazable";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, CONTENEDOR_TABLA_CLS, FILA_TABLA_CLS, FILA_TABLA_HOVER_CLS } from "@/components/tablero/vistasDetalle/comun";
import type {
  EstadoFuenteDeuda,
  FuenteDeuda,
  FuenteDeudaImpuesto,
  FuenteDeudaPanel,
  FuenteDeudaSimit,
  FuenteDeudaVigencia,
  PanelDeuda,
} from "@/lib/saneamiento/api";
import { ROTULO_FUENTE } from "@/lib/saneamiento/estados";
import { fechaCorta } from "@/lib/tablero/detalle";

// Secciones «Dictamen» y «Deuda por fuente» del panel de deuda (spec-ui §1.5, §3.4 y §3.5; HU #14577 AC14 y AC15). Solo
// pinta lo que recibe: no pide nada, así la gestión del backoffice (PS-5) la reutiliza en modo lectura con su propia
// lectura de la deuda. Todo valor visual sale de los tokens del kit (--di-*, --badge-*); el estado va siempre en texto.
// SP-11: de la persona del SIMIT solo se pinta tipo y número de documento, nunca un nombre aunque llegara.

/** Etiqueta del kit con punto y texto (spec-ui §1.1): sin role="status", es contenido estático. */
export function EtiquetaTono({ tono, children }: { tono: StatusTone; children: ReactNode }) {
  return (
    <span {...etiquetaKit(tono)}>
      <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
      {children}
    </span>
  );
}

/** Orden fijo de las fuentes en el panel (§3.5). */
const ORDEN_FUENTES: readonly FuenteDeuda[] = ["SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO", "IMPUESTO", "SOAT", "RTM"];

const ESTADO_FUENTE: Record<EstadoFuenteDeuda, { texto: string; tono: StatusTone; abierta: boolean }> = {
  CON_DEUDA: { texto: "Con deuda", tono: "warning", abierta: true },
  NO_VIGENTE: { texto: "No vigente", tono: "warning", abierta: true },
  SIN_VERIFICAR: { texto: "Sin verificar", tono: "neutral", abierta: true },
  SIN_HALLAZGO: { texto: "Sin pendientes", tono: "success", abierta: false },
  NO_APLICA: { texto: "No aplica", tono: "neutral", abierta: false },
};

/** Fuentes con algo que mirar: deuda, vencida o sin verificar. */
const CON_HALLAZGO: ReadonlySet<EstadoFuenteDeuda> = new Set<EstadoFuenteDeuda>(["CON_DEUDA", "NO_VIGENTE", "SIN_VERIFICAR"]);

/** `true` si alguna fuente tiene hallazgo o el dictamen trae motivos: decide el aviso «Sin pendientes» (§3.2). */
export function hayPendientes(deuda: Pick<PanelDeuda, "dictamen" | "fuentes">): boolean {
  return deuda.fuentes.some((f) => CON_HALLAZGO.has(f.estado)) || (deuda.dictamen?.motivos.length ?? 0) > 0;
}

const MILES = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 });
/** «$ 1.250.000»: pesos con separador de miles, sin decimales. */
const pesos = (valor: number) => `$ ${MILES.format(valor)}`;
/**
 * UA-6: un saldo que llega como texto (BE-C-2). Si es un número («300000», «300000.50») se pinta en pesos; si no («$1.250.000»,
 * «1.250.000,00») va tal como llega, como el «Valor» de los comparendos (S17). Nunca «$ NaN».
 */
const saldo = (valor: string) => {
  const texto = valor.trim();
  const n = texto === "" ? Number.NaN : Number(texto);
  return Number.isFinite(n) ? pesos(n) : valor;
};
const siNo = (valor: boolean | null | undefined) => (valor === true ? "Sí" : valor === false ? "No" : null);

/** «Consultado el {fecha}» (fecha de la consulta, regla de lib/tablero/detalle.ts). */
function Consultado({ en }: { en?: string | null }) {
  return en ? <p className={AYUDA_CLS}>{`Consultado el ${fechaCorta(en)}`}</p> : null;
}

/** Tabla simple que pasa a tarjetas por debajo de 1024 px (recetas TABLA_MOVIL_*), dentro de la región desplazable. */
function TablaSimple({ nombre, columnas, filas }: { nombre: string; columnas: { titulo: string; derecha?: boolean }[]; filas: ReactNode[][] }) {
  return (
    <RegionDesplazable nombre={nombre} className={CONTENEDOR_TABLA_CLS}>
      <table aria-label={nombre} className={`w-full border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {columnas.map((c) => (
              <th key={c.titulo} scope="col" className={`${CABECERA_TABLA_CLS}${c.derecha ? " text-right" : ""}`} style={CABECERA_TABLA_ESTILO}>
                {c.titulo}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {filas.map((fila, i) => (
            <tr key={i} className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
              {fila.map((celda, j) => (
                <td
                  key={columnas[j].titulo}
                  data-label={columnas[j].titulo}
                  className={`${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}${j === 0 ? ` ${TABLA_MOVIL_CELL_TITULO_CLS}` : ""}${columnas[j].derecha ? " text-right" : ""}`}
                >
                  {celda}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </RegionDesplazable>
  );
}

function ContenidoSimit({ fuente, rotulo }: { fuente: FuenteDeudaSimit; rotulo: string }) {
  const comparendos = fuente.comparendos ?? [];
  const acuerdos = fuente.acuerdos_pago ?? [];
  const items = [
    ...(fuente.persona ? [{ termino: "Documento", valor: `${fuente.persona.tipo_documento} ${fuente.persona.documento}` }] : []),
    ...(siNo(fuente.paz_y_salvo) ? [{ termino: "Paz y salvo", valor: siNo(fuente.paz_y_salvo) }] : []),
  ];
  return (
    <div className="flex flex-col gap-3">
      {items.length > 0 && <ListaDefinicion items={items} columnas={2} />}
      {comparendos.length > 0 && (
        <TablaSimple
          nombre={`Comparendos de ${rotulo}`}
          columnas={[{ titulo: "Número" }, { titulo: "Fecha" }, { titulo: "Valor", derecha: true }, { titulo: "Estado" }]}
          // S17: el valor va tal como llega, sin sumar ni convertir; no hay fila de total.
          filas={comparendos.map((c) => [c.numero, fechaCorta(c.fecha), c.valor, c.estado ?? "—"])}
        />
      )}
      {fuente.comparendos_truncados && (
        <InlineAlert tone="info">Se muestran los primeros 100 comparendos. El PDF y el Excel llevan los mismos.</InlineAlert>
      )}
      {acuerdos.length > 0 && (
        <ul aria-label={`Acuerdos de pago de ${rotulo}`} className="flex flex-col gap-1 text-sm text-flit-primary">
          {acuerdos.map((a, i) => (
            <li key={i}>{`Acuerdo de pago con saldo pendiente de ${saldo(a.saldo_pendiente)}`}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ContenidoImpuesto({ fuente }: { fuente: FuenteDeudaImpuesto }) {
  const vigencias = fuente.vigencias ?? [];
  return (
    <div className="flex flex-col gap-3">
      {typeof fuente.monto_total === "number" && <p className="text-sm font-semibold text-flit-primary">{`Total pendiente: ${pesos(fuente.monto_total)}`}</p>}
      {vigencias.length > 0 && (
        <TablaSimple
          nombre="Vigencias del impuesto"
          columnas={[{ titulo: "Año" }, { titulo: "Valor", derecha: true }, { titulo: "Pagada" }]}
          filas={vigencias.map((v) => [String(v.anio), pesos(v.valor), v.pagada ? "Sí" : "No"])}
        />
      )}
    </div>
  );
}

function ContenidoVigencia({ fuente }: { fuente: FuenteDeudaVigencia }) {
  if (!fuente.vence) return null;
  const texto = fuente.estado === "NO_VIGENTE" ? "Vencido el" : "Vence el";
  return <p className="text-sm text-flit-primary">{`${texto} ${fechaCorta(fuente.vence)}`}</p>;
}

function ContenidoFuente({ fuente, rotulo }: { fuente: FuenteDeudaPanel; rotulo: string }) {
  if (fuente.estado === "NO_APLICA") {
    return (
      <p className="text-sm text-flit-primary">
        {fuente.motivo_na === "SIN_DOCUMENTO_LOCATARIO" ? "No se consultó: el RUNT no reporta locatario." : "No aplica para este vehículo."}
      </p>
    );
  }
  let detalle: ReactNode;
  switch (fuente.fuente) {
    case "IMPUESTO":
      detalle = <ContenidoImpuesto fuente={fuente} />;
      break;
    case "SOAT":
    case "RTM":
      detalle = <ContenidoVigencia fuente={fuente} />;
      break;
    default:
      detalle = <ContenidoSimit fuente={fuente} rotulo={rotulo} />;
  }
  return (
    <div className="flex flex-col gap-2">
      <Consultado en={fuente.consultado_en} />
      {detalle}
    </div>
  );
}

/**
 * UA-3: rótulo de la tarjeta. Con varias entradas de la misma fuente (BE-C-1: una por propietario), con su ordinal: «SIMIT
 * del propietario 1 de 2». Nunca el documento, para no ponerlo en el nombre de un botón.
 */
function rotuloConOrdinal(fuentes: readonly FuenteDeudaPanel[], i: number): string {
  const base = ROTULO_FUENTE[fuentes[i].fuente];
  const mismas = fuentes.filter((f) => f.fuente === fuentes[i].fuente);
  if (mismas.length < 2) return base;
  return `${base} ${mismas.indexOf(fuentes[i]) + 1} de ${mismas.length}`;
}

export interface SeccionesDeudaProps {
  /** Respuesta de GET …/deuda (o la del backoffice): solo se leen el dictamen y las fuentes. */
  deuda: Pick<PanelDeuda, "dictamen" | "fuentes">;
}

/**
 * Dictamen (oculto en modo Simple o sin dictamen vigente) y deuda por fuente: una tarjeta plegable por fuente, en orden
 * fijo, con su estado en texto; abiertas las que tienen hallazgo. Sin hallazgos ni motivos, «Sin pendientes»: el panel
 * nunca queda vacío (S4).
 */
export function SeccionesDeuda({ deuda }: SeccionesDeudaProps) {
  const { dictamen } = deuda;
  const id = useId();
  const fuentes = ORDEN_FUENTES.flatMap((clave) => deuda.fuentes.filter((f) => f.fuente === clave && ESTADO_FUENTE[f.estado]));
  const bloquean = dictamen?.motivos.filter((m) => m.efecto === "BLOQUEA") ?? [];
  const advierten = dictamen?.motivos.filter((m) => m.efecto !== "BLOQUEA") ?? [];

  return (
    <div className="flex flex-col gap-6">
      {dictamen && (
        <section aria-labelledby={`${id}-dictamen`} className="flex flex-col gap-3">
          <h3 id={`${id}-dictamen`} className="text-base font-semibold text-flit-primary">
            Dictamen
          </h3>
          <div className="flex flex-wrap items-center gap-3">
            <EtiquetaTono tono={dictamen.veredicto === "BLOQUEADO" ? "danger" : "success"}>
              {dictamen.veredicto === "BLOQUEADO" ? "Bloqueado" : "Viable"}
            </EtiquetaTono>
            <span className={AYUDA_CLS}>{`Versión del catálogo ${dictamen.catalogo_version}`}</span>
          </div>
          {dictamen.motivos.length > 0 && (
            <ul aria-label="Motivos del dictamen" className="flex flex-col gap-2">
              {bloquean.map((m, i) => (
                <li key={`b-${i}`} className="flex items-start gap-2 text-sm text-flit-primary">
                  <Icono nombre="candado" size={16} className="mt-0.5 shrink-0 text-[color:var(--di-danger)]" />
                  <span>
                    <span className="font-semibold">Bloquea:</span> {m.texto}
                  </span>
                </li>
              ))}
              {advierten.map((m, i) => (
                <li key={`a-${i}`} className="flex items-start gap-2 text-sm text-flit-primary">
                  <Icono nombre="alerta" size={16} className="mt-0.5 shrink-0 text-[color:var(--badge-warning-fg)]" />
                  <span>
                    <span className="font-semibold">Advertencia:</span> {m.texto}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
      <section aria-labelledby={`${id}-fuentes`} className="flex flex-col gap-3">
        <h3 id={`${id}-fuentes`} className="text-base font-semibold text-flit-primary">
          Deuda por fuente
        </h3>
        {!hayPendientes(deuda) && (
          <InlineAlert tone="success" title="Sin pendientes">
            No encontramos deudas ni pendientes en las fuentes consultadas.
          </InlineAlert>
        )}
        {fuentes.map((fuente, i) => {
          const estado = ESTADO_FUENTE[fuente.estado];
          const rotulo = rotuloConOrdinal(fuentes, i);
          return (
            <TarjetaPlegable
              // UA-3: puede haber varias entradas de la misma fuente (una por propietario): la key no se repite.
              key={`${fuente.fuente}-${i}`}
              nivelTitulo={4}
              abiertaInicial={estado.abierta}
              titulo={
                <span className="flex flex-wrap items-center gap-2">
                  <span>{rotulo}</span>
                  <span className="sr-only">: </span>
                  <EtiquetaTono tono={estado.tono}>{estado.texto}</EtiquetaTono>
                </span>
              }
            >
              <ContenidoFuente fuente={fuente} rotulo={rotulo} />
            </TarjetaPlegable>
          );
        })}
      </section>
    </div>
  );
}
