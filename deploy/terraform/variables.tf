variable "subscription_id" {
  description = "Azure Subscription ID"
  type        = string
  # Set in terraform.tfvars
}

variable "resource_group_name" {
  description = "Name of the Azure Resource Group"
  type        = string
  default     = "k8s-vmjob-workers"
}

variable "location" {
  description = "Azure region for resources"
  type        = string
  default     = "East US"
}

variable "vm_count" {
  description = "Number of Windows worker VMs to create"
  type        = number
  default     = 2
}

variable "vm_size" {
  description = "Size of the Windows VMs"
  type        = string
  default     = "Standard_B2s"  # 2 vCPUs, 4 GB RAM
}

variable "admin_username" {
  description = "Admin username for Windows VMs"
  type        = string
  default     = "vmjobadmin"
}

variable "admin_password" {
  description = "Admin password for Windows VMs"
  type        = string
  sensitive   = true
  # Set in terraform.tfvars
}

variable "windows_version" {
  description = "Windows Server version SKU"
  type        = string
  default     = "2022-datacenter-azure-edition"
}

variable "vnet_address_space" {
  description = "Address space for the virtual network"
  type        = string
  default     = "10.1.0.0/16"
}

variable "subnet_address_prefix" {
  description = "Address prefix for the subnet"
  type        = string
  default     = "10.1.1.0/24"
}

variable "control_plane_ip" {
  description = "Your public IP address for debugging access (use format: x.x.x.x/32)"
  type        = string
  # Set in terraform.tfvars
}

variable "enable_public_ips" {
  description = "Enable public IPs for VMs (needed for initial setup)"
  type        = bool
  default     = true
}

variable "deploy_aks" {
  description = "Deploy AKS cluster and ACR"
  type        = bool
  default     = false
}

variable "storage_account_suffix" {
  description = "Suffix for storage account name. If not provided, a random one will be generated."
  type        = string
  default     = ""
}

variable "disk_size_gb" {
  description = "OS disk size in GB"
  type        = number
  default     = 128
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default = {
    Environment = "K8sVMJob"
    ManagedBy   = "Terraform"
    Purpose     = "VM-Worker-Nodes"
    Project     = "TestFramework"
  }
}

