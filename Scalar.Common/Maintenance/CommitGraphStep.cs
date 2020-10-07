using Scalar.Common.FileSystem;
using Scalar.Common.Git;
using Scalar.Common.Tracing;
using System.IO;
using System.Text;

namespace Scalar.Common.Maintenance
{
    public class CommitGraphStep : GitMaintenanceStep
    {
        private const string CommitGraphChainLock = "commit-graph-chain.lock";

        public CommitGraphStep(ScalarContext context, GitFeatureFlags gitFeatures = GitFeatureFlags.None, bool requireObjectCacheLock = true)
            : base(context, requireObjectCacheLock, gitFeatures)
        {
        }

        public override string Area => "CommitGraphStep";

        public override string ProgressMessage => "Updating commit-graph";

        protected override void PerformMaintenance()
        {
            using (ITracer activity = this.Context.Tracer.StartActivity("TryWriteGitCommitGraph", EventLevel.Informational))
            {
                string commitGraphLockPath = Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", "commit-graphs", CommitGraphChainLock);
                this.Context.FileSystem.TryDeleteFile(commitGraphLockPath);

                GitProcess.Result writeResult;
                if (this.GitFeatures.HasFlag(GitFeatureFlags.MaintenanceBuiltin))
                {
                    writeResult = this.RunGitCommand(
                            (process) => process.MaintenanceRunTask(GitProcess.MaintenanceTask.CommitGraph, this.Context.Enlistment.GitObjectsRoot),
                            nameof(GitProcess.MaintenanceRunTask));

                }
                else
                {
                    writeResult = this.RunGitCommand((process) => process.WriteCommitGraph(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.WriteCommitGraph));
                }

                StringBuilder sb = new StringBuilder();
                string commitGraphsDir = Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", "commit-graphs");

                if (this.Context.FileSystem.DirectoryExists(commitGraphsDir))
                {
                    foreach (DirectoryItemInfo info in this.Context.FileSystem.ItemsInDirectory(commitGraphsDir))
                    {
                        sb.Append(info.Name);
                        sb.Append(";");
                    }
                }

                activity.RelatedInfo($"commit-graph list after write: {sb}");

                if (writeResult.ExitCodeIsFailure)
                {
                    this.LogErrorAndRewriteCommitGraph(activity);
                }

                GitProcess.Result verifyResult = this.RunGitCommand((process) => process.VerifyCommitGraph(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.VerifyCommitGraph));

                if (!this.Stopping && verifyResult.ExitCodeIsFailure)
                {
                    this.LogErrorAndRewriteCommitGraph(activity);
                }
            }
        }
    }
}
