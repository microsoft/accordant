// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;

    public class Logger : IDisposable
    {
        public delegate void LogDelegate(string logLine);

        private static string defaultOutputDirectory = Path.Combine(
                new FileInfo(Assembly.GetExecutingAssembly().Location).Directory.FullName,
                "test-logs",
                 DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-ms"));

        public static AsyncLocal<string> AsyncLocalOutputDirectory = new AsyncLocal<string>();

        public static AsyncLocal<int> AsyncLocalIndentation = new AsyncLocal<int>();

        public static AsyncLocal<bool> AsyncLocalEmitTimestamp = new AsyncLocal<bool>();

        public static string OutputDirectory
        {
            get
            {
                if (AsyncLocalOutputDirectory.Value == null)
                {
                    AsyncLocalOutputDirectory.Value = defaultOutputDirectory;
                }

                return AsyncLocalOutputDirectory.Value;
            }
        }

        public static int Indentation => AsyncLocalIndentation.Value;

        public static bool EmitTimestamp => AsyncLocalEmitTimestamp.Value;

        public static int IndentationDelta { get; } = 4;

        private readonly string previousOutputDirectory = null;

        private readonly int previousIndentation = 0;

        private readonly bool previousEmitTimestamp = false;

        private static object logLock = new object();

        public static LogDelegate LogLambda { get; set; } = DefaultLogDelegate;

        public Logger(
            bool indent = false,
            bool? emitTimestamp = null,
            string outputDirectory = null)
        {
            previousOutputDirectory = OutputDirectory;
            previousIndentation = Indentation;
            previousEmitTimestamp = EmitTimestamp;

            if (outputDirectory != null)
            {
                AsyncLocalOutputDirectory.Value = outputDirectory;
            }

            if (indent)
            {
                AsyncLocalIndentation.Value = Indentation + IndentationDelta;
            }

            if (emitTimestamp != null)
            {
                AsyncLocalEmitTimestamp.Value = (bool)emitTimestamp;
            }
        }

        public static void Log(string logLine)
        {
            if (logLine == null)
            {
                logLine = string.Empty;
            }

            var indentationString = string.Join(string.Empty, Enumerable.Repeat(" ", Indentation));

            var timestampPrefix = string.Empty;
            if (EmitTimestamp)
            {
                timestampPrefix += DateTime.Now.ToString() + " ";
            }

            var indentedLogLine = string.Join(
                "\n",
                logLine.Split('\n').Select(s =>
                    timestampPrefix + indentationString + s));

            LogLambda(indentedLogLine);
        }

        public static string Serialize<T>(T value, bool indented = true)
        {
            return JsonSerializer.Serialize(value, new JsonSerializerOptions() { WriteIndented = indented });
        }

        private static void DefaultLogDelegate(string logLine)
        {
            if (!Directory.Exists(OutputDirectory))
            {
                Directory.CreateDirectory(OutputDirectory);
            }

            lock (logLock)
            {
                var logFilePath = Path.Combine(OutputDirectory, "test-runner.txt");
                using (var sw = File.AppendText(logFilePath))
                {
                    sw.WriteLine(logLine);
                }
            }
        }

        public void Dispose()
        {
            AsyncLocalOutputDirectory.Value = previousOutputDirectory;
            AsyncLocalIndentation.Value = previousIndentation;
            AsyncLocalEmitTimestamp.Value = previousEmitTimestamp;
        }
    }
}
