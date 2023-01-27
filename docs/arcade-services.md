# Arcade services project breakdown

A breakdown of the projects contained in the arcade-services repo and a recommendation on how they should be split. The repo will be split into three repos:
 - Product Construction
 - Engineering Services
 - Shared
If a library/app is used by both Product Construction and Engineering Services, then it falls into the Shared repo. Otherwise, it falls in its designated repository.

| Project name | Product Construction/Engineering Services | Description |
| --- | --- | --- |
| DotNet.Status.Web | Engineering Services | Build monitor implementation. Users can configure which builds (pipelines) they want they want to monitor and who to send email notifications if the configured builds break. Also creates issues in arcade with the desired labels |
| Maestro | Product Construction | The service responsable for the Dependancy flow between the repo and registering builds to the BAR (Build Asset Registry). Uses DarcLib, and currently runs in the same cluster as the Telemetry application.
| RolloutScorer | Engineering Services | Calculates scorecards for our rollouts |
| PatGenerator | Engineering Services | Generates Pats. Has the ability to generate a Pat for multiple organizations |
| SecretManager | Engineering Services | Does automatic secret rotation if possible, or notifies users if it's not able to |
| Microsoft.DncEng.Configuration.Bootstrap | Shared | Library for bootstraping ServiceFabric functionality. Used for all of our ServiceFabric clusters (arcade-services, helix-services) |
| Microsoft.DotNet.ServiceFabric.ServiceHost | Shared | Similar as Microsoft.DncEng.Configuration.Bootstrap |
| Monitoring | Shared | Contains the arcade-services grafana boards. These will have to be split based on the (the Maestro dashboards will go to the Product Construction repo, while the Telemetry Dashboards will go to the EngServices repo). This Folder also contains Microsoft.DotNet.Monitoring.Sdk, which is used to set up all the pages. The Sdk will be in the Shared repo |
| Shared | Shared | A bunch of different libraries that are used throughout our repositories |
| Darc | Product Construction | The tools that gives Maestro its abilities to work with channels, subscriptions and builds |
| Telemetry | Engineering Services | A service that periodically wakes up and fetches information about the latest AzDo pipeline runs, and stores that information into our Kusto cluster |
| Microsoft.DncEng.DeployServiceFabricCluster | Shared | An attempt to enable starting ServiceFabric programatically, need to check with Alex on the status of it |
| Microsoft.DncEng.Configuration.Extensions | Shared | Brings extra functionallity to the json config files |
| Microsoft.DotNet.Internal.Tools.SynchronizePackageProps | ??? | Not sure what it does, but it's used in the Helix Service repo | 

The Repo currently contains 3 services, the DotNet.Status.Web Build monitoring service, Maestro and the Telemetry Service.
Maestro and the Telemetry service are running in the same cluster, so they will need to get split in the future.

## Maestro folder

Helix-Services consumes some of the projects in the Maestro folder as nugets. These projects are:
 - Microsoft.AspNetCore.ApiVersioning
 - Microsoft.AspNetCore.ApiVersioning.Swashbuckle
 - CoreHealthMonitor
 - Microsoft.AspNetCore.ApiVersioning.Analyzers
These projects should be extracted from the Maestro Folder and added to the Shared repo during the split

All of the Shared projects are already published to the dotnet-eng feed as nugets, so the split shouldn't require a lot of work.
We can also partialy arcadify the new repo, so that we can use darc to update the dependencies from the Shared repo

## Shared folder projects
| Project name | Product Construction/Engineering Services | Description |
| --- | --- | --- |
| Microsoft.DncEng.CommandLineLib | EngServices | A library that implements a lot of different command line helpers. Only used by EngService projects, so putting it in EngServices |
| Microsoft.DotNet.Authentication.Algorithms | EngServices | A library that that generates passwords. Only used by the SecretManager, so putting it in EngServices |
| Microsoft.DotNet.GitHub.Authentication | Shared | A library that helps with Octokit.GiTHubClient
| Microsoft.DotNet.Internal.DependencyInjection | Shared | A library that provides aditional functionality to DependencyInjection
| Microsoft.DotNet.Internal.DependencyInjection.Testing | Shared | A library that provides testing capabilities to the aditional functionalities added with Microsoft.DotNet.Internal.DependencyInjection |
| Microsoft.DotNet.Internal.Health | Shared | A library that provides health reporting for ServiceFabric services |
| Microsoft.DotNet.Internal.Logging | Shared | A library that provides loggin abilities to ServiceFabric services |
| Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen | Shared | A library that makes testing projects with DependencyInjection easier, by allowing generating a lot of the boilerplate code itself |
| Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions | Shared | Part of the Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen |
| Microsoft.DotNet.Internal.Testing.Utility | Shared | A library that provides some common testing utilities, like MockHttpClientFactory |
| Microsoft.DotNet.Kusto | Shared | A wrapper around Microsoft.Azure.Kusto |
| Microsoft.DotNet.Services.Utility | Shared | A library that provides some common utilities used by services |
| Microsoft.DotNet.Web.Authentication | Shared | A library that provides helpers for authentication |

A simplified dependancy graph can be seen at the following [link](https://microsofteur-my.sharepoint.com/:u:/r/personal/dkurepa_microsoft_com/_layouts/15/Doc.aspx?sourcedoc=%7B6A37A922-A4B9-4AEC-BDD4-175230AF0A1E%7D&file=Arcade-services%20split.vsdx&action=default&mobileredirect=true&share=IQEiqTdquaTsSr3UF1IwrwoeAfEdVdIRLrilPQSlsCSj2KM&cid=ffa9c5c1-2ca3-41a5-a3fd-7c8d653fb8d3)

A dependancy table can seen at the following [link](https://microsofteur-my.sharepoint.com/:x:/r/personal/dkurepa_microsoft_com/_layouts/15/Doc.aspx?sourcedoc=%7B29DA631A-B66D-44CB-9D0A-2D11E4162DA8%7D&file=arcade%20services%20dependancy%20graph.xlsx&action=default&mobileredirect=true&share=IQEaY9opbbbLRJ0KLRHkFi2oAU4jATewWPrrixZbx9EgCl0)


