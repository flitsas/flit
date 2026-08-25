'use client';

import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { superadminClient } from '@/lib/api/superadmin-client';
import { FAMILIA_OPCIONES } from '@/lib/api/types/familia-labels';
import type {
  ProcedureFamily,
  ProcedureTypeSummary,
} from '@/lib/api/types/procedure-parametrization';

const CAMPO =
  'w-full rounded-xl border px-3 py-2 text-xs border-[#DFE5ED] bg-white text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]';

/**
 * Alta de un tipo de trámite.
 *
 * El tipo nace en BORRADOR y con la barrera apagada: todavía no tiene recorrido, y ofrecerlo al
 * gestor sería prometerle un asistente vacío. Después hay que parametrizarlo, validarlo, publicarlo
 * y habilitarlo — el formulario lo dice, porque crear el tipo es el primero de cuatro pasos y no el
 * último.
 *
 * El CÓDIGO no se puede cambiar más adelante: es la llave con la que el tipo viaja a ICT, a Quipux y
 * a los snapshots congelados de cada expediente.
 */
export function NuevoTipoTramiteModal({
  onCreado,
  onCerrar,
}: {
  onCreado: (tipo: ProcedureTypeSummary) => void;
  onCerrar: () => void;
}) {
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [family, setFamily] = useState<ProcedureFamily>('OTROS');
  const [description, setDescription] = useState('');
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const codeNormalizado = code.trim().toUpperCase().replace(/\s+/g, '_');
  const codeValido = /^[A-Z][A-Z0-9_]{2,59}$/.test(codeNormalizado);
  const puedeGuardar = codeValido && name.trim().length > 0 && !guardando;

  const crear = async () => {
    setGuardando(true);
    setError(null);
    try {
      const creado = await superadminClient.createProcedureType({
        code: codeNormalizado,
        name: name.trim(),
        family,
        description: description.trim() || null,
      });
      onCreado(creado);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo crear el tipo.');
    } finally {
      setGuardando(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: 'rgba(22,39,68,0.45)' }}
      role="dialog"
      aria-modal="true"
      aria-label="Nuevo tipo de trámite"
    >
      <div className="w-full max-w-lg rounded-2xl border bg-white p-5 border-[#DFE5ED] dark:border-white/10 dark:bg-[#0B0F14]">
        <h2 className="text-base font-semibold text-[#162744] dark:text-white">
          Nuevo tipo de trámite
        </h2>
        <p className="mt-1 text-xs opacity-70">
          Nace en borrador y sin operar. Después hay que darle recorrido, validarlo, publicarlo y
          habilitarlo.
        </p>

        <div className="mt-4 flex flex-col gap-3">
          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-[#162744] dark:text-white">Código</span>
            <input
              className={`${CAMPO} font-mono`}
              value={code}
              aria-label="Código"
              onChange={(e) => setCode(e.target.value)}
              placeholder="LEVANTAMIENTO_PRENDA"
            />
            <span className="text-xs opacity-60">
              No se puede cambiar después: es la llave con la que el tipo viaja a ICT, a Quipux y a
              los expedientes ya creados.
              {code.trim() && !codeValido && (
                <span className="ml-1 font-medium" style={{ color: '#C2410C' }}>
                  Solo letras, dígitos y guion bajo; empieza por letra; mínimo 3 caracteres.
                </span>
              )}
              {codeValido && codeNormalizado !== code.trim() && (
                <span className="ml-1">
                  Se guardará como <code className="font-mono">{codeNormalizado}</code>.
                </span>
              )}
            </span>
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-[#162744] dark:text-white">Nombre</span>
            <input
              className={CAMPO}
              value={name}
              aria-label="Nombre del tipo"
              onChange={(e) => setName(e.target.value)}
              placeholder="Levantamiento de prenda"
            />
            <span className="text-xs opacity-60">
              Es el rótulo del trámite en el mandato y en la portada del expediente.
            </span>
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-[#162744] dark:text-white">Familia</span>
            <select
              className={CAMPO}
              value={family}
              aria-label="Familia del tipo"
              onChange={(e) => setFamily(e.target.value as ProcedureFamily)}
            >
              {FAMILIA_OPCIONES.map((f) => (
                <option key={f.value} value={f.value}>
                  {f.label}
                </option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-[#162744] dark:text-white">
              Descripción <span className="font-normal opacity-60">(opcional)</span>
            </span>
            <input
              className={CAMPO}
              value={description}
              aria-label="Descripción"
              onChange={(e) => setDescription(e.target.value)}
            />
          </label>

          {/* La advertencia va aquí y no después: crear el tipo en FLIT no lo da de alta en las
              integraciones, y descubrirlo cuando un pre-trámite de ICT no materialice es tarde. */}
          <p
            className="rounded-xl border px-3 py-2 text-xs"
            style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)' }}
          >
            Crear el tipo aquí no lo da de alta en ICT ni en Quipux. Si este trámite llega por
            integración o se radica en la secretaría, hay que mapear su código en esos catálogos.
          </p>
        </div>

        {error && (
          <p className="mt-3 text-xs" role="alert" style={{ color: '#C2410C' }}>
            {error}
          </p>
        )}

        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCerrar}
            className="rounded-xl border px-4 py-2 text-xs font-medium border-[#DFE5ED] dark:border-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void crear()}
            disabled={!puedeGuardar}
            className="rounded-xl px-5 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            {guardando ? (
              <span className="inline-flex items-center gap-1.5">
                <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
                Creando…
              </span>
            ) : (
              'Crear tipo'
            )}
          </button>
        </div>
      </div>
    </div>
  );
}
