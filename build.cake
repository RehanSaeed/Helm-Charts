var target = Argument("Target", "Default");
var version =
    HasArgument("Version") ? Argument<string>("Version") :
    TFBuild.IsRunningOnVSTS ? TFBuild.Environment.Build.Number :
    EnvironmentVariable("Version") != null ? EnvironmentVariable("Version") :
    "1.0.0";
var artefactsDirectory =
    HasArgument("ArtefactsDirectory") ? Directory(Argument<string>("ArtefactsDirectory")) :
    EnvironmentVariable("ArtefactsDirectory") != null ? Directory(EnvironmentVariable("ArtefactsDirectory")) :
    Directory("./Artefacts");
var azureSubscriptionId =
    HasArgument("AzureSubscriptionId") ? Argument<string>("AzureSubscriptionId") :
    EnvironmentVariable("AzureSubscriptionId") != null ? EnvironmentVariable("AzureSubscriptionId") :
    null;
var azureContainerRegistryName =
    HasArgument("AzureContainerRegistryName") ? Argument<string>("AzureContainerRegistryName") :
    EnvironmentVariable("AzureContainerRegistryName") != null ? EnvironmentVariable("AzureContainerRegistryName") :
    null;
var azureContainerRegistryUsername =
    HasArgument("AzureContainerRegistryUsername") ? Argument<string>("AzureContainerRegistryUsername") :
    EnvironmentVariable("AzureContainerRegistryUsername") != null ? EnvironmentVariable("AzureContainerRegistryUsername") :
    null;
var azureContainerRegistryPassword =
    HasArgument("AzureContainerRegistryPassword") ? Argument<string>("AzureContainerRegistryPassword") :
    EnvironmentVariable("AzureContainerRegistryPassword") != null ? EnvironmentVariable("AzureContainerRegistryPassword") :
    null;

var chartVersion = version.Replace("-master", string.Empty);

Task("Clean")
    .Does(() =>
    {
        CleanDirectory(artefactsDirectory);
        CreateDirectory(artefactsDirectory);
        Information($"Cleaned {artefactsDirectory}");
    });

Task("Lint")
    .Does(() =>
    {
        var exitCode = StartProcess(
            "helm",
            new ProcessSettings()
            {
                Arguments = new ProcessArgumentBuilder()
                    .Append("lint")
                    .Append("Ummati")
                    .Append("--strict")
            });
        if (exitCode != 0)
        {
            throw new Exception("helm lint failed");
        }
    });

Task("Init")
    .Does(() =>
    {
        var exitCode = StartProcess(
            "helm",
            new ProcessSettings()
            {
                Arguments = new ProcessArgumentBuilder()
                    .Append("init")
                    .Append("--client-only")
            });
        if (exitCode != 0 && !TFBuild.IsRunningOnVSTS)
        {
            throw new Exception("helm init failed");
        }
    });

Task("Version")
    .Does(() =>
    {
        var chartYamlFilePath = GetFiles("./**/Chart.yaml").First().ToString();
        var chartYaml = System.IO.File.ReadAllText(chartYamlFilePath);
        chartYaml.Replace("1.0.0", chartVersion);
        System.IO.File.WriteAllText(chartYamlFilePath, chartYaml);

        Information($"Version set to {chartVersion} in Chart.yaml");
    });

Task("Package")
    .IsDependentOn("Clean")
    .IsDependentOn("Init")
    .IsDependentOn("Version")
    .Does(() =>
    {
        var exitCode = StartProcess(
            "helm",
            new ProcessSettings()
            {
                Arguments = new ProcessArgumentBuilder()
                    .Append("package")
                    .Append("Ummati")
                    .Append("--dependency-update")
                    .AppendSwitch("--destination", MakeAbsolute(artefactsDirectory).ToString())
                    .AppendSwitch("--version", chartVersion)
            });
        if (exitCode != 0)
        {
            throw new Exception("helm package failed");
        }
    });

Task("Push")
    .Does(() =>
    {
        // az acr helm push does not work without doing helm version
        StartProcess(
            "helm",
            new ProcessSettings()
            {
                Arguments = new ProcessArgumentBuilder()
                    .Append("version")
            });

        foreach(var package in GetFiles("./**/*.tgz"))
        {
             var exitCode = StartProcess(
                 Context.Tools.Resolve(IsRunningOnWindows() ? "az.cmd" : "az"),
                 new ProcessSettings()
                 {
                     Arguments = new ProcessArgumentBuilder()
                        .Append("acr helm push")
                        .Append("--force")
                        .AppendSwitch("--name", azureContainerRegistryName)
                        .AppendSwitch("--username", azureContainerRegistryUsername)
                        .AppendSwitch("--password", azureContainerRegistryPassword)
                        .AppendQuoted(package.ToString())
                 });
            if (exitCode != 0)
            {
                throw new Exception("acr helm push failed");
            }
        }
    });

Task("Default")
    .IsDependentOn("Package");

RunTarget(target);