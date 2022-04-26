using System.Collections.Generic;

namespace MSBuildMultiProjectBuilder;

internal class SolutionInfo
{
    // Ignore Spelling: wildcards

    /// <summary>
    /// Command line arguments to use when building this solution with MSBuild.exe
    /// </summary>
    public string BuildArgs { get; set; }

    /// <summary>
    /// Target directories to use for post build file copies
    /// </summary>
    public List<string> CopyTargetDirectories { get; } = new();

    /// <summary>
    /// Solution ID number
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defines the order with which the solutions will be built
    /// </para>
    /// <para>
    /// Multiple solutions can have the same solution ID
    /// </para>
    /// <para>
    /// If the solution list column does not have a solution ID column,
    /// or if the value in the column is not an integer,
    /// will use the most recent solution ID
    /// </para>
    /// </remarks>
    public int ID { get; }

    /// <summary>
    /// Line number of this solution in the Solution List file
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Batch file to run after the build completes
    /// </summary>
    /// <remarks>If a relative path, will use the solution file path as the base path</remarks>
    public string PostBuildBatchFile { get; set; }

    /// <summary>
    /// Comma separated list of files to copy after the build completes (supports wildcards)
    /// </summary>
    /// <remarks>If relative paths, will use the solution file path as the base path</remarks>
    public List<string> PostBuildCopyList { get; } = new();

    /// <summary>
    /// Relative or absolute path to the Visual Studio solution file (.sln) to build
    /// </summary>
    public string SolutionPath { get; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="solutionID">Solution ID number</param>
    /// <param name="solutionPath">Relative or absolute path to the Visual Studio solution file (.sln)</param>
    /// <param name="lineNumber">Line number of this solution in the Solution List file</param>
    public SolutionInfo(int solutionID, string solutionPath, int lineNumber)
    {
        ID = solutionID;
        SolutionPath = solutionPath;
        LineNumber = lineNumber;
    }

    /// <summary>
    /// Show the Solution ID and .sln file path
    /// </summary>
    public override string ToString()
    {
        return string.Format("{0}: {1}", ID, SolutionPath);
    }
}