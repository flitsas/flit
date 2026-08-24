'use client';

import { useEffect, useState } from 'react';
import { Loader2 } from 'lucide-react';
import { superadminClient } from '@/lib/api/superadmin-client';
import type {
  GateProfile,
  MapeoQuipux,
  ProcedureFamily,
} from '@/lib/api/types/procedure-parametrization';

const CAMPO =
  'w-full rounded-xl border px-3 py-2 text-xs border-[#DFE5ED] bg-white text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]';
const ETIQUETA = 'text-xs font-semibold text-[#162744] dark:text-white';

const FAMILIAS_QUIPUX = ['MATRICULA', 'TRASPASO', 'OTROS'];

/**
 * Equivalencia del tipo con Quipux — dónde y cómo se radica en la secretaría.
 *
 * Vivía solo en un DDL, así que dar de alta un trámite en la secretaría exigía una migración. Ahora
 * se guarda en `external_refs` junto al resto de la parametrización del tipo: un punto central en
 * vez de un catálogo por integración.
 *
 * Tres de los campos NO son derivables — `tipoTramite`, `tipoRequisito` y `prefijo` los asigna la
 * secretaría—, y por eso van separados de los que sí se proponen desde la parametrización. Sin
 * mapeo el trámite simplemente no se radica, que es un estado legítimo del catálogo.
 */
export function TipoTramiteQuipux({
  procedureTypeId,
  familiaFlit,
  gateProfile,
}: {
  procedureTypeId: string;
  familiaFlit: ProcedureFamily;
  gateProfile: GateProfile;
}) {
  const [mapeo, setMapeo] = useState<MapeoQuipux | null>(null);
  const [cargando, setCargando] = useState(true);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState<string | null>(null);

  useEffect(() => {
    let vivo = true;
    setCargando(true);
    setError(null);
    setOk(null);
    void superadminClient
      .getQuipuxMapping(procedureTypeId)
      .then((m) => {
        if (vivo) setMapeo(m ?? null);
      })
      .catch((e: unknown) => {
        if (vivo) setError(e instanceof Error ? e.message : 'No se pudo cargar la equivalencia.');
      })
      .finally(() => {
        if (vivo) setCargando(false);
      });
    return () => {
      vivo = false;
    };
  }, [procedureTypeId]);

  /**
   * Propuesta inicial al configurar por primera vez. Deriva lo que FLIT sí sabe —el identificador
   * del vehículo sale del `entryMode`, el tope de empresa es 25 en matrícula y 35 en el resto— y
   * deja en blanco los tres códigos que solo la secretaría puede dar.
   */
  const propuesta = (): MapeoQuipux => {
    const entraPorVin = (gateProfile.entryMode ?? '').toUpperCase() === 'VIN';
    return {
      familia: familiaFlit === 'MATRICULAS' ? 'MATRICULA' : familiaFlit,
      tipoTramite: 0,
      tipoRequisito: 51,
      prefijo: '',
      campoPlaca: entraPorVin ? null : 'plate',
      campoVin: entraPorVin ? 'vin' : null,
      maxLongitudEmpresa: entraPorVin ? 25 : 35,
    };
  };

  const cambiar = (parcial: Partial<MapeoQuipux>) => {
    setMapeo((m) => ({ ...(m ?? propuesta()), ...parcial }));
    setOk(null);
  };

  const guardar = async (valor: MapeoQuipux | null) => {
    setGuardando(true);
    setError(null);
    setOk(null);
    try {
      const guardado = await superadminClient.setQuipuxMapping(procedureTypeId, valor);
      setMapeo(guardado ?? null);
      setOk(valor ? 'Guardado' : 'Equivalencia retirada');
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar la equivalencia.');
    } finally {
      setGuardando(false);
    }
  };

  if (cargando) {
    return (
      <p className="flex items-center gap-2 py-6 text-xs opacity-70">
        <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
        Cargando la equivalencia con Quipux…
      </p>
    );
  }

  if (!mapeo) {
    return (
      <div className="flex flex-col items-start gap-3">
        <p className="text-xs opacity-75">
          Este trámite no se radica en la secretaría por Quipux. Configúralo solo si debe radicarse:
          hará falta que la secretaría te dé su código de trámite y su prefijo.
        </p>
        <button
          type="button"
          onClick={() => setMapeo(propuesta())}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
        >
          Configurar radicación
        </button>
        {error && (
          <p className="text-xs" role="alert" style={{ color: '#C2410C' }}>
            {error}
          </p>
        )}
      </div>
    );
  }

  const completo =
    mapeo.tipoTramite > 0 && mapeo.tipoRequisito > 0 && mapeo.prefijo.trim().length > 0;

  return (
    <div className="flex flex-col gap-4">
      <p
        className="rounded-xl border px-3 py-2 text-xs"
        style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)' }}
      >
        Estos tres códigos los asigna la <strong>secretaría</strong>, no FLIT. Sin ellos el trámite
        no se radica, aunque el resto esté configurado.
      </p>

      <div className="grid gap-3 sm:grid-cols-3">
        <label className="flex flex-col gap-1">
          <span className={ETIQUETA}>Código de trámite</span>
          <input
            type="number"
            className={CAMPO}
            aria-label="Código de trámite en la secretaría"
            value={mapeo.tipoTramite || ''}
            onChange={(e) => cambiar({ tipoTramite: Number(e.target.value) || 0 })}
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className={ETIQUETA}>Código de requisito</span>
          <input
            type="number"
            className={CAMPO}
            aria-label="Código de requisito"
            value={mapeo.tipoRequisito || ''}
            onChange={(e) => cambiar({ tipoRequisito: Number(e.target.value) || 0 })}
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className={ETIQUETA}>Prefijo</span>
          <input
            className={`${CAMPO} font-mono`}
            aria-label="Prefijo del documento radicado"
            value={mapeo.prefijo}
            onChange={(e) => cambiar({ prefijo: e.target.value })}
            placeholder="TR"
          />
        </label>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <label className="flex flex-col gap-1">
          <span className={ETIQUETA}>Familia en la secretaría</span>
          <select
            className={CAMPO}
            aria-label="Familia en la secretaría"
            value={mapeo.familia}
            onChange={(e) => cambiar({ familia: e.target.value })}
          >
            {FAMILIAS_QUIPUX.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
          <span className="text-xs opacity-60">
            Es la taxonomía de la secretaría; puede no coincidir con la familia FLIT.
          </span>
        </label>

        <label className="flex flex-col gap-1">
          <span className={ETIQUETA}>Identificador del vehículo</span>
          <select
            className={CAMPO}
            aria-label="Identificador del vehículo"
            value={mapeo.campoVin ? 'vin' : 'plate'}
            onChange={(e) =>
              cambiar(
                e.target.value === 'vin'
                  ? { campoVin: 'vin', campoPlaca: null }
                  : { campoVin: null, campoPlaca: 'plate' },
              )
            }
          >
            <option value="plate">Placa</option>
            <option value="vin">VIN</option>
          </select>
          <span className="text-xs opacity-60">Propuesto desde cómo entra el trámite.</span>
        </label>

        <label className="flex flex-col gap-1">
          <span className={ETIQUETA}>Tope del nombre de empresa</span>
          <input
            type="number"
            className={CAMPO}
            aria-label="Tope del nombre de empresa"
            value={mapeo.maxLongitudEmpresa || ''}
            onChange={(e) => cambiar({ maxLongitudEmpresa: Number(e.target.value) || 0 })}
          />
          <span className="text-xs opacity-60">25 en matrícula, 35 en traspaso.</span>
        </label>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          onClick={() => void guardar(mapeo)}
          disabled={guardando || !completo}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
        >
          {guardando ? (
            <span className="inline-flex items-center gap-1.5">
              <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
              Guardando…
            </span>
          ) : (
            'Guardar equivalencia'
          )}
        </button>

        <button
          type="button"
          onClick={() => void guardar(null)}
          disabled={guardando}
          className="rounded-xl border px-3 py-2 text-xs font-semibold border-[#DFE5ED] disabled:opacity-40 dark:border-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          style={{ color: '#C2410C' }}
        >
          Retirar de Quipux
        </button>

        {!completo && (
          <span className="text-xs opacity-70">
            Faltan los códigos de la secretaría.
          </span>
        )}
        {ok && (
          <span className="text-xs font-medium" style={{ color: '#0E9F6E' }} role="status">
            {ok}
          </span>
        )}
      </div>

      {error && (
        <p className="text-xs" role="alert" style={{ color: '#C2410C' }}>
          {error}
        </p>
      )}
    </div>
  );
}
