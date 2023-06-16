using UnityEditor;

namespace Anatawa12.UdonSharpMigrationFix
{
    /// <summary>
    /// Handles cache data for U# that gets saved to the Library. All data this uses is intermediate generated data that is not required and can be regenerated from the source files.
    /// </summary>
    internal class UdonSharpEditorCache
    {
        private const string ProjectNeedsUpgradeKey = "com.anatawa12.udon-sharp-migration-fix.needs-upgrade";

        public static bool ProjectNeedsUpgrade
        {
            get => SessionState.GetBool(ProjectNeedsUpgradeKey, true);
            private set => SessionState.SetBool(ProjectNeedsUpgradeKey, value);
        }

        public static void QueueUpgradePass()
        {
            ProjectNeedsUpgrade = true;
        }

        public static void ClearUpgradePassQueue()
        {
            ProjectNeedsUpgrade = false;
        }
    }
}
