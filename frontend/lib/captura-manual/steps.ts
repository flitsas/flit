// Máquina de estados de los 5 pasos de la captura manual (HU #13291). Pura y testeable.

export const STEPS = [
  { id: "datos", label: "Datos" },
  { id: "rostro", label: "Rostro" },
  { id: "anverso", label: "Anverso" },
  { id: "reverso", label: "Reverso" },
  { id: "firma", label: "Firma" },
] as const;

export type StepId = (typeof STEPS)[number]["id"];

export interface StepsState {
  /** Índice base 0 del paso activo. */
  current: number;
  /** Índices completados. */
  completed: number[];
  /** true cuando se completó el último paso. */
  finished: boolean;
}

export type StepsAction = { type: "next" } | { type: "back" };

export const initialStepsState: StepsState = { current: 0, completed: [], finished: false };

export function stepsReducer(state: StepsState, action: StepsAction): StepsState {
  const last = STEPS.length - 1;
  switch (action.type) {
    case "next": {
      if (state.finished) return state;
      const completed = state.completed.includes(state.current)
        ? state.completed
        : [...state.completed, state.current];
      if (state.current === last) return { ...state, completed, finished: true };
      return { current: state.current + 1, completed, finished: false };
    }
    case "back": {
      if (state.finished || state.current === 0) return state;
      const current = state.current - 1;
      // Al volver se «des-completa» el paso al que se regresa y los posteriores.
      return { ...state, current, completed: state.completed.filter((i) => i < current) };
    }
    default:
      return state;
  }
}

export type StepStatus = "done" | "active" | "pending";

export function statusOf(state: StepsState, index: number): StepStatus {
  if (state.completed.includes(index)) return "done";
  return index === state.current && !state.finished ? "active" : "pending";
}

/** Texto para lectores de pantalla: se anuncia «Paso N de 5» (no de 3 como Kyverum). */
export function progressText(state: StepsState): string {
  const n = Math.min(state.current + 1, STEPS.length);
  return `Paso ${n} de ${STEPS.length}: ${STEPS[n - 1].label}`;
}
