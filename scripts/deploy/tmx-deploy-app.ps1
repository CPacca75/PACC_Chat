./package-webapi.ps1 -BuildConfiguration Debug 
./package-plugins.ps1 -BuildConfiguration Debug
./package-memorypipeline.ps1 -BuildConfiguration Debug

./deploy-webapi.ps1 -Subscription 62d6eec4-c14f-4a64-a1d3-e7936044db02 -ResourceGroupName rg-max-copilot -DeploymentName max-copilot
./deploy-plugins.ps1 -Subscription 62d6eec4-c14f-4a64-a1d3-e7936044db02 -ResourceGroupName rg-max-copilot -DeploymentName max-copilot
./deploy-memorypipeline.ps1 -Subscription 62d6eec4-c14f-4a64-a1d3-e7936044db02 -ResourceGroupName rg-max-copilot -DeploymentName max-copilot

