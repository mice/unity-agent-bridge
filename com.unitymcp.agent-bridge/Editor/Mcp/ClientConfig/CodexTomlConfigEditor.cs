using System;

namespace UnityMcp.AgentBridge.Mcp
{
    internal sealed class CodexTomlConfigEditor
    {
        private readonly ManagedTomlConfigEditor _configEditor;

        public CodexTomlConfigEditor()
            : this(new ManagedBlockTextEditor())
        {
        }

        internal CodexTomlConfigEditor(ManagedBlockTextEditor textEditor)
        {
            _configEditor = new ManagedTomlConfigEditor(
                textEditor ?? throw new ArgumentNullException(nameof(textEditor)));
        }

        public ManagedBlockApplyResult Apply(string targetPath, string executableCommand)
        {
            return Apply(targetPath, executableCommand, string.Empty);
        }

        public ManagedBlockApplyResult Apply(string targetPath, string executableCommand, string projectRoot)
        {
            return _configEditor.Apply(
                targetPath,
                preservedChildSections => CodexProjectConfigWriter.BuildManagedBlockBody(
                    executableCommand,
                    projectRoot,
                    preservedChildSections),
                createBackup: true);
        }

        public ManagedBlockApplyResult ApplyManagedBlock(string targetPath, string managedBlockBody)
        {
            return _configEditor.Apply(
                targetPath,
                preservedChildSections => ManagedTomlConfigEditor.AppendPreservedChildSections(
                    managedBlockBody,
                    preservedChildSections),
                createBackup: true);
        }

        public ManagedBlockApplyResult Remove(string targetPath)
        {
            return _configEditor.Remove(targetPath, createBackup: true);
        }
    }
}
