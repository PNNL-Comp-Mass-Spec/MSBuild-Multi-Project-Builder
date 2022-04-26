using System;
using System.IO;
using PRISM;

namespace MSBuildMultiProjectBuilder;

internal class MultiProjectBuilderOptions
{
    // Ignore Spelling: wildcards

    public const string PROGRAM_DATE = "April 26, 2022";

    /// <summary>
    /// Solution list file
    /// </summary>
    /// <remarks>.txt file</remarks>
    [Option("SolutionListFilePath", "InputFilePath", "I",
        ArgPosition = 1, Required = true, HelpShowsDefault = false, IsInputFilePath = true,
        HelpText = "Tab-delimited text file that lists the Visual Studio solutions to build. " +
                   "It can optionally include a comma or semicolon separated list of file names to copy (wildcards are accepted), " +
                   "along with one or more target directories (comma or semicolon separated). " +
                   "It can optionally also include the relative or absolute path to a " +
                   "batch file to run after the build completes.\n\n" +
                   "Required columns:\n" +
                   "ID    SolutionPath\n\n" +
                   "Optional columns:\n" +
                   "BuildArgs    PostBuildCopyList    CopyTargetDirectories    PostBuildBatchFile")]
    public string SolutionListFilePath { get; set; }

    /// <summary>
    /// Base directory path
    /// </summary>
    [Option("BaseDirectoryPath", "BasePath",
        HelpShowsDefault = false,
        HelpText = "Directory path to use for any .sln files in the solution list file that are a relative path (not rooted)\n" +
                   "Also used when the target directory for the post build copy is a relative path" )]
    public string BaseDirectoryPath { get; set; }

    /// <summary>
    /// Start ID
    /// </summary>
    [Option("StartID", "Start", "First",
        HelpShowsDefault = false,
        HelpText = "The first solution ID to build; 0 to start with the first solution")]
    public int StartID { get; set; } = 0;

    /// <summary>
    /// End ID
    /// </summary>
    [Option("EndID", "End", "Stop", "Last",
        HelpShowsDefault = false,
        HelpText = "The last solution ID to build; 0 to build all (optionally starting with StartID)")]
    public int EndID { get; set; }

    [Option("MSBuildPath", "MSBuild",
        HelpShowsDefault = false,
        HelpText = "Path to MSBuild.exe; if an empty string, will auto-select the newest version of MSBuild")]
    public string MSBuildPath { get; set; }

    [Option("WorkingDirectory", "WorkDir",
        HelpShowsDefault = false,
        HelpText = "Working directory path; if an empty string, will use the default working directory")]
    public string WorkingDirectory { get; set; }

    /// <summary>
    /// Preview mode
    /// </summary>
    [Option("PreviewMode", "Preview",
        HelpShowsDefault = false,
        HelpText = "If provided at the command line (or if set to True in a parameter file), preview the solution build commands")]
    public bool Preview { get; set; }

    /// <summary>
    /// Validate the options
    /// </summary>
    /// <returns>True if all options are valid</returns>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(SolutionListFilePath))
        {
            ConsoleMsgUtils.ShowError($"ERROR: Solution list file must be provided and not empty; \"{SolutionListFilePath}\" was provided");
            return false;
        }

        Console.WriteLine("Processing options");
        Console.WriteLine();

        Console.WriteLine("{0,-25} {1}", "Solution list file:", SolutionListFilePath);

        if (!string.IsNullOrWhiteSpace(BaseDirectoryPath))
        {
            Console.WriteLine("{0,-25} {1}", "Base directory path:", BaseDirectoryPath);
        }

        if (StartID > 0 || EndID > 0)
        {
            Console.WriteLine("{0,-25} {1}", "Start ID:", StartID);
            Console.WriteLine("{0,-25} {1}", "End ID:", EndID > 0 ? EndID : "Process to end");
        }

        if (!string.IsNullOrWhiteSpace(MSBuildPath))
        {
            Console.WriteLine("{0,-25} {1}", "MSBuild path:", MSBuildPath);
        }

        if (string.IsNullOrWhiteSpace(WorkingDirectory))
        {
            WorkingDirectory = new DirectoryInfo(".").FullName;
        }

        Console.WriteLine("{0,-25} {1}", "Working directory:", WorkingDirectory);

        Console.WriteLine("{0,-25} {1}", "Preview mode:", Preview.ToString());

        return true;
    }
}