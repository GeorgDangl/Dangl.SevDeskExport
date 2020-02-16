using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DocFX;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using Nuke.GitHub;
using Nuke.WebDocu;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using static Nuke.Common.ChangeLog.ChangelogTasks;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.IO.TextTasks;
using static Nuke.Common.Tools.DocFX.DocFXTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.GitHub.GitHubTasks;
using static Nuke.WebDocu.WebDocuTasks;

[GitHubActions(
    "continuous",
    GitHubActionsImage.WindowsServer2019,
    On = new[] { GitHubActionsTrigger.Push },
    InvokedTargets = new[] { nameof(UploadDocumentation), nameof(PublishGitHubRelease) },
    ImportGitHubTokenAs = nameof(GitHubAuthenticationToken),
    AutoGenerate = false,
    ImportSecrets = new[] { nameof(DocuApiKey) })]
[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter] readonly string DocuApiKey;
    [Parameter] readonly string DocuBaseUrl = "https://docs.dangl-it.com";
    [Parameter] readonly string GitHubAuthenticationToken;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion] readonly GitVersion GitVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath DocFxFile => RootDirectory / "docfx.json";
    AbsolutePath ChangeLogFile => RootDirectory / "CHANGELOG.md";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => EnsureCleanDirectory(d));
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => EnsureCleanDirectory(d));
            EnsureCleanDirectory(OutputDirectory);
            EnsureCleanDocFxArtifactx();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(_ => _
                .SetProjectFile(Solution));
        });

    Target GenerateVersionService => _ => _
        .Executes(() =>
        {
            var buildDate = DateTime.UtcNow;
            var filePath = SourceDirectory / "Dangl.SevDeskExport" / "VersionInfo.cs";

            var currentDateUtc = $"new DateTime({buildDate.Year}, {buildDate.Month}, {buildDate.Day}, {buildDate.Hour}, {buildDate.Minute}, {buildDate.Second}, DateTimeKind.Utc)";

            var content = $@"using System;
namespace Dangl.SevDeskExport
{{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    // This file is automatically generated
    [System.CodeDom.Compiler.GeneratedCode(""GitVersionBuild"", """")]
    public static class VersionInfo
    {{
        public static string Version => ""{GitVersion.NuGetVersionV2}"";
        public static string CommitInfo => ""{GitVersion.FullBuildMetaData}"";
        public static string CommitDate => ""{GitVersion.CommitDate}"";
        public static string CommitHash => ""{GitVersion.Sha}"";
        public static string InformationalVersion => ""{GitVersion.InformationalVersion}"";
        public static DateTime BuildDateUtc => {currentDateUtc};
    }}
}}";
            WriteAllText(filePath, content);
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .DependsOn(GenerateVersionService)
        .Executes(() =>
        {
            DotNetBuild(_ => _
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .EnableNoRestore());
        });

    Target Publish => _ => _
        .DependsOn(Clean)
        .DependsOn(GenerateVersionService)
        .Executes(() =>
        {
            foreach (var publishTarget in PublishTargets)
            {
                var tempPublishPath = OutputDirectory / "TempPublish";
                EnsureCleanDirectory(tempPublishPath);
                var zipPath = OutputDirectory / $"{publishTarget[0]}.zip";
                DotNetPublish(x => x
                    .SetWorkingDirectory(SourceDirectory / "Dangl.SevDeskExport")
                    .SetSelfContained(true)
                    .SetConfiguration(Configuration.Release)
                    .SetRuntime(publishTarget[1])
                    .SetOutput(tempPublishPath)
                    .SetFileVersion(GitVersion.AssemblySemFileVer)
                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
                    .SetInformationalVersion(GitVersion.InformationalVersion)
                    .When(publishTarget[1] == "ubuntu-x64", c => c.SetArgumentConfigurator(a => a
                       .Add("/p:PublishTrimmed=true")
                       .Add("/p:PublishSingleFile=true")
                       .Add("/p:DebugType=None")))
                    .When(publishTarget[1] != "ubuntu-x64", c => c.SetArgumentConfigurator(a => a
                       .Add("/p:PublishTrimmed=true")
                       .Add("/p:PublishSingleFile=true")
                       .Add("/p:DebugType=None")
                       .Add("/p:PublishReadyToRun=true")))
                    );
                ZipFile.CreateFromDirectory(tempPublishPath, zipPath);
            }
        });

    string[][] PublishTargets => new string[][]
             {
                new [] { "CLI_Windows_x86", "win-x86"},
                new [] { "CLI_Windows_x64", "win-x64"},
                new [] { "CLI_Linux_Ubuntu_x86", "ubuntu-x64"}
             };

    Target PublishGitHubRelease => _ => _
         .Requires(() => GitHubAuthenticationToken)
         .OnlyWhenDynamic(() => GitVersion.BranchName.Equals("master") || GitVersion.BranchName.Equals("origin/master"))
         .Executes(async () =>
         {
             var releaseTag = $"v{GitVersion.MajorMinorPatch}";

             var changeLogSectionEntries = ExtractChangelogSectionNotes(ChangeLogFile);
             var latestChangeLog = changeLogSectionEntries
                 .Aggregate((c, n) => c + Environment.NewLine + n);
             var completeChangeLog = $"## {releaseTag}" + Environment.NewLine + latestChangeLog;

             var repositoryInfo = GetGitHubRepositoryInfo(GitRepository);

             await PublishRelease(x => x
                     .SetCommitSha(GitVersion.Sha)
                     .SetReleaseNotes(completeChangeLog)
                     .SetRepositoryName(repositoryInfo.repositoryName)
                     .SetRepositoryOwner(repositoryInfo.gitHubOwner)
                     .SetTag(releaseTag)
                     .SetToken(GitHubAuthenticationToken));
         });

    Target BuildDocFxMetadata => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DocFXMetadata(x => x
                .SetProjects(DocFxFile)
                .SetLogLevel(DocFXLogLevel.Warning));
        });

    Target BuildDocumentation => _ => _
        .DependsOn(Clean)
        .DependsOn(BuildDocFxMetadata)
        .Executes(() =>
        {
            // Using README.md as index.md
            if (File.Exists(RootDirectory / "index.md"))
            {
                File.Delete(RootDirectory / "index.md");
            }

            File.Copy(RootDirectory / "README.md", RootDirectory / "index.md");

            DocFXBuild(x => x
                .SetConfigFile(DocFxFile)
                .SetLogLevel(DocFXLogLevel.Warning));

            File.Delete(RootDirectory / "index.md");
            EnsureCleanDocFxArtifactx();
        });

    void EnsureCleanDocFxArtifactx()
    {
        DeleteDirectory(RootDirectory / "obj");
    }

    Target UploadDocumentation => _ => _
         .DependsOn(BuildDocumentation)
         .DependsOn(Publish)
         .Requires(() => DocuApiKey)
         .Requires(() => DocuBaseUrl)
         .Executes(() =>
         {
             var markdownChangelog = ReadAllText(ChangeLogFile);

             WebDocu(s => s
                 .SetDocuBaseUrl(DocuBaseUrl)
                 .SetDocuApiKey(DocuApiKey)
                 .SetSourceDirectory(OutputDirectory / "docs")
                 .SetVersion(GitVersion.NuGetVersion)
                 .SetMarkdownChangelog(markdownChangelog)
                 .SetAssetFilePaths(PublishTargets.Select(t => (OutputDirectory / $"{t[0]}.zip").ToString()).ToArray())
             );
         });
}
