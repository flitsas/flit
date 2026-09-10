-- HU #11504 — Identidad ADMIN: conteo de intentos antes de terminalizar un rechazo.
-- Preventiva de la contraparte del Bug #11503 para el camino admin (hoy `ReconcileAsync` no tiene ningún
-- llamador real): agrega el conteo de intentos al agregado `AdminIdentityValidation` para que, el día que
-- se cablee un worker o endpoint que reconcilie contra Kyverum real, un intento fallido aislado
-- (`rechazado_intento`) no congele la fila en `rechazado` como ocurría en trámites.
--
-- attempts / max_attempts / last_attempt_at son el equivalente admin de
-- tramites.procedure_instance_biometric_validations.attempts/max_attempts/last_attempt_at (HU #10233 /
-- Bug #11503). last_attempt_at es la clave de DEDUP del último intento ya contado (el AttemptAt/validadoAt
-- de Kyverum): sin ella, el mismo intento reportado en pollings sucesivos agotaría max_attempts sin que el
-- ciudadano hubiera vuelto a intentar.
--
-- DEFAULT coherente para filas HISTÓRICAS: attempts = 0 (ninguna fila existente pudo haber contado un
-- intento con una columna que no existía) y max_attempts = 3 (AdminIdentityRules.KyverumMaxIntentos, el
-- mismo valor observado que usa el camino de trámite). last_attempt_at queda NULL: no hay forma de
-- reconstruir a posteriori qué intento fue el último sin haber tenido la columna.
--
-- IDEMPOTENTE: ADD COLUMN IF NOT EXISTS (re-aplicable).

ALTER TABLE admin.admin_identity_validations
  ADD COLUMN IF NOT EXISTS attempts int NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS max_attempts int NOT NULL DEFAULT 3,
  ADD COLUMN IF NOT EXISTS last_attempt_at varchar(100);
