'use client';

import { buttonClass } from '@flit/ui/Button';
import { DIALOGO_PANEL_CLS, DIALOGO_TITULO_CLS, DIALOGO_VELO_CLS, FranjaDialogo } from '@/components/admin/dialogo-propio';
import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { superadminClient } from '@/lib/api/superadmin-client';
import { FAMILIA_OPCIONES } from '@/lib/api/types/familia-labels';
import type {
  ProcedureFamily,
  ProcedureTypeSummary,
} from '@/lib/api/types/procedure-parametrization';

const CAMPO =
  'w-full rounded-xl border px-3 py-2 text-xs border-border bg-card text-flit-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand';

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
      className={`fixed inset-0 z-50 flex items-center justify-center p-4 ${DIALOGO_VELO_CLS}`}
      role="dialog"
      aria-modal="true"
      aria-label="Nuevo tipo de trámite"
    >
      <div className={`w-full max-w-lg overflow-hidden p-5 ${DIALOGO_PANEL_CLS}`}>
        <FranjaDialogo className="-mx-5 -mt-5 mb-1">
          <div>
            <h2 className={`text-base font-semibold ${DIALOGO_TITULO_CLS}`}>
              Nuevo tipo de trámite
            </h2>
            <p className="mt-1 text-xs opacity-85">
              Nace en borrador y sin operar. Después hay que darle recorrido, validarlo, publicarlo y
              habilitarlo.
            </p>
          </div>
        </FranjaDialogo>

        <div className="mt-4 flex flex-col gap-3">
          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-flit-primary">Código</span>
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
                <span className="ml-1 font-medium" style={{ color: 'var(--destructive)' }}>
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
            <span className="text-xs font-semibold text-flit-primary">Nombre</span>
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
            <span className="text-xs font-semibold text-flit-primary">Familia</span>
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
            <span className="text-xs font-semibold text-flit-primary">
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
            style={{ borderColor: 'var(--badge-warning-border)', background: 'var(--badge-warning-bg)' }}
          >
            Crear el tipo aquí no lo da de alta en ICT ni en Quipux. Si este trámite llega por
            integración o se radica en la secretaría, hay que mapear su código en esos catálogos.
          </p>
        </div>

        {error && (
          <p className="mt-3 text-xs" role="alert" style={{ color: 'var(--destructive)' }}>
            {error}
          </p>
        )}

        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCerrar}
            className={buttonClass({ variant: 'secondary', size: 'sm' })}
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void crear()}
            disabled={!puedeGuardar}
            className={buttonClass({ size: 'sm' })}
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
