# JPCFIX Optimizer

Aplicación portable para técnicos: C# / .NET 10, WPF, MVVM e inyección por constructor. Interfaz en español. Diagnóstico de solo lectura, **una sola optimización: cambiar a un plan de energía existente**, y **Smart Backup — Entrega A**, independiente del optimizador.

## Estado verificable de esta entrega

Se generó la distribución autocontenida Windows x64 y pasaron **93 casos de prueba**, incluidos presentación/navegación WPF, Smart Backup, regresiones de energía y protección del auxiliar UAC. La interfaz se rediseñó con un dashboard de datos reales, navegación lateral, tarjetas y controles oscuros compartidos. Se comprobó el arranque publicado, diagnóstico, simulación, copia sintética verificada y preview de restauración en Windows 11. La matriz de VMs limpias Windows 10/11, DPI físico y USB sigue pendiente. No se realizaron commits ni push. Ver [rediseño UI, archivos y validación](docs/UI_REDESIGN.md).

## Ejecutar sin instalar

Copiar **toda** la carpeta `dist/JPCFIX-Optimizer-Portable/` al pendrive, incluida `Runner`, y hacer doble clic en **JPCFIX-Optimizer.exe**. No se necesita instalar .NET ni ejecutar scripts. El inicio no solicita administrador; solo los cambios privilegiados solicitan UAC. No copiar únicamente el EXE principal, porque el auxiliar se distribuye separado para conservar el límite de privilegios. Ver [distribución portable, publicación y validación](docs/PORTABLE_DISTRIBUTION.md).

## Smart Backup — Entrega A

En el menú lateral, elegir Smart Backup. Crear nuevo respaldo detecta las carpetas conocidas del usuario de la sesión; permite personalizar, agregar archivos/carpetas, analizar tamaño/errores y elegir un destino local, USB o UNC. La confirmación muestra espacio/formato y advertencias de disco. La copia usa temporales, SHA-256 por streaming y una segunda verificación; cualquier error, exclusión o descarga pendiente impide anunciar un respaldo completo.

Para restaurar, trasladar **toda la carpeta JPCFIX_Backup_…**, seleccionar Restaurar respaldo, verificar, elegir categorías y revisar rutas/conflictos. Se usan las carpetas actuales del usuario nuevo; Extras va a JPCFIX Recuperados. Por defecto conserva ambos archivos; reemplazar requiere confirmación. El backup incluye manifest.json y reports/backup-report.txt; los informes de restauración quedan en LocalAppData/JPCFIX.Optimizer/BackupReports.

Solo se ofrece el usuario actual: para otra cuenta hay que iniciar sesión con ella. Esta entrega no incluye marcadores de navegadores, Mis aplicaciones/WinGet, fondo de pantalla ni reanudación automática (entregas B/C). No descarga archivos de nube y rechaza enlaces/reparse points. **Los respaldos no están cifrados. No formatear con errores, pendientes o un destino que se perderá al borrar el disco.**

Ver [arquitectura de Smart Backup](docs/SMART_BACKUP_ARCHITECTURE.md), [seguridad](docs/SMART_BACKUP_SECURITY.md), [pruebas y procedimiento en VM](docs/SMART_BACKUP_TESTING.md) y [límites y pendientes](docs/SMART_BACKUP_LIMITATIONS.md).

## Compilar y probar en Windows

Requisitos de desarrollo: Windows 10/11 x64, SDK .NET 10 y acceso a NuGet para la primera restauración (System.Management, xUnit y test SDK). No se necesita Visual Studio; si se usa, debe soportar .NET 10 y desarrollo de escritorio WPF.

```powershell
dotnet restore JPCFIX.Optimizer.slnx
dotnet build JPCFIX.Optimizer.slnx -c Release --no-restore
dotnet test JPCFIX.Optimizer.slnx -c Release --no-build
.\scripts\publish.cmd
```

La carpeta `dist/JPCFIX-Optimizer-Portable` contiene `JPCFIX-Optimizer.exe` y `Runner/JPCFIX.Optimizer.Runner.exe`, cada uno como un único archivo autocontenido con sus bibliotecas nativas incluidas. Copiar **toda la carpeta** al pendrive. Ejecutar el principal sin elevación; UAC se solicita al ejecutar cambios o crear puntos de restauración, después de comprobar la identidad del auxiliar. No se instala runtime, servicio ni proceso residente. Solo la compilación/publicación requiere SDK y descarga inicial de paquetes; el uso del paquete es offline.

Para desarrollar solo la interfaz: `dotnet run --project src/JPCFIX.Optimizer.Desktop`. Las operaciones privilegiadas requieren la distribución completa generada por el script.

## Uso

1. Analizar equipo: CPU, RAM, volúmenes, discos físicos, Windows, procesos, inicio y alimentación. Datos faltantes se indican como no disponibles; el inventario de inicio puede ser parcial.
2. Optimizaciones: actualizar planes, seleccionar uno y **Simular (DryRun)**. Revisar origen, destino, compatibilidad, riesgo y reversión.
3. Aplicar: confirmar en la interfaz, autorizar UAC y confirmar los valores nuevamente en el ejecutor. No se crean planes. Si hay una operación incompleta, recuperarla primero.
4. Historial: seleccionar una operación y revertir/recuperar. No se sobrescriben planes cambiados posteriormente por otra herramienta.
5. Configuración: elegir directorio local fijo de reportes y exportar JSON bajo solicitud. El registro de cambios es obligatorio. Los puntos de restauración son opcionales; no se habilita protección del sistema automáticamente.

Respaldos: `%ProgramData%\JPCFIX.Optimizer\Changes`. Auditoría: directorio hermano `Audit`. Solo Administradores y SYSTEM tienen escritura; Usuarios tienen lectura. Se rechazan directorios redirigidos. Configuración y reportes predeterminados: `%LocalAppData%\JPCFIX.Optimizer`. No borrar respaldos si hay operaciones pendientes. Las fechas del historial incluyen UTC.

Ver [arquitectura](docs/ARCHITECTURE.md), [seguridad](docs/SECURITY.md), [reglas](docs/OPTIMIZATION_RULES.md) y [validación](docs/TESTING.md).
