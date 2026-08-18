'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { FineDetailList, preflightOverall, statusPillWord } from '@/components/operacion/PreflightPanel';
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
  FieldValue,
  PreflightCheckStatus,
  PreflightSnapshot,
} from '@/lib/api/types/procedure-runtime';

/**
 * Sección «Especificaciones técnicas y pre-vuelo» del modal de detalle de trámite (Paso 1 de la
 * propuesta, Frente C).
 *
 * Dos llamadas independientes (`getInstance` para `fieldValues` + `getPreflight`): un trámite
 * puede tener especificaciones sin pre-vuelo ejecutado o viceversa, y el fallo de una no debe
 * borrar lo que sí llegó de la otra — por eso cada tarjeta lleva su propio estado de
 * carga/error/vacío, igual que `TramiteDetalleComercial`.
 *
 * Placa, VIN y marca/línea NO se repiten aquí: ya los pinta la tarjeta lateral del modal.
 */

/** Tono semántico por estado del check, mismo criterio que `PreflightPanel` (no exportado allá). */
const CHECK_STATUS_TONE: Record<PreflightCheckStatus, StatusTone> = {
  ok: 'success',
  warn: 'warning',
  fail: 'danger',
  unknown: 'warning',
  error: 'danger',
};

/** Umbral para decidir si el mensaje del check cabe junto a la etiqueta o va en su propia línea. */
const MENSAJE_CORTO_MAX = 60;

/**
 * Claves de `fieldValues` que alimentan las especificaciones técnicas, en el MISMO orden y con las
 * MISMAS claves que usa el resumen previo a la radicación (`MatriculaResumen`/`FirmaFurStep`,
 * casilla 19 del FUR incluida). Las claves ausentes en el trámite se omiten — nunca se inventan.
 */
function buildEspecificaciones(fieldValues: FieldValue[]): { campo: string; valor: string }[] {
  const valorDe = (key: string) => fieldValues.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';

  const servicio = valorDe('vehicle_service');
  // Casilla 19 del FUR: la empresa vinculadora solo aplica con servicio Público o Especial (mismo
  // criterio que `MatriculaResumen`).
  const requiereEmpresaVinculadora = ['PUBLICO', 'ESPECIAL'].includes(servicio.toUpperCase());
  const cilindraje = valorDe('vehicle_engine_displacement');

  return [
    { campo: 'Clase', valor: valorDe('vehicle_class') },
    { campo: 'Servicio', valor: servicio },
    ...(requiereEmpresaVinculadora
      ? [
          { campo: 'Empresa vinculadora', valor: valorDe('empresa_vinculadora_razon_social') },
          { campo: 'NIT empresa vinculadora', valor: valorDe('empresa_vinculadora_nit') },
        ]
      : []),
    {
      campo: 'Cilindraje',
      valor: cilindraje ? (cilindraje.includes('cc') ? cilindraje : `${cilindraje} cc`) : '',
    },
    { campo: 'Combustible', valor: valorDe('vehicle_fuel') },
    { campo: 'Carrocería', valor: valorDe('vehicle_body_type') },
    { campo: 'Capacidad', valor: valorDe('vehicle_passengers') },
    { campo: 'Ejes', valor: valorDe('vehicle_axles') },
    { campo: 'Estado', valor: valorDe('vehicle_state') },
    { campo: 'N. Motor', valor: valorDe('vehicle_engine_number') },
    { campo: 'N. Chasis', valor: valorDe('vehicle_chassis') },
    { campo: 'N. Serie', valor: valorDe('vehicle_series') },
    // Solo las que sí llegaron: una clave ausente en `fieldValues` se omite, nunca se deja en «—».
  ].filter((s) => s.valor !== '');
}

interface CargaEstado<T> {
  loading: boolean;
  error: string | null;
  data: T | null;
}

export function TramiteDetalleVehiculo({ instanceId, tenantId }: SeccionDetalleProps) {
  const [especificaciones, setEspecificaciones] = useState<CargaEstado<FieldValue[]>>({
    loading: true,
    error: null,
    data: null,
  });
  const [preflight, setPreflight] = useState<CargaEstado<PreflightSnapshot | null>>({
    loading: true,
    error: null,
    data: null,
  });
  // Incrementan para forzar un nuevo intento de carga desde "Reintentar" sin duplicar el efecto.
  const [especificacionesIntento, setEspecificacionesIntento] = useState(0);
  const [preflightIntento, setPreflightIntento] = useState(0);

  useEffect(() => {
    let active = true;
    // setState dentro de la función async (no en el cuerpo síncrono del effect), mismo patrón que
    // TramiteDetalleComercial, para no disparar react-hooks/set-state-in-effect.
    const load = async () => {
      setEspecificaciones((s) => ({ ...s, loading: true, error: null }));
      try {
        const detail = await tramitesClient.getInstance(instanceId, tenantId);
        if (active) setEspecificaciones({ loading: false, error: null, data: detail.fieldValues });
      } catch (err) {
        if (active) {
          setEspecificaciones({
            loading: false,
            error: err instanceof Error ? err.message : 'No se pudieron cargar las especificaciones técnicas.',
            data: null,
          });
        }
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [instanceId, tenantId, especificacionesIntento]);

  useEffect(() => {
    let active = true;
    const load = async () => {
      setPreflight((s) => ({ ...s, loading: true, error: null }));
      try {
        const snapshot = await tramitesClient.getPreflight(instanceId, tenantId);
        if (active) setPreflight({ loading: false, error: null, data: snapshot });
      } catch (err) {
        if (active) {
          setPreflight({
            loading: false,
            error: err instanceof Error ? err.message : 'No se pudo cargar el pre-vuelo de requisitos.',
            data: null,
          });
        }
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [instanceId, tenantId, preflightIntento]);

  const specs = especificaciones.data ? buildEspecificaciones(especificaciones.data) : [];
  const overall = preflight.data ? preflightOverall(preflight.data.overall) : null;
  const checks = preflight.data?.checks ?? [];

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <TarjetaDetalle titulo="Especificaciones técnicas">
        {especificaciones.loading ? (
          <SeccionCargando etiqueta="Cargando especificaciones técnicas" />
        ) : especificaciones.error ? (
          <SeccionError
            mensaje={especificaciones.error}
            onReintentar={() => setEspecificacionesIntento((n) => n + 1)}
          />
        ) : specs.length === 0 ? (
          <SeccionVacia mensaje="Este trámite no tiene especificaciones técnicas del vehículo registradas todavía." />
        ) : (
          <ListaCampos>
            {specs.map((s) => (
              <CampoValor key={s.campo} campo={s.campo} valor={s.valor} />
            ))}
          </ListaCampos>
        )}
      </TarjetaDetalle>

      <TarjetaDetalle
        titulo="Pre-vuelo de requisitos"
        accion={overall ? <StatusBadge label={overall.label} tone={overall.tone} /> : null}
      >
        {preflight.loading ? (
          <SeccionCargando etiqueta="Cargando pre-vuelo de requisitos" filas={4} />
        ) : preflight.error ? (
          <SeccionError
            mensaje={preflight.error}
            onReintentar={() => setPreflightIntento((n) => n + 1)}
          />
        ) : !preflight.data ? (
          <SeccionVacia mensaje="Este trámite no tiene un pre-vuelo de requisitos ejecutado (RUNT/SIMIT/RNMC). Se ejecuta desde el asistente, no desde este detalle." />
        ) : checks.length === 0 ? (
          <SeccionVacia mensaje="El pre-vuelo no registró resultados individuales por requisito." />
        ) : (
          <ul className="flex flex-col gap-2" aria-label="Resultados del pre-vuelo">
            {checks.map((check) => {
              const tone = CHECK_STATUS_TONE[check.status];
              const word = statusPillWord(check.status);
              const message = check.message?.trim() ?? '';
              const inline = message.length > 0 && message.length <= MENSAJE_CORTO_MAX;
              return (
                <li key={check.key} className="rounded-xl border px-3 py-2 border-[#DFE5ED] dark:border-white/10">
                  <div className="flex items-center justify-between gap-2">
                    <span className="min-w-0 text-xs font-medium text-[#162744] dark:text-white">
                      {check.label}
                      {inline && (
                        <span className="font-normal text-[#162744]/70 dark:text-white/70"> — {message}</span>
                      )}
                    </span>
                    <StatusBadge
                      label={word}
                      tone={tone}
                      ariaLabel={`${check.label}: ${word}`}
                      className="shrink-0"
                    />
                  </div>
                  {!inline && message && (
                    <p className="mt-1 text-xs text-[#162744]/70 dark:text-white/70">{message}</p>
                  )}
                  {check.details && check.details.length > 0 && <FineDetailList details={check.details} />}
                </li>
              );
            })}
          </ul>
        )}
      </TarjetaDetalle>
    </div>
  );
}
