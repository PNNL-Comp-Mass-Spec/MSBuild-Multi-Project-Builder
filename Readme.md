# MSBuild Multi Project Builder

This program compiles a list of Visual Studio solutions using MSBuild.
Following each compile, files can optionally be copied from the output directory to a target directory.
A batch file can also optionally be called to perform custom actions.

## Console Switches

The MSBuild Multi Project Builder is a console application, and must be run from the Windows command prompt.

```
MSBuildMultiProjectBuilder.exe
 InputFilePath 
 [/BasePath:BaseDirectoryPath]
 [/StartID:0] [/EndID:0]
 [MSBuildPath:PathToMSBuild]
 [/WorkDir:DirectoryPath]
 [/PreviewMode] 
 [/ParamFile:ParamFileName.conf] [/CreateParamFile]
```

The input file path is a Solution List File
* Tab-delimited text file that lists paths to the Visual Studio solutions to build
  * Both absolute and relative paths are supported
* It can optionally include a comma or semicolon separated list of file names to copy (wildcards are accepted), along with one or more target directories (comma or semicolon separated)
* It can optionally also include the relative or absolute path to a batch file to run after the build completes
* The expected column names are shown in the following table
  * Columns `ID` and `SolutionPath` are required; the others are optional

| ID | SolutionPath                                                                                    | BuildArgs                                                  | PostBuildCopyList                                                        | CopyTargetDirectories                               | PostBuildBatchFile                                                                                    | Comment |
|----|-------------------------------------------------------------------------------------------------|------------------------------------------------------------|--------------------------------------------------------------------------|-----------------------------------------------------|-------------------------------------------------------------------------------------------------------|---------|
| 1  | C:\Projects\DMS_Managers\PRISM_Class_Library\PRISM_NET472.sln                                   | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" |                                                                          |                                                     |                                                                                                       |         |
| 2  | C:\Projects\PeptideHitResultsProcessor\PHRPReader\PHRPReader.sln                                | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" |                                                                          |                                                     | C:\Projects\PeptideHitResultsProcessor\PHRPReader\bin\Distribute_Files.bat NoPause                    |         |
| 3  | C:\Projects\ProteinFileReaderDLL\ProteinFileReader.sln                                          | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" |                                                                          |                                                     |                                                                                                       |         |
| 4  | C:\Projects\Protein_Coverage_Summarizer\ProteinCoverageSummarizer\ProteinCoverageSummarizer.sln | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" |                                                                          |                                                     | C:\Projects\Protein_Coverage_Summarizer\ProteinCoverageSummarizer\bin\Distribute_DLL.bat Call NoPause |         |
| 27 | C:\Projects\ProteinParsimony\SetCover.sln                                                       | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" | SetCover\bin\Debug\SetCover.dll                                          | C:\Projects\DMS_Managers\Analysis_Manager\AM_Common |                                                                                                       |         |
| 15 | C:\Projects\Protein_Digestion_Simulator\ProteinDigestionSimulator.NET.sln                       | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" | ProteinDigestionSimulator\bin\*.dll; ProteinDigestionSimulator\bin\*.exe | \\floyd\software\ProteinDigestionSimulator\Exe_Only |                                                                                                       |         |


Use `/BasePath` to define the directory path to use for any `.sln` files in the Solution List File that are a relative path (not rooted)
* This path is also used when the target directory for the post build copy is a relative path
* The following table is similar to the previous one, but uses relative paths, relative to `/BasePath:C:\Projects`

| ID | SolutionPath                                                                        | BuildArgs                                                  | PostBuildCopyList                                                        | CopyTargetDirectories                               | PostBuildBatchFile                  | Comment |
|----|-------------------------------------------------------------------------------------|------------------------------------------------------------|--------------------------------------------------------------------------|-----------------------------------------------------|-------------------------------------|---------|
| 1  | DMS_Managers\PRISM_Class_Library\PRISM_NET472.sln                                   | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" |                                                                          |                                                     |                                     |         |
| 2  | PeptideHitResultsProcessor\PHRPReader\PHRPReader.sln                                | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" |                                                                          |                                                     | bin\Distribute_Files.bat NoPause    |         |
| 3  | ProteinFileReaderDLL\ProteinFileReader.sln                                          | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" |                                                                          |                                                     |                                     |         |
| 4  | Protein_Coverage_Summarizer\ProteinCoverageSummarizer\ProteinCoverageSummarizer.sln | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" |                                                                          |                                                     | bin\Distribute_DLL.bat Call NoPause |         |
| 27 | ProteinParsimony\SetCover.sln                                                       | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" | SetCover\bin\Debug\SetCover.dll                                          | DMS_Managers\Analysis_Manager\AM_Common             |                                     |         |
| 15 | Protein_Digestion_Simulator\ProteinDigestionSimulator.NET.sln                       | /t:restore;build /p:Configuration=Debug;Platform="Any CPU" | ProteinDigestionSimulator\bin\*.dll; ProteinDigestionSimulator\bin\*.exe | \\floyd\software\ProteinDigestionSimulator\Exe_Only |                                     |         |


Use `/StartID` and `/EndId` to specify a range of solution IDs to build
* Use `/StartID:0` or `/StartID:1` to start with the first solution
* Use `/EndID:0` to build all solutions (optionally starting with StartID)


Use `/MSBuildPath` to define the path to MSBuild.exe
* If this parameter is not defined, the program will auto-select the newest version of MSBuild
* It looks for directories below `C:\Program Files\Microsoft Visual Studio` and are a year (e.g. 2022)
* Within each year-named directories, it looks for a subdirectory named `Community` then looks for:
  * `Msbuild\Current\bin\amd64\MSBuild.exe`
  * `Msbuild\Current\bin\MSBuild.exe`

Use `/WorkDir` to customize the working directory path
* Console output files are created in the working directory

Use `/Preview` to preview the solutions that would be compiled
* Also previews files that would be copied and batch files that would be run

The processing options can be specified in a parameter file using `/ParamFile:Options.conf` or `/Conf:Options.conf`
* Define options using the format `ArgumentName=Value`
* Lines starting with `#` or `;` will be treated as comments
* Additional arguments on the command line can supplement or override the arguments in the parameter file

Use `/CreateParamFile` to create an example parameter file
* By default, the example parameter file content is shown at the console
* To create a file named Options.conf, use `/CreateParamFile:Options.conf`

## Contacts

Written by Matthew Monroe for PNNL (Richland, WA) \
E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov \
Website: https://github.com/PNNL-Comp-Mass-Spec/ or https://panomics.pnnl.gov/ or https://www.pnnl.gov/integrative-omics

## License

The MSBuild Multi Project Builder is licensed under the 2-Clause BSD License; 
you may not use this program except in compliance with the License. You may obtain 
a copy of the License at https://opensource.org/licenses/BSD-2-Clause

Copyright 2022 Battelle Memorial Institute
