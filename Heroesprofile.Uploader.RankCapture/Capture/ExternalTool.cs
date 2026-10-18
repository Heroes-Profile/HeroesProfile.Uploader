using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>Runs a helper program (tesseract, the macOS rank helper), feeding it bytes and taking its output.</summary>
    internal static class ExternalTool
    {
        public sealed record Result(int ExitCode, byte[] Output, string Errors);

        /// <summary>
        /// Runs <paramref name="file"/> with <paramref name="args"/>, writes <paramref name="input"/> (if any) to its
        /// standard input, and waits up to <paramref name="timeout"/>; a program that takes longer is killed.
        /// </summary>
        public static async Task<Result> RunAsync(string file, string[] args, byte[] input, TimeSpan timeout)
        {
            var start = new ProcessStartInfo(file) {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args) {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {file}");
            var output = new MemoryStream();
            var copyOut = process.StandardOutput.BaseStream.CopyToAsync(output);
            var readErrors = process.StandardError.ReadToEndAsync();
            if (input != null) {
                await process.StandardInput.BaseStream.WriteAsync(input);
            }
            process.StandardInput.Close();

            var exited = process.WaitForExitAsync();
            if (await Task.WhenAny(exited, Task.Delay(timeout)) != exited) {
                try {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) {
                }
                throw new TimeoutException($"{Path.GetFileName(file)} took longer than {timeout.TotalSeconds:0} s");
            }
            await copyOut;
            return new Result(process.ExitCode, output.ToArray(), await readErrors);
        }

        /// <summary><paramref name="file"/>'s full path if it's on the PATH, else null.</summary>
        public static string FindOnPath(string file)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
                var candidate = Path.Combine(dir, file);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }
            return null;
        }

        public static string Text(byte[] output) => Encoding.UTF8.GetString(output);
    }
}
