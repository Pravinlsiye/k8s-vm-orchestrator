param(
    [Parameter(Mandatory = $true)]
    [string]$Subscription,
    [string]$ResourceGroup = "k8s-vmjob-workers"
)

Write-Host "`nImporting Existing Azure Resources into Terraform State" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "`nThis script will import all existing resources to recover terraform state" -ForegroundColor Yellow
Write-Host ""

$resourceGroup = $ResourceGroup
$subscription = $Subscription

# Function to safely import resources
function Import-TerraformResource {
    param(
        [string]$ResourceType,
        [string]$ResourceName,
        [string]$AzureResourceId
    )
    
    Write-Host "`nImporting $ResourceType..." -ForegroundColor Yellow
    Write-Host "Resource: $ResourceName" -ForegroundColor Gray
    
    terraform import $ResourceType $AzureResourceId
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Successfully imported $ResourceName" -ForegroundColor Green
    } else {
        Write-Host "❌ Failed to import $ResourceName" -ForegroundColor Red
    }
}

# 1. Import Resource Group
Write-Host "`n📁 Importing Resource Group..." -ForegroundColor Cyan
Import-TerraformResource `
    -ResourceType "azurerm_resource_group.vmjob_workers" `
    -ResourceName "k8s-vmjob-workers" `
    -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup"

# 2. Import Virtual Network
Write-Host "`n🌐 Importing Virtual Network..." -ForegroundColor Cyan
Import-TerraformResource `
    -ResourceType "azurerm_virtual_network.vmjob_vnet" `
    -ResourceName "k8s-vmjob-workers-vnet" `
    -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Network/virtualNetworks/k8s-vmjob-workers-vnet"

# 3. Import Subnet
Write-Host "`n🔗 Importing Subnet..." -ForegroundColor Cyan
Import-TerraformResource `
    -ResourceType "azurerm_subnet.vmjob_subnet" `
    -ResourceName "k8s-vmjob-workers-subnet" `
    -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Network/virtualNetworks/k8s-vmjob-workers-vnet/subnets/k8s-vmjob-workers-subnet"

# 4. Import Network Security Group
Write-Host "`n🛡️ Importing Network Security Group..." -ForegroundColor Cyan
Import-TerraformResource `
    -ResourceType "azurerm_network_security_group.vmjob_nsg" `
    -ResourceName "k8s-vmjob-workers-nsg" `
    -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Network/networkSecurityGroups/k8s-vmjob-workers-nsg"

# 5. Import Public IPs
Write-Host "`n🌍 Importing Public IPs..." -ForegroundColor Cyan
for ($i = 1; $i -le 3; $i++) {
    $index = $i - 1
    Import-TerraformResource `
        -ResourceType "azurerm_public_ip.vmjob_vm_pip[$index]" `
        -ResourceName "k8s-vmjob-workers-vm-$i-pip" `
        -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Network/publicIPAddresses/k8s-vmjob-workers-vm-$i-pip"
}

# 6. Import Network Interfaces
Write-Host "`n🔌 Importing Network Interfaces..." -ForegroundColor Cyan
for ($i = 1; $i -le 3; $i++) {
    $index = $i - 1
    Import-TerraformResource `
        -ResourceType "azurerm_network_interface.vmjob_vm_nic[$index]" `
        -ResourceName "k8s-vmjob-workers-vm-$i-nic" `
        -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Network/networkInterfaces/k8s-vmjob-workers-vm-$i-nic"
}

# 7. Import NSG Associations
Write-Host "`n🔒 Importing NSG Associations..." -ForegroundColor Cyan
for ($i = 1; $i -le 3; $i++) {
    $index = $i - 1
    $nicId = "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Network/networkInterfaces/k8s-vmjob-workers-vm-$i-nic"
    Import-TerraformResource `
        -ResourceType "azurerm_network_interface_security_group_association.vmjob_nic_nsg[$index]" `
        -ResourceName "vm-$i-nic-nsg-association" `
        -AzureResourceId "$nicId|/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Network/networkSecurityGroups/k8s-vmjob-workers-nsg"
}

# 8. Import Virtual Machines
Write-Host "`n💻 Importing Virtual Machines..." -ForegroundColor Cyan
for ($i = 1; $i -le 3; $i++) {
    $index = $i - 1
    Import-TerraformResource `
        -ResourceType "azurerm_windows_virtual_machine.vmjob_worker[$index]" `
        -ResourceName "k8s-vmjob-workers-vm-$i" `
        -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Compute/virtualMachines/k8s-vmjob-workers-vm-$i"
}

# 9. Check for Storage Account
Write-Host "`n💾 Checking for Storage Account..." -ForegroundColor Cyan
$storageAccount = az storage account list --resource-group $resourceGroup --query "[0].name" -o tsv
if ($storageAccount) {
    Write-Host "Found storage account: $storageAccount" -ForegroundColor Gray
    Import-TerraformResource `
        -ResourceType "azurerm_storage_account.vmjob_storage" `
        -ResourceName $storageAccount `
        -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccount"
}

# 10. Check for AKS Cluster
Write-Host "`n☸️ Checking for AKS Cluster..." -ForegroundColor Cyan
$aksCluster = az aks list --resource-group $resourceGroup --query "[0].name" -o tsv
if ($aksCluster) {
    Write-Host "Found AKS cluster: $aksCluster" -ForegroundColor Gray
    Import-TerraformResource `
        -ResourceType "azurerm_kubernetes_cluster.vmjob_aks" `
        -ResourceName $aksCluster `
        -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.ContainerService/managedClusters/$aksCluster"
}

# 11. Check for Container Registry
Write-Host "`n📦 Checking for Container Registry..." -ForegroundColor Cyan
$acr = az acr list --resource-group $resourceGroup --query "[0].name" -o tsv
if ($acr) {
    Write-Host "Found ACR: $acr" -ForegroundColor Gray
    Import-TerraformResource `
        -ResourceType "azurerm_container_registry.vmjob_acr" `
        -ResourceName $acr `
        -AzureResourceId "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.ContainerRegistry/registries/$acr"
}

Write-Host "`n✅ Import process completed!" -ForegroundColor Green
Write-Host "`nNow running terraform plan to verify state..." -ForegroundColor Yellow
terraform plan
