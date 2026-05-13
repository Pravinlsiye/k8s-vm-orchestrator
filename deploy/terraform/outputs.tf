output "resource_group_name" {
  value       = azurerm_resource_group.vmjob_workers.name
  description = "Name of the resource group"
}

output "admin_username" {
  value       = var.admin_username
  description = "Admin username for VMs"
}

output "admin_password" {
  value       = var.admin_password
  description = "Admin password for VMs"
  sensitive   = true
}

output "vm_count" {
  value       = var.vm_count
  description = "Number of VMs created"
}

output "vnet_id" {
  value       = azurerm_virtual_network.vmjob_vnet.id
  description = "Virtual Network ID"
}

output "subnet_id" {
  value       = azurerm_subnet.vmjob_subnet.id
  description = "Subnet ID"
}

output "vm_details" {
  value = [
    for i in range(var.vm_count) : {
      name        = azurerm_windows_virtual_machine.vmjob_worker[i].name
      vm_id       = azurerm_windows_virtual_machine.vmjob_worker[i].id
      private_ip  = azurerm_network_interface.vmjob_vm_nic[i].private_ip_address
      public_ip   = var.enable_public_ips ? azurerm_public_ip.vmjob_vm_pip[i].ip_address : null
      admin_user  = var.admin_username
    }
  ]
  description = "Details of created Windows worker VMs"
  sensitive   = false
}

output "storage_account_name" {
  value       = azurerm_storage_account.vmjob_storage.name
  description = "Storage account name for job results"
}

output "storage_account_key" {
  value       = azurerm_storage_account.vmjob_storage.primary_access_key
  description = "Storage account primary access key"
  sensitive   = true
}

output "storage_container_name" {
  value       = azurerm_storage_container.vmjob_results.name
  description = "Storage container name for job results"
}

output "winrm_connection_info" {
  value = [
    for i in range(var.vm_count) : {
      vm_name = azurerm_windows_virtual_machine.vmjob_worker[i].name
      host    = var.enable_public_ips ? azurerm_public_ip.vmjob_vm_pip[i].ip_address : azurerm_network_interface.vmjob_vm_nic[i].private_ip_address
      port    = 5985
      user    = var.admin_username
    }
  ]
  description = "WinRM connection information for each VM"
  sensitive   = false
}

# AKS cluster information
output "aks_cluster_name" {
  value       = var.deploy_aks ? azurerm_kubernetes_cluster.vmjob_aks[0].name : null
  description = "Name of the AKS cluster"
}

output "aks_resource_group" {
  value       = var.deploy_aks ? azurerm_kubernetes_cluster.vmjob_aks[0].resource_group_name : null
  description = "Resource group of the AKS cluster"
}

output "aks_get_credentials_command" {
  value       = var.deploy_aks ? "az aks get-credentials --resource-group ${azurerm_kubernetes_cluster.vmjob_aks[0].resource_group_name} --name ${azurerm_kubernetes_cluster.vmjob_aks[0].name}" : null
  description = "Command to get AKS credentials"
}

# ACR information
output "acr_login_server" {
  value       = var.deploy_aks ? azurerm_container_registry.vmjob_acr[0].login_server : null
  description = "ACR login server"
}

output "acr_name" {
  value       = var.deploy_aks ? azurerm_container_registry.vmjob_acr[0].name : null
  description = "ACR name"
}

output "acr_admin_username" {
  value       = var.deploy_aks ? azurerm_container_registry.vmjob_acr[0].admin_username : null
  description = "ACR admin username"
  sensitive   = true
}

output "acr_admin_password" {
  value       = var.deploy_aks ? azurerm_container_registry.vmjob_acr[0].admin_password : null
  description = "ACR admin password"
  sensitive   = true
}

