# Modulo.Despachos -- pendientes

- Etapa 1 en construccion (ver docs/superpowers/plans/).
- Migraciones (Postgres + SqlServer) se generan una vez definidas las entidades; aplicar SIEMPRE en las 4 bases (ps_comdepor/_qa y portalsaas_saas_prod/_qa del modulo) y verificar.
- Desplegar Host + plugin juntos; el dist/ se genera con publish-dist.ps1.
- Verificar nombres de comunas.csv contra listado oficial CUT (SUBDERE): codigos y nombres se escribieron de memoria (346 filas, 16 regiones).
- Task 4 (mantenedores): verificacion manual en Host local pendiente (no se pudo correr aqui): por cada una de las 10 pantallas /despachos/configuracion/<codigo> crear, editar y eliminar un registro; confirmar que el eliminar bloqueado muestra el motivo; que busqueda y orden por columna funcionan; que el drawer abre vacio al "Nuevo" y relleno al editar; que /js/despachos-maestros.js y /js/table-tools.js cargan (200); Zonas: filtro y multiseleccion de comunas (se siembran 346 al primer acceso); Vehiculos: capacidad con decimal ("12.5") se guarda bien con el Host en es-CL; usuario sin permisos recibe Forbid; log del Host sin "Colision de ruta Razor".
