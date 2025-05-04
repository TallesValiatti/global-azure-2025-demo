# Create resource group
az group create --name rg-ai-project-prod-westus3 --location westus3

# Bicep what-if command

az deployment group what-if \
--resource-group rg-ai-project-prod-westus3 \
--template-file main.bicep \
--parameters azuredeploy.parameters.json

# Deploy Bicep template
az deployment group create \
--resource-group rg-ai-project-prod-westus3 \
--template-file main.bicep \
--parameters azuredeploy.parameters.json

# Delete resource group
az group delete --name rg-ai-project-prod-westus3 --yes --no-wait