-- =============================================================================
-- core-ict — Bug #13445 (D6): SUPERADO por 28-ICT-transformation-type-catalogo-b.sql.
-- Este script adoptaba el catálogo A de tipo de trámite (5 blindaje, 6 carrocería, 7 color, 9 combustible)
-- para more_transaction_transaction_type[].transactionType; FLIT 1 confirmó que el campo usa el
-- catálogo B «Tipo de transformación ICT» (5 color, 9 combustible, 17 carrocería). Como el bootstrapper
-- re-ejecuta todos los scripts en cada arranque, el cuerpo original se retiró para que no deshaga al 28
-- (reactivar 6/7 y desactivar 17 en cada arranque). El nombre del archivo se conserva por historia.
-- =============================================================================

SELECT 1;
