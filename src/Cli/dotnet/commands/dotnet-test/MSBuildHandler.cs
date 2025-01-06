﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace Microsoft.DotNet.Cli
{
    internal sealed class MSBuildHandler : IDisposable
    {
        private readonly List<string> _args;
        private readonly TestApplicationActionQueue _actionQueue;
        private readonly int _degreeOfParallelism;

        private readonly ConcurrentBag<TestApplication> _testApplications = new();
        private bool _areTestingPlatformApplications = true;

        private const string BinLogFileName = "msbuild.binlog";
        private static readonly object s_buildLock = new();

        public MSBuildHandler(List<string> args, TestApplicationActionQueue actionQueue, int degreeOfParallelism)
        {
            _args = args;
            _actionQueue = actionQueue;
            _degreeOfParallelism = degreeOfParallelism;
        }

        public int RunWithMSBuild(bool allowBinLog)
        {
            bool solutionOrProjectFileFound = SolutionAndProjectUtility.TryGetSolutionOrProjectFilePath(Directory.GetCurrentDirectory(), out string filePath, out bool isSolution);

            if (!solutionOrProjectFileFound)
            {
                return ExitCodes.GenericFailure;
            }

            (IEnumerable<Module> modules, bool restored) = GetProjectsProperties(filePath, isSolution, allowBinLog);

            InitializeTestApplications(modules);

            return restored ? ExitCodes.Success : ExitCodes.GenericFailure;
        }

        public int RunWithMSBuild(string filePath, bool allowBinLog)
        {
            (IEnumerable<Module> modules, bool restored) = GetProjectsProperties(filePath, false, allowBinLog);

            InitializeTestApplications(modules);

            return restored ? ExitCodes.Success : ExitCodes.GenericFailure;
        }

        private void InitializeTestApplications(IEnumerable<Module> modules)
        {
            foreach (Module module in modules)
            {
                if (module.IsTestProject && module.IsTestingPlatformApplication)
                {
                    var testApp = new TestApplication(module, _args);
                    _testApplications.Add(testApp);
                }
                else // If one test app has IsTestingPlatformApplication set to false, then we will not run any of the test apps
                {
                    _areTestingPlatformApplications = false;
                    return;
                }
            }
        }

        public bool EnqueueTestApplications()
        {
            if (!_areTestingPlatformApplications)
            {
                return false;
            }

            foreach (var testApp in _testApplications)
            {
                _actionQueue.Enqueue(testApp);
            }
            return true;
        }

        private (IEnumerable<Module>, bool Restored) GetProjectsProperties(string solutionOrProjectFilePath, bool isSolution, bool allowBinLog)
        {
            var allProjects = new ConcurrentBag<Module>();
            bool restored = true;

            if (isSolution)
            {
                var projects = SolutionAndProjectUtility.GetProjectsFromSolutionFile(solutionOrProjectFilePath);
                ProcessProjectsInParallel(projects, allowBinLog, allProjects, ref restored);
            }
            else
            {
                var (relatedProjects, isProjectBuilt) = GetProjectPropertiesInternal(solutionOrProjectFilePath, allowBinLog);
                foreach (var relatedProject in relatedProjects)
                {
                    allProjects.Add(relatedProject);
                }

                if (!isProjectBuilt)
                {
                    restored = false;
                }
            }
            return (allProjects, restored);
        }

        private void ProcessProjectsInParallel(IEnumerable<string> projects, bool allowBinLog, ConcurrentBag<Module> allProjects, ref bool restored)
        {
            bool allProjectsRestored = true;

            Parallel.ForEach(
                projects,
                new ParallelOptions { MaxDegreeOfParallelism = _degreeOfParallelism },
                () => true,
                (project, state, localRestored) =>
                {
                    var (relatedProjects, isRestored) = GetProjectPropertiesInternal(project, allowBinLog);
                    foreach (var relatedProject in relatedProjects)
                    {
                        allProjects.Add(relatedProject);
                    }

                    return localRestored && isRestored;
                },
                localRestored =>
                {
                    if (!localRestored)
                    {
                        allProjectsRestored = false;
                    }
                });

            restored = allProjectsRestored;
        }

        private static (IEnumerable<Module> Modules, bool Restored) GetProjectPropertiesInternal(string projectFilePath, bool allowBinLog)
        {
            var projectCollection = new ProjectCollection();
            var project = projectCollection.LoadProject(projectFilePath);
            var buildResult = RestoreProject(projectFilePath, allowBinLog, projectCollection);

            bool restored = buildResult.OverallResult == BuildResultCode.Success;

            if (!restored)
            {
                return (Array.Empty<Module>(), restored);
            }

            return (ExtractModulesFromProject(project), restored);
        }

        private static IEnumerable<Module> ExtractModulesFromProject(Project project)
        {
            _ = bool.TryParse(project.GetPropertyValue(ProjectProperties.IsTestingPlatformApplication), out bool isTestingPlatformApplication);
            _ = bool.TryParse(project.GetPropertyValue(ProjectProperties.IsTestProject), out bool isTestProject);

            string targetFramework = project.GetPropertyValue(ProjectProperties.TargetFramework);
            string targetFrameworks = project.GetPropertyValue(ProjectProperties.TargetFrameworks);
            string targetPath = project.GetPropertyValue(ProjectProperties.TargetPath);
            string projectFullPath = project.GetPropertyValue(ProjectProperties.ProjectFullPath);
            string runSettingsFilePath = project.GetPropertyValue(ProjectProperties.RunSettingsFilePath);

            var projects = new List<Module>();

            if (string.IsNullOrEmpty(targetFrameworks))
            {
                projects.Add(new Module(targetPath, projectFullPath, targetFramework, runSettingsFilePath, isTestingPlatformApplication, isTestProject));
            }
            else
            {
                var frameworks = targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var framework in frameworks)
                {
                    project.SetProperty(ProjectProperties.TargetFramework, framework);
                    project.ReevaluateIfNecessary();

                    projects.Add(new Module(project.GetPropertyValue(ProjectProperties.TargetPath),
                        projectFullPath,
                        framework,
                        runSettingsFilePath,
                        isTestingPlatformApplication,
                        isTestProject));
                }
            }

            return projects;
        }

        private static BuildResult RestoreProject(string projectFilePath, bool allowBinLog, ProjectCollection projectCollection)
        {
            BuildParameters parameters = new(projectCollection)
            {
                Loggers = [new ConsoleLogger(LoggerVerbosity.Quiet)]
            };

            if (allowBinLog)
            {
                parameters.Loggers = parameters.Loggers.Concat([
                    new BinaryLogger
                    {
                        Parameters = BinLogFileName
                    }
                ]);
            }

            var buildRequestData = new BuildRequestData(projectFilePath, new Dictionary<string, string>(), null, [CliConstants.RestoreCommand], null);
            BuildResult buildResult;
            lock (s_buildLock)
            {
                buildResult = BuildManager.DefaultBuildManager.Build(parameters, buildRequestData);
            }

            return buildResult;
        }

        public void Dispose()
        {
            foreach (var testApplication in _testApplications)
            {
                testApplication.Dispose();
            }
        }
    }
}
