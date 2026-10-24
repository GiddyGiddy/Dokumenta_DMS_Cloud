variable "subscription_id" {
  type        = string
  default     = null
  description = "Azure subscription ID (or set ARM_SUBSCRIPTION_ID)."
}

variable "resource_group_name" {
  type        = string
  default     = "rg-dokumenta"
  description = "Existing resource group (create it first: az group create -n rg-dokumenta -l westeurope)."
}

variable "location" {
  type        = string
  default     = null
  description = "Defaults to the resource group's location."
}

variable "prefix" {
  type        = string
  default     = "dokumenta"
  description = "Short lowercase prefix used in resource names (3-10 chars)."
  validation {
    condition     = can(regex("^[a-z0-9]{3,10}$", var.prefix))
    error_message = "prefix must be 3-10 lowercase letters/digits."
  }
}

variable "static_web_app_location" {
  type        = string
  default     = "westeurope"
  description = "Static Web Apps are only available in a few regions."
  validation {
    condition     = contains(["westeurope", "westus2", "centralus", "eastus2", "eastasia"], var.static_web_app_location)
    error_message = "Unsupported Static Web Apps region."
  }
}

variable "deploy_apps" {
  type        = bool
  default     = false
  description = "false = infra only (ACR, DB, storage, env, RabbitMQ). Build/push images, then apply again with true."
}

variable "image_tag" {
  type    = string
  default = "1"
}

variable "pg_admin_user" {
  type    = string
  default = "dokumentaadmin"
}

variable "pg_admin_password" {
  type      = string
  sensitive = true
}

variable "rabbit_password" {
  type      = string
  sensitive = true
}

variable "download_api_key" {
  type        = string
  sensitive   = true
  description = "Shared secret: ExtractionService -> DocuManagementApp downloads."
}

variable "artifact_api_key" {
  type        = string
  sensitive   = true
  description = "Shared secret: SearchService -> ExtractionService artifacts."
}

variable "search_api_key" {
  type        = string
  sensitive   = true
  description = "Shared secret: DocuManagementApp -> SearchService."
}

variable "document_intelligence_endpoint" {
  type    = string
  default = ""
}

variable "document_intelligence_key" {
  type      = string
  default   = ""
  sensitive = true
}

variable "gemini_key" {
  type      = string
  default   = ""
  sensitive = true
}

variable "gemini_model" {
  type        = string
  default     = "gemini-3.8-flash"
  description = "A current Gemini model, e.g. gemini-3.8-flash (see ai.google.dev/gemini-api/docs/models)."
}

variable "openai_key" {
  type      = string
  default   = ""
  sensitive = true
}

variable "embeddings_model" {
  type    = string
  default = "text-embedding-3-small"
}
