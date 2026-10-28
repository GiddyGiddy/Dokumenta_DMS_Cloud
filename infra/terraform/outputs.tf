output "acr_name" {
  value = azurerm_container_registry.acr.name
}

output "acr_login_server" {
  value = azurerm_container_registry.acr.login_server
}

output "postgres_host" {
  value = local.pg_host
}

output "static_web_app_name" {
  value = azurerm_static_web_app.web.name
}

output "static_web_app_url" {
  value = "https://${azurerm_static_web_app.web.default_host_name}"
}

output "static_web_app_custom_domain" {
  value = var.swa_custom_domain
}

output "static_web_app_dns_zone_name" {
  value = var.dns_zone_name
}

output "static_web_app_dns_name_servers" {
  value = azurerm_dns_zone.custom_domain.name_servers
}

output "api_url" {
  value = var.deploy_apps ? "https://${azurerm_container_app.svc["docmanager"].ingress[0].fqdn}" : ""
}
