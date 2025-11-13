# MCPs - Model Context Protocol Servers

Repositorio con tres servidores MCP (Model Context Protocol) especializados para diferentes funcionalidades.

## 📦 Servidores Disponibles

### 1. **Dataverse MCP Server** (`Dataverse_MCP`)
Servidor MCP especializado en operaciones con Microsoft Dataverse.

**Herramientas:**
- `list_entities` - Lista todas las entidades
- `get_entity_metadata` - Metadata detallada de entidades
- `get_entity_attributes` - Atributos de entidades
- `get_entity_relationships` - Relaciones entre entidades
- `create_record` - Crear registros
- `get_record` - Obtener registros por ID
- `update_record` - Actualizar registros
- `delete_record` - Eliminar registros
- `query_records` - Consultar múltiples registros

### 2. **DevOps MCP Server** (`DevOps_MCP`)
Servidor MCP especializado en Azure DevOps para gestión de work items e iteraciones.

**Herramientas:**
- `create_work_item` - Crear work items
- `update_work_item` - Actualizar work items
- `delete_work_item` - Eliminar work items
- `get_work_item` - Obtener work item por ID
- `list_work_items` - Listar work items (WIQL)
- `create_ado_project_from_tasks` - Crear iteraciones y work items desde un plan

### 3. **Planning MCP Server** (`Planning_MCP`)
Servidor MCP especializado en planificación de proyectos desde documentos.

**Herramientas:**
- `parse_document_to_tasks` - Extraer tareas de documentos (PDF/DOCX/MD/TXT)
- `infer_schedule_from_document` - Inferir calendario del proyecto
- `plan_tasks_into_iterations` - Asignar tareas a sprints

### 4. **Dataverse DevOps MCP Server** (`Dataverse_Devops_MCP`) - LEGACY
Servidor MCP original que combina todas las funcionalidades. **Se mantiene para compatibilidad pero se recomienda usar los servidores especializados.**

### 📋 Requisitos

- .NET 8.0 SDK
- Acceso a un ambiente Dataverse
- Credenciales de Application User (Client ID, Client Secret)
- Visual Studio Code (para usar el servidor MCP)

### 🚀 Configuración

Puedes usar uno o varios servidores según tus necesidades. Cada servidor es independiente.

#### Opción A: Configurar un Servidor Individual

Edita el archivo `%APPDATA%\Code\User\mcp.json` en VS Code:

**Para Dataverse MCP:**
```json
{
  "servers": {
    "Dataverse_MCP": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/Users/Tu.Usuario/source/repos/MCPs/src/Dataverse_MCP/Dataverse_MCP.csproj"
      ],
      "type": "stdio",
      "env": {
        "DATAVERSE_TENANT_ID": "tu-tenant-id",
        "DATAVERSE_CLIENT_ID": "tu-client-id",
        "DATAVERSE_CLIENT_SECRET": "tu-client-secret",
        "DATAVERSE_INSTANCE_URL": "https://tuorg.crm.dynamics.com"
      }
    }
  }
}
```

**Para DevOps MCP:**
```json
{
  "servers": {
    "DevOps_MCP": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/Users/Tu.Usuario/source/repos/MCPs/src/DevOps_MCP/DevOps_MCP.csproj"
      ],
      "type": "stdio",
      "env": {
        "ADO_ORG_URL": "https://dev.azure.com/tuorg",
        "ADO_PROJECT_NAME": "TuProyecto",
        "ADO_PAT": "tu-personal-access-token"
      }
    }
  }
}
```

**Para Planning MCP:**
```json
{
  "servers": {
    "Planning_MCP": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/Users/Tu.Usuario/source/repos/MCPs/src/Planning_MCP/Planning_MCP.csproj"
      ],
      "type": "stdio"
    }
  }
}
```

#### Opción B: Configurar Múltiples Servidores

Puedes combinar varios servidores en el mismo archivo `mcp.json`:

```json
{
  "servers": {
    "Dataverse_MCP": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/ruta/Dataverse_MCP/Dataverse_MCP.csproj"],
      "type": "stdio",
      "env": {
        "DATAVERSE_TENANT_ID": "...",
        "DATAVERSE_CLIENT_ID": "...",
        "DATAVERSE_CLIENT_SECRET": "...",
        "DATAVERSE_INSTANCE_URL": "..."
      }
    },
    "DevOps_MCP": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/ruta/DevOps_MCP/DevOps_MCP.csproj"],
      "type": "stdio",
      "env": {
        "ADO_ORG_URL": "...",
        "ADO_PROJECT_NAME": "...",
        "ADO_PAT": "..."
      }
    },
    "Planning_MCP": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/ruta/Planning_MCP/Planning_MCP.csproj"],
      "type": "stdio"
    }
  }
}
```

#### 2. Compilar los Proyectos

```powershell
# Compilar un servidor específico
cd src/Dataverse_MCP
dotnet build

cd src/DevOps_MCP
dotnet build

cd src/Planning_MCP
dotnet build

# O compilar todos desde la raíz
dotnet build MCPs.sln
```

#### 3. Probar los Servidores

Cada servidor puede ejecutarse individualmente:

```powershell
# Probar servidor de Dataverse
cd src/Dataverse_MCP/bin/Debug/net8.0
./Dataverse_MCP.exe

# Probar servidor de DevOps
cd src/DevOps_MCP/bin/Debug/net8.0
./DevOps_MCP.exe

# Probar servidor de Planning
cd src/Planning_MCP/bin/Debug/net8.0
./Planning_MCP.exe
```

Los servidores ejecutarán el protocolo MCP a través de stdio. Para probar, envía un mensaje de inicialización:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0.0"}}}
```

#### 4. Usar en VS Code

1. **Reinicia VS Code completamente** (cierra todas las ventanas)
2. Abre VS Code de nuevo
3. Las herramientas de los servidores MCP deberían estar disponibles en GitHub Copilot
4. Ejemplos de uso según el servidor:

**Dataverse MCP:**
   - "List all entities in Dataverse"
   - "Get metadata for the account entity"
   - "Create a contact record"

**DevOps MCP:**
   - "Create a new work item"
   - "List all tasks in the current sprint"
   - "Update work item 123"

**Planning MCP:**
   - "Parse tasks from this project document"
   - "Infer the project schedule"
   - "Plan tasks into sprints"

### 🔍 Solución de Problemas

Si algún servidor no funciona:

1. **Verifica las credenciales**: Asegúrate de que las credenciales en `mcp.json` sean correctas para cada servidor
2. **Revisa los logs**: Los logs de cada servidor se escriben a STDERR
3. **Prueba la conexión**: Ejecuta cada servidor manualmente desde su carpeta bin para verificar errores
4. **Verifica dependencias**: Asegúrate de tener instalado .NET 8.0 SDK
5. **Compila de nuevo**: Si hay errores, intenta `dotnet clean` y `dotnet build` en el proyecto específico

### 📚 Estructura de los Proyectos

**Dataverse MCP:**
```
src/Dataverse_MCP/
├── Program.cs                 # Punto de entrada con DI
├── Mcp/
│   └── McpServer.cs          # 9 herramientas de Dataverse
├── Models/
│   └── McpModels.cs          # Modelos JSON-RPC
├── Services/
│   ├── IDataverseService.cs  # Interfaz del servicio
│   └── DataverseService.cs   # Implementación con SDK de Dataverse
└── appsettings.json          # Configuración de conexión
```

**DevOps MCP:**
```
src/DevOps_MCP/
├── Program.cs                 # Punto de entrada con DI
├── Mcp/
│   └── McpServer.cs          # 6 herramientas de Azure DevOps
├── Models/
│   ├── McpModels.cs          # Modelos JSON-RPC
│   └── Planning/
│       └── AzureDevOpsModels.cs  # Modelos de ADO
├── Services/
│   ├── IDevOpsService.cs     # Interfaz del servicio
│   └── DevOpsService.cs      # Implementación con REST API
└── appsettings.json          # Configuración de ADO
```

**Planning MCP:**
```
src/Planning_MCP/
├── Program.cs                 # Punto de entrada con DI
├── Mcp/
│   └── McpServer.cs          # 3 herramientas de planificación
├── Models/
│   ├── McpModels.cs          # Modelos JSON-RPC
│   └── Planning/
│       ├── ParsedTask.cs     # Modelo de tarea
│       ├── ProjectSchedule.cs # Modelo de calendario
│       └── TaskPlan.cs       # Modelo de plan
├── Services/
│   └── Planning/
│       ├── DocumentTaskParser.cs        # Parser de documentos
│       ├── ScheduleInferenceService.cs  # Inferencia de fechas
│       └── TaskPlanner.cs               # Planificador de sprints
└── appsettings.json          # Verbos y palabras clave
```

**Servidor Legacy (Dataverse_Devops_MCP):**
- Mantiene todas las 18 herramientas combinadas
- Se preserva para compatibilidad con configuraciones existentes

### 🔐 Seguridad

**Importante**: Las credenciales en `mcp.json` están en texto plano. Para producción, considera:
- Usar Azure Key Vault para almacenar secretos
- Variables de entorno del sistema en lugar de archivos de configuración
- Managed Identity cuando ejecutes en Azure
- Personal Access Tokens con permisos mínimos necesarios

### 📖 Protocolo MCP

Estos servidores implementan el [Model Context Protocol](https://spec.modelcontextprotocol.io/) versión 2024-11-05.

El protocolo MCP permite que herramientas de IA accedan a datos y funcionalidades externas de forma estandarizada:
- **Comunicación**: stdio (stdin/stdout) usando JSON-RPC 2.0
- **Herramientas**: Cada servidor expone un conjunto específico de herramientas
- **Inicialización**: Handshake de versión del protocolo antes de ejecutar herramientas

### 📊 Resumen de Herramientas por Servidor

| Servidor | Herramientas | Propósito |
|----------|-------------|-----------|
| **Dataverse MCP** | 9 | Operaciones CRUD en Dataverse (entities, records, metadata) |
| **DevOps MCP** | 6 | Gestión de work items en Azure DevOps |
| **Planning MCP** | 3 | Parsing de documentos y planificación de sprints |
| **Legacy Combined** | 18 | Todas las funcionalidades combinadas (deprecated) |

### 🐛 Reportar Problemas

Si encuentras problemas:
1. Verifica que estés usando .NET 8.0 SDK o superior
2. Revisa los logs del servidor en STDERR
3. Asegúrate de que las credenciales tengan los permisos necesarios
4. Comprueba que las URLs de conexión sean correctas
2. Ejecuta `quick-test.ps1` para diagnóstico
3. Revisa los logs en STDERR
4. Crea un issue con los detalles

### 📝 Licencia

Ver archivo LICENSE en la raíz del repositorio.
