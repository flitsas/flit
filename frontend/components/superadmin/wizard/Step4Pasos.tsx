import { useState } from 'react';
import { GripVertical, ChevronUp, ChevronDown, Trash2, Plus } from 'lucide-react';
import type { ProcedureStep } from '@/lib/api/types/procedure-parametrization';

interface Step4Props {
  steps: ProcedureStep[];
  onMove: (index: number, direction: 'up' | 'down') => void;
  onAdd: (name: string) => void;
  onRemove: (index: number) => void;
}

export function Step4Pasos({ steps, onMove, onAdd, onRemove }: Step4Props) {
  const [newStepName, setNewStepName] = useState('');

  const handleAdd = () => {
    const trimmed = newStepName.trim();
    if (!trimmed) return;
    onAdd(trimmed);
    setNewStepName('');
  };

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-base font-bold mb-1">Pasos del trámite</h2>
        <p className="text-xs opacity-60">
          Define el orden de los pasos que verá el operario.
        </p>
      </div>

      <div className="space-y-2">
        {steps.map((step, index) => (
          <div
            key={index}
            className="flex items-center gap-3 rounded-xl p-3 border bg-[rgba(85,126,255,0.03)] dark:bg-white/5"
            style={{ borderColor: '#DFE5ED' }}
          >
            <GripVertical className="h-4 w-4 opacity-30 shrink-0" aria-hidden="true" />
            <span
              className="h-7 w-7 rounded-full grid place-items-center text-[11px] font-bold text-white shrink-0"
              style={{ background: '#557EFF' }}
              aria-hidden="true"
            >
              {index + 1}
            </span>
            <span className="text-xs font-medium flex-1">{step.title}</span>
            <div className="flex items-center gap-1">
              <button
                onClick={() => onMove(index, 'up')}
                disabled={index === 0}
                className="p-1 rounded opacity-50 hover:opacity-100 disabled:opacity-20"
                aria-label={`Mover ${step.title} hacia arriba`}
              >
                <ChevronUp className="h-3.5 w-3.5" />
              </button>
              <button
                onClick={() => onMove(index, 'down')}
                disabled={index === steps.length - 1}
                className="p-1 rounded opacity-50 hover:opacity-100 disabled:opacity-20"
                aria-label={`Mover ${step.title} hacia abajo`}
              >
                <ChevronDown className="h-3.5 w-3.5" />
              </button>
              <button
                onClick={() => onRemove(index)}
                className="p-1 rounded opacity-40 hover:opacity-100"
                aria-label={`Eliminar paso ${step.title}`}
              >
                <Trash2 className="h-3.5 w-3.5" style={{ color: '#FF4E00' }} />
              </button>
            </div>
          </div>
        ))}
      </div>

      <div className="flex items-center gap-2 pt-2">
        <label htmlFor="new-step-name" className="sr-only">
          Nombre del nuevo paso
        </label>
        <input
          id="new-step-name"
          type="text"
          placeholder="Nombre del nuevo paso..."
          value={newStepName}
          onChange={(e) => setNewStepName(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleAdd()}
          className="flex-1 px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF]"
          style={{ borderColor: '#DFE5ED' }}
        />
        <button
          onClick={handleAdd}
          disabled={!newStepName.trim()}
          className="flex items-center gap-1.5 px-3 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-40"
          style={{ background: '#557EFF' }}
          aria-label="Agregar nuevo paso"
        >
          <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          Agregar
        </button>
      </div>
    </div>
  );
}
