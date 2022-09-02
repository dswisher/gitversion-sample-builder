using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SimpleExec;

namespace SampleBuilder
{
    public class DiagramBuilder
    {
        private readonly List<string> lines = new();
        private readonly Dictionary<string, string> branchMap = new();

        public DiagramBuilder()
        {
            lines.Add("@startuml");
        }


        public void SetTitle(string title)
        {
            lines.Add($"title {title}");
        }


        public void AddBranch(string toBranchName, string fromBranchName = null)
        {
            if (branchMap.ContainsKey(toBranchName))
            {
                throw new Exception($"Attempt to create duplicate branch: {toBranchName}");
            }

            var toBranchId = $"b{branchMap.Count + 1}";
            
            branchMap.Add(toBranchName, toBranchId);

            var create = string.Empty;
            if (fromBranchName != null)
            {
                create = "create ";
            }

            // TODO - better color?
            lines.Add($"{create}participant \"{toBranchName}\" as {toBranchId} #99FF99");
            
            // If there is a source branch, add a link
            if (fromBranchName != null)
            {
                var fromBranchId = branchMap[fromBranchName];
                
                lines.Add($"{fromBranchId} -> {toBranchId}: branch from {fromBranchName}");
            }
        }
        
        
        public void AddCommit(string branchName, int commitNumber)
        {
            var branchId = branchMap[branchName];
            
            lines.Add($"{branchId} -> {branchId}: commit");
        }


        public void AddTag(string branchName, string tag)
        {
            var branchId = branchMap[branchName];
            
            lines.Add($"{branchId} -> {branchId}: tag {tag}");
        }


        public void AddVersion(string branchName, string version, bool sameLine)
        {
            var branchId = branchMap[branchName];
            var slash = sameLine ? "/ " : string.Empty;
            
            lines.Add($"{slash}rnote over {branchId}: {version}");
        }


        public void AddMerge(string toBranchName, string fromBranchName)
        {
            var toBranchId = branchMap[toBranchName];
            var fromBranchId = branchMap[fromBranchName];
            
            lines.Add($"{toBranchId} <- {fromBranchId}: merge");
        }


        public async Task WriteAsync(string name, CancellationToken cancellationToken)
        {
            // Figure out that paths
            var outputDir = new DirectoryInfo("OUTPUT");
            var umlPath = Path.Join(outputDir.FullName, $"{name}.uml");

            // Make sure the directory exists
            if (!outputDir.Exists)
            {
                outputDir.Create();
            }
            
            // Finish up the diagram
            lines.Add("@enduml");

            // Write out the UML file
            Console.WriteLine("Writing {0}", umlPath);
            await using (var stream = new FileStream(umlPath, FileMode.Create, FileAccess.Write))
            await using (var writer = new StreamWriter(stream))
            {
                foreach (var line in lines)
                {
                    await writer.WriteLineAsync(line);
                }
            }

            // Use plantuml to turn it into a PNG
            Console.WriteLine("Running plantuml");
            await Command.ReadAsync("plantuml", umlPath, workingDirectory: outputDir.FullName, cancellationToken: cancellationToken);
        }
    }
}
