# AKS Cluster Configuration

# Create a subnet for AKS
resource "azurerm_subnet" "aks" {
  count                = var.deploy_aks ? 1 : 0
  name                 = "${var.resource_group_name}-aks-subnet"
  resource_group_name  = azurerm_resource_group.vmjob_workers.name
  virtual_network_name = azurerm_virtual_network.vmjob_vnet.name
  address_prefixes     = ["10.1.2.0/24"]
}

# AKS Cluster
resource "azurerm_kubernetes_cluster" "vmjob_aks" {
  count               = var.deploy_aks ? 1 : 0
  name                = "${var.resource_group_name}-aks"
  location            = azurerm_resource_group.vmjob_workers.location
  resource_group_name = azurerm_resource_group.vmjob_workers.name
  dns_prefix          = "${var.resource_group_name}-aks"
  kubernetes_version  = "1.31.11"  # Use latest stable version

  default_node_pool {
    name            = "default"
    node_count      = 1  # Single node for control plane
    vm_size         = "Standard_B4ms"  # 4 vCPU, 16GB RAM - better for controller
    os_disk_size_gb = 30
    vnet_subnet_id  = azurerm_subnet.aks[0].id
  }

  identity {
    type = "SystemAssigned"
  }

  network_profile {
    network_plugin    = "azure"
    network_policy    = "azure"
    service_cidr      = "10.2.0.0/16"
    dns_service_ip    = "10.2.0.10"
  }

  tags = merge(var.tags, {
    Environment = "vmjob-orchestrator"
    Type        = "ControlPlane"
  })
}

# Azure Container Registry for controller images
resource "azurerm_container_registry" "vmjob_acr" {
  count               = var.deploy_aks ? 1 : 0
  name                = "${replace(lower(var.resource_group_name), "-", "")}acr${random_string.acr_suffix[0].result}"
  resource_group_name = azurerm_resource_group.vmjob_workers.name
  location            = azurerm_resource_group.vmjob_workers.location
  sku                 = "Basic"
  admin_enabled       = true

  tags = merge(var.tags, {
    Environment = "vmjob-orchestrator"
    Type        = "Registry"
  })
}

# Random suffix for ACR name uniqueness
resource "random_string" "acr_suffix" {
  count   = var.deploy_aks ? 1 : 0
  length  = 4
  special = false
  upper   = false
}
