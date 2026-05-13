# Resource Group
resource "azurerm_resource_group" "vmjob_workers" {
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

# Virtual Network
resource "azurerm_virtual_network" "vmjob_vnet" {
  name                = "${var.resource_group_name}-vnet"
  address_space       = [var.vnet_address_space]
  location            = azurerm_resource_group.vmjob_workers.location
  resource_group_name = azurerm_resource_group.vmjob_workers.name
  tags                = var.tags
}

# Subnet
resource "azurerm_subnet" "vmjob_subnet" {
  name                 = "${var.resource_group_name}-subnet"
  resource_group_name  = azurerm_resource_group.vmjob_workers.name
  virtual_network_name = azurerm_virtual_network.vmjob_vnet.name
  address_prefixes     = [var.subnet_address_prefix]
}

# Network Security Group
resource "azurerm_network_security_group" "vmjob_nsg" {
  name                = "${var.resource_group_name}-nsg"
  location            = azurerm_resource_group.vmjob_workers.location
  resource_group_name = azurerm_resource_group.vmjob_workers.name
  tags                = var.tags

  # WinRM HTTP
  security_rule {
    name                       = "WinRM-HTTP"
    priority                   = 1001
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "5985"
    source_address_prefix      = var.control_plane_ip
    destination_address_prefix = "*"
  }

  # WinRM HTTPS
  security_rule {
    name                       = "WinRM-HTTPS"
    priority                   = 1002
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "5986"
    source_address_prefix      = var.control_plane_ip
    destination_address_prefix = "*"
  }

  # Kubelet API
  security_rule {
    name                       = "Kubelet"
    priority                   = 1003
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "10250"
    source_address_prefix      = var.control_plane_ip
    destination_address_prefix = "*"
  }

  # Kube-proxy metrics
  security_rule {
    name                       = "KubeProxy"
    priority                   = 1004
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "10256"
    source_address_prefix      = var.control_plane_ip
    destination_address_prefix = "*"
  }

  # NodePort range
  security_rule {
    name                       = "NodePorts"
    priority                   = 1005
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "30000-32767"
    source_address_prefix      = var.control_plane_ip
    destination_address_prefix = "*"
  }

  # RDP (for debugging only)
  security_rule {
    name                       = "RDP"
    priority                   = 1006
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "3389"
    source_address_prefix      = var.control_plane_ip
    destination_address_prefix = "*"
  }

  # Allow internal VNet traffic
  security_rule {
    name                       = "AllowVNetInbound"
    priority                   = 1100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "*"
    source_port_range          = "*"
    destination_port_range     = "*"
    source_address_prefix      = "VirtualNetwork"
    destination_address_prefix = "VirtualNetwork"
  }
}

# Public IPs (optional, for initial setup)
resource "azurerm_public_ip" "vmjob_vm_pip" {
  count               = var.enable_public_ips ? var.vm_count : 0
  name                = "${var.resource_group_name}-vm-${count.index + 1}-pip"
  location            = azurerm_resource_group.vmjob_workers.location
  resource_group_name = azurerm_resource_group.vmjob_workers.name
  allocation_method   = "Static"
  sku                 = "Standard"
  tags                = var.tags
}

# Network Interfaces
resource "azurerm_network_interface" "vmjob_vm_nic" {
  count               = var.vm_count
  name                = "${var.resource_group_name}-vm-${count.index + 1}-nic"
  location            = azurerm_resource_group.vmjob_workers.location
  resource_group_name = azurerm_resource_group.vmjob_workers.name
  tags                = var.tags

  ip_configuration {
    name                          = "internal"
    subnet_id                     = azurerm_subnet.vmjob_subnet.id
    private_ip_address_allocation = "Dynamic"
    public_ip_address_id          = var.enable_public_ips ? azurerm_public_ip.vmjob_vm_pip[count.index].id : null
  }
}

# Associate NSG with NICs
resource "azurerm_network_interface_security_group_association" "vmjob_nic_nsg" {
  count                     = var.vm_count
  network_interface_id      = azurerm_network_interface.vmjob_vm_nic[count.index].id
  network_security_group_id = azurerm_network_security_group.vmjob_nsg.id
}

# Random suffix for storage account uniqueness (only if not provided)
resource "random_string" "storage_suffix" {
  count   = var.storage_account_suffix == "" ? 1 : 0
  length  = 8
  special = false
  upper   = false
}

# Local value for storage account suffix
locals {
  storage_suffix = var.storage_account_suffix != "" ? var.storage_account_suffix : random_string.storage_suffix[0].result
}

# Storage Account for job results
resource "azurerm_storage_account" "vmjob_storage" {
  name                     = "vmjobresults${local.storage_suffix}"
  resource_group_name      = azurerm_resource_group.vmjob_workers.name
  location                 = azurerm_resource_group.vmjob_workers.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  tags                     = var.tags
}

resource "azurerm_storage_container" "vmjob_results" {
  name                  = "job-results"
  storage_account_name  = azurerm_storage_account.vmjob_storage.name
  container_access_type = "private"
}

# Windows Virtual Machines
resource "azurerm_windows_virtual_machine" "vmjob_worker" {
  count               = var.vm_count
  name                = "${var.resource_group_name}-vm-${count.index + 1}"
  computer_name       = "vmjob-vm${count.index + 1}"
  resource_group_name = azurerm_resource_group.vmjob_workers.name
  location            = azurerm_resource_group.vmjob_workers.location
  size                = var.vm_size
  admin_username      = var.admin_username
  admin_password      = var.admin_password
  tags                = merge(var.tags, {
    WorkerIndex = count.index + 1
    Role        = "VMJobWorker"
  })

  network_interface_ids = [
    azurerm_network_interface.vmjob_vm_nic[count.index].id,
  ]

  os_disk {
    name                 = "${var.resource_group_name}-vm-${count.index + 1}-osdisk"
    caching              = "ReadWrite"
    storage_account_type = "Premium_LRS"
    disk_size_gb         = var.disk_size_gb
  }

  source_image_reference {
    publisher = "MicrosoftWindowsServer"
    offer     = "WindowsServer"
    sku       = var.windows_version
    version   = "latest"
  }

  # Enable WinRM for new VMs
  winrm_listener {
    protocol = "Http"
  }

  # Auto-login configuration for new VMs
  additional_unattend_content {
    setting = "AutoLogon"
    content = "<AutoLogon><Password><Value>${var.admin_password}</Value></Password><Enabled>true</Enabled><LogonCount>1</LogonCount><Username>${var.admin_username}</Username></AutoLogon>"
  }

  # Lifecycle rules to prevent recreation of existing VMs
  lifecycle {
    ignore_changes = [
      additional_unattend_content,
      winrm_listener,
      os_disk[0].name,
      secure_boot_enabled,
      vtpm_enabled,
      vm_agent_platform_updates_enabled
    ]
  }
}

# VM extensions are configured via az vm run-command in the setup script
# This provides better control and error handling for WinRM setup
