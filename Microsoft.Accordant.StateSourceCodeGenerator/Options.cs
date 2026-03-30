// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using CommandLine;

    public class Options
    {
        [Option('i', "input", Required = true, HelpText = "Input assembly containing state definitions.")]
        public string InputAssembly { get; set; }

        [Option('n', "namespace", Required = true, HelpText = "Target namespace containing generated state classes.")]
        public string TargetNamespace { get; set; }

        [Option('o', "output", Required = false, HelpText = "Path of output file containing generated state classes.")]
        public string OutputFilePath { get; set; }
    }
}
