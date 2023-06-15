
using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace Anatawa12.UdonSharpMigrationFix
{
    [InitializeOnLoad]
    internal class UdonSharpEditorManager
    {
        static UdonSharpEditorManager()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.isPlaying)
                return;
            
            UpgradeAssetsIfNeeded();
        }

        // Rely on assembly reload to clear this since it indicates the user needs to change a script
        private static bool _upgradeDeferredByScriptError;

        private static void UpgradeAssetsIfNeeded()
        {
            if (UdonSharpEditorCache.Instance.Info.projectNeedsUpgrade && 
                !EditorApplication.isCompiling && !EditorApplication.isUpdating && !_upgradeDeferredByScriptError)
            {
                if (UdonSharpUpgrader.NeedsUpgradeScripts())
                {
                    UdonSharpUtils.LogWarning("Needed to update scripts, deferring asset update.");
                    return;
                }

                if (UdonSharpUtils.DoesUnityProjectHaveCompileErrors())
                {
                    UdonSharpUtils.LogWarning("C# scripts have compile errors, prefab upgrade deferred until script errors are resolved.");
                    _upgradeDeferredByScriptError = true;
                    return;
                }
                
                UdonSharpProgramAsset.CompileAllCsPrograms();
                //UdonSharpCompilerV1.WaitForCompile();
                
                if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
                {
                    // Give chance to compile and resolve errors in case they are fixed already
                    UdonSharpCompilerV1.CompileSync();
                    
                    if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
                    {
                        UdonSharpUtils.LogWarning("U# scripts have compile errors, prefab upgrade deferred until script errors are resolved.");
                        _upgradeDeferredByScriptError = true;
                        return;
                    }
                }

                try
                {
                    UdonSharpEditorUtility1.UpgradePrefabs(GetAllPrefabsWithUdonSharpBehaviours());
                }
                catch (Exception e)
                {
                    UdonSharpUtils.LogError($"Exception while upgrading prefabs. Exception: {e}");
                    EditorUtility.DisplayDialog("Error", "Exception while upgrading prefabs! This might be a bug!", "OK");
                }
                finally
                {
                    UdonSharpEditorCache.Instance.ClearUpgradePassQueue();
                }
            }
        }

        private static IEnumerable<GameObject> GetAllPrefabsWithUdonSharpBehaviours()
        {
            List<GameObject> roots = new List<GameObject>();
            List<UdonBehaviour> behaviourScratch = new List<UdonBehaviour>();
            
            IEnumerable<string> allPrefabPaths = AssetDatabase.FindAssets("t:prefab").Select(AssetDatabase.GUIDToAssetPath);

            foreach (string prefabPath in allPrefabPaths)
            {
                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                
                if (prefabRoot == null)
                    continue;
                
                PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(prefabRoot);
                if (prefabAssetType == PrefabAssetType.Model || 
                    prefabAssetType == PrefabAssetType.MissingAsset)
                    continue;

                prefabRoot.GetComponentsInChildren<UdonBehaviour>(true, behaviourScratch);
                
                if (behaviourScratch.Count == 0)
                    continue;

                bool hasUdonSharpBehaviour = false;

                foreach (UdonBehaviour behaviour in behaviourScratch)
                {
                    if (UdonSharpEditorUtility1.IsUdonSharpBehaviour(behaviour))
                    {
                        hasUdonSharpBehaviour = true;
                        break;
                    }
                }

                if (hasUdonSharpBehaviour)
                    roots.Add(prefabRoot);
            }

            return roots;
        }
    }
    
    internal class UdonSharpPrefabPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            List<UdonBehaviour> behaviours = new List<UdonBehaviour>();
            
            foreach (string importedAsset in importedAssets)    
            {
                if (!importedAsset.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;

                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(importedAsset);
                
                prefabRoot.GetComponentsInChildren<UdonBehaviour>(true, behaviours);

                if (behaviours.Count == 0)
                    continue;

                bool needsUpdate = false;

                foreach (UdonBehaviour behaviour in behaviours)
                {
                    if (!UdonSharpEditorUtility1.IsUdonSharpBehaviour(behaviour))
                        continue;

                    if (UdonSharpEditorUtility1.GetBehaviourVersion(behaviour) < UdonSharpBehaviourVersion.CurrentVersion)
                    {
                        needsUpdate = true;
                        break;
                    }
                }
                
                if (!needsUpdate)
                    continue;
                
                if (PrefabUtility.IsPartOfImmutablePrefab(prefabRoot))
                {
                    UdonSharpUtils.LogWarning($"Imported prefab with U# behaviour that needs update pass '{importedAsset}' is immutable");
                    continue;
                }

                UdonSharpEditorCache.Instance.QueueUpgradePass();
                break;
            }
        }
    }
}
