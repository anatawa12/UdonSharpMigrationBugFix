﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
            // Append OnEditorUpdate to head of the list.
            EditorApplication.CallbackFunction oldValue, newValue;
            var onEditorUpdate = new[] { new EditorApplication.CallbackFunction(OnEditorUpdate) };
            do
            {
                oldValue = EditorApplication.update;
                newValue = onEditorUpdate[0] + oldValue;
            } while (Interlocked.CompareExchange(ref EditorApplication.update, newValue, oldValue) != oldValue);
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
            if (UdonSharpEditorCache.ProjectNeedsUpgrade && 
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
                    UdonSharpEditorCache.ClearUpgradePassQueue();
                }
            }
        }

        private static IEnumerable<GameObject> GetAllPrefabsWithUdonSharpBehaviours()
        {
            var roots = new List<GameObject>();
            var behaviourScratch = new List<UdonBehaviour>();
            
            foreach (var prefabPath in AssetDatabase.FindAssets("t:prefab").Select(AssetDatabase.GUIDToAssetPath))
            {
                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                
                if (prefabRoot == null)
                    continue;
                
                var prefabAssetType = PrefabUtility.GetPrefabAssetType(prefabRoot);
                if (prefabAssetType == PrefabAssetType.Model || prefabAssetType == PrefabAssetType.MissingAsset)
                    continue;

                prefabRoot.GetComponentsInChildren(true, behaviourScratch);
                
                if (behaviourScratch.Count == 0)
                    continue;

                if (behaviourScratch.Any(UdonSharpEditorUtility1.IsUdonSharpBehaviour))
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
            
            foreach (var importedAsset in importedAssets)    
            {
                if (!importedAsset.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;

                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(importedAsset);
                
                prefabRoot.GetComponentsInChildren(true, behaviours);

                if (behaviours.Count == 0)
                    continue;

                var needsUpdate = behaviours
                    .Where(UdonSharpEditorUtility1.IsUdonSharpBehaviour)
                    .Any(behaviour => UdonSharpEditorUtility1.GetBehaviourVersion(behaviour) < UdonSharpBehaviourVersion.CurrentVersion);

                if (!needsUpdate)
                    continue;
                
                if (PrefabUtility.IsPartOfImmutablePrefab(prefabRoot))
                {
                    UdonSharpUtils.LogWarning($"Imported prefab with U# behaviour that needs update pass '{importedAsset}' is immutable");
                    continue;
                }

                UdonSharpEditorCache.QueueUpgradePass();
                break;
            }
        }
    }
}
