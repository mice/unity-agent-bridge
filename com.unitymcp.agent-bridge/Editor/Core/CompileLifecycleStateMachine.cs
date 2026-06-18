using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UnityMcp.AgentBridge
{
    internal static class CompileLifecycleStateMachine
    {
        public static CompileLifecycleState EnsureInitialized(CompileLifecycleState state, string projectPath)
        {
            state ??= new CompileLifecycleState();
            state.activeCommandIds ??= new List<string>();
            state.activeTargetEpochs ??= new List<int>();
            state.projectPath = projectPath ?? state.projectPath ?? string.Empty;
            state.currentStage = state.currentStage ?? string.Empty;
            state.lastTransition = state.lastTransition ?? string.Empty;
            state.timeoutReason = state.timeoutReason ?? string.Empty;
            return state;
        }

        public static CompileLifecycleState RecordCompilationStarted(CompileLifecycleState state, DateTime nowUtc, string projectPath)
        {
            state = EnsureInitialized(state, projectPath);
            state.compileEpoch = Math.Max(state.compileEpoch, state.lastStartedEpoch) + 1;
            state.lastStartedEpoch = state.compileEpoch;
            state.isCompiling = true;
            state.startedAtUtc = FormatUtc(nowUtc);
            state.finishedAtUtc = string.Empty;
            state.lastAssemblyPath = string.Empty;
            state.assemblyFinishedCount = 0;
            state.errorCount = 0;
            state.warningCount = 0;
            state.currentStage = "compile_started";
            state.timeoutReason = string.Empty;
            state.lastTransition = "compile_started";
            state.lastTransitionAtUtc = FormatUtc(nowUtc);
            state.activeTargetEpochs = state.activeCommandIds.Count == 0
                ? new List<int>()
                : state.activeCommandIds.Select(_ => state.compileEpoch).ToList();
            return state;
        }

        public static CompileLifecycleState RecordAssemblyFinished(CompileLifecycleState state, DateTime nowUtc, string assemblyPath, int errorCount, int warningCount)
        {
            state = EnsureInitialized(state, state?.projectPath);
            state.lastAssemblyPath = assemblyPath ?? string.Empty;
            state.assemblyFinishedCount++;
            state.errorCount += Math.Max(0, errorCount);
            state.warningCount += Math.Max(0, warningCount);
            state.currentStage = "assembly_finished";
            state.lastTransition = "assembly_finished";
            state.lastTransitionAtUtc = FormatUtc(nowUtc);
            return state;
        }

        public static CompileLifecycleState RecordCompilationFinished(CompileLifecycleState state, DateTime nowUtc)
        {
            state = EnsureInitialized(state, state?.projectPath);
            state.lastFinishedEpoch = Math.Max(state.lastFinishedEpoch, state.lastStartedEpoch);
            state.isCompiling = false;
            state.finishedAtUtc = FormatUtc(nowUtc);
            state.currentStage = "compile_finished";
            state.lastTransition = "compile_finished";
            state.lastTransitionAtUtc = FormatUtc(nowUtc);
            state.timeoutReason = string.Empty;
            return state;
        }

        public static CompileLifecycleState RegisterWaitingCommand(CompileLifecycleState state, string commandId, int targetEpoch, string stage, string projectPath)
        {
            state = EnsureInitialized(state, projectPath);
            if (!state.activeCommandIds.Contains(commandId))
            {
                state.activeCommandIds.Add(commandId);
            }

            if (targetEpoch > 0 && !state.activeTargetEpochs.Contains(targetEpoch))
            {
                state.activeTargetEpochs.Add(targetEpoch);
            }

            state.currentStage = stage ?? state.currentStage;
            return state;
        }

        public static CompileLifecycleState UnregisterCommand(CompileLifecycleState state, string commandId, int targetEpoch)
        {
            state = EnsureInitialized(state, state?.projectPath);
            state.activeCommandIds.RemoveAll(id => string.Equals(id, commandId, StringComparison.Ordinal));
            if (targetEpoch > 0)
            {
                state.activeTargetEpochs.RemoveAll(epoch => epoch == targetEpoch);
            }

            return state;
        }

        public static bool HasFinishedEpoch(CompileLifecycleState state, int targetEpoch)
        {
            return state != null && targetEpoch > 0 && state.lastFinishedEpoch >= targetEpoch;
        }

        public static string FormatUtc(DateTime value)
        {
            return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }
    }
}
