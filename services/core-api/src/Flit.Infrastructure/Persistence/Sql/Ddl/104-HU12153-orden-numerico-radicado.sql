-- HU #12153 (Feature #12150) — ordenar el listado por el radicado con criterio numérico.
--
-- El orden se resuelve por (longitud, texto), NO con un cast a bigint. Sobre enteros sin ceros
-- a la izquierda las dos ordenaciones son idénticas —comprobado fila a fila sobre 967 trámites
-- reales: cero discrepancias— y esta evita tres problemas del cast:
--
--   · Un CREATE INDEX sobre ((reference_number)::bigint) NO puede convivir en la misma
--     transacción que la renumeración de la HU #12151: el UPDATE es no-HOT (la columna está
--     indexada), así que el índice evalúa el cast también sobre las versiones VIEJAS de cada
--     fila y revienta con «invalid input syntax for type bigint». Sin cast, no hay trampa.
--   · Un cast en el ORDER BY puede fallar en tiempo de ejecución si alguna fila no fuera
--     numérica. length() no falla nunca.
--   · length() y la comparación de texto son traducibles por EF sin trucos.
--
-- La equivalencia depende de un invariante: nada de ceros a la izquierda. Se pasa de suponerlo
-- a EXIGIRLO, endureciendo el CHECK que introdujo la HU #12151.

ALTER TABLE tramites.procedure_instances
    DROP CONSTRAINT IF EXISTS ck_procedure_instances_reference_numerico;

-- Antes: '^[0-9]+$' (admitía '0123', que rompería el orden por longitud).
-- Ahora: entero positivo sin ceros a la izquierda. Lo cumplen por construcción los tres
-- orígenes que existen: la secuencia (nextval), la renumeración de la 103 (row_number) y los
-- rangos sintéticos de los seeds (91/92/93…).
ALTER TABLE tramites.procedure_instances
    ADD CONSTRAINT ck_procedure_instances_reference_numerico
    CHECK (reference_number ~ '^[1-9][0-9]*$');

-- Índice de apoyo para ORDER BY length(reference_number), reference_number. length() es
-- inmutable y no falla sobre ningún texto, así que este índice sí puede crearse sin ceremonia.
CREATE INDEX IF NOT EXISTS ix_procedure_instances_reference_orden
    ON tramites.procedure_instances (length(reference_number), reference_number);
