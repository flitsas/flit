"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EtiquetaEnProceso } from "@flit/ui/EtiquetaEnProceso";
import { InlineAlert, type InlineAlertTone } from "@flit/ui/InlineAlert";
import { ListaDefinicion } from "@flit/ui/ListaDefinicion";
import { VehicleLoader } from "@flit/ui/Loader";
import { Placa } from "@flit/ui/Placa";
import { TarjetaPlegable } from "@flit/ui/TarjetaPlegable";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import {
  BOTON_REINTENTAR_CLS,
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  CONTENEDOR_TABLA_CLS,
  ENLACE_ACCION_CLS,
  FILA_TABLA_CLS,
  TARJETA_CLS,
  TEXTO_SECUNDARIO_CLS,
} from "@/components/comun/estilos";
import { ChipEstado, Marca } from "@/components/liquidaciones/ChipEstado";
import { DialogoRelanzar } from "@/components/liquidaciones/DialogoRelanzar";
import { EligeCompania } from "@/components/liquidaciones/EligeCompania";
import { ListaRecibos } from "@/components/liquidaciones/ListaRecibos";
import { Monto, TablaVigencias } from "@/components/liquidaciones/TablaVigencias";
import { ApiError } from "@/lib/api";
import { hrefConEmpresa, useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { esErrorDeEmpresa, mensajeErrorFijo } from "@/lib/errores";
import { obtenerItem, obtenerLiquidacion, useCargaConSondeo } from "@/lib/liquidaciones/api";
import {
  ERRORES_LIQUIDACIONES,
  PANTALLA,
  TITULO_PLACA_NO_INSCRITA,
  chipItem,
  esPlacaNoInscrita,
  esRelanzable,
  itemEnCurso,
  mensajeError,
  nombreCampoFaltante,
  nombreClaseError,
  nombreTipo,
} from "@/lib/liquidaciones/estados";
import { formatoDocumento, formatoFecha, formatoFechaDia, formatoHora, formatoSecretaria, idCorto } from "@/lib/liquidaciones/formato";
import type { Departamento, IntentoItem, ItemDetalle, Liquidacion } from "@/lib/liquidaciones/tipos";

interface Props {
  liquidacionId: string;
  itemId: string;
  /** impuestos.liquidaciones.crear: «Relanzar». */
  puedeRelanzar: boolean;
  /** impuestos.recibos.descargar: «Descargar recibo». */
  puedeDescargar: boolean;
  /** El Super Admin ve además «Código: {errorCodigo}». */
  esSuperAdmin: boolean;
}

interface Vista {
  item: ItemDetalle;
  /** Cabecera del lote para la miga y el tipo; null si no se pudo leer (no bloquea la pantalla). */
  lote: Liquidacion | null;
}

const TITULO_CLS = "text-lg font-semibold text-flit-primary";

/** Observaciones del proveedor: textos de `warnings`, `validations` (campo y mensaje; nunca `sent`) y `file_errors`. */
function observaciones(resultado: Record<string, unknown> | null) {
  const lista = (clave: string) => (Array.isArray(resultado?.[clave]) ? (resultado?.[clave] as unknown[]) : []);
  const texto = (v: unknown, k: string) => (v && typeof v === "object" && typeof (v as Record<string, unknown>)[k] === "string" ? ((v as Record<string, unknown>)[k] as string) : null);
  const advertencias = lista("warnings").map((w) => texto(w, "message")).filter((t): t is string => Boolean(t));
  const validaciones = lista("validations")
    .map((v) => {
      const mensaje = texto(v, "message");
      const campo = texto(v, "field");
      return mensaje ? (campo ? `${campo}: ${mensaje}` : mensaje) : null;
    })
    .filter((t): t is string => Boolean(t));
  const archivos = lista("file_errors").map((f) => texto(f, "message")).filter((t): t is string => Boolean(t));
  return { advertencias, validaciones, archivos, hay: advertencias.length + validaciones.length + archivos.length > 0 };
}

/** Aviso de NO_APLICA (1.3.0, servicio público): informativo, no es un error. */
const AVISO_NO_APLICA = {
  titulo: "No aplica",
  texto:
    "El RUNT reporta el vehículo como de servicio público. Los vehículos de servicio público no pagan impuesto vehicular, por eso no se consultó al proveedor.",
} as const;

const TEXTO_POSIBLE_DUPLICADO = "Esta liquidación es un relanzamiento: puede estar duplicada en el proveedor.";

/**
 * Ruta del ítem original de un relanzamiento (contrato 1.2.0, `relanzadoDesdeLiquidacionId` + `relanzadoDesdeItemId`;
 * H-17), con la `empresa` del Super Admin. null si falta cualquiera de los dos ids: sin enlace.
 */
function rutaOriginal(item: ItemDetalle, empresa: string | null): string | null {
  const liquidacion = item.relanzadoDesdeLiquidacionId;
  const original = item.relanzadoDesdeItemId;
  if (!liquidacion || !original) return null;
  return hrefConEmpresa(`/liquidaciones/${encodeURIComponent(liquidacion)}/items/${encodeURIComponent(original)}`, empresa);
}

/** Aviso de resultado según el estado (spec-visual §3.6), estático (vivo=false). */
function avisos(item: ItemDetalle, empresa: string | null): Array<{ tono: InlineAlertTone; titulo?: string; texto: ReactNode }> {
  const lista: Array<{ tono: InlineAlertTone; titulo?: string; texto: ReactNode }> = [];
  if (itemEnCurso(item.estado) && item.estado !== "INCIERTO")
    lista.push({
      tono: "info",
      texto: (
        <span className="flex flex-wrap items-center gap-2">
          <EtiquetaEnProceso nombreAccesible="Placa en proceso" />
          Esperando el resultado del proveedor. Esta página se actualiza sola.
        </span>
      ),
    });
  if (item.estado === "INCIERTO") lista.push({ tono: "pending", texto: PANTALLA.detalleIncierto });
  if (item.estado === "INCIERTO_REQUIERE_DECISION")
    lista.push({ tono: "warning", texto: "No se pudo confirmar si el proveedor recibió la solicitud. Puedes relanzarla; si sí la recibió, puede quedar duplicada." });
  if (item.estado === "ERROR")
    lista.push(
      esPlacaNoInscrita(item)
        ? { tono: "warning", titulo: TITULO_PLACA_NO_INSCRITA, texto: mensajeError(item) }
        : {
            tono: "error",
            titulo: item.errorClase ? nombreClaseError(item.errorClase) : "El proveedor no pudo liquidar esta placa",
            texto: mensajeError(item),
          },
    );
  if (item.estado === "DATOS_INCOMPLETOS") lista.push({ tono: "warning", texto: `Faltan datos del RUNT para liquidar: ${nombreCampoFaltante(item.campoFaltante)}.` });
  if (item.estado === "DEPARTAMENTO_NO_HABILITADO")
    lista.push({ tono: "info", texto: `El departamento ${item.departamento?.nombre ?? ""} no está habilitado en el contrato de tu compañía.`.replace("  ", " ") });
  if (item.estado === "NO_APLICA") lista.push({ tono: "info", titulo: AVISO_NO_APLICA.titulo, texto: AVISO_NO_APLICA.texto });
  if (item.estado === "SIN_RESULTADO_LOTE_CERRADO") lista.push({ tono: "info", texto: "El lote se cerró antes de recibir el resultado de esta placa." });
  if (item.parcial) lista.push({ tono: "pending", texto: "Resultado parcial: el proveedor no pudo liquidar todas las vigencias." });
  if (item.resultadoTardio) lista.push({ tono: "info", texto: "Este resultado llegó después de cerrar el lote." });
  // posibleDuplicado solo llega true en relanzamientos anteriores a la 1.3.0 (datos viejos); desde entonces siempre false.
  if (item.posibleDuplicado) {
    const original = rutaOriginal(item, empresa);
    lista.push({
      tono: "warning",
      texto: original ? (
        <span className="flex flex-wrap items-center gap-x-2">
          {TEXTO_POSIBLE_DUPLICADO}
          <Link href={original} className={ENLACE_ACCION_CLS}>
            Ver la original
          </Link>
        </span>
      ) : (
        TEXTO_POSIBLE_DUPLICADO
      ),
    });
  }
  return lista;
}

const CELDA_INTENTO_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;
const COLUMNAS_INTENTOS = ["Intento", "Estado", "Mensaje", "Total a pagar", "Fecha", "Relanzó"] as const;

/** Mensaje de un intento: el del proveedor o el texto propio del código; «—» si el intento no tuvo error. */
function mensajeIntento(intento: IntentoItem, departamento: Departamento | null): string {
  if (!intento.errorCodigo && !intento.errorMensaje?.trim()) return "—";
  return mensajeError({ estado: intento.estado, errorCodigo: intento.errorCodigo, errorMensaje: intento.errorMensaje, departamento });
}

/**
 * Historial de intentos (contrato 1.3.0, spec-visual §3.6): cada relanzamiento cierra el intento en curso y lo deja aquí,
 * el más reciente primero. Una fila por intento con su número, el chip del estado en que quedó, el mensaje, el total
 * («Sin dato» si es null), cuándo se relanzó y quién. Sin datos del propietario.
 */
function HistorialIntentos({ intentos, departamento }: { intentos: IntentoItem[]; departamento: Departamento | null }) {
  return (
    <section aria-labelledby="item-intentos" className={`${TARJETA_CLS} flex flex-col gap-3`}>
      <h2 id="item-intentos" className={TITULO_CLS}>
        Historial de intentos
      </h2>
      <div className={CONTENEDOR_TABLA_CLS}>
        <table aria-label="Historial de intentos" className={`w-full min-w-[760px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
          <thead className={TABLA_MOVIL_THEAD_CLS}>
            <tr>
              {COLUMNAS_INTENTOS.map((t) => (
                <th key={t} scope="col" className={`${CABECERA_TABLA_CLS} ${t === "Total a pagar" ? "text-right" : ""}`} style={CABECERA_TABLA_ESTILO}>
                  {t}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className={TABLA_MOVIL_TBODY_CLS}>
            {intentos.map((intento) => (
              <tr key={intento.numero} className={`${FILA_TABLA_CLS} bg-card ${TABLA_MOVIL_ROW_CLS}`}>
                <th scope="row" data-label="Intento" className={`${CELDA_INTENTO_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} text-left font-semibold tabular-nums`}>
                  {intento.numero}
                </th>
                <td data-label="Estado" className={CELDA_INTENTO_CLS}>
                  <ChipEstado chip={chipItem({ estado: intento.estado, resultadoStatus: null, errorCodigo: intento.errorCodigo, errorMensaje: intento.errorMensaje })} />
                </td>
                <td data-label="Mensaje" className={CELDA_INTENTO_CLS}>
                  {mensajeIntento(intento, departamento)}
                </td>
                <td data-label="Total a pagar" className={`${CELDA_INTENTO_CLS} text-right`}>
                  <Monto valor={intento.totalAPagar} />
                </td>
                <td data-label="Fecha" className={`${CELDA_INTENTO_CLS} whitespace-nowrap tabular-nums`}>
                  <time dateTime={intento.registradoEn}>{formatoFecha(intento.registradoEn)}</time>
                </td>
                <td data-label="Relanzó" className={CELDA_INTENTO_CLS}>
                  {intento.relanzadoPor?.trim() || "—"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/**
 * Detalle de la placa (spec-visual §3.6, F2b-CF7). Cabecera con la placa, el chip vivo del estado, las marcas y
 * «Relanzar» (solo en placas relanzables y con permiso). Aviso de resultado por estado; vehículo y propietario (sin
 * dirección, correo ni teléfono); totales y vigencias con «Sin dato» para cada null; recibos por el BFF; observaciones
 * del proveedor plegadas. Contrato 1.3.0: «Intento N» cuando la placa se relanzó (numeroIntento > 1), el historial de
 * intentos cerrados y el aviso informativo de NO_APLICA (servicio público). Relanzar no navega: se relee la misma placa
 * y el sondeo sigue al nuevo intento. Se sondea mientras la placa espera resultado. 404 → notFound(); otro error con Reintentar.
 * El Super Admin lee con ?empresa (regla del backend 1.1.0): sin compañía elegida no se pide nada y se le pide elegir
 * una; un error de compañía (p. ej. 404 EMPRESA_NO_ENCONTRADA) va en línea con su literal, no a la 404.
 */
export function DetalleItem(props: Props) {
  const { empresa } = useEmpresaSeleccionada();
  if (props.esSuperAdmin && !empresa) return <EligeCompania />;
  return <VistaItem {...props} />;
}

function VistaItem({ liquidacionId, itemId, puedeRelanzar, puedeDescargar, esSuperAdmin }: Props) {
  const { empresa } = useEmpresaSeleccionada();
  const [relanzando, setRelanzando] = useState(false);
  const { carga, reintentar } = useCargaConSondeo<Vista>(
    `${liquidacionId}/${itemId}/${empresa ?? ""}`,
    async (signal) => {
      const [item, lote] = await Promise.all([
        obtenerItem(liquidacionId, itemId, { empresa, signal }),
        obtenerLiquidacion(liquidacionId, { empresa, signal }).catch(() => null),
      ]);
      return { item, lote };
    },
    (v) => itemEnCurso(v.item.estado),
  );

  if (carga.tipo === "cargando") return <VehicleLoader label="Cargando la placa…" />;
  if (carga.tipo === "error") {
    if (carga.error instanceof ApiError && carga.error.status === 404 && !esErrorDeEmpresa(carga.error)) notFound();
    return (
      <InlineAlert
        tone="error"
        title="No se pudo cargar la placa"
        action={
          <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {mensajeErrorFijo(carga.error, "Revisa tu conexión e intenta de nuevo.", ERRORES_LIQUIDACIONES)}
      </InlineAlert>
    );
  }

  const { item, lote } = carga.dato;
  const masiva = lote?.tipo === "MASIVA";
  const obs = observaciones(item.resultado);
  const vencimientos = item.periodos.map((p) => p.vencimiento).filter((v): v is string => Boolean(v)).sort();
  const pendientes = item.periodos.filter((p) => !p.noLiquidable).length;
  const relanzable = puedeRelanzar && esRelanzable(item, lote?.estado);
  const numeroIntento = item.numeroIntento ?? 1;
  const intentos = item.intentos ?? [];

  return (
    <div className="flex flex-col gap-4">
      <CabeceraModulo
        titulo={item.placa}
        tituloClassName="uppercase tracking-[0.12em]"
        icono="pesos"
        descripcion={`${lote ? `Liquidación ${nombreTipo(lote.tipo).toLowerCase()} · ` : ""}${formatoFecha(item.creadaEn)}${esSuperAdmin ? ` · ${item.empresa.nombre}` : ""}`}
        miga={[
          { etiqueta: "Liquidaciones", href: hrefConEmpresa("/liquidaciones", empresa) },
          ...(masiva ? [{ etiqueta: `Lote ${idCorto(liquidacionId)}`, href: hrefConEmpresa(`/liquidaciones/${encodeURIComponent(liquidacionId)}`, empresa) }] : []),
          { etiqueta: item.placa },
        ]}
        acciones={
          <div className="flex flex-wrap items-center gap-2">
            <ChipEstado chip={chipItem(item)} vivo />
            {numeroIntento > 1 ? <Marca tono="neutral">{`Intento ${numeroIntento}`}</Marca> : null}
            {item.parcial ? <Marca tono="purple">{PANTALLA.marcaParcial}</Marca> : null}
            {item.posibleDuplicado ? <Marca tono="warning">{PANTALLA.marcaPosibleDuplicado}</Marca> : null}
            {item.resultadoTardio ? <Marca tono="info">{PANTALLA.marcaResultadoTardio}</Marca> : null}
            {relanzable ? (
              <Button size="sm" onClick={() => setRelanzando(true)}>
                {PANTALLA.accionRelanzar}
              </Button>
            ) : null}
          </div>
        }
      />
      {carga.desactualizadoDesde ? (
        <InlineAlert
          tone="warning"
          vivo={false}
          title={`Datos desactualizados desde ${formatoHora(carga.desactualizadoDesde)}`}
          action={
            <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
              Reintentar
            </button>
          }
        >
          Se muestra lo último que se leyó.
        </InlineAlert>
      ) : null}
      {avisos(item, empresa).map((a, i) => (
        <InlineAlert key={i} tone={a.tono} title={a.titulo} vivo={false}>
          {a.texto}
        </InlineAlert>
      ))}
      {esSuperAdmin && item.errorCodigo ? <p className={TEXTO_SECUNDARIO_CLS}>{`Código: ${item.errorCodigo}`}</p> : null}
      <div className="grid gap-4 lg:grid-cols-2">
        <section aria-labelledby="item-vehiculo" className={`${TARJETA_CLS} flex flex-col gap-3`}>
          <h2 id="item-vehiculo" className={TITULO_CLS}>
            Vehículo y propietario
          </h2>
          <ListaDefinicion
            columnas={2}
            vacio="Sin dato"
            items={[
              { termino: "Placa", valor: <Placa placa={item.placa} /> },
              { termino: "Departamento", valor: item.departamento?.nombre },
              // Secretaria (contrato 1.1.0, delta 5): hoy el backend solo trae la ciudad; el nombre llega null.
              { termino: "Secretaría", valor: formatoSecretaria(item.secretaria) },
              { termino: "Propietario", valor: item.propietario?.nombre },
              {
                termino: "Documento",
                valor: item.propietario?.documentoNumero ? formatoDocumento(item.propietario.documentoTipo, item.propietario.documentoNumero) : null,
              },
            ]}
          />
        </section>
        <section aria-labelledby="item-totales" className={`${TARJETA_CLS} flex flex-col gap-3`}>
          <h2 id="item-totales" className={TITULO_CLS}>
            Totales
          </h2>
          <ListaDefinicion
            vacio="Sin dato"
            items={[
              { termino: "Total a pagar", valor: <Monto valor={item.totalAPagar} className="text-base font-bold" /> },
              { termino: "Vigencias pendientes", valor: String(pendientes) },
              { termino: "Vencimiento más próximo", valor: vencimientos[0] ? formatoFechaDia(vencimientos[0]) : null },
            ]}
          />
        </section>
      </div>
      <section aria-labelledby="item-vigencias" className={`${TARJETA_CLS} flex flex-col gap-3`}>
        <h2 id="item-vigencias" className={TITULO_CLS}>
          Vigencias
        </h2>
        <TablaVigencias periodos={item.periodos} />
      </section>
      <section aria-labelledby="item-recibos" className={`${TARJETA_CLS} flex flex-col gap-3`}>
        <h2 id="item-recibos" className={TITULO_CLS}>
          Recibos
        </h2>
        <ListaRecibos liquidacionId={liquidacionId} itemId={itemId} recibos={item.recibos} empresa={empresa} puedeDescargar={puedeDescargar} />
      </section>
      {intentos.length > 0 ? <HistorialIntentos intentos={intentos} departamento={item.departamento} /> : null}
      {obs.hay ? (
        <TarjetaPlegable titulo="Observaciones del proveedor" nivelTitulo={2}>
          <div className="flex flex-col gap-3 px-4 pb-4">
            {(
              [
                ["Advertencias", obs.advertencias],
                ["Validaciones", obs.validaciones],
                ["Archivos con error", obs.archivos],
              ] as const
            ).map(([titulo, textos]) =>
              textos.length > 0 ? (
                <div key={titulo} className="flex flex-col gap-1">
                  <h3 className="text-sm font-semibold text-flit-primary">{titulo}</h3>
                  <ul className="list-disc pl-5 text-sm text-flit-primary">
                    {textos.map((t, i) => (
                      <li key={i}>{t}</li>
                    ))}
                  </ul>
                </div>
              ) : null,
            )}
          </div>
        </TarjetaPlegable>
      ) : null}
      <DialogoRelanzar
        open={relanzando}
        liquidacionId={liquidacionId}
        item={item}
        empresa={empresa}
        onClose={() => setRelanzando(false)}
        onRelanzado={() => {
          // 1.3.0: el mismo registro en su nuevo intento; se relee la placa y el sondeo la sigue hasta el resultado.
          setRelanzando(false);
          reintentar();
        }}
      />
    </div>
  );
}
