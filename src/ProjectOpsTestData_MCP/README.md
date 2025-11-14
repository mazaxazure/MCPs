# Project Operations Test Data MCP

Este es un servidor MCP (Model Context Protocol) especializado para generar y crear datos de prueba para el módulo de **Project Operations** de **Dynamics 365**.

## Características

### Generación de Datos
- **Cuentas (Accounts)**: Compañías con información realista incluyendo industria, ubicación, contactos y datos financieros
- **Recursos (Resources)**: Usuarios, equipos y instalaciones con precios y información de facturación
- **Proyectos (Projects)**: Proyectos con fechas, presupuestos y detalles del proyecto
- **Tareas (Tasks)**: Tareas del proyecto con estimaciones, asignaciones y progreso

### Funcionalidades del MCP
- **generate_accounts**: Genera datos de prueba para cuentas
- **generate_resources**: Genera datos de prueba para recursos 
- **generate_projects**: Genera datos de prueba para proyectos con tareas
- **create_accounts**: Crea cuentas de prueba en Dataverse
- **create_resources**: Crea recursos de prueba en Dataverse
- **create_projects**: Crea proyectos con tareas en Dataverse
- **create_complete_dataset**: Crea un conjunto completo de datos de prueba
- **delete_all_test_data**: Elimina todos los datos de prueba de proyectos y tareas
- **get_statistics**: Obtiene estadísticas de los datos de prueba existentes

## Configuración

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "ConnectionStrings": {
    "Dataverse": "AuthType=ClientSecret;Url=https://yourorg.crm.dynamics.com;ClientId=your-client-id;ClientSecret=your-client-secret;TenantId=your-tenant-id;"
  }
}
```

### Variables de Entorno

También puedes configurar usando variables de entorno:
- `DATAVERSE_URL`: URL de tu instancia de Dataverse
- `DATAVERSE_CLIENT_ID`: Client ID de la aplicación Azure AD
- `DATAVERSE_CLIENT_SECRET`: Client Secret de la aplicación
- `DATAVERSE_TENANT_ID`: Tenant ID de Azure AD

## Uso

### Ejecución del Servidor MCP

```bash
cd src/ProjectOpsTestData_MCP
dotnet run
```

### Herramientas Disponibles

#### 1. Crear Conjunto Completo de Datos
```json
{
  "tool": "create_complete_dataset",
  "arguments": {
    "accountCount": 25,
    "resourceCount": 20,
    "projectCount": 15,
    "tasksPerProject": 8
  }
}
```

#### 2. Crear Solo Proyectos
```json
{
  "tool": "create_projects",
  "arguments": {
    "projectCount": 10,
    "tasksPerProject": 5
  }
}
```

#### 3. Obtener Estadísticas
```json
{
  "tool": "get_statistics",
  "arguments": {}
}
```

#### 4. Limpiar Datos de Prueba
```json
{
  "tool": "delete_all_test_data",
  "arguments": {}
}
```

## Entidades de Dataverse Utilizadas

- **account**: Cuentas/Compañías
- **bookableresource**: Recursos reservables
- **msdyn_project**: Proyectos de Project Operations
- **msdyn_projecttask**: Tareas de proyecto
- **transactioncurrency**: Monedas del proyecto

## Datos Generados

### Cuentas
- Nombres de compañías realistas
- Industrias variadas (Tecnología, Manufactura, Servicios Financieros, etc.)
- Ubicaciones geográficas diversas
- Información de contacto (teléfono, email, sitio web)
- Datos financieros (ingresos, número de empleados)

### Recursos
- Nombres de personas realistas
- Tipos de recurso: Usuario, Equipo, Instalación
- Precios de costo y venta configurables
- Información de facturación

### Proyectos
- Nombres de proyecto descriptivos
- Fechas de inicio y fin realistas
- Monedas múltiples (EUR, USD, GBP, MXN)
- Asignación de clientes y gerentes de proyecto

### Tareas
- Nombres de tareas técnicos
- Estimaciones de esfuerzo realistas (8-120 horas)
- Fechas de programación coherentes
- Asignación de recursos
- Estados de progreso variables

## Dependencias

- .NET 8.0
- Microsoft.PowerPlatform.Dataverse.Client
- Bogus (para generación de datos falsos)
- Microsoft.Extensions.Hosting
- System.Text.Json

## Notas de Implementación

- Utiliza la librería **Bogus** para generar datos realistas y variados
- Implementa conversión de tipos apropiada para campos de Dataverse
- Maneja referencias de entidades y campos de lookup correctamente
- Incluye logging detallado para troubleshooting
- Soporta limpieza automática de datos de prueba

## Ejemplo de Uso con GitHub Copilot

1. "Crea 20 proyectos de prueba con 6 tareas cada uno"
2. "Genera estadísticas de los datos de prueba actuales"
3. "Elimina todos los datos de proyectos de prueba"
4. "Crea un conjunto completo de datos con 30 cuentas y 15 proyectos"