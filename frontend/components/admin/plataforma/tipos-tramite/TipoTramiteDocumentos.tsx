'use client';

import { useEffect, useMemo, useState } from 'react';
import { Loader2, Plus, Trash2 } from 'lucide-react';
import { superadminClient } from '@/lib/api/superadmin-client';
import { fetchDocumentTypes } from '@/lib/api/admin-document-types';
import type { DocumentType } from '@/lib/api/types-documents';
import type {
  ConformationDocumentRequirement,
  ConformationProfile,
} from '@/lib/api/types/procedure-parametrization';

/**
 * Matriz documental del tipo (CFD-06): qué documentos pide y cuáles son obligatorios.
 *
 * Es lo que el gestor ve como checklist en el paso de requisitos, y lo que decide si un expediente
 * puede radicarse. Un documento marcado obligatorio bloquea el avance; uno opcional se ofrece pero
 * no detiene.
 *
 * Los documentos se referencian por CÓDIGO del catálogo, no por id: es lo que hace que la
 * parametrización sobreviva a un reseed del catálogo de documentos.
 */
export function TipoTramiteDocumentos({
  perfil,
  onGuardado,
}: {
  perfil: ConformationProfile;
  onGuardado: () => void;
}) {
  const [borrador, setBorrador] = useState<ConformationDocumentRequirement[]>(
    perfil.documentRequirements ?? [],
  );
  const [catalogo, setCatalogo] = useState<DocumentType[]>([]);
  const [porAgregar, setPorAgregar] = useState('');
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState(false);

  useEffect(() => {
    let vivo = true;
    void fetchDocumentTypes({ pageSize: 300 })
      .then((r) => {
        if (vivo) setCatalogo(r.data ?? []);
      })
      .catch(() => {
        // Sin catálogo no se puede AÑADIR, pero lo ya configurado se sigue viendo y editando: es
        // preferible a dejar la pestaña en blanco por una llamada auxiliar.
        if (vivo) setCatalogo([]);
      });
    return () => {
      vivo = false;
    };
  }, []);

  const nombrePorCodigo = useMemo(
    () => new Map(catalogo.map((d) => [d.codigo, d.nombre])),
    [catalogo],
  );

  const disponibles = useMemo(() => {
    const usados = new Set(borrador.map((r) => r.documentTypeCode));
    return catalogo.filter((d) => !usados.has(d.codigo));
  }, [catalogo, borrador]);

  const tocar = (siguiente: ConformationDocumentRequirement[]) => {
    setBorrador(siguiente);
    setOk(false);
  };

  const agregar = () => {
    if (!porAgregar) return;
    tocar([...borrador, { documentTypeCode: porAgregar, isRequired: false, isDummy: false }]);
    setPorAgregar('');
  };

  const guardar = async () => {
    setGuardando(true);
    setError(null);
    setOk(false);
    try {
      await superadminClient.updateConformationProfile(perfil.procedureTypeId, {
        documentRequirements: borrador.map((r, i) => ({ ...r, sortOrder: i + 1 })),
      });
      onGuardado();
      setOk(true);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar la matriz documental.');
    } finally {
      setGuardando(false);
    }
  };

  const obligatorios = borrador.filter((r) => r.isRequired).length;

  return (
    <div className="flex flex-col gap-3">
      <p className="text-xs opacity-70">
        {borrador.length} documentos · {obligatorios} obligatorios. Los obligatorios bloquean la
        radicación; los opcionales se ofrecen sin detener el avance.
      </p>

      {borrador.length === 0 ? (
        <p className="text-xs opacity-70">
          Este tipo no pide ningún documento. El paso de requisitos aparecerá vacío.
        </p>
      ) : (
        <ul className="flex flex-col gap-1.5">
          {borrador.map((r, i) => (
            <li
              key={r.documentTypeCode}
              className="flex items-center justify-between gap-3 rounded-xl border px-3 py-2 border-[#DFE5ED] dark:border-white/10"
            >
              <span className="min-w-0">
                <span className="block text-xs font-medium text-[#162744] dark:text-white">
                  {nombrePorCodigo.get(r.documentTypeCode) ?? r.documentTypeCode}
                </span>
                <code className="block text-xs opacity-55">{r.documentTypeCode}</code>
              </span>

              <span className="flex shrink-0 items-center gap-3">
                <label className="flex items-center gap-1.5 text-xs">
                  <input
                    type="checkbox"
                    checked={r.isRequired}
                    onChange={(e) =>
                      tocar(
                        borrador.map((x, k) =>
                          k === i ? { ...x, isRequired: e.target.checked } : x,
                        ),
                      )
                    }
                    className="h-3.5 w-3.5 accent-[#557EFF]"
                    aria-label={`${r.documentTypeCode} obligatorio`}
                  />
                  <span className="text-[#162744] dark:text-white">Obligatorio</span>
                </label>
                <button
                  type="button"
                  aria-label={`Quitar ${r.documentTypeCode}`}
                  title="Quitar"
                  onClick={() => tocar(borrador.filter((_, k) => k !== i))}
                  className="rounded-lg border p-1.5 border-[#DFE5ED] transition hover:bg-[#557EFF]/10 dark:border-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                  style={{ color: '#557EFF' }}
                >
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <select
          className="min-w-0 flex-1 rounded-xl border px-3 py-2 text-xs border-[#DFE5ED] bg-white text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          value={porAgregar}
          onChange={(e) => setPorAgregar(e.target.value)}
          aria-label="Documento a añadir"
        >
          <option value="">Añadir un documento…</option>
          {disponibles.map((d) => (
            <option key={d.codigo} value={d.codigo}>
              {d.nombre}
            </option>
          ))}
        </select>
        <button
          type="button"
          onClick={agregar}
          disabled={!porAgregar}
          className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-2 text-xs font-semibold border-[#DFE5ED] disabled:opacity-40 dark:border-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          style={{ color: '#557EFF' }}
        >
          <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          Añadir
        </button>
      </div>

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
            'Guardar documentos'
          )}
        </button>
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
