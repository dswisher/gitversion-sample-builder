using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SimpleExec;

namespace SampleBuilder
{
    public class CommandRunner
    {
        private const string GitCommand = "git";
        private const string CommitCommand = "commit";
        private const string ShowVersionCommand = "showver";
        private const string IncludeCommand = "include";
        private const string TitleCommand = "title";
        private const string VersionCommand = "ver";
        private const string ConfigCommand = "config";

        private readonly DiagramBuilder diagram;
        private readonly DirectoryInfo workDir;
        private readonly List<GitEntry> gitEntries = new();
        private readonly HashSet<string> activeBranches = new();
        private readonly Stack<FileInfo> fileStack = new();

        private string currentBranch;
        private int commitNumber;


        public CommandRunner(DiagramBuilder diagram)
        {
            this.diagram = diagram;

            // Determine the path
            // var workPath = Path.Join(Path.GetTempPath(), "sample-builder");
            var workPath = "/tmp/sample-builder";   // TODO - HACK!
            workDir = new DirectoryInfo(workPath);

            // Set up the handlers for the various git commands
            AddEntry("init", IgnoreSubCommand);
            AddEntry("branch -m main", IgnoreSubCommand);
            AddEntry("tag (.*)", TagSubCommand);
            AddEntry("branch -d (.*)", DeleteBranchSubCommand);
            AddEntry("branch (.*)", CreateBranchSubCommand);
            AddEntry("checkout (.*)", CheckoutSubCommand);
            AddEntry("merge (.*)", MergeSubCommand);
        }


        public async Task InitAsync(CancellationToken cancellationToken)
        {
            // Make sure the directory exists and is empty
            if (workDir.Exists)
            {
                workDir.Delete(true);
            }

            workDir.Create();

            // Initialize git.
            await RunCommandAsync("git init", cancellationToken);
            await RunCommandAsync("git branch -m main", cancellationToken);

            // Bootstrap the diagram by creating the main branch
            currentBranch = "main";
            diagram.AddBranch(currentBranch);
            activeBranches.Add(currentBranch);

            // We need at least one commit for GitVersion to work at all.
            await RunCommandAsync("commit file1.txt", cancellationToken);
        }


        public async Task ProcessFileAsync(string filename, CancellationToken cancellationToken)
        {
            // Figure out the path to use
            FileInfo info;
            if (fileStack.Any())
            {
                var parent = fileStack.Peek();
                info = new FileInfo(Path.Join(parent?.Directory?.FullName, filename));
            }
            else
            {
                info = new FileInfo(filename);
            }

            fileStack.Push(info);

            // Process the file
            await using (var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read))
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

                    await RunCommandAsync(line, cancellationToken);
                }
            }

            // Clean the stack
            fileStack.Pop();
        }


        public async Task RunCommandAsync(string command, CancellationToken cancellationToken)
        {
            Console.WriteLine("command: {0}", command);
            if (command.StartsWith(GitCommand))
            {
                var subCommand = command.Substring(GitCommand.Length + 1);

                await ExecuteGitCommandAsync(subCommand, cancellationToken);

                UpdateDiagramWithGitCommand(subCommand);
            }
            else if (command.StartsWith(CommitCommand))
            {
                var filename = command.Substring(CommitCommand.Length + 1);

                await DoCommitAsync(filename, cancellationToken);
            }
            else if (command.StartsWith(ShowVersionCommand))
            {
                var verb = string.Empty;
                if (command.Length > ShowVersionCommand.Length)
                {
                    verb = command.Substring(ShowVersionCommand.Length + 1);
                }

                await DoShowVersionAsync(verb == "all", cancellationToken);
            }
            else if (command.StartsWith(TitleCommand))
            {
                var title = command.Substring(TitleCommand.Length + 1);

                diagram.SetTitle(title);
            }
            else if (command.StartsWith(VersionCommand))
            {
                var kind = command.Substring(VersionCommand.Length + 1);

                diagram.SetVersion(kind);
            }
            else if (command.StartsWith(IncludeCommand))
            {
                var filename = command.Substring(IncludeCommand.Length + 1);

                await ProcessFileAsync(filename, cancellationToken);
            }
            else if (command.StartsWith(ConfigCommand))
            {
                var filename = command.Substring(ConfigCommand.Length + 1);

                ProcessConfig(filename);
            }
            else
            {
                Console.WriteLine("Do not know how to handle command: \"{0}\"", command);
            }
        }


        private async Task DoCommitAsync(string filename, CancellationToken cancellationToken)
        {
            var path = Path.Join(workDir.FullName, filename);

            commitNumber += 1;

            await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            await using (var writer = new StreamWriter(stream))
            {
                writer.WriteLine("Commit #{0}", commitNumber);
            }

            await ExecuteGitCommandAsync($"add {filename}", cancellationToken);
            await ExecuteGitCommandAsync($"commit -m\"Commit #{commitNumber}\"", cancellationToken);

            diagram.AddCommit(currentBranch, commitNumber);
        }


        private void ProcessConfig(string filename)
        {
            var sourceInfo = new FileInfo(Path.Join(fileStack.Peek()?.Directory?.FullName, filename));
            var destPath = Path.Join(workDir.FullName, "GitVersion.yml");

            sourceInfo.CopyTo(destPath);
        }


        private async Task DoShowVersionAsync(bool all, CancellationToken cancellationToken)
        {
            if (all)
            {
                var first = true;
                foreach (var branch in activeBranches)
                {
                    await AddVersionToDiagramAsync(branch, !first, cancellationToken);

                    first = false;
                }
            }
            else
            {
                await AddVersionToDiagramAsync(currentBranch, false, cancellationToken);
            }
        }


        private async Task AddVersionToDiagramAsync(string branchName, bool sameLine, CancellationToken cancellationToken)
        {
            // Make sure we are on the specified branch
            await ExecuteGitCommandAsync($"checkout {branchName}", cancellationToken);

            // Get the version info and parse it
            var (stdout, _) = await Command.ReadAsync("gitversion", workingDirectory: workDir.FullName, cancellationToken: cancellationToken);

            var versionInfo = JsonSerializer.Deserialize<GitVersionInfo>(stdout);

            if (versionInfo == null)
            {
                throw new Exception("Could not deserialize version info.");
            }

            // Add a note to the branch within the diagram
            diagram.AddVersion(branchName, versionInfo, sameLine);

            // Make sure we go back to the current branch
            await ExecuteGitCommandAsync($"checkout {currentBranch}", cancellationToken);
        }


        private async Task ExecuteGitCommandAsync(string subcommand, CancellationToken cancellationToken)
        {
            // Use ReadAsync instead of RunAsync, to avoid echo, but capture stdout/stderr in the case of an error.
            await Command.ReadAsync(GitCommand, subcommand, workingDirectory: workDir.FullName, cancellationToken: cancellationToken);
        }


        private void AddEntry(string pattern, Action<List<string>> worker)
        {
            var entry = new GitEntry
            {
                Pattern = new Regex(pattern, RegexOptions.Compiled),
                Worker = worker
            };

            gitEntries.Add(entry);
        }


        private void UpdateDiagramWithGitCommand(string subCommand)
        {
            foreach (var entry in gitEntries)
            {
                var match = entry.Pattern.Match(subCommand);

                if (match.Success)
                {
                    entry.Worker(match.Groups.Values.Select(x => x.Value).Skip(1).ToList());
                    return;
                }
            }

            // If we make it here, no match was found.
            throw new Exception($"Git subcommand '{subCommand}' was not handled.");
        }


        private void IgnoreSubCommand(List<string> parameters)
        {
            // Nothing to do for these
        }


        private void TagSubCommand(List<string> parameters)
        {
            diagram.AddTag(currentBranch, parameters[0]);
        }


        private void CreateBranchSubCommand(List<string> parameters)
        {
            diagram.AddBranch(parameters[0], currentBranch);
            activeBranches.Add(parameters[0]);
        }


        private void DeleteBranchSubCommand(List<string> parameters)
        {
            diagram.DeleteBranch(parameters[0]);
            activeBranches.Remove(parameters[0]);
        }


        private void CheckoutSubCommand(List<string> parameters)
        {
            currentBranch = parameters[0];
        }


        private void MergeSubCommand(List<string> parameters)
        {
            diagram.AddMerge(currentBranch, parameters[0]);
        }


        private class GitEntry
        {
            public Regex Pattern { get; init; }
            public Action<List<string>> Worker { get; init; }
        }
    }
}
