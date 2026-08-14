'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { formatCOP } from '@/lib/format/currency';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import {
  TarjetaDetalle,
  CampoValor,
  ListaCampos,
  SeccionCargando,
  SeccionError,
  SeccionVacia,
  type SeccionDetalleProps,
} from './primitivos';
import type {
  CommercialCausal,
  CommercialData,
  PrendaData,
  PrendaDecision,
} from '@/lib/api/types/procedure-runtime';

/**
 * Sección «Datos comerciales» del modal de detalle de trámite (Paso 4 de la propuesta, Frente C).
 *
 * Dos llamadas independientes (`getCommercial` + `getPrenda`): un trámite puede tener datos
 * comerciales sin decisión de prenda, o viceversa, y el fallo de una no debe borrar lo que sí llegó
 * de la otra — por eso cada tarjeta lleva su propio estado de carga/error/vacío.
 */

/** Etiqueta legible de la causal comercial (mismo catálogo cerrado que `CommercialForm`). */
const CAUSAL_LABELS: Record<CommercialCausal, string> = {
  COMPRAVENTA: 'Compraventa',
  DONACION: 'Donación',
  DACION_EN_PAGO: 'Dación en pago',
  ADJUDICACION: 'Adjudicación',
};

/** Etiqueta legible de la decisión de prenda (mismo catálogo cerrado que `PrendaForm`). */
const PRENDA_DECISION_LABELS: Record<PrendaDecision, string> = {
  solicitar: 'Solicitar constitución de prenda',
  registrar: 'Registrar prenda',
  levantar: 'Levantar gravamen',
  omitir: 'Continuar sin gestionar (asumo el riesgo)',
  sin_prenda: 'Sin prenda',
};

/**
 * Tono semántico por decisión: `sin_prenda`/`levantar` son un resultado favorable (sin gravamen
 * vigente), `solicitar`/`registrar` señalan un gravamen activo o en trámite, y `omitir` es una
 * advertencia (riesgo asumido por el gestor).
 */
const PRENDA_DECISION_TONE: Record<PrendaDecision, StatusTone> = {
  sin_prenda: 'success',
  levantar: 'success',
  registrar: 'warning',
  solicitar: 'warning',
  omitir: 'danger',
};

function esComercialVacio(data: CommercialData | null): boolean {
  if (!data) return true;
  return (
    data.valorVenta == null &&
    data.causal == null &&
    data.tasaImpuesto == null &&
    data.derechos == null &&
    data.metodoPago == null
  );
}

interface CargaEstado<T> {
  loading: boolean;
  error: string | null;
  data: T | null;
}

export function TramiteDetalleComercial({ instanceId, tenantId }: SeccionDetalleProps) {
  const [comercial, setComercial] = useState<CargaEstado<CommercialData>>({
    loading: true,
    error: null,
    data: null,
  });
  const [prenda, setPrenda] = useState<CargaEstado<PrendaData>>({
    loading: true,
    error: null,
    data: null,
  });
  // Incrementa para forzar un nuevo intento de carga desde "Reintentar" sin duplicar el efecto.
  const [comercialIntento, setComercialIntento] = useState(0);
  const [prendaIntento, setPrendaIntento] = useState(0);

  useEffect(() => {
    let active = true;
    // setState dentro de la función async (no en el cuerpo síncrono del effect), mismo patrón que
    // CommercialForm/PrendaForm, para no disparar react-hooks/set-state-in-effect.
    const load = async () => {
      setComercial((s) => ({ ...s, loading: true, error: null }));
      try {
        const data = await tramitesClient.getCommercial(instanceId, tenantId);
        if (active) setComercial({ loading: false, error: null, data: data ?? null });
      } catch (err) {
        if (active) {
          setComercial({
            loading: false,
            error: err instanceof Error ? err.message : 'No se pudieron cargar los datos comerciales.',
            data: null,
          });
        }
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [instanceId, tenantId, comercialIntento]);

  useEffect(() => {
    let active = true;
    const load = async () => {
      setPrenda((s) => ({ ...s, loading: true, error: null }));
      try {
        const data = await tramitesClient.getPrenda(instanceId, tenantId);
        if (active) setPrenda({ loading: false, error: null, data: data ?? null });
      } catch (err) {
        if (active) {
          setPrenda({
            loading: false,
            error: err instanceof Error ? err.message : 'No se pudo cargar la decisión de prenda.',
            data: null,
          });
        }
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [instanceId, tenantId, prendaIntento]);

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <TarjetaDetalle titulo="Datos comerciales">
        {comercial.loading ? (
          <SeccionCargando etiqueta="Cargando datos comerciales" />
        ) : comercial.error ? (
          <SeccionError
            mensaje={comercial.error}
            onReintentar={() => setComercialIntento((n) => n + 1)}
          />
        ) : esComercialVacio(comercial.data) ? (
          <SeccionVacia mensaje="Este trámite no tiene datos comerciales registrados (p. ej. una matrícula inicial no captura valor de venta ni causal)." />
        ) : (
          <ListaCampos>
            <CampoValor campo="Valor de venta" valor={formatCOP(comercial.data!.valorVenta)} />
            <CampoValor
              campo="Causal"
              valor={comercial.data!.causal ? CAUSAL_LABELS[comercial.data!.causal] : null}
            />
            <CampoValor
              campo="Tasa de impuestos"
              valor={comercial.data!.tasaImpuesto != null ? `${comercial.data!.tasaImpuesto}%` : null}
            />
            <CampoValor campo="Derechos de trámite" valor={formatCOP(comercial.data!.derechos)} />
            <CampoValor campo="Método de pago" valor={comercial.data!.metodoPago} />
          </ListaCampos>
        )}
      </TarjetaDetalle>

      <TarjetaDetalle titulo="Prenda / gravamen">
        {prenda.loading ? (
          <SeccionCargando etiqueta="Cargando decisión de prenda" filas={2} />
        ) : prenda.error ? (
          <SeccionError
            mensaje={prenda.error}
            onReintentar={() => setPrendaIntento((n) => n + 1)}
          />
        ) : !prenda.data ? (
          <SeccionVacia mensaje="Este trámite no tiene una decisión de prenda o gravamen registrada." />
        ) : (
          <div className="flex flex-col gap-1.5">
            <p className="text-xs text-[#162744]/70 dark:text-white/70">
              Decisión registrada por el operador durante la creación del trámite.
            </p>
            <StatusBadge
              label={PRENDA_DECISION_LABELS[prenda.data.decision]}
              tone={PRENDA_DECISION_TONE[prenda.data.decision]}
            />
            {/* Quién es el acreedor SÍ se muestra cuando existe. La propuesta no lo dibuja porque
                su maqueta no tenía prenda, pero en un gravamen real saber a favor de quién está
                es la mitad del dato: una prenda sin acreedor no dice nada operativo. */}
            {prenda.data.acreedorNombre || prenda.data.acreedorDocumento ? (
              <div className="mt-2 border-t pt-2" style={{ borderColor: '#DFE5ED' }}>
                <ListaCampos>
                  {prenda.data.acreedorNombre ? (
                    <CampoValor campo="Acreedor" valor={prenda.data.acreedorNombre} />
                  ) : null}
                  {prenda.data.acreedorDocumento ? (
                    <CampoValor campo="Documento" valor={prenda.data.acreedorDocumento} />
                  ) : null}
                </ListaCampos>
              </div>
            ) : null}
          </div>
        )}
      </TarjetaDetalle>
    </div>
  );
}
