# Pruebas y validación pendiente

## Estado de la entrega

Actualización de la fase 2: se obtuvo el SDK .NET 10, se compiló la solución y se ejecutaron los casos xUnit de energía junto con Smart Backup. No se aplicaron cambios reales de energía ni puntos de restauración. La UI y la distribución portable todavía requieren la validación manual indicada abajo. Ver también [pruebas de Smart Backup](SMART_BACKUP_TESTING.md).

Ejecutar `dotnet restore`, `dotnet build -c Release` y `dotnet test -c Release` sobre JPCFIX.Optimizer.slnx. El proyecto de pruebas usa FakePower y almacén en memoria; la prueba JSON escribe solo un directorio temporal único. Nunca llama APIs de energía ni restauración del sistema. Cubre compatibilidad, destino inválido, original irrecuperable, dry run, idempotencia, respaldo previo, fallo de persistencia, preview obsoleto, solicitud alterada, aplicación fallida, verificación fallida, compensación fallida, reversión individual, interrupciones, conflictos externos, protocolo y reapertura JSON.

## Validación manual en Windows 10/11 y notebook

1. Publicar con scripts/publish.cmd y copiar toda la carpeta a un pendrive. Iniciar Desktop con usuario estándar y sin SDK/runtime instalado. Confirmar navegación, tamaños mínimos, estados de carga y ausencia de UAC durante diagnóstico y simulación.
2. Comparar datos con Administrador de tareas, Información del sistema y administración de discos. Probar proveedor WMI no disponible, procesos protegidos, discos USB, batería y alimentación AC. Verificar mensajes de información parcial.
3. Comparar planes enumerados y activo con `powercfg /list` y `powercfg /getactivescheme` (solo lectura). Probar equipo OEM/Modern Standby con un solo plan: sin alternativas no debe ofrecer una modificación efectiva.
4. Seleccionar alternativa, simular y cancelar ambas confirmaciones/UAC por separado. Verificar que Windows no cambió. Revisar advertencias de batería y consumo.
5. Autorizar un cambio en equipo de pruebas. Comprobar ACL/propietario de ProgramData, respaldo Prepared anterior al cambio, resultado Applied y plan activo. Elegir el mismo plan de nuevo: cero cambios adicionales.
6. Restaurar y comprobar GUID original exacto. Intentar revertir una operación vieja después de un cambio ajeno: debe bloquearse si el activo no coincide con original/destino.
7. En VM descartable, interrumpir el runner después del respaldo y antes/después de aplicar. Reiniciar Desktop, comprobar aviso y recuperar explícitamente. Probar almacenamiento de solo lectura, registro corrupto y desaparición del plan original: bloqueo y mensajes, sin asumir éxito.
8. Invocar runner con argumentos desconocidos, GUID inválidos y argumentos extra: rechazo. Probar dos instancias y usuarios simultáneos; solo una transacción propia a la vez. Intentar escritura del historial desde usuario estándar: acceso denegado. Probar carpeta precreada por usuario y junction: bloqueo.
9. Detectar puntos de restauración con protección habilitada/deshabilitada. Solicitar uno opcional, comprobar nueva secuencia en SystemRestore. Si Windows lo omite por frecuencia o política, mostrar fallo de verificación; no borrar ni alterar puntos existentes.
10. Exportar reporte, guardar configuración, reiniciar y comprobar persistencia fuera del pendrive. Medir temperatura, autonomía y una carga reproducible antes/después; no afirmar mejora solo por cambiar el nombre del plan.

Firmar binarios y repetir publicación/pruebas en Windows limpio antes de entrega a técnicos. Registrar sistema, hardware, resultados y cualquier limitación OEM en cada prueba manual.
