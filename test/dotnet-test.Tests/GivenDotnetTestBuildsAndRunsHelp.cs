﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandResult = Microsoft.DotNet.Cli.Utils.CommandResult;

namespace Microsoft.DotNet.Cli.Test.Tests
{
    public class GivenDotnetTestBuildsAndRunsHelp : SdkTest
    {
        public GivenDotnetTestBuildsAndRunsHelp(ITestOutputHelper log) : base(log)
        {
        }

        [Fact]
        public void RunHelpOnTestProject_ShouldReturnZeroAsExitCode()
        {
            TestAsset testInstance = _testAssetsManager.CopyTestAsset("TestProjectSolutionWithTestsAndArtifacts", Guid.NewGuid().ToString()).WithSource();

            CommandResult result = new DotnetTestCommand(Log, disableNewOutput: false)
                                    .WithWorkingDirectory(testInstance.Path)
                                    .WithEnableTestingPlatform()
                                    .WithTraceOutput()
                                    .Execute(CliConstants.HelpOptionKey);

            if (!TestContext.IsLocalized())
            {
                Assert.Matches(@"Extension options:\s+--[\s\S]*", result.StdOut);
                Assert.Matches(@"Options:\s+--[\s\S]*", result.StdOut);
            }

            result.ExitCode.Should().Be(0);
        }

        [Fact]
        public void RunHelpOnMultipleTestProjects_ShouldReturnZeroAsExitCode()
        {
            TestAsset testInstance = _testAssetsManager.CopyTestAsset("ProjectSolutionForMultipleTFMs", Guid.NewGuid().ToString())
                .WithSource();

            CommandResult result = new DotnetTestCommand(Log, disableNewOutput: false)
                                    .WithWorkingDirectory(testInstance.Path)
                                    .WithEnableTestingPlatform()
                                    .WithTraceOutput()
                                    .Execute(CliConstants.HelpOptionKey);

            if (!TestContext.IsLocalized())
            {
                Assert.Matches(@"Extension options:\s+--[\s\S]*", result.StdOut);
                Assert.Matches(@"Options:\s+--[\s\S]*", result.StdOut);

                string net9ProjectDllRegex = @"\s+.*\\net9\.0\\TestProjectWithNet9\.dll.*\s+--report-trx\s+--report-trx-filename";
                string net48ProjectExeRegex = @"\s+.*\\net4\.8\\TestProjectWithNetFramework\.exe.*\s+--report-trx\s+--report-trx-filename";

                Assert.Matches(@$"Unavailable extension options:(?:({net9ProjectDllRegex})|({net48ProjectExeRegex}))(?:({net9ProjectDllRegex})|({net48ProjectExeRegex}))", result.StdOut);
            }

            result.ExitCode.Should().Be(0);
        }

        [Fact]
        public void RunHelpOnTestProjectsWithHybridModeTestRunners_ShouldReturnOneAsExitCode()
        {
            TestAsset testInstance = _testAssetsManager.CopyTestAsset("HybridTestRunnerTestProjects", Guid.NewGuid().ToString())
                .WithSource();

            //.WithTraceOutput() should be removed later
            CommandResult result = new DotnetTestCommand(Log, disableNewOutput: false)
                                    .WithWorkingDirectory(testInstance.Path)
                                    .WithEnableTestingPlatform()
                                    .WithTraceOutput()
                                    .Execute(CliConstants.HelpOptionKey);

            if (!TestContext.IsLocalized())
            {
                result.StdOut.Should().Contain("Test application(s) that support VSTest are not supported.");
            }

            result.ExitCode.Should().Be(1);
        }
    }
}
