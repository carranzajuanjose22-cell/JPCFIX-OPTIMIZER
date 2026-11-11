# JPCFIX Optimizer

Primera implementación portable para técnicos: C# / .NET 10, WPF, MVVM e inyección por constructor. Interfaz en español. Diagnóstico real de solo lectura y **una sola optimización: cambiar a un plan de energía existente**.

## Estado verificable de esta entrega

El repositorio inicial estaba vacío. Se generaron la solución, los seis proyectos, interfaz, API nativa, ejecutor UAC, persistencia, pruebas y documentación. **No se compiló, no se ejecutó WPF y no se ejecutaron los tests en este entorno**: no hay SDK `dotnet` en PATH y el acceso al servidor de descarga falló. La solicitud de descarga con permisos adicionales fue abortada. No existe aún un binario portable validado. Esta entrega requiere completar los pasos siguientes antes de usarla en equipos de clientes.

## Compilar y probar en Windows

Requisitos de desarrollo: Windows 10/11 x64, SDK .NET 10 y acceso a NuGet para la primera restauración (System.Management, xUnit y test SDK). No se necesita Visual Studio; si se usa, debe soportar .NET 10 y desarrollo de escritorio WPF.

```powershell
dotnet restore JPCFIX.Optimizer.slnx
dotnet build JPCFIX.Optimizer.slnx -c Release --no-restore
dotnet test JPCFIX.Optimizer.slnx -c Release --no-build
.\scripts\publish.cmd
```

La carpeta `artifacts/portable` contendrá `JPCFIX.Optimizer.Desktop.exe` y `Runner/JPCFIX.Optimizer.Runner.exe`, cada uno con sus dependencias autocontenidas. Copiar **toda la carpeta** al pendrive. Ejecutar Desktop sin elevación; UAC se solicita al ejecutar cambios o crear puntos de restauración. No se instala runtime, servicio ni proceso residente. La publicación requiere descargar paquetes de runtime; su uso posterior es offline.

Para desarrollar solo la interfaz: `dotnet run --project src/JPCFIX.Optimizer.Desktop`. Las operaciones privilegiadas requieren la distribución completa generada por el script.

## Uso

1. Analizar equipo: CPU, RAM, volúmenes, discos físicos, Windows, procesos, inicio y alimentación. Datos faltantes se indican como no disponibles; el inventario de inicio puede ser parcial.
2. Optimizaciones: actualizar planes, seleccionar uno y **Simular (DryRun)**. Revisar origen, destino, compatibilidad, riesgo y reversión.
3. Aplicar: confirmar en la interfaz, autorizar UAC y confirmar los valores nuevamente en el ejecutor. No se crean planes. Si hay una operación incompleta, recuperarla primero.
4. Historial: seleccionar una operación y revertir/recuperar. No se sobrescriben planes cambiados posteriormente por otra herramienta.
5. Configuración: elegir directorio local fijo de reportes y exportar JSON bajo solicitud. El registro de cambios es obligatorio. Los puntos de restauración son opcionales; no se habilita protección del sistema automáticamente.

Respaldos: `%ProgramData%\JPCFIX.Optimizer\Changes`. Auditoría: directorio hermano `Audit`. Solo Administradores y SYSTEM tienen escritura; Usuarios tienen lectura. Se rechazan directorios redirigidos. Configuración y reportes predeterminados: `%LocalAppData%\JPCFIX.Optimizer`. No borrar respaldos si hay operaciones pendientes. Las fechas del historial incluyen UTC.

Ver [arquitectura](docs/ARCHITECTURE.md), [seguridad](docs/SECURITY.md), [reglas](docs/OPTIMIZATION_RULES.md) y [validación](docs/TESTING.md).
