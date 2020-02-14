using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using System;
using System.IO.Compression;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.IO.TextTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion] readonly GitVersion GitVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
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
                     .SetInformationalVersion(GitVersion.InformationalVersion));
                 ZipFile.CreateFromDirectory(tempPublishPath, zipPath);
             }
         });

    string[][] PublishTargets => new string[][]
             {
                new [] { "CLI_Windows_x86", "win-x86"},
                new [] { "CLI_Windows_x64", "win-x64"},
                new [] { "CLI_Linux_Ubuntu_x86", "ubuntu-x64"}
             };
}
