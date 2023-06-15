﻿
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.Udon.Serialization.OdinSerializer;

namespace UdonSharp
{
    /// <summary>
    /// Handles cache data for U# that gets saved to the Library. All data this uses is intermediate generated data that is not required and can be regenerated from the source files.
    /// </summary>
    [InitializeOnLoad]
    internal class UdonSharpEditorCache
    {
    #region Instance and serialization management
        [Serializable]
        private struct UdonSharpCacheStorage
        {
            public ProjectInfo info;
        }

        private const string CACHE_DIR_PATH = "Library/UdonSharpCache/";
        private const string CACHE_FILE_PATH = "Library/UdonSharpCache/UdonSharpEditorCache.dat"; // Old cache ended in .asset

        public static UdonSharpEditorCache Instance => GetInstance();

        private static UdonSharpEditorCache _instance;
        private static readonly object InstanceLock = new object();

        private const int CURR_CACHE_VER = 2;
        
        private static UdonSharpEditorCache GetInstance()
        {
            lock (InstanceLock)
            {
                if (_instance != null)
                    return _instance;

                _instance = new UdonSharpEditorCache();
                _instance._info.version = CURR_CACHE_VER;
                _instance._info.projectNeedsUpgrade = true;

                if (!File.Exists(CACHE_FILE_PATH))
                    return _instance;
                
                UdonSharpCacheStorage storage = SerializationUtility.DeserializeValue<UdonSharpCacheStorage>(File.ReadAllBytes(CACHE_FILE_PATH), DataFormat.Binary);
                _instance._info = storage.info;

                // For now we just use this to see if we need to check for project serialization upgrade, may be extended later on. At the moment only used to avoid wasting time on extra validation when possible.
                // Hey now we use this to nuke out old data too
                if (_instance._info.version < CURR_CACHE_VER)
                {
                    _instance._info.version = CURR_CACHE_VER;
                    _instance._info.projectNeedsUpgrade = true;
                }

                return _instance;
            }
        }

        static UdonSharpEditorCache()
        {
            AssemblyReloadEvents.beforeAssemblyReload += AssemblyReloadSave;
        }

        // Saves cache on play mode exit/enter and once we've entered the target mode reload the state from disk to persist the changes across play/edit mode

        internal static void ResetInstance()
        {
            _instance = null;
        }

        private class UdonSharpEditorCacheWriter : UnityEditor.AssetModificationProcessor
        {
            public static string[] OnWillSaveAssets(string[] paths)
            {
                Instance.SaveAllCacheData();

                return paths;
            }

            public static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);

                if (script)
                {
                }
                else if(AssetDatabase.IsValidFolder(assetPath))
                {
                }

                return AssetDeleteResult.DidNotDelete;
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
                if (!Directory.Exists(CACHE_DIR_PATH))
                    Directory.CreateDirectory(CACHE_DIR_PATH);

                UdonSharpCacheStorage storage = new UdonSharpCacheStorage() {
                    info = _info,
                };
                File.WriteAllBytes(CACHE_FILE_PATH, SerializationUtility.SerializeValue<UdonSharpCacheStorage>(storage, DataFormat.Binary));
                _infoDirty = false;
            }
        }
    #endregion

    #region Project Global State
        
        [Serializable]
        public struct ProjectInfo
        {
            [SerializeField]
            internal int version;
            
            public bool projectNeedsUpgrade;
        }

        private bool _infoDirty;
        private ProjectInfo _info;

        public ProjectInfo Info
        {
            get => _info;
            private set
            {
                _info = value;
                _infoDirty = true;
            }
        }

        public void QueueUpgradePass()
        {
            ProjectInfo info = Info;

            info.projectNeedsUpgrade = true;

            Info = info;
        }
        
        public void ClearUpgradePassQueue()
        {
            ProjectInfo info = Info;

            info.projectNeedsUpgrade = false;

            Info = info;
        }

        #endregion
    }
}