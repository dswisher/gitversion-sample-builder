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
                    
                    // Pull the script name from args
                    if (args.Length == 0)
                    {
                        Console.WriteLine("You must specify the file to process.");
                        return;
                    }

                    var scriptName = args[0];

                    // Set up the command runner
                    var diagram = new DiagramBuilder();
                    var runner = new CommandRunner(diagram);
                    await runner.InitAsync(tokenSource.Token);

                    // Process the script
                    await runner.ProcessFileAsync(scriptName, tokenSource.Token);
                    
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
