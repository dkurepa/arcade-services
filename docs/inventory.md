## Global .NET Engineering services primary entities

- The C# code
- Different scripts (yaml, bash, powershell)
- Azure Resources
- Service Tree
- AzDo resources (Service connections, etc.., these will need to be set up with the new Azure Resources)
- Security entities (S360, CG, etc..)

## Service Tree

We currently have the following services registered in Service Tree:
- **.NET Engineering Services - Non-Prod**. This is for the R&D Subscriptions that are used to support the .NET Engineering Services. Contains the following subscriptions:
    - dnceng-internaltooling (84a65c9a-787d-45da-b10a-3a1cefce8060), not sure what's inside of this one
    - hurlburb-sandbox (4998401b-8682-431b-96d5-46bd2f8684f2), disabled
    - preetik-hurlburb (0c249eea-065b-4034-955e-795d56b1e5d1), not sure what's inside of this one, can't find it on the Azure Portal
    - DotNetEng - AzDO (f3680867-423d-4372-9515-a3819012c492), not sure waht's inside of this one, can't find it on the Azure Portal
- **.NET Engineering Services Azure DevOps**. This is for the machines we have hooked up to VSTS in order to do our official builds. Only contains the NESBUILD subscription (aba09ef5-8279-4d1f-bdba-02c56fc3ca73)
- **Helix**. Top node service containing other services:
    - Arcade
    - Arcade Pool Provider (I think this one isn't used anymore, as we're using 1ES now)
    - Arcade Services - DotNet Status Web
    - Arcade Services - Grafana
    - Arcade Services - Maestro
    - Arcade Services - Rollout Scorer
    - Helix

    None of these sub services contain any subscriptions. All of the subscriptions are in the top node service:
    - HelixStaging (cab65fc3-d077-467d-931f-3932eabf36d3)
    - dncenghelix-02 (a6ad62fb-177e-40ef-af51-5c342911ebf5)
    - Dotnet Engineering services (a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1)
    - dncenghelix-01 (f8c1f536-2a9b-41ba-9868-811cc982bb25)
    - dncenghelix-03 (06ba37e3-6c20-430c-8d4f-9a6b70d2fef1)
    - Helix-PME (eaab930d-5f52-41af-b5b8-e936752447eb)
    - dncenghelix-04 (4700f441-ec3b-442a-9f0c-f5cbe283eea4)
    - Helix (68672ab8-de0c-40f1-8d1b-ffb20bd62c0f)

## Repos

The joint engineering services team currently owns the following repos:
- dotnet/arcade: mainly contains Arcade SDK, which is a set of msbuild props and targets files and packages that provide common build features used across multiple repos, such as CI integration, packaging, VSIX and VS setup authoring, testing, and signing via Microbuild
- dotnet/arcade-validation: contains testing and validation scenarios for Arcade
- dotnet/arcade-services: contains different services used by the Engineering Services team, the Monitoring Sdk, and some Service Fabric libraries used by the repos
- dotnet/xharness: contains XHarness, a CI tool that enalbes xUnit like testing for Android, Apple iOS / tvOS / WatchOS / Mac Catalyst, WASI and desktop browsers (WASM)
- dotnet/versions: This repo contains information about the various component versions that ship with .NET Core. Currently, it's mainly used for the mirroring service configuration
- dotnet-migrate-package: contains the tool used to mirror packages from nuget.org to the dnceng dotnet-public feed
- dotnet-helix-service: contains the HelixAPI service, the Auto Scaler, along side other different tools that are used by Helix
- dotnet-helix-machines: contains configurations for the OSOB machines, and tooling around them
- dotnet-release: contains the Stage-DotNet and Release-DotNet pipelines, along with other tooling needed for the .NET Release process

## Azure Resources

Azure resources are split in a few different subscriptions. I will briefly describe what is inside of them an go more in depth if the subscription contains resources interesting to Product construction:
- **dnceng-internaltooling**: contains Helix VM image definitions, helix base-image definitions, MoT storage account and some resource groups around docker stuff. All in all, this looks like a purely Helix centric subscription
- **dncenghelix-01**: contains scale sets and networks for some of the Helix queues
- **dncenghelix-02**: contains scale sets and networks for some of the Helix queues
- **dncenghelix-03**: contains scale sets and networks for some of the Helix queues
- **dncenghelix-04**: contains scale sets and networks for some of the Helix queues
- **Dotnet Engineering services**: contains different Resource groups with potentially interesting content:
    - **AlperoviTest**-deployment
    - **ARM_Deploy_Staging**
    - **aspnet-investigate-vs2019**: this one is empty
    - **AzSecPackAutoConfigRG**: I'm not sure what this is, but it sounds interesting. Contains a few Managed Identity instances
    - **AzureBackupRG_westus2_1**: Contains Restore Point Collections, not sure what those are used for
    - **barviztest**: empty
    - **BuildTelemetry**: Contains two Application Insight instances that sound interesting, but have not been used for a while, might be dead
    - **chadnedz-test**
    - **chcosta**
    - **cloud-shell-storage-southcentralus**: doesn't look interesting
    - **cloud-shell-storage-westus**: doesn't look interesting
    - **cloud-shell-storage-southeastasia**: doesn't look interesting
    - **CLRJIT**: doesn't look interesting for Product Construction
    - **dashboards**: contains one dashboard that doesn't have any data
    - **DefaultResourceGroup-EUS**
    - **DefaultResourceGroup-EUS2**
    - **DefaultResourceGroup-SCUS**
    - **DefaultResourceGroup-WUS**
    - **DefaultResourceGroup-WUS2**
    - **dnceng-partners-kv**: contains the dnceng-partners-kv key vault, probably not interesting for Product Construction
    - **dotnet-release**: contains the DotnetReleaseKV, the vault only has 1 Certificate and 1 Secret tho
    - **DotNet.SourceBuild**: contains the dotnetsourcebuild container registry. Can't see what's inside of it
    - **dotnetbuild**: contains some storage accounts that don't appear to be used anymore. Could be interesting, not sure
    - **DotnetDocker**: empty
    - **dotnetrelease**: contains a storage account with the same name that has the `linuxpublishingverification` table, not sure what it's used for
    - **GitHubTeamSupport**: contains the Bot Channel Registration, which appears to be a bot that can bridge conversations between github and Teams
    - **helixaiexport**: not sure
    - **HelixImages**: contains a virtual network and a NSG (Network Security Group)
    - **HelixImageTest**
    - **helixprodanalysis**:
    - **HelixReproVMs**
    - **helixstaginganalysis**
    - **ImageFactoryImages**: empty
    - **KeyVaults**: contains many KeyVaults used in many different repos, some of these are needed for the Staging pipeline for sure
    - **LogAnalyticsDefaultResources**
    - **managed-nsg-westus2**: contains the managed-silent-nsg, don't know how it's used
    - **mistuckeeus**: contains a few storage accounts, the name looks personal tho
    - **mistucke-sourcedotnet**: empty
    - **MLpublicassets**: empty
    - **monitoring**: contains different Azure resources around grafana dashboards
    - **NetworkWatcherRG**: contains 4 different Network Watchers, don't know where or how they're used
    - **rollout-scorecards**: contains the storage account for the Rollout Scorer
    - **rollout-scorecards-staging**: contains the storage account for the Staging Rollout Scorer
    - **secret-manager-scenario-tests**: contains different azure resources that appear to be used for Secret Manager testing
    - **source.dot.net**: contains Azure resources for the source.dot.net app, which looks like a webapp that lets people browse .NET source code easily
    - **StorageAccounts**: contains different storage accounts, some of these might be interesting for Product Construction, we should take a closer look with someone
    - **t-jperez**: looks personal, think this was used for the Intern project
    - **test_group**: empty
    - **winrelease**: contains the WinReleaseKV, don't have the permissions to view the content
- **Helix**: this subscription contains both HelixAPI and Maestro, among other things, so it's a big mix. I will skip all of the Helix queue related Resource Groups:
    - **1ESHostedPoolImages-rg**: empty
    - **1ESHostedPoolImagesWestUS-rg**: empty
    - **1ESHostedPoolManagedIdentity-rg**: as the name says, it just contains the ManagedIdentity with the same name
    - **ARM_Deploy_Staging**: contains a storage account that stage68672ab8de0c40f18d1, which has a maestro-prod-stageartifacts Container, with some ps1 and jsons, might be interesting. We have a RG with the same name in the Dotnet Engineering services subscription for some reason
    - **AzSecPackAutoConfigRG**: we have a RG with the same name in the Dotnet Engineering services subscription. This one also just contains some Managed Identities
    - **cleanupservice**: empty
    - **cloud-shell-storage-westus**: contains an empty storage account
    - **CPTAzureNSG**: empty
    - **dashboards**: contains the repro tool prod dashboard
    - **Default-ServiceBus-WestUS2**: contains the NetHelix Service Bus
    - **Default-Storage-CentralUS**: empty
    - **Default-ActivityLogAlerts**: empty
    - **Default-Storage-WestUS2**: empty
    - **DefaultResourceGroup-EUS**
    - **DefaultResourceGroup-WUS2**
    - **DefaultResourceGroup-WUS**
    - **dotnet-eng-cluster**: contains the dotnet-eng Application Insights
    - **dotnet-eng-helix-cluster-1**: contains the dotnet-eng-helix-c1 Service Fabric cluster, this might be the Helix Api cluster
    - **DotNetEngSvcs**: contains a key vault with the same name, that only contains one certificate: -NET-Core-Engineering-Secret-Access
    - **DotnetFeedInternalProxy**: contains the dotnet-feed-internal Function app and app insights, not sure what this is
    - **dotnetpackaging**: contains a storage account dotnetpackagearchive, that doesn't look too interesting. Might be wrong
    - **helix-autoscale-prod**
    - **helix-gateway-prod**
    - **helix-image-factory**
    - **helix-os-automation**
    - **helix-prod-deployment**
    - **helixdata-prod**
    - **helixinfrarg**
    - **HelixMachineLogs**
    - **HelixManagedDisks**
    - **HelixManagedDisks-WestUS**
    - **HelixManagedStorageAccounts**
    - **HelixMonitoring**
    - **HelixNSG**
    - **helixprodkusto**: contains the prod Kusto cluster, might be interesting for us too
    - **HelixProdKV**: empty
    - **HelixSigningRG**: empty
    - **LogAnalyticsDefaultResources**
    - **ImageFactoryImages**: empty
    - **maestro**: contains the BAR SQL database, some other maestro storage accounts and the maestroKV
    - **maestro-prod-cluster**: contains the Maestro Service Fabric cluster, with some other things
    - **managed-nsg**: contains the managed-silent-nsg
    - **managed-nsg-westus2**: contains the managed-silent-nsg
    - **managed-nsg-westus**: contains the managed-silent-nsg
    - **managed-nsg-eastus**: contains the managed-silent-nsg
    - **monitoring**: contains the DotNet.Status App service, storage account and app insights
    - **MsrcFeedProxy**: contains the dotnetclimsrc and dotnetfeedmsrc function apps. Not sure what these are
    - **NetworkWatcherRG**: contains network watchers for different locations (westus, westus2, eastus)
    - **performance**: contains perfbenchmarkstorage storage account. Don't know what it's used for
    - **repro-tool**: no tool is actually here, just the Primary-nsg network security group
    - **riarenas-pim-tests**
    - **rollout-scorer-prod**: has the Rollout scorer function app and storage acc
    - **securitydata**: contains the 681857westus2 storage account. looks like it hasn't been used in a while
    - **ToolsKV**: empty
- **HelixStaging**: similar to the Helix subscription, just staging instead of prod:
    - 



