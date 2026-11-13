# Seguridad de Smart Backup

Smart Backup corre con el usuario actual y no llama al ejecutor UAC. No cambia planes de energía, configuración de sincronización, ACL del origen, servicios ni puntos de restauración. No hay telemetría propia, cuentas JPCFIX, servicios remotos ni carga de datos. Seleccionar explícitamente una ruta de red puede generar tráfico SMB hacia ese destino autorizado.

El técnico debe contar con autorización del propietario. Solo se ofrece la cuenta actual. Los archivos y carpetas agregados manualmente también requieren autorización y se enumeran únicamente después de solicitar el análisis. No se inspeccionan volúmenes completos por iniciativa del programa. Nunca se borran originales ni archivos de usuario existentes para sincronizar.

## Exclusiones y secretos

Se omiten Windows, Program Files, ProgramData y AppData detectados por el sistema; en las raíces de unidades se omiten papelera, información de volumen, paginación, hibernación y swap. Una carpeta personal llamada cache y un archivo personal .tmp, .exe, .zip, .iso o de cualquier otra extensión no se excluyen por su nombre o extensión. Toda exclusión de alcance elegido se muestra y mantiene el respaldo incompleto para exigir revisión.

No se copian perfiles de navegador, cookies, sesiones, contraseñas ni AppData indiscriminadamente. Los adaptadores de marcadores pertenecen a B. Una carpeta o documento elegido por el propietario puede contener información confidencial: no se inspecciona ni filtra su contenido. El inventario conserva rutas, nombre de equipo, SID y nombres de archivos necesarios para migración; debe custodiarse igual que los datos. No se registran contenido, tokens ni excepciones completas en reportes.

## Destino y formateo

Se comprueban existencia, escritura con archivo efímero, espacio libre con margen de 16 MiB, FAT32 y solapamiento con orígenes. Se rechazan reparse points en ancestros. Se aceptan destinos locales, extraíbles y UNC que pasen estas comprobaciones; un servidor puede imponer cuotas y límites no detectables.

Se advierte si WMI identifica un disco compartido con el perfil/orígenes; también se advierte por volumen compartido y por imposibilidad de comprobar discos. Siempre se solicita revisar el medio que se conservará. Discos dinámicos, Storage Spaces, montaje por carpeta, RAID, volúmenes virtuales y recursos de red requieren comprobación manual: la identificación WMI no demuestra independencia física ni seguridad frente al borrado. Una copia en el disco a formatear NO está protegida. No borrar el Windows anterior por ver una barra al 100%.

## Manifiestos no confiables

Antes de copiar se validan versión, GUID, categorías, estados, tamaños, hashes y unicidad de destinos. Se rechazan rutas absolutas/UNC en el inventario relativo, .., segmentos vacíos, barras inversas, ADS (:), caracteres inválidos, nombres de dispositivo y componentes con puntos/espacios finales. El BackupPath debe corresponder exactamente a files/user-001/categoría/ruta-relativa.

Las rutas destino se construyen con carpetas actuales de Windows y contenedores Extras propios; no se usa el SourcePath del manifiesto para escribir. Se revisan los ancestros para bloquear junctions y enlaces, incluso al ejecutar después del preview. Un hash SHA-256 comprueba consistencia con el manifiesto; no autentica al autor si un atacante puede reemplazar ambos. Restaurar solo respaldos de confianza. No se ejecutan archivos ni instaladores restaurados.

La aplicación no constituye un límite frente a un administrador malicioso o un proceso con permisos para sustituir directorios simultáneamente entre comprobaciones de ruta y apertura/publicación (TOCTOU). Trabajar con carpetas y medios bajo control del técnico, sin escritores concurrentes. No usar la herramienta para importar respaldos hostiles en un perfil de producción. No se garantiza protección frente a desconexiones, fallas de firmware o hardware que no respete flush/atomicidad. Revisar desde otro equipo y conservar otra copia cuando los datos sean irremplazables.

## Copias sin cifrar

Esta entrega guarda datos en claro y hereda permisos del medio. Un USB perdido expone su contenido; no incluirlo en repositorios ni carpetas públicas. Usar almacenamiento protegido según el acuerdo con el cliente y retirar el respaldo cuando corresponda. No aplicar claves DPAPI ligadas al Windows original: impedirían portabilidad tras reinstalar.

La separación entre inventario, copia y publicación permite incorporar después un contenedor autenticado y cifrado por streaming con contraseña del propietario, KDF/salt propios, versionado y autenticación del manifiesto. Ese formato requerirá migración explícita, pruebas de recuperación y custodia de contraseña; aún no está implementado.
