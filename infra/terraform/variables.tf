variable "project" {
  type    = string
  default = "rush-todo"
}

variable "location" {
  type    = string
  default = "australiaeast" # pick the region with quota
}

variable "environment" {
  type    = string
  default = "dev"
}

variable "db_admin_password" {
  type      = string
  sensitive = true
  # supply via TF_VAR_db_admin_password env var or a .tfvars file excluded from git — never commit
}
