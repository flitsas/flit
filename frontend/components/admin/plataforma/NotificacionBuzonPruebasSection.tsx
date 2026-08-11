"use client";

import { useEffect, useState, type FormEvent } from "react";
import { Inbox, Pencil, RefreshCw, TriangleAlert } from "lucide-react";
import { ApiError } from "@/lib/api/types";
import {
  updateTestMailbox,
  type NotificationTestMailbox,
} from "@/lib/api/admin-plataforma-notificaciones";

export interface NotificacionBuzonPruebasSectionProps {
  mailbox: NotificationTestMailbox | null;
  status: "loading" | "error" | "ready";
  onRetry: () => void;
  /** Se invoca tras un `PUT` exitoso, con el buzón ya actualizado (AC6). */
  onSaved: (mailbox: NotificationTestMailbox) => void;
}

/**
 * SuperAdmin — Plataforma → Notificaciones → Buzón de pruebas (HU #11371, AC5/AC6).
 * Muestra el buzón configurado (o su ausencia) y permite editarlo. El envío de prueba de las
 * filas del banco de pruebas lee `mailbox.isConfigured` desde el padre para decidir si ejecuta
 * el envío o pide configurarlo (AC5) — este componente no gobierna esa decisión.
 *
 * Uso de ejemplo:
 * <NotificacionBuzonPruebasSection mailbox={mailbox} status="ready" onRetry={reload} onSaved={setMailbox} />
 */
export function NotificacionBuzonPruebasSection({
  mailbox,
  status,
  onRetry,
  onSaved,
}: NotificacionBuzonPruebasSectionProps) {
  const [editing, setEditing] = useState(false);
  const [email, setEmail] = useState("");
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  useEffect(() => {
    if (!editing) {
      setEmail(mailbox?.testRecipientEmail ?? "");
    }
  }, [mailbox, editing]);

  const startEditing = () => {
    setFormError(null);
    setEmail(mailbox?.testRecipientEmail ?? "");
    setEditing(true);
  };

  const cancelEditing = () => {
    setFormError(null);
    setEditing(false);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setFormError(null);
    setSaving(true);
    try {
      const updated = await updateTestMailbox(email.trim(), mailbox?.rowVersion ?? null);
      onSaved(updated);
      setEditing(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 400) {
        setFormError("Ese correo no es válido. Verifica el formato e inténtalo de nuevo.");
      } else if (err instanceof ApiError && err.status === 409) {
        setFormError(
          "El buzón cambió mientras editabas. Recarga la información e inténtalo de nuevo.",
        );
      } else {
        setFormError("No se pudo guardar el buzón de pruebas. Inténtalo de nuevo.");
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <section
      aria-labelledby="notificaciones-buzon-heading"
      className="flex flex-col gap-3 rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]"
      data-testid="notificaciones-buzon-pruebas"
    >
      <h2
        id="notificaciones-buzon-heading"
        className="inline-flex items-center gap-1.5 text-xs font-semibold text-[#162244] dark:text-white"
      >
        <Inbox className="h-3.5 w-3.5" aria-hidden="true" />
        Buzón de pruebas
      </h2>

      {status === "loading" ? (
        <div
          role="status"
          aria-busy="true"
          aria-live="polite"
          data-testid="notificaciones-buzon-cargando"
          className="h-10 w-full animate-pulse rounded-xl border bg-[#F4F7FC] dark:bg-white/5"
        >
          <span className="sr-only">Cargando buzón de pruebas…</span>
        </div>
      ) : status === "error" ? (
        <div
          role="alert"
          data-testid="notificaciones-buzon-error"
          className="flex items-center justify-between gap-3 rounded-xl border border-[#FF4E00]/30 bg-[#FF4E00]/10 px-3 py-2 text-xs text-[#9A3412] dark:text-[#FFB199]"
        >
          <span>No se pudo cargar el buzón de pruebas.</span>
          <button
            type="button"
            onClick={onRetry}
            className="inline-flex shrink-0 items-center gap-1.5 rounded-full px-3 py-1.5 text-[11px] font-semibold text-white"
            style={{ background: "#557EFF" }}
          >
            <RefreshCw className="h-3 w-3" aria-hidden="true" />
            Reintentar
          </button>
        </div>
      ) : (
        <>
        {editing ? (
          <form onSubmit={handleSubmit} className="flex flex-col gap-2" noValidate>
            <label
              htmlFor="notificaciones-buzon-email"
              className="text-[11px] font-semibold text-[#59677D] dark:text-white/55"
            >
              Correo del buzón de pruebas
            </label>
            <div className="flex flex-col gap-2 sm:flex-row">
              <input
                id="notificaciones-buzon-email"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                disabled={saving}
                placeholder="pruebas@flit.com.co"
                className="flex-1 rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
              />
              <div className="flex gap-2">
                <button
                  type="submit"
                  disabled={saving || email.trim().length === 0}
                  className="rounded-full bg-gradient-to-r from-[#22D3C5] to-[#557EFF] px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
                >
                  {saving ? "Guardando…" : "Guardar"}
                </button>
                <button
                  type="button"
                  onClick={cancelEditing}
                  disabled={saving}
                  className="rounded-full border border-[#DFE5ED] px-4 py-2 text-xs font-semibold text-[#59677D] disabled:opacity-50 dark:border-white/10 dark:text-white/70"
                >
                  Cancelar
                </button>
              </div>
            </div>
            {formError ? (
              <p role="alert" className="inline-flex items-center gap-1.5 text-xs text-[#B42318]">
                <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                {formError}
              </p>
            ) : null}
          </form>
        ) : (
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
            {mailbox?.isConfigured && mailbox.testRecipientEmail ? (
              <p className="text-sm text-[#162244] dark:text-white">
                <span className="font-semibold">{mailbox.testRecipientEmail}</span>
                <span className="ml-2 text-[11px] text-[#59677D] dark:text-white/55">
                  {mailbox.lastTestSentAt
                    ? `Última prueba enviada: ${new Date(mailbox.lastTestSentAt).toLocaleString("es-CO")}`
                    : "Sin envíos de prueba todavía."}
                </span>
              </p>
            ) : (
              <p
                className="inline-flex items-center gap-1.5 text-xs text-[#9A3412] dark:text-[#FFB199]"
                data-testid="notificaciones-buzon-sin-configurar"
              >
                <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                No hay un buzón de pruebas configurado. Configúralo para poder enviar correos de
                prueba.
              </p>
            )}
            <button
              type="button"
              onClick={startEditing}
              className="inline-flex w-fit items-center gap-1.5 rounded-full border border-[#DFE5ED] px-3 py-1.5 text-[11px] font-semibold text-[#162244] dark:border-white/10 dark:text-white"
            >
              <Pencil className="h-3 w-3" aria-hidden="true" />
              {mailbox?.isConfigured ? "Modificar" : "Configurar"}
            </button>
          </div>
        )}
        </>
      )}
    </section>
  );
}
