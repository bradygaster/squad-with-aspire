# Todo List App — Azure Infrastructure

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                  Azure Container Apps                     │
│  ┌─────────────────┐       ┌──────────────────────┐    │
│  │  TodoList.Web   │──────▶│   TodoList.Api       │    │
│  │  (Blazor UI)    │       │   (Minimal API)      │    │
│  └─────────────────┘       └──────────┬───────────┘    │
│                                        │                 │
└────────────────────────────────────────┼─────────────────┘
                                         │
                    ┌────────────────────┼────────────────────┐
                    │                    │                     │
            ┌───────▼──────┐   ┌────────▼───────┐   ┌───────▼──────┐
            │  Cosmos DB   │   │   Key Vault    │   │  App Insights│
            │  (NoSQL)     │   │   (Secrets)    │   │  (Telemetry) │
            │              │   │                │   │              │
            │ DB: TodoListDb│   │                │   │              │
            │ Container:   │   │                │   │              │
            │   todos      │   │                │   │              │
            └──────────────┘   └────────────────┘   └──────────────┘
```

## Resources

| Resource | SKU / Tier | Purpose |
|----------|-----------|---------|
| Azure Container Apps | Consumption (serverless) | Host API + Web, scales 0→5 |
| Azure Cosmos DB | Serverless + Free Tier | Document store for todo items |
| Azure Key Vault | Standard | Connection strings & secrets |
| Application Insights | Pay-as-you-go | Distributed tracing & logs |
| Log Analytics Workspace | PerGB2018 | Backing store for App Insights |

## Cosmos DB Schema

**Database:** `TodoListDb`  
**Container:** `todos`  
**Partition Key:** `/id`

```json
{
  "id": "guid",
  "title": "string",
  "isComplete": false,
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

## .NET Aspire Integration (Local Dev)

In the `TodoList.AppHost/Program.cs`, wire up resources like this:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Azure Cosmos DB — uses emulator locally, real instance in production
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsEmulator()
    .AddDatabase("TodoListDb");

// Application Insights
var insights = builder.AddAzureApplicationInsights("insights");

// Key Vault
var keyVault = builder.AddAzureKeyVault("secrets");

// API project
var api = builder.AddProject<Projects.TodoList_Api>("api")
    .WithReference(cosmos)
    .WithReference(insights)
    .WithReference(keyVault);

// Web frontend
builder.AddProject<Projects.TodoList_Web>("web")
    .WithReference(api)
    .WithReference(insights);

builder.Build().Run();
```

## Deployment

Deploy with Azure CLI + Bicep:

```bash
az group create --name rg-todolist --location eastus2
az deployment group create \
  --resource-group rg-todolist \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam
```

## Security Notes (per security squad)

- Cosmos DB uses private endpoints in production (network isolation)
- Key Vault accessed via Managed Identity (no client secrets)
- TLS 1.2+ enforced on all ingress
- RBAC authorization on Key Vault (no access policies)
- Container Apps use system-assigned managed identity
