/**
 * Contrato imperativo de un formulario de paso embebido en el wizard. La shell
 * dispara `save()` desde el footer "Guardar y continuar" (en vez de que cada
 * form tenga su propio botón de guardado). Lo implementan los pasos de captura
 * que persisten antes de avanzar: actores (vendedor/comprador) y comercial.
 */
export interface WizardStepFormHandle {
  /** Valida y guarda; resuelve `true` si persistió, `false` si inválido o falló. */
  save: () => Promise<boolean>;
}
