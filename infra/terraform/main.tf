terraform {
  required_version = ">= 1.6"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.10"
    }
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
  # Storage data-plane calls use Entra ID because shared keys are disabled.
  storage_use_azuread = true
}

data "azurerm_resource_group" "rg" {
  name = var.resource_group_name
}

data "azurerm_client_config" "current" {}

locals {
  location = coalesce(var.location, data.azurerm_resource_group.rg.location)
  uniq     = substr(md5(data.azurerm_resource_group.rg.id), 0, 13)

  acr_name     = lower("${var.prefix}${local.uniq}")
  pg_name      = lower("${var.prefix}-pg-${local.uniq}")
  swa_name     = "${var.prefix}-web-${local.uniq}"
  storage_name = lower("st${substr(var.prefix, 0, 8)}${substr(local.uniq, 0, 12)}")
  pg_host      = "${local.pg_name}.postgres.database.azure.com"

  rabbit_user = "dms"
  databases   = ["documanagementdb", "orchestratordb", "extractiondb", "searchdb"]

  role_base                = "/subscriptions/${data.azurerm_client_config.current.subscription_id}/providers/Microsoft.Authorization/roleDefinitions"
  acr_pull_role_id         = "7f951dda-4ed3-4680-a7ca-43fe172d538d"
  blob_data_contributor_id = "ba92f5b4-2d11-453d-a403-e96b0029c9fe"

  pg_conn = {
    for db in local.databases : db =>
    "Host=${local.pg_host};Port=5432;Database=${db};Username=${var.pg_admin_user};Password=${var.pg_admin_password};Ssl Mode=Require;Trust Server Certificate=true"
  }

  rabbit_secret = { "rabbit-pass" = var.rabbit_password }
  rabbit_env = [
    { name = "RabbitMq__HostName", value = "rabbitmq", secret = null },
    { name = "RabbitMq__UserName", value = local.rabbit_user, secret = null },
    { name = "RabbitMq__Password", value = null, secret = "rabbit-pass" },
  ]

  # Per app: sizing, ingress mode (none/internal/external), secrets and environment variables.
  apps = var.deploy_apps ? {
    docmanager = {
      cpu = 0.5, memory = "1Gi", min = 1, max = 2, ingress = "external"
      secrets = merge(local.rabbit_secret, {
        "db"           = local.pg_conn["documanagementdb"]
        "download-key" = var.download_api_key
        "search-key"   = var.search_api_key
      })
      env = concat(local.rabbit_env, [
        { name = "ASPNETCORE_URLS", value = "http://+:8080", secret = null },
        { name = "ConnectionStrings__DefaultConnection", value = null, secret = "db" },
        { name = "ServiceDownload__ApiKey", value = null, secret = "download-key" },
        { name = "SearchProxy__Enabled", value = "true", secret = null },
        { name = "SearchProxy__BaseUrl", value = "http://search", secret = null },
        { name = "SearchProxy__ApiKey", value = null, secret = "search-key" },
        { name = "BlobStorage__AccountUrl", value = azurerm_storage_account.docs.primary_blob_endpoint, secret = null },
        { name = "AZURE_CLIENT_ID", value = azurerm_user_assigned_identity.apps.client_id, secret = null },
        { name = "Cors__AllowedOrigins__0", value = "https://${azurerm_static_web_app.web.default_host_name}", secret = null },
        { name = "Database__MigrateOnStartup", value = "true", secret = null },
      ])
    }
    orchestrator = {
      cpu     = 0.25, memory = "0.5Gi", min = 1, max = 1, ingress = "none"
      secrets = merge(local.rabbit_secret, { "db" = local.pg_conn["orchestratordb"] })
      env = concat(local.rabbit_env, [
        { name = "ConnectionStrings__Orchestrator", value = null, secret = "db" },
      ])
    }
    extraction = {
      cpu = 0.5, memory = "1Gi", min = 1, max = 1, ingress = "internal"
      secrets = merge(local.rabbit_secret, {
        "db"           = local.pg_conn["extractiondb"]
        "download-key" = var.download_api_key
        "artifact-key" = var.artifact_api_key
        "di-key"       = var.document_intelligence_key != "" ? var.document_intelligence_key : "unset"
        "gemini-key"   = var.gemini_key != "" ? var.gemini_key : "unset"
      })
      env = concat(local.rabbit_env, [
        { name = "ConnectionStrings__Extraction", value = null, secret = "db" },
        { name = "ExtractionArtifactApi__ListenUrl", value = "http://0.0.0.0:8080", secret = null },
        { name = "ExtractionArtifactApi__PublicBaseUrl", value = "http://extraction", secret = null },
        { name = "ExtractionArtifactApi__ApiKey", value = null, secret = "artifact-key" },
        { name = "DocumentContentApi__BaseUrl", value = "http://docmanager/", secret = null },
        { name = "DocumentContentApi__ApiKey", value = null, secret = "download-key" },
        { name = "DocumentIntelligence__Endpoint", value = var.document_intelligence_endpoint, secret = null },
        { name = "DocumentIntelligence__ApiKey", value = null, secret = "di-key" },
        { name = "Foundry__Provider", value = "Gemini", secret = null },
        { name = "Foundry__Gemini__ModelName", value = var.gemini_model, secret = null },
        { name = "Foundry__Gemini__ApiKey", value = null, secret = "gemini-key" },
      ])
    }
    search = {
      cpu = 0.5, memory = "1Gi", min = 1, max = 1, ingress = "internal"
      secrets = merge(local.rabbit_secret, {
        "db"           = local.pg_conn["searchdb"]
        "search-key"   = var.search_api_key
        "artifact-key" = var.artifact_api_key
        "emb-key"      = var.openai_key != "" ? var.openai_key : "unset"
      })
      env = [
        { name = "ConnectionStrings__Search", value = null, secret = "db" },
        { name = "Search__ListenUrl", value = "http://+:8080", secret = null },
        { name = "Search__ApiKey", value = null, secret = "search-key" },
        { name = "Search__SourceDocumentBaseUrl", value = "http://docmanager/", secret = null },
        { name = "Search__RabbitMq__HostName", value = "rabbitmq", secret = null },
        { name = "Search__RabbitMq__UserName", value = local.rabbit_user, secret = null },
        { name = "Search__RabbitMq__Password", value = null, secret = "rabbit-pass" },
        { name = "Search__ArtifactApi__BaseUrl", value = "http://extraction", secret = null },
        { name = "Search__ArtifactApi__ApiKey", value = null, secret = "artifact-key" },
        { name = "Search__Embeddings__Provider", value = "OpenAI", secret = null },
        { name = "Search__Embeddings__Endpoint", value = "https://api.openai.com/v1", secret = null },
        { name = "Search__Embeddings__ModelName", value = var.embeddings_model, secret = null },
        { name = "Search__Embeddings__ApiKey", value = null, secret = "emb-key" },
      ]
    }
  } : {}
}

resource "azurerm_log_analytics_workspace" "logs" {
  name                = "${var.prefix}-logs"
  location            = local.location
  resource_group_name = data.azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  daily_quota_gb      = 0.5
}

resource "azurerm_container_registry" "acr" {
  name                = local.acr_name
  location            = local.location
  resource_group_name = data.azurerm_resource_group.rg.name
  sku                 = "Basic"
  admin_enabled       = false
}

resource "azurerm_user_assigned_identity" "apps" {
  name                = "${var.prefix}-id"
  location            = local.location
  resource_group_name = data.azurerm_resource_group.rg.name
}

resource "azurerm_role_assignment" "acr_pull" {
  scope              = azurerm_container_registry.acr.id
  role_definition_id = "${local.role_base}/${local.acr_pull_role_id}"
  principal_id       = azurerm_user_assigned_identity.apps.principal_id
  principal_type     = "ServicePrincipal"
}

resource "azurerm_storage_account" "docs" {
  name                            = local.storage_name
  location                        = local.location
  resource_group_name             = data.azurerm_resource_group.rg.name
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  account_kind                    = "StorageV2"
  access_tier                     = "Hot"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  min_tls_version                 = "TLS1_2"
  https_traffic_only_enabled      = true
}

resource "azurerm_storage_container" "documents" {
  name                  = "documents"
  storage_account_id    = azurerm_storage_account.docs.id
  container_access_type = "private"
}

resource "azurerm_role_assignment" "blob_contributor" {
  scope              = azurerm_storage_account.docs.id
  role_definition_id = "${local.role_base}/${local.blob_data_contributor_id}"
  principal_id       = azurerm_user_assigned_identity.apps.principal_id
  principal_type     = "ServicePrincipal"
}

resource "azurerm_postgresql_flexible_server" "pg" {
  name                          = local.pg_name
  location                      = local.location
  resource_group_name           = data.azurerm_resource_group.rg.name
  version                       = "16"
  sku_name                      = "B_Standard_B1ms"
  storage_mb                    = 32768
  administrator_login           = var.pg_admin_user
  administrator_password        = var.pg_admin_password
  backup_retention_days         = 7
  geo_redundant_backup_enabled  = false
  public_network_access_enabled = true

  lifecycle {
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_configuration" "extensions" {
  name      = "azure.extensions"
  server_id = azurerm_postgresql_flexible_server.pg.id
  value     = "VECTOR,PG_TRGM"
}

# 0.0.0.0 = "Allow Azure services" (cheapest; no VNet). Tighten with VNet integration later.
resource "azurerm_postgresql_flexible_server_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.pg.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# Flexible Server rejects concurrent database creation; if apply reports a conflict, re-run it
# or use `terraform apply -parallelism=1`.
resource "azurerm_postgresql_flexible_server_database" "db" {
  for_each  = toset(local.databases)
  name      = each.value
  server_id = azurerm_postgresql_flexible_server.pg.id
  charset   = "UTF8"
  collation = "en_US.utf8"

  depends_on = [azurerm_postgresql_flexible_server_configuration.extensions]
}

resource "azurerm_container_app_environment" "env" {
  name                       = "${var.prefix}-env"
  location                   = local.location
  resource_group_name        = data.azurerm_resource_group.rg.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.logs.id
}

resource "azurerm_container_app" "rabbit" {
  name                         = "rabbitmq"
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = data.azurerm_resource_group.rg.name
  revision_mode                = "Single"

  secret {
    name  = "rabbit-pass"
    value = var.rabbit_password
  }

  ingress {
    external_enabled = false
    transport        = "tcp"
    target_port      = 5672
    exposed_port     = 5672
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1
    container {
      name   = "rabbitmq"
      image  = "docker.io/library/rabbitmq:3.13-alpine"
      cpu    = 0.25
      memory = "0.5Gi"
      env {
        name  = "RABBITMQ_DEFAULT_USER"
        value = local.rabbit_user
      }
      env {
        name        = "RABBITMQ_DEFAULT_PASS"
        secret_name = "rabbit-pass"
      }
    }
  }
}

resource "azurerm_container_app" "svc" {
  for_each = local.apps

  name                         = each.key
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = data.azurerm_resource_group.rg.name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.acr.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  dynamic "secret" {
    for_each = each.value.secrets
    content {
      name  = secret.key
      value = secret.value
    }
  }

  dynamic "ingress" {
    for_each = each.value.ingress == "none" ? [] : [each.value.ingress]
    content {
      external_enabled = ingress.value == "external"
      target_port      = 8080
      transport        = "auto"
      traffic_weight {
        latest_revision = true
        percentage      = 100
      }
    }
  }

  template {
    min_replicas = each.value.min
    max_replicas = each.value.max
    container {
      name   = each.key
      image  = "${azurerm_container_registry.acr.login_server}/${each.key}:${var.image_tag}"
      cpu    = each.value.cpu
      memory = each.value.memory
      dynamic "env" {
        for_each = each.value.env
        content {
          name        = env.value.name
          value       = env.value.value
          secret_name = env.value.secret
        }
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.acr_pull,
    azurerm_role_assignment.blob_contributor,
    azurerm_postgresql_flexible_server_database.db,
    azurerm_container_app.rabbit,
  ]

  # CI (deploy-backend.yml) owns the image tag after the first apply.
  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}

resource "azurerm_static_web_app" "web" {
  name                = local.swa_name
  resource_group_name = data.azurerm_resource_group.rg.name
  location            = var.static_web_app_location
  sku_tier            = "Free"
  sku_size            = "Free"
}
