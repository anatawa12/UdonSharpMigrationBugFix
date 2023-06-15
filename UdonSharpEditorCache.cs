
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Anatawa12.UdonSharpMigrationFix
{
    /// <summary>
    /// Handles cache data for U# that gets saved to the Library. All data this uses is intermediate generated data that is not required and can be regenerated from the source files.
    /// </summary>
    [InitializeOnLoad]
    internal class UdonSharpEditorCache
    {
        [Serializable]
        public struct ProjectInfo
        {
            public bool projectNeedsUpgrade;
        }

        #region Instance and serialization management

        private static readonly Encoding JsonUTF8 = new UTF8Encoding(false);

        private const string CacheFilePath = "Library/com.anatawa12.udon-sharp-migration-fix.editor.json";

        public static UdonSharpEditorCache Instance => GetInstance();

        private static UdonSharpEditorCache _instance;
        private static readonly object InstanceLock = new object();

        private static UdonSharpEditorCache GetInstance()
        {
            lock (InstanceLock)
            {
                if (_instance != null)
                    return _instance;

                _instance = new UdonSharpEditorCache();
                _instance._info.projectNeedsUpgrade = true;

                if (!File.Exists(CacheFilePath))
                    return _instance;

                _instance._info = JsonUtility.FromJson<ProjectInfo>(File.ReadAllText(CacheFilePath, JsonUTF8));

                return _instance;
            }
        }

        static UdonSharpEditorCache()
        {
            AssemblyReloadEvents.beforeAssemblyReload += AssemblyReloadSave;
        }

        // Saves cache on play mode exit/enter and once we've entered the target mode reload the state from disk to persist the changes across play/edit mode

        private class UdonSharpEditorCacheWriter : UnityEditor.AssetModificationProcessor
        {
            public static string[] OnWillSaveAssets(string[] paths)
            {
                Instance.SaveAllCacheData();

                return paths;
            }
        }

        private static void AssemblyReloadSave()
        {
            Instance.SaveAllCacheData();
        }

        private void SaveAllCacheData()
        {
            if (_infoDirty)
            {
                File.WriteAllText(CacheFilePath, JsonUtility.ToJson(_info), JsonUTF8);
                _infoDirty = false;
            }
        }

        #endregion

        #region Project Global State

        private bool _infoDirty;
        private ProjectInfo _info;

        public ProjectInfo Info => _info;

        public void QueueUpgradePass()
        {
            _info.projectNeedsUpgrade = true;
            _infoDirty = true;
        }

        public void ClearUpgradePassQueue()
        {
            _info.projectNeedsUpgrade = false;
            _infoDirty = true;
        }

        #endregion
    }
}
