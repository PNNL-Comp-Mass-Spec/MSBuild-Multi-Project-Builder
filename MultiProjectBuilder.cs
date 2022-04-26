using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using PRISM;

namespace MSBuildMultiProjectBuilder;

internal class MultiProjectBuilder : EventNotifier
{
    // Ignore Spelling: chcp, msbuild

    private const int PROGRAM_RUNNER_MONITOR_INTERVAL_MSEC = 300;

    private const int PROGRAM_RUNNER_MAX_RUNTIME_MINUTES = 10;

    private const string SOLUTION_LIST_COLUMN_SOLUTION_PATH = "SolutionPath";

    private enum SolutionListFileColumns
    {
        SolutionID = 0,
        SolutionPath = 1,
        BuildArgs = 2,
        PostBuildCopyList = 3,
        CopyTargetDirectories = 4,
        PostBuildBatchFile = 5,
        Comment = 6
    }

    public MultiProjectBuilderOptions Options { get; set; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="options"></param>
    public MultiProjectBuilder(MultiProjectBuilderOptions options)
    {
        Options = options;
    }

    private void AppendFileContents(FileSystemInfo sourceFile, TextWriter writer)
    {
        using var reader = new StreamReader(new FileStream(sourceFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        while (!reader.EndOfStream)
        {
            writer.WriteLine(reader.ReadLine());
        }
    }

    public bool BuildSolutions(string solutionListFilePath)
    {
        try
        {
            Console.WriteLine();

            var solutionListFile = new FileInfo(solutionListFilePath);

            if (!solutionListFile.Exists)
            {
                OnWarningEvent("Solution list file not found: " + solutionListFile.FullName);
                return false;
            }

            OnStatusEvent("Opening: " + solutionListFile.FullName);

            var solutionsRead = ReadSolutionListFile(solutionListFile, out var solutionList);

            if (!solutionsRead)
                return false;

            return BuildSolutions(solutionList);
        }
        catch (Exception ex)
        {
            OnErrorEvent("Error occurred in MultiProjectBuilder->BuildSolutions", ex);
            return false;
        }
    }

    public bool BuildSolutions(List<SolutionInfo> solutionList)
    {
        try
        {
            Console.WriteLine();

            var msBuildFound = FindMSBuild(out var msBuild);

            if (!msBuildFound)
                return false;

            var msBuildBatchFile = new FileInfo(Path.Combine(Options.WorkingDirectory, "ChangeCodePage_and_Build_Solution.bat"));

            var consoleOutputFile = new FileInfo(Path.Combine(Options.WorkingDirectory, "ConsoleOutput.txt"));

            var combinedConsoleOutputFile = new FileInfo(Path.Combine(Options.WorkingDirectory, "ConsoleOutput_Combined.txt"));

            if (!Options.Preview && combinedConsoleOutputFile.Exists)
            {
                OnDebugEvent("Overwriting existing combined console output file: " +
                             PathUtils.CompactPathString(combinedConsoleOutputFile.FullName, 110));

                combinedConsoleOutputFile.Delete();
            }

            foreach (var solution in (from item in solutionList orderby item.ID, item.SolutionPath select item))
            {
                if (Options.StartID > 0 && solution.ID < Options.StartID)
                    continue;

                if (Options.EndID > 0 && solution.ID > Options.EndID)
                    continue;

                Console.WriteLine();

                if (string.IsNullOrWhiteSpace(solution.SolutionPath))
                {
                    OnStatusEvent("Skipping solution ID {0} since the solution file path was not provided; see line {1} in the Solution List file",
                        solution.ID, solution.LineNumber);

                    continue;
                }

                FileInfo solutionFile;

                if (Path.IsPathRooted(solution.SolutionPath))
                {
                    solutionFile = new FileInfo(solution.SolutionPath);

                    if (!solutionFile.Exists)
                    {
                        OnWarningEvent("Solution file not found; see line {0} in the Solution List file: " + solutionFile.FullName);
                        continue;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(Options.BaseDirectoryPath))
                {
                    solutionFile = new FileInfo(Path.Combine(Options.BaseDirectoryPath, solution.SolutionPath));

                    if (!solutionFile.Exists)
                    {
                        OnWarningEvent("Solution file not found; see line {0} in the Solution List file: " + solutionFile.FullName);
                        OnWarningEvent("Note: used the provided base directory path since the solution file path is a relative path");
                        continue;
                    }
                }
                else
                {
                    solutionFile = new FileInfo(Path.Combine(Options.WorkingDirectory, solution.SolutionPath));

                    if (!solutionFile.Exists)
                    {
                        OnWarningEvent("Solution file not found; see line {0} in the Solution List file: " + solutionFile.FullName);
                        OnWarningEvent("Note: used the working directory since the solution file path is a relative path");
                        continue;
                    }
                }

                if (solutionFile.Directory == null)
                {
                    OnWarningEvent("Unable to determine the parent directory of the solution file: " + solutionFile.FullName);
                    continue;
                }

                var commands = new List<string>
                {
                    "chcp 1252",
                    string.Format("\"{0}\" {1} \"{2}\"", msBuild.FullName, solution.BuildArgs, solutionFile.FullName)
                };

                if (Options.Preview)
                {
                    OnStatusEvent("Commands to run for Solution ID {0}:", solution.ID);

                    foreach (var command in commands)
                    {
                        OnDebugEvent(command);
                    }

                    Console.WriteLine();

                    PerformPostBuildOperations(solution, solutionFile, null, true);
                    continue;
                }

                using (var batchFileWriter = new StreamWriter(new FileStream(msBuildBatchFile.FullName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)))
                {
                    foreach (var command in commands)
                    {
                        batchFileWriter.WriteLine(command);
                    }
                }

                consoleOutputFile.Refresh();
                if (consoleOutputFile.Exists)
                    consoleOutputFile.Delete();

                // Instantiate a new instance of the program runner for each solution
                // This is required because the program runner disposes objects after the target program finishes

                var programRunner = new ProgRunner
                {
                    Arguments = string.Empty,
                    CreateNoWindow = false,
                    MonitoringInterval = PROGRAM_RUNNER_MONITOR_INTERVAL_MSEC,
                    Name = "MSBuild.exe",
                    Program = msBuildBatchFile.FullName,
                    Repeat = false,
                    RepeatHoldOffTime = 0,
                    WorkDir = solutionFile.Directory.FullName,
                    CacheStandardOutput = false,
                    EchoOutputToConsole = false,
                    WriteConsoleOutputToFile = true,
                    ConsoleOutputFilePath = consoleOutputFile.FullName,
                    ConsoleOutputFileIncludesCommandLine = false
                };

                RegisterEvents(programRunner);

                programRunner.ConsoleErrorEvent += ProgramRunner_ConsoleErrorEvent;
                programRunner.ConsoleOutputEvent += ProgramRunner_ConsoleOutputEvent;

                programRunner.StartAndMonitorProgram();

                var startTime = DateTime.UtcNow;
                var programAborted = false;

                // Loop until program is complete, or until MaxRuntimeSeconds elapses
                while (programRunner.State != ProgRunner.States.NotMonitoring)
                {
                    ProgRunner.SleepMilliseconds(PROGRAM_RUNNER_MONITOR_INTERVAL_MSEC);

                    if (DateTime.UtcNow.Subtract(startTime).TotalMinutes < PROGRAM_RUNNER_MAX_RUNTIME_MINUTES)
                        continue;

                    programRunner.StopMonitoringProgram(kill: true);
                    OnWarningEvent("Aborted the build process since {0} minutes have elapsed", PROGRAM_RUNNER_MAX_RUNTIME_MINUTES);
                    programAborted = true;
                    break;
                }

                if (programAborted)
                    continue;

                var stopTime = DateTime.UtcNow;

                // Append console output to the combined console output file

                using var writer = new StreamWriter(new FileStream(combinedConsoleOutputFile.FullName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));

                consoleOutputFile.Refresh();
                if (consoleOutputFile.Exists)
                {
                    AppendFileContents(consoleOutputFile, writer);
                }

                writer.WriteLine();
                writer.WriteLine("Build time for solution {0}: {1:F2} seconds", solutionFile.Name, stopTime.Subtract(startTime).TotalSeconds);

                PerformPostBuildOperations(solution, solutionFile, writer);

                writer.WriteLine();
                writer.WriteLine("--------------------------------------------------------------------------------");
            }

            return true;
        }
        catch (Exception ex)
        {
            OnErrorEvent("Error occurred in MultiProjectBuilder->BuildSolutions", ex);
            return false;
        }
    }

    private bool FilesMatch(FileInfo file1, FileInfo file2)
    {
        if (!file1.Exists || !file2.Exists)
            return false;

        if (file1.Length != file2.Length)
            return false;

        using var reader1 = new BinaryReader(new FileStream(file1.FullName, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var reader2 = new BinaryReader(new FileStream(file2.FullName, FileMode.Open, FileAccess.Read, FileShare.Read));

        while (reader1.BaseStream.Position < file1.Length)
        {
            if (reader1.ReadByte() != reader2.ReadByte())
            {
                return false;
            }
        }

        return true;
    }

    private bool FindMSBuild(out FileInfo msBuild)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Options.MSBuildPath))
            {
                msBuild = new FileInfo(Options.MSBuildPath);

                if (!msBuild.Exists)
                {
                    OnWarningEvent("MSBuild path specified in the options does not exist: " + msBuild.FullName);
                    return false;
                }

                OnStatusEvent("Using MSBuild.exe at " + PathUtils.CompactPathString(msBuild.FullName, 110));
                return true;
            }

            var visualStudioDirectory = new DirectoryInfo(@"C:\Program Files\Microsoft Visual Studio\");

            if (!visualStudioDirectory.Exists)
            {
                OnWarningEvent("Cannot find MSBuild; Visual Studio directory not found: " + visualStudioDirectory.FullName);
                msBuild = null;
                return false;
            }

            var versionDirectories = new Dictionary<int, DirectoryInfo>();

            foreach (var directory in visualStudioDirectory.GetDirectories("*"))
            {
                if (int.TryParse(directory.Name, out var directoryYear))
                {
                    versionDirectories.Add(directoryYear, directory);
                }
            }

            if (versionDirectories.Count == 0)
            {
                OnWarningEvent("Cannot find MSBuild; none of the directories below the Visual Studio directory is a 4 digit year: " + visualStudioDirectory.FullName);
                msBuild = null;
                return false;
            }

            var candidatePaths = new List<string>();

            foreach (var directoryYear in (from item in versionDirectories.Keys orderby item descending select item))
            {
                foreach (var directory in versionDirectories[directoryYear].GetDirectories())
                {
                    if (!directory.Name.Equals("Community", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Look for the 64-bit version
                    var msBuild64Bit = new FileInfo(Path.Combine(directory.FullName, @"Msbuild\Current\bin\amd64\MSBuild.exe"));

                    candidatePaths.Add(msBuild64Bit.FullName);

                    if (msBuild64Bit.Exists)
                    {
                        msBuild = msBuild64Bit;
                        OnStatusEvent("Using 64-bit MSBuild.exe at " + PathUtils.CompactPathString(msBuild.FullName, 110));
                        return true;
                    }

                    // Look for the 32-bit version
                    var msBuild32Bit = new FileInfo(Path.Combine(directory.FullName, @"Msbuild\Current\bin\MSBuild.exe"));

                    candidatePaths.Add(msBuild32Bit.FullName);

                    if (msBuild32Bit.Exists)
                    {
                        msBuild = msBuild32Bit;
                        OnStatusEvent("Using 32-bit MSBuild.exe at " + PathUtils.CompactPathString(msBuild.FullName, 110));
                        return true;
                    }
                }
            }

            if (candidatePaths.Count > 0)
            {
                OnWarningEvent("Did not find MSBuild.exe in the expected locations; files checked:");
                foreach (var msBuildPath in candidatePaths)
                {
                    OnWarningEvent(PathUtils.CompactPathString(msBuildPath, 110));
                }
            }
            else
            {
                OnWarningEvent("Did not find a Visual Studio Community directory in the expected location(s):");
                foreach (var directory in versionDirectories)
                {
                    OnWarningEvent(PathUtils.CompactPathString(directory.Value.FullName, 110));
                }
            }

            msBuild = null;
            return false;
        }
        catch (Exception ex)
        {
            OnErrorEvent("Error occurred in MultiProjectBuilder->FindMSBuild", ex);
            msBuild = null;
            return false;
        }
    }

    private bool GetColumnMap(IReadOnlyList<string> lineParts, IDictionary<SolutionListFileColumns, int> columnMap)
    {
        for (var i = 0; i < lineParts.Count; i++)
        {
            if (lineParts[i].Equals("ID", StringComparison.OrdinalIgnoreCase) ||
                lineParts[i].Equals("SolutionID", StringComparison.OrdinalIgnoreCase))
            {
                columnMap.Add(SolutionListFileColumns.SolutionID, i);
            }
            else if (lineParts[i].Equals(SOLUTION_LIST_COLUMN_SOLUTION_PATH, StringComparison.OrdinalIgnoreCase))
            {
                columnMap.Add(SolutionListFileColumns.SolutionPath, i);
            }
            else if (lineParts[i].Equals("BuildArgs", StringComparison.OrdinalIgnoreCase))
            {
                columnMap.Add(SolutionListFileColumns.BuildArgs, i);
            }
            else if (lineParts[i].Equals("PostBuildCopyList", StringComparison.OrdinalIgnoreCase))
            {
                columnMap.Add(SolutionListFileColumns.PostBuildCopyList, i);
            }
            else if (lineParts[i].Equals("CopyTargetDirectories", StringComparison.OrdinalIgnoreCase))
            {
                columnMap.Add(SolutionListFileColumns.CopyTargetDirectories, i);
            }
            else if (lineParts[i].Equals("PostBuildBatchFile", StringComparison.OrdinalIgnoreCase))
            {
                columnMap.Add(SolutionListFileColumns.PostBuildBatchFile, i);
            }
            else if (lineParts[i].StartsWith("Comment", StringComparison.OrdinalIgnoreCase))
            {
                columnMap.Add(SolutionListFileColumns.Comment, i);
            }
            else
            {
                OnWarningEvent("Ignoring unrecognized header column in the Solution List File: " + lineParts[i]);
            }
        }

        return columnMap.ContainsKey(SolutionListFileColumns.SolutionPath);
    }

    /// <summary>
    /// Show a warning message at the console and append to the writer
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="message"></param>
    private void LogWarning(TextWriter writer, string message)
    {
        OnWarningEvent(message);
        writer?.WriteLine(message);
    }

    /// <summary>
    /// Show a warning message at the console and append to the writer
    /// </summary>
    /// <param name="writer">Writer</param>
    /// <param name="format">Status message format string</param>
    /// <param name="args">String format arguments</param>
    [StringFormatMethod("format")]
    private void LogWarning(TextWriter writer, string format, params object[] args)
    {
        LogWarning(writer, string.Format(format, args));
    }

    private void PerformPostBuildOperations(SolutionInfo solution, FileInfo solutionFile, TextWriter consoleOutputWriter, bool previewMode = false)
    {
        try
        {
            if (solution.PostBuildCopyList.Count > 0)
            {
                PostBuildCopyFiles(solution, solutionFile, consoleOutputWriter, previewMode);
            }

            if (!string.IsNullOrWhiteSpace(solution.PostBuildBatchFile))
            {
                RunPostBuildBatchFile(solution, solutionFile, consoleOutputWriter, previewMode);
            }
        }
        catch (Exception ex)
        {
            OnErrorEvent("Error occurred in MultiProjectBuilder->PerformPostBuildOperations for Solution ID " + solution.ID, ex);
        }
    }

    private void PostBuildCopyFiles(SolutionInfo solution, FileInfo solutionFile, TextWriter consoleOutputWriter, bool previewMode = false)
    {
        try
        {
            if (solution.PostBuildCopyList.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(solutionFile.DirectoryName))
            {
                LogWarning(consoleOutputWriter,
                    "Error: cannot post build copy files since the parent directory of the solution file is undefined for {0}",
                    solutionFile.FullName);

                return;
            }

            if (solution.PostBuildCopyList.Count > 0 && solution.CopyTargetDirectories.Count == 0)
            {
                LogWarning(consoleOutputWriter,
                    "Error: cannot post build copy files since the CopyTargetDirectories value is empty for Solution ID {0}",
                    solution.ID);

                return;
            }

            var filesToCopy = new List<FileInfo>();

            foreach (var fileSpec in solution.PostBuildCopyList)
            {
                var fileSpecClean = fileSpec.Replace('*', '_').Replace('?', '_');

                var filesToFind = Path.IsPathRooted(fileSpecClean) ? fileSpec : Path.Combine(solutionFile.DirectoryName, fileSpec);

                try
                {
                    var lastSlashIndex = filesToFind.LastIndexOf(Path.DirectorySeparatorChar);

                    if (lastSlashIndex <= 0 || lastSlashIndex >= filesToFind.Length - 1)
                    {
                        LogWarning(consoleOutputWriter,
                            "Error: cannot determine the parent directory of the post build files to copy; see {0}",
                            filesToFind);

                        continue;
                    }

                    var parentDirectory = new DirectoryInfo(filesToFind.Substring(0, lastSlashIndex));
                    var fileSpecToFind = filesToFind.Substring(lastSlashIndex + 1);

                    var foundFiles = parentDirectory.GetFiles(fileSpecToFind).ToList();

                    if (foundFiles.Count == 0)
                    {
                        if (!previewMode)
                        {
                            LogWarning(consoleOutputWriter,
                                "Warning: no files were found matching {0} in {1}",
                                fileSpecToFind, parentDirectory.FullName);
                        }

                        continue;
                    }

                    filesToCopy.AddRange(foundFiles);
                }
                catch (Exception ex)
                {
                    LogWarning(consoleOutputWriter,
                        "Error looking for files matching {0} for Solution ID {1}: {2}",
                        filesToFind, solution.ID, ex.Message);
                }
            }

            if (filesToCopy.Count == 0)
            {
                if (!previewMode)
                {
                    LogWarning(consoleOutputWriter,
                        "Warning: no files were found to copy following the build of Solution ID {0}",
                        solution.ID);
                }

                return;
            }

            foreach (var sourceFile in filesToCopy)
            {
                foreach (var targetDirectoryPath in solution.CopyTargetDirectories)
                {
                    try
                    {
                        string targetPathToUse;

                        if (Path.IsPathRooted(targetDirectoryPath))
                        {
                            targetPathToUse = targetDirectoryPath;
                        }
                        else if (!string.IsNullOrWhiteSpace(Options.BaseDirectoryPath))
                        {
                            targetPathToUse = Path.Combine(Options.BaseDirectoryPath, targetDirectoryPath);
                        }
                        else
                        {
                            LogWarning(consoleOutputWriter,
                                "Error: Target directory is a relative path and the base directory path is not defined; " +
                                "skipping post build copy to: {0}", targetDirectoryPath);

                            continue;
                        }

                        var targetDirectory = new DirectoryInfo(targetPathToUse);

                        if (!targetDirectory.Exists)
                        {
                            LogWarning(consoleOutputWriter,
                                "Error: Target directory for post build copy does not exist: {0}",
                                targetDirectory.FullName);

                            continue;
                        }

                        if (previewMode)
                        {
                            OnDebugEvent("Preview copy of {0} to {1}",
                                PathUtils.CompactPathString(sourceFile.FullName, 80),
                                PathUtils.CompactPathString(targetDirectory.FullName, 80));

                            continue;
                        }

                        var targetFile = new FileInfo(Path.Combine(targetDirectory.FullName, sourceFile.Name));

                        if (FilesMatch(sourceFile, targetFile))
                        {
                            OnDebugEvent("Files match; skipping copy of {0} to {1}",
                                PathUtils.CompactPathString(sourceFile.FullName, 80),
                                PathUtils.CompactPathString(targetDirectory.FullName, 80));

                            continue;
                        }

                        sourceFile.CopyTo(targetFile.FullName, true);

                        var copyMessage = string.Format("Copied {0} to {1}", sourceFile.FullName, targetDirectory.FullName);

                        OnDebugEvent(copyMessage);
                        consoleOutputWriter?.WriteLine(copyMessage);
                    }
                    catch (Exception ex)
                    {
                        LogWarning(consoleOutputWriter,
                            "Error copying files to {0} for Solution ID {1}: {2}",
                            targetDirectoryPath, solution.ID, ex.Message);

                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OnErrorEvent("Error occurred in MultiProjectBuilder->PostBuildCopyFiles for Solution ID " + solution.ID, ex);
        }
    }

    private void ProgramRunner_ConsoleErrorEvent(string message)
    {
        OnWarningEvent("Program runner error: " + message);
    }

    private void ProgramRunner_ConsoleOutputEvent(string message)
    {
        OnDebugEvent(message);
    }

    private bool ReadSolutionListFile(FileSystemInfo solutionListFile, out List<SolutionInfo> solutionList)
    {
        solutionList = new List<SolutionInfo>();

        // This matches two double quotes: ""
        var quoteMatcher = new Regex("\"\"", RegexOptions.Compiled);

        try
        {
            // This dictionary is used to track the column positions
            // Keys are column enum, values are column index
            var columnMap = new Dictionary<SolutionListFileColumns, int>();

            var delimiters = new[] { ',', ';' };

            var lastSolutionID = 0;
            var lineNumber = 0;

            using var reader = new StreamReader(new FileStream(solutionListFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

            while (!reader.EndOfStream)
            {
                var dataLine = reader.ReadLine();
                lineNumber++;

                if (string.IsNullOrWhiteSpace(dataLine))
                {
                    continue;
                }

                var lineParts = dataLine.Split('\t');

                if (columnMap.Count == 0)
                {
                    if (!GetColumnMap(lineParts, columnMap))
                        return false;

                    if (!columnMap.ContainsKey(SolutionListFileColumns.SolutionID))
                    {
                        OnWarningEvent("Aborting since the header line in the Solution List File is missing column '{0}'", "ID");
                        return false;
                    }

                    if (!columnMap.ContainsKey(SolutionListFileColumns.SolutionPath))
                    {
                        OnWarningEvent("Aborting since the header line in the Solution List File is missing column '{0}", SOLUTION_LIST_COLUMN_SOLUTION_PATH);
                        return false;
                    }

                    continue;
                }

                for (var i = 0; i < lineParts.Length; i++)
                {
                    // Excel surrounds column values with double quotes if the value includes double quotes
                    // Check for and remove the double quotes
                    if (lineParts[i].StartsWith("\"") && lineParts[i].EndsWith("\""))
                        lineParts[i] = lineParts[i].Substring(1, lineParts[i].Length - 2);

                    // Excel escapes double quotes using ""
                    // Convert to "

                    if (quoteMatcher.IsMatch(lineParts[i]))
                    {
                        lineParts[i] = quoteMatcher.Replace(lineParts[i], "\"");
                    }
                }

                int solutionID;

                if (TryGetColumnValue(lineParts, columnMap, SolutionListFileColumns.SolutionID, out var solutionIDText) &&
                    int.TryParse(solutionIDText, out var solutionIDValue))
                {
                    solutionID = solutionIDValue;
                    lastSolutionID = solutionID;
                }
                else
                {
                    solutionID = lastSolutionID;
                }

                TryGetColumnValue(lineParts, columnMap, SolutionListFileColumns.SolutionPath, out var solutionPath);

                var solutionInfo = new SolutionInfo(solutionID, solutionPath, lineNumber);

                if (TryGetColumnValue(lineParts, columnMap, SolutionListFileColumns.BuildArgs, out var buildArgs))
                {
                    solutionInfo.BuildArgs = buildArgs.Trim();
                }

                if (TryGetColumnValue(lineParts, columnMap, SolutionListFileColumns.PostBuildCopyList, out var postBuildCopyList))
                {
                    foreach (var item in postBuildCopyList.Split(delimiters))
                    {
                        var trimmedItem = item.Trim();
                        if (string.IsNullOrWhiteSpace(trimmedItem))
                            continue;

                        solutionInfo.PostBuildCopyList.Add(trimmedItem);
                    }
                }

                if (TryGetColumnValue(lineParts, columnMap, SolutionListFileColumns.CopyTargetDirectories, out var copyTargetDirectories))
                {
                    foreach (var item in copyTargetDirectories.Split(delimiters))
                    {
                        var trimmedItem = item.Trim();
                        if (string.IsNullOrWhiteSpace(trimmedItem))
                            continue;

                        solutionInfo.CopyTargetDirectories.Add(trimmedItem);
                    }
                }

                if (TryGetColumnValue(lineParts, columnMap, SolutionListFileColumns.PostBuildBatchFile, out var postBuildBatchFile))
                {
                    solutionInfo.PostBuildBatchFile = postBuildBatchFile.Trim();
                }

                solutionList.Add(solutionInfo);
            }

            Console.WriteLine("Loaded {0} solution{1} from the Solution List file", solutionList.Count, solutionList.Count == 1 ? string.Empty : "s");
            return true;
        }
        catch (Exception ex)
        {
            OnErrorEvent("Error occurred in MultiProjectBuilder->ReadSolutionListFile", ex);
            return false;
        }
    }

    private void RunPostBuildBatchFile(SolutionInfo solution, FileInfo solutionFile, TextWriter consoleOutputWriter, bool previewMode = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(solution.PostBuildBatchFile))
                return;

            if (string.IsNullOrWhiteSpace(solutionFile.DirectoryName))
            {
                LogWarning(consoleOutputWriter,
                    "Error: cannot run the post build batch file since the parent directory of the solution file is undefined for {0} ",
                    solutionFile.FullName);

                return;
            }

            string batchFilePath;
            string additionalArguments;

            if (solution.PostBuildBatchFile.EndsWith(".bat"))
            {
                batchFilePath = solution.PostBuildBatchFile;
                additionalArguments = string.Empty;
            }
            else
            {
                var extensionIndex = solution.PostBuildBatchFile.IndexOf(".bat ", StringComparison.OrdinalIgnoreCase);
                if (extensionIndex > 0)
                {
                    batchFilePath = solution.PostBuildBatchFile.Substring(0, extensionIndex + 4);
                    additionalArguments = solution.PostBuildBatchFile.Substring(extensionIndex + 4).Trim();
                }
                else
                {
                    if (solution.PostBuildBatchFile.StartsWith("Compare "))
                    {
                        var message = string.Format("Post build task for Solution ID {0}: {1}", solution.ID, solution.PostBuildBatchFile);

                        OnStatusEvent(message);
                        consoleOutputWriter?.WriteLine(message);

                        return;
                    }

                    LogWarning(consoleOutputWriter,
                        "Post build batch file column for Solution ID {0} does not end in '.bat' and does not contain '.bat ': {1}",
                        solution.ID, solution.PostBuildBatchFile);

                    return;
                }
            }

            var batchFile = new FileInfo(
                Path.IsPathRooted(batchFilePath)
                    ? batchFilePath
                    : Path.Combine(solutionFile.DirectoryName, batchFilePath));

            if (!batchFile.Exists)
            {
                LogWarning(consoleOutputWriter, "Error: post build batch file not found at {0}", batchFile.FullName);
                return;
            }

            if (batchFile.Directory == null)
            {
                LogWarning(consoleOutputWriter, "Error: unable to determine the parent directory of batch file {0}", batchFile.FullName);
                return;
            }

            if (previewMode)
            {
                OnStatusEvent("Batch file to run for Solution ID {0}: {1}", solution.ID, PathUtils.CompactPathString(batchFile.FullName, 110));
                return;
            }

            var consoleOutputFile = new FileInfo(Path.Combine(Options.WorkingDirectory, "ConsoleOutput_BatchFile.txt"));

            var programRunner = new ProgRunner
            {
                Arguments = additionalArguments,
                CreateNoWindow = false,
                MonitoringInterval = PROGRAM_RUNNER_MONITOR_INTERVAL_MSEC,
                Name = batchFile.Name,
                Program = batchFile.FullName,
                Repeat = false,
                RepeatHoldOffTime = 0,
                WorkDir = batchFile.Directory.FullName,
                CacheStandardOutput = false,
                EchoOutputToConsole = false,
                WriteConsoleOutputToFile = true,
                ConsoleOutputFilePath = consoleOutputFile.FullName,
                ConsoleOutputFileIncludesCommandLine = true
            };

            RegisterEvents(programRunner);

            programRunner.ConsoleErrorEvent += ProgramRunner_ConsoleErrorEvent;
            programRunner.ConsoleOutputEvent += ProgramRunner_ConsoleOutputEvent;

            consoleOutputFile.Refresh();
            if (consoleOutputFile.Exists)
                consoleOutputFile.Delete();

            programRunner.StartAndMonitorProgram();

            var startTime = DateTime.UtcNow;
            var programAborted = false;

            // Loop until program is complete, or until MaxRuntimeSeconds elapses
            while (programRunner.State != ProgRunner.States.NotMonitoring)
            {
                ProgRunner.SleepMilliseconds(PROGRAM_RUNNER_MONITOR_INTERVAL_MSEC);

                if (DateTime.UtcNow.Subtract(startTime).TotalMinutes < PROGRAM_RUNNER_MAX_RUNTIME_MINUTES)
                    continue;

                programRunner.StopMonitoringProgram(kill: true);

                LogWarning(consoleOutputWriter,
                    "Aborted running the batch file since {0} minutes have elapsed",
                    PROGRAM_RUNNER_MAX_RUNTIME_MINUTES);

                programAborted = true;
                break;
            }

            if (programAborted)
                return;

            // Append console output to the combined console output file

            consoleOutputFile.Refresh();

            if (consoleOutputFile.Exists && consoleOutputWriter != null)
            {
                consoleOutputWriter.WriteLine("Console output from call to " + batchFile.FullName);
                consoleOutputWriter.WriteLine();

                AppendFileContents(consoleOutputFile, consoleOutputWriter);
            }
        }
        catch (Exception ex)
        {
            OnErrorEvent("Error occurred in MultiProjectBuilder->RunPostBuildBatchFile for Solution ID " + solution.ID, ex);
        }
    }

    private bool TryGetColumnValue(
        IReadOnlyList<string> lineParts,
        IReadOnlyDictionary<SolutionListFileColumns, int> columnMap,
        SolutionListFileColumns column,
        out string value)
    {
        if (columnMap.TryGetValue(column, out var columnIndex) && columnIndex < lineParts.Count)
        {
            value = lineParts[columnIndex].Trim();
            return true;
        }

        value = null;
        return false;
    }
}