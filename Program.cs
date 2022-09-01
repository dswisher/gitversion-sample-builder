using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SampleBuilder
{
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                using (var tokenSource = new CancellationTokenSource())
                {
                    // Shut down semi-gracefully on ctrl+c...
                    Console.CancelKeyPress += (_, eventArgs) =>
                    {
                        Console.WriteLine("*** Cancel event triggered ***");
                        tokenSource.Cancel();
                        eventArgs.Cancel = true;
                    };
                    
                    // TODO - pull script name from args
                    var scriptName = "sample.txt";

                    // Set up the command runner
                    var diagram = new DiagramBuilder();
                    var runner = new CommandRunner(diagram);
                    await runner.InitAsync(tokenSource.Token);

                    // Process the script
                    await using (var stream = new FileStream(scriptName, FileMode.Open, FileAccess.Read))
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            line = line.TrimEnd();
                            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                            {
                                continue;
                            }

                            await runner.RunCommandAsync(line, tokenSource.Token);
                        }
                    }
                    
                    // Write out the diagram
                    await diagram.WriteAsync(Path.GetFileNameWithoutExtension(scriptName), tokenSource.Token);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
