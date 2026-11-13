# Smart Backup — Entrega A

## Inspección y plan

La fase anterior implementa seis proyectos .NET 10 Windows: Core (contratos), Application (coordinación), Infrastructure (APIs nativas/WMI/persistencia), Desktop (WPF/MVVM), Runner (UAC de energía) y Tests (xUnit). Se inspeccionaron sus fuentes, proyectos, scripts y documentación antes de cambiar código. No había un motor general de archivos ni un servicio común de progreso.

Se mantiene la dirección de dependencias y la inyección por constructor de App. Se reutilizan AsyncCommand, INotifyPropertyChanged, los recursos visuales, la navegación y el patrón temporal → flush → publicación de JsonChangeStore. Los estilos originales se trasladaron sin cambiar sus valores a Styles.xaml, compartido por App y la prueba de carga WPF. La auditoría existente tiene permisos administrativos y describe transacciones de energía: no se usa para datos personales. Cada respaldo conserva su manifiesto y reporte propios. Runner, PowerOptimization y su protocolo permanecen separados. Un alias de EnumerationOptions elimina una ambigüedad de compilación preexistente de WMI.

Plan ejecutado: contratos/versionado → detección y selección → análisis acotado a ubicaciones elegidas → validación de destino → copia/hash → manifiesto/reporte → verificación al abrir → previsualización y restauración → interfaz → pruebas y documentación. No se implementan B y C en esta entrega.

## Componentes

- Core/SmartBackup.cs: perfil, categorías, entradas, estados, progreso, destino y plan de restauración; IBackupEnvironment permite simular carpetas y fallos de almacenamiento.
- Application/SmartBackupService.cs: análisis, exclusiones, copia, checkpoints, verificación, lectura del formato y restauración. BackupPaths centraliza confinamiento y reglas de nombres Windows.
- Infrastructure/WindowsBackupEnvironment.cs: SHGetKnownFolderPath para ubicaciones actuales, SID del token actual, capacidad/formato mediante Win32 y correspondencia de disco mediante WMI de solo lectura. File ID y volumen permiten deduplicar alias de directorios cuando el proveedor expone identidad.
- Desktop/BackupView y BackupViewModel: dos flujos independientes, selección editable, resumen, selectores nativos, confirmaciones propias, progreso y cancelación. El menú principal usa pestañas laterales y conserva los índices de los módulos anteriores.

## Alcance y selección

Usuario explícitamente ofrecido: cuenta de la sesión actual, sin elevación ni enumeración de perfiles ajenos. Para otro usuario hay que iniciar sesión con esa cuenta. Las API consultan Escritorio, Documentos, Descargas, Imágenes, Música, Videos, Favoritos y Partidas guardadas, incluidas rutas redirigidas. Una ubicación inaccesible queda visible. Agregar archivo/carpeta permite incluir ubicaciones adicionales autorizadas; la búsqueda automática de candidatos corresponde a B.

Se preservan todas las extensiones y la estructura relativa, incluidos directorios vacíos. Las selecciones idénticas o contenidas en otra se consolidan; se conserva el mapeo de la categoría contenedora. Un alias físico exacto se consolida cuando puede verificarse su identidad. No se siguen reparse points. Los errores, exclusiones técnicas y descargas pendientes impiden el estado completo.

## Copia y formato v1

Cada ejecución crea JPCFIX_Backup_fecha_hora_GUID con:

```
manifest.json
operation.lock
files/user-001/category-001/...
reports/backup-report.txt
```

No se crean todavía carpetas vacías de navegadores, aplicaciones o personalización. El manifiesto contiene GUID, versión, UTC, máquina/SO mínimos, perfil/SID, categorías, origen original informativo, mapeo relativo, directorios, tamaños, fechas, atributos compatibles, hashes, estados, errores y duración. Las rutas originales nunca deciden el destino de una restauración. Se requiere toda la carpeta portable, no solamente manifest.json.

El análisis registra una instantánea de nombres/tamaños/fechas. La copia abre cada archivo para lectura con FileShare.Read, impidiendo escritores compatibles con el modelo de compartición Windows. Rechaza cambios de tamaño/fecha desde el análisis, calcula SHA-256 por streaming, vuelve a leer y verificar el origen y verifica el temporal escrito. Solo entonces publica mediante movimiento dentro del mismo directorio. No hay mirrors ni borrados del destino. Después verifica nuevamente todos los archivos publicados antes de completar.

El manifiesto se publica atómicamente tras vaciar el temporal a disco. Hay checkpoints cada 25 entradas, al cambiar fases y al finalizar. Una interrupción puede dejar un archivo publicado todavía Pending; eso nunca se interpreta como éxito. Al abrir Copying/Verifying/Prepared/Analyzing se informa Recoverable y se permite recuperar exclusivamente entradas ya marcadas Verified y nuevamente comprobadas. No hay reanudación automática en A.

El formato acepta hasta 10.000 categorías, 500.000 entradas y 64 MiB de manifiesto. Un límite excedido bloquea el análisis o la publicación del manifiesto; hay que dividir el trabajo. La memoria depende principalmente del inventario, no del tamaño de los archivos (buffer de copia de 128 KiB).

## Restauración

Abrir → validar versión/esquema/nombres/mapeos → comprobar hashes → seleccionar categorías verificadas → resolver carpetas del usuario actual → mostrar rutas/conflictos → confirmar → copiar/verificar. Las categorías no mapeables y Extras van a Perfil/JPCFIX Recuperados/GUID/categoría. Nunca se restauran rutas absolutas originales.

Conservar ambos agrega un identificador al nombre y publica sin reemplazo; omitir mantiene la versión existente; reemplazar exige confirmación expresa y usa publicación verificada. Se recalculan las rutas autorizadas al ejecutar; un preview alterado o con conflictos nuevos se rechaza. Un archivo que aparece durante la copia no se sobrescribe si no era un conflicto revisado. Se conservan fechas de archivos y atributos de archivo/oculto/solo lectura cuando el destino lo permite, heredando permisos del Windows nuevo. El informe de restauración se guarda en LocalAppData/JPCFIX.Optimizer/BackupReports; el respaldo puede estar en un medio de solo lectura.

Los estados completos son verificables, no promesas frente a fallas físicas, ransomware o cambios posteriores. Las evoluciones del formato deberán implementar lectores/migradores explícitos; una versión desconocida se rechaza.

Referencias: [Known Folders y redirección](https://learn.microsoft.com/windows/win32/shell/working-with-known-folders), [flags de carpetas conocidas](https://learn.microsoft.com/windows/win32/api/shlobj_core/ne-shlobj_core-known_folder_flag), [reparse points](https://learn.microsoft.com/windows/win32/fileio/reparse-points).
