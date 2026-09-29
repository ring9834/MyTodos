resource "azurerm_resource_group" "this" {
  name     = "${var.project}-${var.environment}-rg"
  location = var.location
}

resource "azurerm_container_registry" "this" {
  name                = replace("${var.project}${var.environment}acr", "-", "")
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  sku                 = "Basic"
  admin_enabled       = false # pull via AKS managed identity, not admin creds
}

# AKS-supported, subscription-allowed system-pool SKU. Must satisfy two separate gates:
# (1) AKS's own rule: >=4 vCPU, no B-series/Av1 for system pools.
# (2) This subscription's allowed-VM-size list for australiaeast (trial/restricted
#     subscriptions often exclude whole families — e.g. F-series wasn't available here;
#     Azure returns the full allowed list in the error if you hit this again).
# D4s_v5: standard general-purpose 4 vCPU/16GB, confirmed present in the allowed list.
resource "azurerm_kubernetes_cluster" "this" {
  name                = "${var.project}-${var.environment}-aks"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  dns_prefix          = "${var.project}${var.environment}"
  sku_tier            = "Free"

  default_node_pool {
    name       = "system"
    node_count = 1 # docs recommend >=2 for system pools; kept at 1 here deliberately for
    # cost, on a personal assessment cluster with no production reliability
    # requirement — verify this is actually accepted at apply time, not assumed
    vm_size = "Standard_D4s_v5"
  }

  identity { type = "SystemAssigned" }

  # Azure returns default upgrade_settings values (drain_timeout_in_minutes, max_surge, etc.)
  # that weren't explicitly set here, which every plan then tries to "correct" back to null —
  # a real, recurring problem: it blocked a targeted apply on ANY other resource whenever the
  # cluster was stopped, since -target pulls in the kubernetes provider's dependency
  # (this cluster) and Terraform then tries to reconcile this drift too. Ignored explicitly
  # rather than fought repeatedly — Azure's defaults here are fine as-is.
  lifecycle {
    ignore_changes = [default_node_pool[0].upgrade_settings]
  }
}

resource "azurerm_role_assignment" "aks_pull" {
  scope                = azurerm_container_registry.this.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
}

resource "azurerm_postgresql_flexible_server" "this" {
  name                   = "${var.project}-${var.environment}-pg"
  resource_group_name    = azurerm_resource_group.this.name
  location               = azurerm_resource_group.this.location
  version                = "16"
  sku_name               = "B_Standard_B1ms" # smallest burstable tier — check current pricing/quota
  storage_mb             = 32768
  administrator_login    = "todoadmin"
  administrator_password = var.db_admin_password
  zone                   = "1"
}

resource "azurerm_postgresql_flexible_server_database" "this" {
  name      = "tododb"
  server_id = azurerm_postgresql_flexible_server.this.id
}

resource "azurerm_key_vault" "this" {
  name                = "${var.project}-${var.environment}-kv"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  sku_name            = "standard"
  tenant_id           = data.azurerm_client_config.current.tenant_id
  # Missing originally — defaults to the legacy Access Policy model, which is why the
  # "Key Vault Secrets User" role assignment below had no actual effect until this was set:
  # the vault wasn't checking RBAC roles at all. Confirmed via a real 403 error, not assumed.
  enable_rbac_authorization = true
}

data "azurerm_client_config" "current" {}

# This Secret is the sole source Helm's api-deployment.yaml references (envFrom) —
# Helm never creates or edits it, avoiding dual ownership of the same K8s object. Values are
# read from Key Vault, never hardcoded here or in any file committed to git. Populate the
# two Key Vault secrets manually (az keyvault secret set) before the first apply of this block.
data "azurerm_key_vault_secret" "db_connection_string" {
  name         = "db-connection-string"
  key_vault_id = azurerm_key_vault.this.id
}

data "azurerm_key_vault_secret" "jwt_signing_key" {
  name         = "jwt-signing-key"
  key_vault_id = azurerm_key_vault.this.id
}

resource "kubernetes_secret" "todo_secrets" {
  metadata {
    name = "todo-secrets" # must match values.yaml's secretsName default in the Helm chart
  }
  data = {
    ConnectionStrings__Default = data.azurerm_key_vault_secret.db_connection_string.value
    Jwt__SigningKey            = data.azurerm_key_vault_secret.jwt_signing_key.value
  }
  type = "Opaque"
}

# Grants whichever identity is actually running `terraform apply` (your local `az login`
# session, or the CI service principal) permission to both read AND write secrets — "Key
# Vault Secrets Officer" rather than the read-only "Key Vault Secrets User", since this same
# identity is also the one running `az keyvault secret set` manually to seed values. Known
# limitation: this only covers whichever identity applies most recently; if BOTH your local
# Split into two static, permanent grants instead of one dynamic one — the dynamic
# `data.azurerm_client_config.current.object_id` only ever matched whoever happened to run
# `apply` most recently, causing a destroy-then-recreate every time a DIFFERENT identity ran
# it next (confirmed via a real 403: CI's service principal genuinely never had Key Vault
# access, since every prior grant/import today was done under the local user's own login).
resource "azurerm_role_assignment" "local_user_keyvault_officer" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = "ead30023-201f-42a5-9e39-f9bc01d812fb" # local user — for manual az keyvault secret set
}

resource "azurerm_role_assignment" "ci_keyvault_reader" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"               # read-only — CI only ever reads secrets, never sets them
  principal_id         = "2490b713-81fd-4fa8-97c7-d2b16d7ee5e7" # CI service principal
}

# Fixes a real connectivity gap: the Postgres Flexible Server ships with zero firewall
# rules by default, meaning NOTHING can reach it until explicitly allowed — confirmed via a
# real Npgsql connection timeout from a running pod, not assumed. start=0.0.0.0/end=0.0.0.0
# is Azure's documented special value meaning "allow any Azure-internal service", not a
# literal open-to-the-internet rule. This is the pragmatic choice for AKS-without-VNet-
# integration; a VNet-integrated setup (private access) would be the more secure alternative,
# deliberately deferred here for the same reasons we chose a single-node cluster.
resource "azurerm_postgresql_flexible_server_firewall_rule" "allow_azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}
