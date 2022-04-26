using System;
using System.IO;
using PRISM;
using PRISM.Logging;

namespace MSBuildMultiProjectBuilder;

internal static class Program
{
    // Ignore Spelling: Conf

    internal static int Main(string[] args)
    {
        var programName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        var exePath = PRISM.FileProcessor.ProcessFilesOrDirectoriesBase.GetAppPath();
        var exeName = Path.GetFileName(exePath);

        var parser = new CommandLineParser<MultiProjectBuilderOptions>(programName, GetAppVersion())
        {
            ProgramInfo = ConsoleMsgUtils.WrapParagraph("This program ... "),
            ContactInfo = "Program written by Matthew Monroe for PNNL (Richland, WA)" + Environment.NewLine +
                          "E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov" + Environment.NewLine +
                          "Website: https://github.com/PNNL-Comp-Mass-Spec/ or https://panomics.pnnl.gov/ or https://www.pnnl.gov/integrative-omics"
        };

        parser.UsageExamples.Add(exeName + " InputFile.txt /Preview");
        parser.UsageExamples.Add(exeName + " InputFile.txt /O:Updated");

        parser.ParamKeysFieldWidth = 25;
        // parser.ParamDescriptionFieldWidth = 60;

        // The default argument name for parameter files is /ParamFile or -ParamFile
        // Also allow /Conf or /P
        parser.AddParamFileKey("Conf");
        parser.AddParamFileKey("P");

        var result = parser.ParseArgs(args);
        var options = result.ParsedResults;

        if (!result.Success || !options.Validate())
        {
            if (parser.CreateParamFileProvided)
            {
                return 0;
            }

            // Delay for 750 msec in case the user double clicked this file from within Windows Explorer (or started the program via a shortcut)
            System.Threading.Thread.Sleep(750);
            return -1;
        }

        try
        {
            var processor = new MultiProjectBuilder(options);
            RegisterEvents(processor);

            var success = processor.BuildSolutions(options.SolutionListFilePath);

            if (success)
            {
                return 0;
            }

            return -1;
        }
        catch (Exception ex)
        {
            ConsoleMsgUtils.ShowError("Error occurred in Program->Main", ex);
            return -1;
        }
    }

    private static string GetAppVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + " (" + MultiProjectBuilderOptions.PROGRAM_DATE + ")";
    }

    /// <summary>
    /// Use this method to chain events between classes
    /// </summary>
    /// <param name="sourceClass"></param>
    private static void RegisterEvents(IEventNotifier sourceClass)
    {
        // Ignore: sourceClass.DebugEvent += OnDebugEvent;
        sourceClass.StatusEvent += OnStatusEvent;
        sourceClass.ErrorEvent += OnErrorEvent;
        sourceClass.WarningEvent += OnWarningEvent;
        // Ignore: sourceClass.ProgressUpdate += OnProgressUpdate;
    }

    private static void OnErrorEvent(string message, Exception ex)
    {
        ConsoleMsgUtils.ShowError(message, ex);
    }

    private static void OnStatusEvent(string message)
    {
        Console.WriteLine(message);
    }

    private static void OnWarningEvent(string message)
    {
        ConsoleMsgUtils.ShowWarning(message);
    }
}