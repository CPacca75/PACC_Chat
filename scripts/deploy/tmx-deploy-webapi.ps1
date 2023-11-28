./package-webapi.ps1 -BuildConfiguration Debug 

./deploy-webapi.ps1 -Subscription 62d6eec4-c14f-4a64-a1d3-e7936044db02 -ResourceGroupName rg-max-copilot -DeploymentName max-copilot


# az deployment group show --name max-copilot --resource-group rg-max-copilot --output json | ConvertFrom-Json
# $deployment = $(az deployment group show --name max-copilot --resource-group rg-max-copilot --output json | ConvertFrom-Json)