"use client";

import { useEffect, useState } from "react";
import { Modal } from "@/components/atom/Modal";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import { ApiValidationError } from "@/lib/api/types";
import {
  updateSignatureVaultEntry,
  type SignatureVaultItem,
} from "@/lib/api/admin-signature-vault";

/**
 * Corrección de los datos capturados de una firma del baúl.
 *
 * <p>Cierra el CRUD, que era alta + consulta + anulación: un dato mal digitado —el código hash sobre
 * todo, que es lo que se estampa como «Hash:» en los documentos— solo se podía arreglar anulando la
 * firma y volviéndola a registrar.</p>
 *
 * <p>El documento y la imagen NO se editan aquí, y se dice por qué en pantalla: el documento
 * identifica a la persona dueña de la firma, y lo ya emitido se estampó con esa imagen. Para cambiar
 * la imagen se captura una firma nueva, que sustituye a la anterior conservándola como histórico.</p>
 */
export function SignatureVaultEditPanel({
  tenantId,
  item,
  onClose,
  onSaved,
}: {
  tenantId: string;
  item: SignatureVaultItem | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [fullName, setFullName] = useState("");
  const [codigoHash, setCodigoHash] = useState("");
  const [desde, setDesde] = useState("");
  const [hasta, setHasta] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!item) return;
    // Precarga al abrir: editar parte de lo que hay, no de un formulario en blanco.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setFullName(item.fullName);
    setCodigoHash(item.codigoHash ?? "");
    setDesde(item.vigenciaDesde);
    setHasta(item.vigenciaHasta);
    setError(null);
    setFieldErrors({});
  }, [item]);

  if (!item) return null;

  const guardar = async () => {
    setSaving(true);
    setError(null);
    setFieldErrors({});
    try {
      const codigo = codigoHash.trim();
      await updateSignatureVaultEntry(tenantId, item.id, {
        fullName: fullName.trim(),
        // null y no "" — el sello del documento omite la línea «Hash:» cuando falta, y una cadena
        // vacía pintaría la etiqueta sin valor.
        codigoHash: codigo === "" ? null : codigo,
        vigenciaDesde: desde,
        vigenciaHasta: hasta,
      });
      onSaved();
      onClose();
    } catch (err) {
      if (err instanceof ApiValidationError) {
        const mapped: Record<string, string> = {};
        for (const e of err.errors) {
          if (e.field) mapped[e.field] = e.message;
        }
        setFieldErrors(mapped);
        setError("Revisa los datos marcados.");
      } else {
        setError("No se pudo guardar la corrección. Inténtalo de nuevo.");
      }
    } finally {
      setSaving(false);
    }
  };

  const valido = fullName.trim() !== "" && desde !== "" && hasta !== "";

  return (
    <Modal open onClose={onClose} title="Corregir firma" size="md">
      <div className="space-y-3 text-xs">
        <p className="rounded-xl border border-[#DFE5ED] px-3 py-2 text-[11px] opacity-80">
          Se corrigen los datos de captura. El documento ({item.documentType} {item.documentNumber}) y
          la imagen de la firma no se editan: para cambiarlos, registra una firma nueva de la persona,
          que sustituye a esta y la conserva como histórico.
        </p>

        <div>
          <label htmlFor="sv-edit-nombre" className="mb-1 block font-semibold">
            Nombre del firmante
          </label>
          <input
            id="sv-edit-nombre"
            value={fullName}
            onChange={(e) => setFullName(e.target.value)}
            className={OT_INPUT_CLS}
            disabled={saving}
          />
          {fieldErrors.fullName && (
            <p className="mt-1 text-[11px]" style={{ color: "#FF4E00" }}>
              {fieldErrors.fullName}
            </p>
          )}
        </div>

        <div>
          <label htmlFor="sv-edit-hash" className="mb-1 block font-semibold">
            Código hash <span className="font-normal opacity-60">(opcional)</span>
          </label>
          <input
            id="sv-edit-hash"
            value={codigoHash}
            onChange={(e) => setCodigoHash(e.target.value)}
            className={OT_INPUT_CLS}
            placeholder="Código alfanumérico"
            maxLength={100}
            disabled={saving}
          />
          <p className="mt-1 text-[10px] opacity-60">
            Es lo que se estampa como «Hash» junto a la firma en el FUR, la compraventa, el mandato y
            la solicitud de trámite virtual. Sin código, esa línea no aparece.
          </p>
          {fieldErrors.codigoHash && (
            <p className="mt-1 text-[11px]" style={{ color: "#FF4E00" }}>
              {fieldErrors.codigoHash}
            </p>
          )}
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div>
            <label htmlFor="sv-edit-desde" className="mb-1 block font-semibold">
              Vigencia desde
            </label>
            <input
              id="sv-edit-desde"
              type="date"
              value={desde}
              onChange={(e) => setDesde(e.target.value)}
              className={OT_INPUT_CLS}
              disabled={saving}
            />
          </div>
          <div>
            <label htmlFor="sv-edit-hasta" className="mb-1 block font-semibold">
              Vigencia hasta
            </label>
            <input
              id="sv-edit-hasta"
              type="date"
              value={hasta}
              onChange={(e) => setHasta(e.target.value)}
              className={OT_INPUT_CLS}
              disabled={saving}
            />
            {fieldErrors.vigenciaHasta && (
              <p className="mt-1 text-[11px]" style={{ color: "#FF4E00" }}>
                {fieldErrors.vigenciaHasta}
              </p>
            )}
          </div>
        </div>

        {error && (
          <p role="alert" className="text-[11px] font-medium" style={{ color: "#FF4E00" }}>
            {error}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold"
            onClick={onClose}
            disabled={saving}
          >
            Cancelar
          </button>
          <button
            type="button"
            className="rounded-xl px-3 py-1.5 text-[11px] font-semibold text-white disabled:opacity-60"
            style={{ background: "#557EFF" }}
            onClick={() => void guardar()}
            disabled={saving || !valido}
          >
            {saving ? "Guardando…" : "Guardar cambios"}
          </button>
        </div>
      </div>
    </Modal>
  );
}
