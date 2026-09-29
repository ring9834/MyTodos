terraform {
  required_version = ">= 1.9"
  required_providers {
    azurerm    = { source = "hashicorp/azurerm", version = "~> 4.0" }
    kubernetes = { source = "hashicorp/kubernetes", version = "~> 2.30" }
  }
  # NOTE: backend blocks cannot reference variables/locals — Terraform must resolve where
  # its own state lives before it evaluates any configuration. These values are therefore
  # hardcoded literals, not an inconsistency with var.project/var.environment used below.
  #
  # This resource group/storage account are bootstrapped manually (az cli, once, outside
  # this config — see docs) and are intentionally separate from the app's own resource
  # group (azurerm_resource_group.this, named from var.project/var.environment). They hold
  # only the Terraform state file, are shared across all environments, and must never be
  # touched by `terraform destroy` run against this config.
  backend "azurerm" {
    resource_group_name  = "rush-todo-tfstate-rg"
    storage_account_name = "rushtodotfstate8791"
    container_name       = "tfstate"
    key                  = "rush-todo.tfstate" # per-environment key if you add more later
    # Without this, the backend authenticates to the state BLOB using the storage
    # account's access key (fetched via listKeys — a separate control-plane permission
    # requiring broad, unscoped account access), not the AAD/OIDC token already used for
    # everything else in this project. Confirmed via a real 403 on listKeys/action.
    # This makes the Storage Blob Data Contributor role already granted sufficient on its
    # own — no listKeys permission needed at all, consistent with RBAC-only access
    # everywhere else in this project (same principle as the Key Vault RBAC switch).
    use_azuread_auth = true
  }
}

provider "azurerm" {
  features {}
}

# Configured against the AKS cluster this same Terraform config creates (main.tf) — this is
# why the kubernetes_secret resource below must come after azurerm_kubernetes_cluster.this
# in Terraform's dependency graph (implicit, via the references in these blocks).
provider "kubernetes" {
  host                   = azurerm_kubernetes_cluster.this.kube_config[0].host
  client_certificate     = base64decode(azurerm_kubernetes_cluster.this.kube_config[0].client_certificate)
  client_key             = base64decode(azurerm_kubernetes_cluster.this.kube_config[0].client_key)
  cluster_ca_certificate = base64decode(azurerm_kubernetes_cluster.this.kube_config[0].cluster_ca_certificate)
}
