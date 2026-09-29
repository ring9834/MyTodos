output "acr_login_server" { value = azurerm_container_registry.this.login_server }
output "aks_cluster_name" { value = azurerm_kubernetes_cluster.this.name }
output "postgres_fqdn" { value = azurerm_postgresql_flexible_server.this.fqdn }
output "resource_group_name" { value = azurerm_resource_group.this.name }
output "azurerm_key_vault" { value = azurerm_key_vault.this.name }
