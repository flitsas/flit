-- Trámites de V1 en estado Entregado: el alcance de la Epic #13046.
--
-- El migrador no filtra por estado (recibe una lista de ids), así que la selección vive aquí y no
-- en el código. Traspaso usa 5 = Delivered y matrícula 6 = Delivered: los catálogos de estado de
-- V1 no coinciden entre los dos trámites (ver Mapping/StateMap.cs y Mapping/RegistrationStateMap.cs).
--
-- Se corre contra la COPIA de V1. En la copia del 28 de julio de 2026 son 62 traspasos y 153
-- matrículas.
--
-- Uso: psql -v tipo=transfer -f entregados.sql      (o tipo=registration)

\if :{?tipo}
\else
  \echo 'Falta -v tipo=transfer|registration'
  \quit
\endif

SELECT id
FROM   vehicle_transfer_master
WHERE  :'tipo' = 'transfer'
  AND  process_status = 5
UNION ALL
SELECT id
FROM   vehicle_registration_master
WHERE  :'tipo' = 'registration'
  AND  process_status = 6
ORDER  BY 1;
