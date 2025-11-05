# MCPs - Model Context Protocol Servers

Repositorio para proyectos de servidores MCP (Model Context Protocol).

## Dataverse DevOps MCP Server

Servidor MCP que expone metadatos de Dataverse para integración con herramientas de IA como GitHub Copilot.

### 🛠️ Herramientas Disponibles

1. **list_entities** - Lista todas las entidades en el ambiente Dataverse
2. **get_entity_metadata** - Obtiene metadata detallada de una entidad específica
3. **get_entity_attributes** - Obtiene todos los atributos de una entidad
4. **get_entity_relationships** - Obtiene todas las relaciones de una entidad

### 📋 Requisitos

- .NET 8.0 SDK
- Acceso a un ambiente Dataverse
- Credenciales de Application User (Client ID, Client Secret)
- Visual Studio Code (para usar el servidor MCP)

### 🚀 Configuración

#### 1. Configurar Credenciales

Edita el archivo `%APPDATA%\Code\User\mcp.json` en VS Code:

```json
{
  "servers": {
    "Dataverse_Devops_MCP": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/Users/Tu.Usuario/source/repos/MCPs/src/Dataverse_Devops_MCP/Dataverse_Devops_MCP.csproj"
      ],
      "type": "stdio",
      "env": {
        "DATAVERSE_TENANT_ID": "tu-tenant-id",
        "DATAVERSE_CLIENT_ID": "tu-client-id",
        "DATAVERSE_CLIENT_SECRET": "tu-client-secret",
        "DATAVERSE_INSTANCE_URL": "https://tuorg.crm4.dynamics.com"
      }
    }
  }
}
```

#### 2. Compilar el Proyecto

```powershell
cd src/Dataverse_Devops_MCP
dotnet build
```

#### 3. Probar el Servidor

Ejecuta el script de prueba rápida:

```powershell
.\quick-test.ps1
```

Este script:
- Compila el proyecto
- Inicia el servidor MCP
- Envía mensajes de prueba
- Muestra las herramientas disponibles

Si ves las 4 herramientas listadas, ¡el servidor funciona correctamente!

#### 4. Usar en VS Code

1. **Reinicia VS Code completamente** (cierra todas las ventanas)
2. Abre VS Code de nuevo
3. Las herramientas del servidor MCP deberían estar disponibles en GitHub Copilot
4. Puedes preguntar cosas como:
   - "List all entities in Dataverse"
   - "Get metadata for the account entity"
   - "Show me attributes of the contact entity"

### 🔍 Solución de Problemas

Si el servidor no funciona:

1. **Verifica las credenciales**: Asegúrate de que las credenciales en `mcp.json` sean correctas
2. **Revisa los logs**: Los logs del servidor se escriben a STDERR
3. **Ejecuta el test**: Usa `quick-test.ps1` para diagnóstico
4. **Lee el diagnóstico completo**: Revisa `DIAGNOSTICO_MCP.md` para más información

### 📚 Estructura del Proyecto

```
src/Dataverse_Devops_MCP/
├── Program.cs                 # Punto de entrada
├── Mcp/
│   └── McpServer.cs          # Implementación del protocolo MCP
├── Models/
│   └── McpModels.cs          # Modelos JSON-RPC
├── Services/
│   ├── IDataverseService.cs  # Interfaz del servicio
│   └── DataverseService.cs   # Implementación del servicio Dataverse
└── appsettings.json          # Configuración (alternativa a env vars)
```

### 🔐 Seguridad

**Importante**: Las credenciales en `mcp.json` están en texto plano. Para producción, considera:
- Usar Azure Key Vault
- Variables de entorno del sistema
- Managed Identity cuando sea posible

### 📖 Protocolo MCP

Este servidor implementa el [Model Context Protocol](https://spec.modelcontextprotocol.io/) versión 2024-11-05.

El protocolo MCP permite que herramientas de IA accedan a datos y funcionalidades externas de forma estandarizada.

### 🐛 Reportar Problemas

Si encuentras problemas:
1. Revisa `DIAGNOSTICO_MCP.md`
2. Ejecuta `quick-test.ps1` para diagnóstico
3. Revisa los logs en STDERR
4. Crea un issue con los detalles

### 📝 Licencia

Ver archivo LICENSE en la raíz del repositorio.
