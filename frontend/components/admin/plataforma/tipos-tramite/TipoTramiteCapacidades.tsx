'use client';

import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { superadminClient } from '@/lib/api/superadmin-client';
import type { ConformationProfile, GateProfile } from '@/lib/api/types/procedure-parametrization';

/**
 * Capacidades del tipo — el `gate_profile`.
 *
 * Es la pieza que hace que un trámite se comporte como lo que es: qué partes captura, si lleva valor
 * de venta, si la prenda bloquea, por qué identificador entra el vehículo. Gobierna a la vez los
 * gates del backend y el render del asistente, así que editarla aquí cambia el recorrido real.
 *
 * Se exponen las capacidades que un administrador puede decidir. Las que solo afectan a validaciones
 * internas del servidor —operabilidad del organismo, modo SIMIT— se conservan al guardar pero no se
 * ofrecen: no son decisiones de configuración de un trámite.
 */
interface Casilla {
  clave: keyof GateProfile;
  etiqueta: string;
  ayuda: string;
}

const CASILLAS: Casilla[] = [
  { clave: 'requiresSeller', etiqueta: 'Parte vendedora', ayuda: 'El trámite transfiere la propiedad: hay quien vende.' },
  { clave: 'requiresBuyer', etiqueta: 'Titular o comprador', ayuda: 'Se captura la parte que queda como propietaria.' },
  { clave: 'allowsMultipleBuyer', etiqueta: 'Varios compradores', ayuda: 'La parte compradora admite más de una persona.' },
  { clave: 'requiresCommercialValue', etiqueta: 'Valor de venta', ayuda: 'Se pide precio y fecha de la operación.' },
  { clave: 'requiresBiometrics', etiqueta: 'Validación de identidad', ayuda: 'Las partes validan identidad antes de radicar.' },
  { clave: 'hasPrendaGate', etiqueta: 'La prenda bloquea', ayuda: 'La decisión de prenda es una puerta, no una declaración.' },
  { clave: 'requiresSignature', etiqueta: 'Firma', ayuda: 'El expediente se firma antes de radicarse.' },
  { clave: 'requiresPlateRequest', etiqueta: 'Solicitud de placa', ayuda: 'Tras entregar, el trámite pide placa al organismo.' },
  { clave: 'validatePazSalvoImpuesto', etiqueta: 'Paz y salvo de impuestos', ayuda: 'Se verifica antes de dejar avanzar.' },
  { clave: 'validateSoat', etiqueta: 'SOAT vigente', ayuda: 'Se verifica antes de dejar avanzar.' },
];

const ENTRADAS = [
  { valor: 'VIN', etiqueta: 'VIN — el vehículo aún no tiene placa' },
  { valor: 'PLATE', etiqueta: 'Placa — el vehículo ya está matriculado' },
  { valor: 'BOTH', etiqueta: 'Cualquiera de los dos' },
];

const IMPRONTAS: { valor: NonNullable<GateProfile['improntaSource']>; etiqueta: string }[] = [
  { valor: 'AUTO', etiqueta: 'Automática — el sistema la genera (paso FUR / Kyverum)' },
  { valor: 'OPERATOR_CHOICE', etiqueta: 'El gestor elige — generar o cargar el archivo' },
  { valor: 'MANUAL', etiqueta: 'Solo carga manual — no se genera sola' },
];

const ACTORES = [
  { valor: 'OWNER', etiqueta: 'Parte saliente (vendedor / propietario actual)' },
  { valor: 'BUYER', etiqueta: 'Parte entrante (comprador / titular)' },
];

export function TipoTramiteCapacidades({
  perfil,
  onGuardado,
}: {
  perfil: ConformationProfile;
  onGuardado: () => void;
}) {
  const [borrador, setBorrador] = useState<GateProfile>(perfil.gateProfile ?? {});
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState(false);

  const cambiar = (parcial: Partial<GateProfile>) => {
    setBorrador((b) => ({ ...b, ...parcial }));
    setOk(false);
  };

  const actores = borrador.biometricActors ?? [];
  const alternarActor = (valor: string) =>
    cambiar({
      biometricActors: actores.includes(valor)
        ? actores.filter((a) => a !== valor)
        : [...actores, valor],
    });

  const guardar = async () => {
    setGuardando(true);
    setError(null);
    setOk(false);
    try {
      // Solo viaja `gateProfile`: las demás listas quedan `undefined` y el backend no las toca.
      await superadminClient.updateConformationProfile(perfil.procedureTypeId, {
        gateProfile: borrador,
      });
      onGuardado();
      setOk(true);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar el perfil.');
    } finally {
      setGuardando(false);
    }
  };

  const avisoBiometria =
    borrador.requiresBiometrics && actores.length === 0
      ? 'Con validación de identidad activada hay que elegir al menos un actor; si no, el gate se satisface siempre y la validación nunca bloquea.'
      : null;

  return (
    <div className="flex flex-col gap-4">
      <label className="flex max-w-md flex-col gap-1">
        <span className="text-xs font-semibold text-[#162744] dark:text-white">
          Identificador de entrada
        </span>
        <select
          className="w-full rounded-xl border px-3 py-2 text-xs border-[#DFE5ED] bg-white text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          value={borrador.entryMode ?? ''}
          onChange={(e) => cambiar({ entryMode: e.target.value || null })}
        >
          <option value="">Sin definir</option>
          {ENTRADAS.map((e) => (
            <option key={e.valor} value={e.valor}>
              {e.etiqueta}
            </option>
          ))}
        </select>
      </label>

      <label className="flex max-w-md flex-col gap-1">
        <span className="text-xs font-semibold text-[#162744] dark:text-white">
          Generación de impronta
        </span>
        <select
          className="w-full rounded-xl border px-3 py-2 text-xs border-[#DFE5ED] bg-white text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          aria-label="Generación de impronta"
          value={borrador.improntaSource ?? ''}
          onChange={(e) =>
            cambiar({
              improntaSource: (e.target.value || null) as GateProfile['improntaSource'],
            })
          }
        >
          <option value="">Sin definir — se puede generar (también si es opcional)</option>
          {IMPRONTAS.map((opt) => (
            <option key={opt.valor} value={opt.valor}>
              {opt.etiqueta}
            </option>
          ))}
        </select>
        <span className="text-xs opacity-65">
          Independiente de si el documento es obligatorio: eso se marca en la pestaña Documentos.
        </span>
      </label>

      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-xs font-semibold text-[#162744] dark:text-white">
          Qué exige el trámite
        </legend>
        <div className="grid gap-2 sm:grid-cols-2">
          {CASILLAS.map((c) => (
            <label
              key={c.clave}
              className="flex items-start gap-2 rounded-xl border px-3 py-2 border-[#DFE5ED] dark:border-white/10"
            >
              <input
                type="checkbox"
                checked={Boolean(borrador[c.clave])}
                onChange={(e) => cambiar({ [c.clave]: e.target.checked } as Partial<GateProfile>)}
                className="mt-0.5 h-3.5 w-3.5 shrink-0 accent-[#557EFF]"
              />
              <span className="min-w-0">
                <span className="block text-xs font-medium text-[#162744] dark:text-white">
                  {c.etiqueta}
                </span>
                <span className="block text-xs opacity-65">{c.ayuda}</span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      {borrador.requiresBiometrics && (
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-xs font-semibold text-[#162744] dark:text-white">
            Quién valida identidad
          </legend>
          <div className="flex flex-wrap gap-2">
            {ACTORES.map((a) => (
              <label
                key={a.valor}
                className="flex items-center gap-2 rounded-xl border px-3 py-2 text-xs border-[#DFE5ED] dark:border-white/10"
              >
                <input
                  type="checkbox"
                  checked={actores.includes(a.valor)}
                  onChange={() => alternarActor(a.valor)}
                  className="h-3.5 w-3.5 accent-[#557EFF]"
                />
                <span className="text-[#162744] dark:text-white">{a.etiqueta}</span>
              </label>
            ))}
          </div>
          {avisoBiometria && (
            <p className="text-xs" style={{ color: '#B87A00' }} role="alert">
              {avisoBiometria}
            </p>
          )}
        </fieldset>
      )}

      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={() => void guardar()}
          disabled={guardando}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
        >
          {guardando ? (
            <span className="inline-flex items-center gap-1.5">
              <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
              Guardando…
            </span>
          ) : (
            'Guardar capacidades'
          )}
        </button>
        <span className="text-xs opacity-70">Versión actual: {perfil.version}</span>
        {ok && (
          <span className="text-xs font-medium" style={{ color: '#0E9F6E' }} role="status">
            Guardado
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
