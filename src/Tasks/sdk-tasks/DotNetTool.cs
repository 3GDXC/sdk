﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Diagnostics;

namespace Microsoft.DotNet.Build.Tasks
{
    public abstract class DotNetTool : ToolTask
    {
        public DotNetTool()
        {
        }

        protected abstract string Command { get; }

        protected abstract string Args { get; }

        protected override ProcessStartInfo GetProcessStartInfo(string pathToTool, string commandLineCommands, string responseFileSwitch)
        {
            var psi = base.GetProcessStartInfo(pathToTool, commandLineCommands, responseFileSwitch);
            foreach (var environmentVariableName in new EnvironmentFilter().GetEnvironmentVariableNamesToRemove())
            {
                psi.Environment.Remove(environmentVariableName);
            }

            return psi;
        }

        public string WorkingDirectory { get; set; }

        protected override string ToolName => $"dotnet{(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty)}";

        protected override MessageImportance StandardOutputLoggingImportance => MessageImportance.High;

        protected override string GenerateFullPathToTool()
        {
            // if ToolPath was not provided by the MSBuild script
            if (string.IsNullOrEmpty(ToolPath))
            {
                Log.LogError($"Could not find the Path to {ToolName}");

                return string.Empty;
            }

            return ToolPath;
        }

        protected override string GetWorkingDirectory() => WorkingDirectory ?? base.GetWorkingDirectory();

        protected override string GenerateCommandLineCommands()
        {
            var commandLineCommands = $"{Command} {Args}";

            LogToolCommand($"[DotNetTool] {commandLineCommands}");

            return commandLineCommands;
        }

        protected override void LogToolCommand(string message) => base.LogToolCommand($"{GetWorkingDirectory()}> {message}");
    }
}
