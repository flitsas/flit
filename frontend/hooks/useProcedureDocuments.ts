'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { DEV_TENANT_ID } from '@/lib/api/dev-constants';
import type {
  ChecklistView,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

export interface ProcedureDocumentsState {
  checklist: ChecklistView | null;
  attachments: ProcedureAttachment[];
  loading: boolean;
  /** tipo del ítem cuya subida está en curso (null = ninguna). */
  uploadingTipo: string | null;
  /** id del adjunto en proceso de borrado (null = ninguno). */
  deletingId: string | null;
  error: string | null;
}

const INITIAL_STATE: ProcedureDocumentsState = {
  checklist: null,
  attachments: [],
  loading: false,
  uploadingTipo: null,
  deletingId: null,
  error: null,
};

/**
 * Carga el checklist guiado por la tipología + los adjuntos de la instancia,
 * y expone subir/borrar/refrescar. `instanceId` null deja el hook inerte
 * (la instancia draft aún no existe). Diseñado para extraerse al step de
 * documentos del wizard (Slice 4).
 */
export function useProcedureDocuments(
  instanceId: string | null,
  tenantId: string = DEV_TENANT_ID,
) {
  const [state, setState] = useState<ProcedureDocumentsState>(INITIAL_STATE);

  const refresh = useCallback(async () => {
    if (!instanceId) return;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const [checklist, attachments] = await Promise.all([
        tramitesClient.getChecklist(instanceId, tenantId),
        tramitesClient.getAttachments(instanceId, tenantId),
      ]);
      setState((s) => ({ ...s, checklist, attachments, loading: false }));
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error:
          err instanceof Error
            ? err.message
            : 'Error al cargar los documentos',
      }));
    }
  }, [instanceId, tenantId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const upload = useCallback(
    async (tipo: string, file: File) => {
      if (!instanceId) return false;
      setState((s) => ({ ...s, uploadingTipo: tipo, error: null }));
      try {
        await tramitesClient.uploadAttachment(instanceId, tipo, file, tenantId);
        setState((s) => ({ ...s, uploadingTipo: null }));
        await refresh();
        return true;
      } catch (err) {
        setState((s) => ({
          ...s,
          uploadingTipo: null,
          error:
            err instanceof Error
              ? err.message
              : 'Error al subir el documento',
        }));
        return false;
      }
    },
    [instanceId, tenantId, refresh],
  );

  const remove = useCallback(
    async (attachmentId: string) => {
      if (!instanceId) return false;
      setState((s) => ({ ...s, deletingId: attachmentId, error: null }));
      try {
        await tramitesClient.deleteAttachment(
          instanceId,
          attachmentId,
          tenantId,
        );
        setState((s) => ({ ...s, deletingId: null }));
        await refresh();
        return true;
      } catch (err) {
        setState((s) => ({
          ...s,
          deletingId: null,
          error:
            err instanceof Error
              ? err.message
              : 'Error al borrar el documento',
        }));
        return false;
      }
    },
    [instanceId, tenantId, refresh],
  );

  const clearError = useCallback(() => {
    setState((s) => ({ ...s, error: null }));
  }, []);

  return { state, refresh, upload, remove, clearError };
}
