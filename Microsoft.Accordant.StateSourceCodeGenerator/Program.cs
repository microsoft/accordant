// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.IO;
    using System.Reflection;
    using CommandLine;

    public class Program
    {
        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    if (!File.Exists(o.InputAssembly))
                    {
                        Console.WriteLine($"Input assembly {o.InputAssembly} not found.");
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(o.OutputFilePath) &&
                        File.Exists(o.OutputFilePath))
                    {
                        var response = GetYesNoResponse($"File {o.OutputFilePath} already exists. Are you sure you want to overwrite? [y/n]");
                        if (!response)
                        {
                            return;
                        }
                    }

                    var inputAssembly = Assembly.LoadFrom(o.InputAssembly);

                    var generator = new StateSourceCodeGenerator(inputAssembly, o.TargetNamespace, args);

                    var generatedSourceCode = generator.GenerateSourceCode();

                    if (string.IsNullOrWhiteSpace(o.OutputFilePath))
                    {
                        Console.WriteLine(generatedSourceCode);
                    }
                    else
                    {
                        File.WriteAllText(o.OutputFilePath, generatedSourceCode);
                        Console.WriteLine($"Generated source code written at {o.OutputFilePath}");
                    }
                });
        }

        private static bool GetYesNoResponse(string message)
        {
            while (true)
            {
                Console.Write(message + ": ");
                string answer = Console.ReadLine();

                if (answer.ToLower() == "y")
                {
                    return true;
                }
                else if (answer.ToLower() == "n")
                {
                    return false;
                }
            }
        }
    }
}
