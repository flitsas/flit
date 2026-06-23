"use client";

import { useEffect, useState } from "react";
import { Loader2, Plus, Trash2 } from "lucide-react";
import type { CreateOtRuleRequest, OtRule, OtRuleCondition } from "@/lib/api/types-ot";
import { OtSidePanel } from "./OtSidePanel";
import { OT_INPUT_CLS } from "./ot-form-styles";
import { OT_RULE_ACTIONS, OT_RULE_FIELDS, OT_RULE_OPERATORS } from "./ot-utils";

export interface RuleFormPanelProps {
  open: boolean;
  onClose: () => void;
  onCreate: (body: CreateOtRuleRequest) => Promise<OtRule>;
  onSaved: (rule: OtRule) => void;
}

const emptyCondition = (): OtRuleCondition => ({
  field: OT_RULE_FIELDS[0].value,
  op: OT_RULE_OPERATORS[0].value,
  value: "",
});

/** Constructor visual de reglas AND/OR (HU #10223). */
export function RuleFormPanel({ open, onClose, onCreate, onSaved }: RuleFormPanelProps) {
  const [name, setName] = useState("");
  const [logic, setLogic] = useState<"AND" | "OR">("AND");
  const [actionType, setActionType] = useState("bloquear");
  const [queueName, setQueueName] = useState("");
  const [conditions, setConditions] = useState<OtRuleCondition[]>([emptyCondition()]);
  const [conditionError, setConditionError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset de formulario al abrir panel lateral
    setName("");
    setLogic("AND");
    setActionType("bloquear");
    setQueueName("");
    setConditions([emptyCondition()]);
    setConditionError(null);
  }, [open]);

  const canSave = conditions.length > 0 && name.trim().length > 0;

  const parseValue = (raw: string, op: string): unknown => {
    if (op === "in") {
      return raw.split(",").map((s) => s.trim()).filter(Boolean);
    }
    if (raw === "true") return true;
    if (raw === "false") return false;
    return raw.trim();
  };

  const submit = async () => {
    if (conditions.length === 0) {
      setConditionError("Debes agregar al menos una condición");
      return;
    }
    setConditionError(null);
    setSubmitting(true);
    try {
      const body: CreateOtRuleRequest = {
        name: name.trim(),
        logic,
        conditions: conditions.map((c) => ({
          field: c.field,
          op: c.op,
          value: parseValue(String(c.value ?? ""), c.op),
        })),
        action: {
          type: actionType as CreateOtRuleRequest["action"]["type"],
          ...(actionType === "cola_especial" ? { queue_name: queueName.trim() } : {}),
        },
      };
      const created = await onCreate(body);
      onSaved(created);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <OtSidePanel
      open={open}
      title="Nueva regla"
      ariaLabel="Nueva regla"
      onClose={onClose}
      disabled={submitting}
      footer={
        <button
          type="button"
          disabled={submitting || !canSave}
          className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: "#557EFF" }}
          onClick={() => void submit()}
        >
          {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
          Guardar regla
        </button>
      }
    >
      <div className="space-y-4">
        <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
          Nombre
          <input
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </label>

        <fieldset className="space-y-2">
          <legend className="text-xs font-semibold" style={{ color: "#162744" }}>
            Condiciones
          </legend>
          {conditions.map((cond, index) => (
            <div
              key={index}
              className="grid grid-cols-1 gap-2 rounded-xl border p-3"
              style={{ borderColor: "#DFE5ED" }}
            >
              <select
                aria-label={`Campo condición ${index + 1}`}
                className={OT_INPUT_CLS}
                value={cond.field}
                onChange={(e) =>
                  setConditions((prev) =>
                    prev.map((c, i) => (i === index ? { ...c, field: e.target.value } : c)),
                  )
                }
              >
                {OT_RULE_FIELDS.map((f) => (
                  <option key={f.value} value={f.value}>
                    {f.label}
                  </option>
                ))}
              </select>
              <select
                aria-label={`Operador condición ${index + 1}`}
                className={OT_INPUT_CLS}
                value={cond.op}
                onChange={(e) =>
                  setConditions((prev) =>
                    prev.map((c, i) => (i === index ? { ...c, op: e.target.value } : c)),
                  )
                }
              >
                {OT_RULE_OPERATORS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
              <input
                aria-label={`Valor condición ${index + 1}`}
                className={OT_INPUT_CLS}
                placeholder={cond.op === "in" ? "matricula, renovacion" : "valor"}
                value={String(cond.value ?? "")}
                onChange={(e) =>
                  setConditions((prev) =>
                    prev.map((c, i) => (i === index ? { ...c, value: e.target.value } : c)),
                  )
                }
              />
              {conditions.length > 1 && (
                <button
                  type="button"
                  className="flex items-center gap-1 text-[10px] font-semibold"
                  style={{ color: "#FF4E00" }}
                  onClick={() => setConditions((prev) => prev.filter((_, i) => i !== index))}
                >
                  <Trash2 className="h-3 w-3" /> Quitar
                </button>
              )}
            </div>
          ))}
          <button
            type="button"
            className="flex items-center gap-1 text-xs font-semibold"
            style={{ color: "#557EFF" }}
            onClick={() => setConditions((prev) => [...prev, emptyCondition()])}
          >
            <Plus className="h-3.5 w-3.5" /> Agregar condición
          </button>
          {conditionError && (
            <p className="text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
              {conditionError}
            </p>
          )}
        </fieldset>

        <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
          Lógica
          <select
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={logic}
            onChange={(e) => setLogic(e.target.value as "AND" | "OR")}
          >
            <option value="AND">AND (todas)</option>
            <option value="OR">OR (cualquiera)</option>
          </select>
        </label>

        <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
          Acción
          <select
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={actionType}
            onChange={(e) => setActionType(e.target.value)}
          >
            {OT_RULE_ACTIONS.map((a) => (
              <option key={a.value} value={a.value}>
                {a.label}
              </option>
            ))}
          </select>
        </label>

        {actionType === "cola_especial" && (
          <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
            Nombre de cola
            <input
              className={`mt-1 ${OT_INPUT_CLS}`}
              value={queueName}
              onChange={(e) => setQueueName(e.target.value)}
            />
          </label>
        )}
      </div>
    </OtSidePanel>
  );
}
