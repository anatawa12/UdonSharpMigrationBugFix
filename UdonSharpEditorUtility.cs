
using System;
using JetBrains.Annotations;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharp.Updater;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Serialization.OdinSerializer.Utilities;
using Object = UnityEngine.Object;

namespace Anatawa12.UdonSharpMigrationFix
{
    /// <summary>
    /// Stored on the backing UdonBehaviour
    /// </summary>
    internal enum UdonSharpBehaviourVersion
    {
        V0,
        V0DataUpgradeNeeded,
        V1,
        NextVer,
        CurrentVersion = NextVer - 1,
    }

    /// <summary>
    /// Various utility functions for interacting with U# behaviours and proxies for editor scripting.
    /// </summary>
    internal static class UdonSharpEditorUtility1
    {
        /// <summary>
        /// Deletes an UdonSharp program asset and the serialized program asset associated with it
        /// </summary>
        [PublicAPI]
        public static void DeleteProgramAsset(UdonSharpProgramAsset programAsset)
        {
            if (programAsset == null)
                return;

            AbstractSerializedUdonProgramAsset serializedAsset = programAsset.GetSerializedUdonProgramAsset();

            if (serializedAsset != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(serializedAsset);
                serializedAsset = AssetDatabase.LoadAssetAtPath<AbstractSerializedUdonProgramAsset>(assetPath);

                if (serializedAsset != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
            }

            string programAssetPath = AssetDatabase.GetAssetPath(programAsset);

            programAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programAssetPath);

            if (programAsset != null)
                AssetDatabase.DeleteAsset(programAssetPath);
        }

        private static Dictionary<MonoScript, UdonSharpProgramAsset> _programAssetLookup;
        private static Dictionary<Type, UdonSharpProgramAsset> _programAssetTypeLookup;

        private static void InitTypeLookups()
        {
            if (_programAssetLookup != null)
                return;

            _programAssetLookup = new Dictionary<MonoScript, UdonSharpProgramAsset>();
            _programAssetTypeLookup = new Dictionary<Type, UdonSharpProgramAsset>();

            UdonSharpProgramAsset[] udonSharpProgramAssets = UdonSharpProgramAsset.GetAllUdonSharpPrograms();

            foreach (UdonSharpProgramAsset programAsset in udonSharpProgramAssets)
            {
                if (programAsset && programAsset.sourceCsScript != null &&
                    !_programAssetLookup.ContainsKey(programAsset.sourceCsScript))
                {
                    _programAssetLookup.Add(programAsset.sourceCsScript, programAsset);
                    if (programAsset.GetClass() != null)
                        _programAssetTypeLookup.Add(programAsset.GetClass(), programAsset);
                }
            }
        }

        private static UdonSharpProgramAsset GetUdonSharpProgramAsset(MonoScript programScript)
        {
            InitTypeLookups();

            _programAssetLookup.TryGetValue(programScript, out var foundProgramAsset);

            return foundProgramAsset;
        }

        /// <summary>
        /// Gets the UdonSharpProgramAsset that represents the program for the given UdonSharpBehaviour
        /// </summary>
        /// <param name="udonSharpBehaviour"></param>
        /// <returns></returns>
        [PublicAPI]
        public static UdonSharpProgramAsset GetUdonSharpProgramAsset(UdonSharpBehaviour udonSharpBehaviour)
        {
            return GetUdonSharpProgramAsset(MonoScript.FromMonoBehaviour(udonSharpBehaviour));
        }

        [PublicAPI]
        public static UdonSharpProgramAsset GetUdonSharpProgramAsset(Type type)
        {
            InitTypeLookups();

            _programAssetTypeLookup.TryGetValue(type, out UdonSharpProgramAsset foundProgramAsset);

            return foundProgramAsset;
        }

        internal const string BackingFieldName = "_udonSharpBackingUdonBehaviour";

        private static readonly FieldInfo _backingBehaviourField =
            typeof(UdonSharpBehaviour).GetField(BackingFieldName, BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Gets the backing UdonBehaviour for a proxy
        /// </summary>
        /// <param name="behaviour"></param>
        /// <returns></returns>
        public static UdonBehaviour GetBackingUdonBehaviour(UdonSharpBehaviour behaviour)
        {
            return (UdonBehaviour)_backingBehaviourField.GetValue(behaviour);
        }

        internal static void SetBackingUdonBehaviour(UdonSharpBehaviour behaviour, UdonBehaviour backingBehaviour)
        {
            _backingBehaviourField.SetValue(behaviour, backingBehaviour);
        }

        private const string UDONSHARP_BEHAVIOUR_VERSION_KEY = "___UdonSharpBehaviourVersion___";
        private const string UDONSHARP_BEHAVIOUR_UPGRADE_MARKER = "___UdonSharpBehaviourPersistDataFromUpgrade___";

        internal static UdonSharpBehaviourVersion GetBehaviourVersion(UdonBehaviour behaviour)
        {
            if (behaviour.publicVariables.TryGetVariableValue<int>(UDONSHARP_BEHAVIOUR_VERSION_KEY, out int val))
                return (UdonSharpBehaviourVersion)val;

            return UdonSharpBehaviourVersion.V0;
        }

        internal static void SetBehaviourVersion(UdonBehaviour behaviour, UdonSharpBehaviourVersion version)
        {
            UdonSharpBehaviourVersion lastVer = GetBehaviourVersion(behaviour);

            if (lastVer == version && lastVer != UdonSharpBehaviourVersion.V0)
                return;

            bool setVer =
                behaviour.publicVariables.TrySetVariableValue<int>(UDONSHARP_BEHAVIOUR_VERSION_KEY, (int)version);

            if (!setVer)
            {
                behaviour.publicVariables.RemoveVariable(UDONSHARP_BEHAVIOUR_VERSION_KEY);
                IUdonVariable newVar = new UdonVariable<int>(UDONSHARP_BEHAVIOUR_VERSION_KEY, (int)version);
                setVer = behaviour.publicVariables.TryAddVariable(newVar);
            }

            if (setVer)
            {
                UdonSharpUtils.SetDirty(behaviour);
                return;
            }

            UdonSharpUtils.LogError("Could not set version variable");
        }

        private static void SetBehaviourUpgraded(UdonBehaviour behaviour)
        {
            if (!PrefabUtility.IsPartOfPrefabAsset(behaviour))
                return;

            if (!behaviour.publicVariables.TrySetVariableValue<bool>(UDONSHARP_BEHAVIOUR_UPGRADE_MARKER, true))
            {
                behaviour.publicVariables.RemoveVariable(UDONSHARP_BEHAVIOUR_UPGRADE_MARKER);

                IUdonVariable newVar = new UdonVariable<bool>(UDONSHARP_BEHAVIOUR_UPGRADE_MARKER, true);
                behaviour.publicVariables.TryAddVariable(newVar);
            }

            UdonSharpUtils.SetDirty(behaviour);
        }

        private static readonly FieldInfo _publicVariablesBytesStrField = typeof(UdonBehaviour)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(fieldInfo => fieldInfo.Name == "serializedPublicVariablesBytesString");

        private static readonly FieldInfo _publicVariablesObjectReferences = typeof(UdonBehaviour)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(e => e.Name == "publicVariablesUnityEngineObjects");

        internal static bool IsAnyProgramAssetOutOfDate()
        {
            UdonSharpProgramAsset[] programs = UdonSharpProgramAsset.GetAllUdonSharpPrograms();

            bool isOutOfDate = false;
            foreach (UdonSharpProgramAsset programAsset in programs)
            {
                if (programAsset.CompiledVersion != UdonSharpProgramVersion.CurrentVersion)
                {
                    isOutOfDate = true;
                    break;
                }
            }

            return isOutOfDate;
        }

        /// <summary>
        /// Runs a two pass upgrade of a set of prefabs, assumes all dependencies of the prefabs are included, otherwise the process could fail to maintain references.
        /// First creates a new UdonSharpBehaviour proxy script and hooks it to a given UdonBehaviour. Then in a second pass goes over all behaviours and serializes their data into the C# proxy and wipes their old data out.
        /// </summary>
        /// <param name="prefabRootEnumerable"></param>
        internal static void UpgradePrefabs(IEnumerable<GameObject> prefabRootEnumerable)
        {
            if (UdonSharpProgramAsset.IsAnyProgramAssetSourceDirty() || IsAnyProgramAssetOutOfDate())
            {
                UdonSharpUtils.Log("We found some program asset is out of date or dirty");
                UdonSharpCompilerV1.CompileSync();
            }

            // Skip upgrades on any intermediate prefab assets which may be considered invalid during the build process, mostly to avoid spamming people's console with logs that may be confusing but also gives slightly faster builds.
            string intermediatePrefabPath = UdonSharpLocator.IntermediatePrefabPath.Replace("\\", "/");

            var prefabRoots = Enumerable.ToList(
                from prefabRoot in prefabRootEnumerable
                let prefabPath = AssetDatabase.GetAssetPath(prefabRoot)
                where !prefabPath.StartsWith(intermediatePrefabPath, StringComparison.Ordinal)
                select prefabRoot);

            // Now we have a set of prefabs that we can actually load and run the two upgrade phases on.
            // Todo: look at merging the two passes since we don't actually need to load prefabs into scenes apparently

            var phase1FixupPrefabRoots = FindPrefabsToUpgradePhase1(prefabRoots);
            var phase2FixupPrefabRoots = FindPrefabsToUpgradePhase2(prefabRoots);

            // Early out and avoid the edit scope
            if (phase1FixupPrefabRoots.Count == 0 && phase2FixupPrefabRoots.Count == 0)
                return;

            phase2FixupPrefabRoots.UnionWith(phase1FixupPrefabRoots);

            var prefabDag = new UdonSharpPrefabDAG(prefabRoots);

            MarkPrefabsMarkToBeUpgraded(prefabDag);

            if (phase2FixupPrefabRoots.Count > 0)
                UdonSharpUtils.Log(
                    $"Running upgrade process on {phase2FixupPrefabRoots.Count} prefabs: {string.Join(", ", phase2FixupPrefabRoots.Select(Path.GetFileName))}");

            UpgradePrefabsPhase1(phase1FixupPrefabRoots, prefabDag);
            UpgradePrefabsPhase2(phase2FixupPrefabRoots, prefabDag);

            UdonSharpUtils.Log("Prefab upgrade pass finished");
        }

        private static bool NeedsNewProxy(UdonBehaviour udonBehaviour) =>
            IsUdonSharpBehaviour(udonBehaviour) && GetProxyBehaviour(udonBehaviour) == null;

        private static bool NeedsSerializationUpgrade(UdonBehaviour udonBehaviour)
        {
            if (!IsUdonSharpBehaviour(udonBehaviour))
                return false;

            if (NeedsNewProxy(udonBehaviour))
                return true;

            if (GetBehaviourVersion(udonBehaviour) == UdonSharpBehaviourVersion.V0DataUpgradeNeeded)
                return true;

            return false;
        }

        private static HashSet<string> FindPrefabsToUpgradePhase1(IEnumerable<GameObject> prefabRoots)
        {

            var phase1FixupPrefabRoots = new HashSet<string>();

            // Phase 1 Pruning - Add missing proxy behaviours
            foreach (GameObject prefabRoot in prefabRoots)
            {
                if (!prefabRoot.GetComponentsInChildren<UdonBehaviour>(true).Any(NeedsNewProxy))
                    continue;

                string prefabPath = AssetDatabase.GetAssetPath(prefabRoot);

                if (!prefabPath.IsNullOrWhitespace())
                    phase1FixupPrefabRoots.Add(prefabPath);
            }

            return phase1FixupPrefabRoots;
        }

        private static HashSet<string> FindPrefabsToUpgradePhase2(IEnumerable<GameObject> prefabRoots)
        {
            HashSet<string> phase2FixupPrefabRoots = new HashSet<string>();

            // Phase 2 Pruning - Check for behaviours that require their data ownership to be transferred Udon -> C#
            foreach (var prefabRoot in prefabRoots.Where(prefabRoot =>
                         prefabRoot.GetComponentsInChildren<UdonBehaviour>(true).Any(NeedsSerializationUpgrade)))
            {
                string prefabPath = AssetDatabase.GetAssetPath(prefabRoot);

                if (!prefabPath.IsNullOrWhitespace())
                    phase2FixupPrefabRoots.Add(prefabPath);
            }

            return phase2FixupPrefabRoots;
        }

        private static void MarkPrefabsMarkToBeUpgraded(UdonSharpPrefabDAG prefabDag)
        {
            // Walk up from children -> parents and mark prefab deltas on all U# behaviours to be upgraded
            // Prevents versioning from being overwritten when a parent prefab is upgraded
            foreach (var prefabPath in prefabDag.Reverse())
            {
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

                bool needsSave = false;

                try
                {
                    HashSet<UdonBehaviour> behavioursToPrepare = new HashSet<UdonBehaviour>();

                    foreach (UdonBehaviour behaviour in prefabRoot.GetComponentsInChildren<UdonBehaviour>(true))
                    {
                        if (PrefabUtility.GetCorrespondingObjectFromSource(behaviour) != behaviour &&
                            (NeedsNewProxy(behaviour) || NeedsSerializationUpgrade(behaviour)))
                        {
                            behavioursToPrepare.Add(behaviour);
                        }
                    }

                    // Deltas are stored per-prefab-instance-root in a given prefab, don't question it. Thanks.
                    // We take care to not accidentally hit any non-U#-behaviour deltas here
                    // These APIs are not documented properly at all and the only mentions of them on forum posts are how they don't work with no solutions posted :))))
                    if (behavioursToPrepare.Count > 0)
                    {
                        HashSet<GameObject> rootGameObjects = new HashSet<GameObject>();

                        foreach (UdonBehaviour behaviourToPrepare in behavioursToPrepare)
                        {
                            GameObject rootPrefab = PrefabUtility.GetOutermostPrefabInstanceRoot(behaviourToPrepare);

                            rootGameObjects.Add(rootPrefab ? rootPrefab : behaviourToPrepare.gameObject);
                        }

                        var originalObjects = new HashSet<Object>(
                            behavioursToPrepare.Select(PrefabUtility.GetCorrespondingObjectFromOriginalSource));

                        foreach (var behaviour in behavioursToPrepare)
                        {
                            var currentBehaviour = behaviour;

                            while (currentBehaviour)
                            {
                                originalObjects.Add(currentBehaviour);

                                var newBehaviour = PrefabUtility.GetCorrespondingObjectFromSource(currentBehaviour);

                                currentBehaviour = newBehaviour != currentBehaviour ? newBehaviour : null;
                            }
                        }

                        foreach (var rootGameObject in rootGameObjects)
                        {
                            var propertyModifications =
                                PrefabUtility.GetPropertyModifications(rootGameObject)?.ToList();

                            if (propertyModifications != null)
                            {
                                propertyModifications = propertyModifications.Where(
                                    modification =>
                                    {
                                        if (modification.target == null)
                                        {
                                            return true;
                                        }

                                        if (!originalObjects.Contains(modification.target))
                                        {
                                            return true;
                                        }

                                        if (modification.propertyPath == "serializedPublicVariablesBytesString" ||
                                            modification.propertyPath.StartsWith("publicVariablesUnityEngineObjects",
                                                StringComparison.Ordinal))
                                        {
                                            // UdonSharpUtils.Log($"Removed property override for {modification.propertyPath} on {modification.target}");
                                            return false;
                                        }

                                        return true;
                                    }).ToList();

                                // UdonSharpUtils.Log($"Modifications found on {rootGameObject}");
                            }
                            else
                            {
                                propertyModifications = new List<PropertyModification>();
                            }

                            foreach (UdonBehaviour behaviour in rootGameObject.GetComponentsInChildren<UdonBehaviour>(
                                         true))
                            {
                                if (!behavioursToPrepare.Contains(behaviour))
                                {
                                    continue;
                                }

                                UdonBehaviour originalBehaviour =
                                    PrefabUtility.GetCorrespondingObjectFromSource(behaviour);

                                propertyModifications.Add(new PropertyModification()
                                {
                                    target = originalBehaviour,
                                    propertyPath = "serializedPublicVariablesBytesString",
                                    value = (string)_publicVariablesBytesStrField.GetValue(behaviour)
                                });

                                List<Object> objectRefs =
                                    (List<Object>)_publicVariablesObjectReferences.GetValue(behaviour);

                                propertyModifications.Add(new PropertyModification()
                                {
                                    target = originalBehaviour,
                                    propertyPath = "publicVariablesUnityEngineObjects.Array.size",
                                    value = objectRefs.Count.ToString()
                                });

                                for (int i = 0; i < objectRefs.Count; ++i)
                                {
                                    propertyModifications.Add(new PropertyModification()
                                    {
                                        target = originalBehaviour,
                                        propertyPath = $"publicVariablesUnityEngineObjects.Array.data[{i}]",
                                        objectReference = objectRefs[i],
                                        value = ""
                                    });
                                }
                            }

                            PrefabUtility.SetPropertyModifications(rootGameObject, propertyModifications.ToArray());
                            EditorUtility.SetDirty(rootGameObject);

                            needsSave = true;
                        }

                        // UdonSharpUtils.Log($"Marking delta on prefab {prefabRoot} because it is not the original definition.");
                    }

                    if (needsSave)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private static void UpgradePrefabsPhase1(HashSet<string> phase1FixupPrefabRoots, UdonSharpPrefabDAG prefabDag)
        {
            foreach (var prefabRootPath in prefabDag)
            {
                if (!phase1FixupPrefabRoots.Contains(prefabRootPath))
                {
                    continue;
                }

                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabRootPath);

                try
                {
                    bool needsSave = false;

                    foreach (UdonBehaviour udonBehaviour in prefabRoot.GetComponentsInChildren<UdonBehaviour>(true))
                    {
                        if (!NeedsNewProxy(udonBehaviour))
                        {
                            if (GetBehaviourVersion(udonBehaviour) == UdonSharpBehaviourVersion.V0)
                            {
                                SetBehaviourVersion(udonBehaviour, UdonSharpBehaviourVersion.V0DataUpgradeNeeded);
                                needsSave = true;
                            }

                            continue;
                        }

                        var newProxy = (UdonSharpBehaviour)udonBehaviour.gameObject.AddComponent(
                            GetUdonSharpBehaviourType(udonBehaviour));
                        newProxy.enabled = udonBehaviour.enabled;

                        SetBackingUdonBehaviour(newProxy, udonBehaviour);

                        // if the GameObject is a prefab instance, we cannot relocate components
                        // so skip MoveComponentRelativeToComponent
                        if (!PrefabUtility.GetCorrespondingObjectFromSource(udonBehaviour.gameObject))
                            MoveComponentRelativeToComponent(newProxy, udonBehaviour, true);

                        SetBehaviourVersion(udonBehaviour, UdonSharpBehaviourVersion.V0DataUpgradeNeeded);

                        UdonSharpUtils.SetDirty(udonBehaviour);
                        UdonSharpUtils.SetDirty(newProxy);

                        needsSave = true;
                    }

                    if (needsSave)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabRootPath);
                    }

                    // UdonSharpUtils.Log($"Ran prefab upgrade phase 1 on {prefabRoot}");
                }
                catch (Exception e)
                {
                    UdonSharpUtils.LogError(
                        $"Encountered exception while upgrading prefab {prefabRootPath}, report exception to Merlin: {e}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private static void UpgradePrefabsPhase2(HashSet<string> phase2FixupPrefabRoots, UdonSharpPrefabDAG prefabDag)
        {

            foreach (string prefabRootPath in prefabDag)
            {
                if (!phase2FixupPrefabRoots.Contains(prefabRootPath))
                {
                    continue;
                }

                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabRootPath);

                try
                {
                    foreach (UdonBehaviour udonBehaviour in prefabRoot.GetComponentsInChildren<UdonBehaviour>(true))
                    {
                        if (!NeedsSerializationUpgrade(udonBehaviour))
                            continue;

                        UdonSharpEditorUtility.CopyUdonToProxy(GetProxyBehaviour(udonBehaviour), ProxySerializationPolicy.RootOnly);

                        // We can't remove this data for backwards compatibility :'(
                        // If we nuke the data, the unity object array on the underlying storage may change.
                        // Which means that if people have copies of this prefab in the scene with no object reference changes, their data will also get nuked which we do not want.
                        // Public variable data on the prefabs will never be touched again by U# after upgrading
                        // We will probably provide an optional upgrade process that strips this extra data, and takes into account all scenes in the project

                        // foreach (string publicVarSymbol in udonBehaviour.publicVariables.VariableSymbols.ToArray())
                        //     udonBehaviour.publicVariables.RemoveVariable(publicVarSymbol);

                        SetBehaviourVersion(udonBehaviour, UdonSharpBehaviourVersion.V1);
                        SetBehaviourUpgraded(udonBehaviour);

                        UdonSharpUtils.SetDirty(udonBehaviour);
                        UdonSharpUtils.SetDirty(GetProxyBehaviour(udonBehaviour));
                    }

                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabRootPath);

                    // UdonSharpUtils.Log($"Ran prefab upgrade phase 2 on {prefabRoot}");
                }
                catch (Exception e)
                {
                    UdonSharpUtils.LogError(
                        $"Encountered exception while upgrading prefab {prefabRootPath}, report exception to Merlin: {e}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private static readonly MethodInfo _moveComponentRelativeToComponent =
            typeof(UnityEditorInternal.ComponentUtility).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .First(e => e.Name == "MoveComponentRelativeToComponent" && e.GetParameters().Length == 3);

        internal static void MoveComponentRelativeToComponent(Component component, Component targetComponent,
            bool aboveTarget)
        {
            _moveComponentRelativeToComponent.Invoke(null, new object[] { component, targetComponent, aboveTarget });
        }

        /// <summary>
        /// Returns true if the given behaviour is a proxy behaviour that's linked to an UdonBehaviour.
        /// </summary>
        /// <param name="behaviour"></param>
        /// <returns></returns>
        [PublicAPI]
        public static bool IsProxyBehaviour(UdonSharpBehaviour behaviour)
        {
            if (behaviour == null)
                return false;

            return GetBackingUdonBehaviour(behaviour) != null;
        }

        private static Dictionary<UdonBehaviour, UdonSharpBehaviour> _proxyBehaviourLookup = new Dictionary<UdonBehaviour, UdonSharpBehaviour>();

        /// <summary>
        /// Finds an existing proxy behaviour, if none exists returns null
        /// </summary>
        /// <param name="udonBehaviour"></param>
        /// <returns></returns>
        private static UdonSharpBehaviour FindProxyBehaviour_Internal(UdonBehaviour udonBehaviour)
        {
            if (_proxyBehaviourLookup.TryGetValue(udonBehaviour, out UdonSharpBehaviour proxyBehaviour))
            {
                if (proxyBehaviour != null)
                    return proxyBehaviour;

                _proxyBehaviourLookup.Remove(udonBehaviour);
            }

            UdonSharpBehaviour[] behaviours = udonBehaviour.GetComponents<UdonSharpBehaviour>();

            foreach (UdonSharpBehaviour udonSharpBehaviour in behaviours)
            {
                UdonBehaviour backingBehaviour = GetBackingUdonBehaviour(udonSharpBehaviour);
                if (backingBehaviour != null && ReferenceEquals(backingBehaviour, udonBehaviour))
                {
                    _proxyBehaviourLookup.Add(udonBehaviour, udonSharpBehaviour);

                    return udonSharpBehaviour;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the C# version of an UdonSharpBehaviour that proxies an UdonBehaviour with the program asset for the matching UdonSharpBehaviour type
        /// </summary>
        /// <param name="udonBehaviour"></param>
        /// <returns></returns>
        public static UdonSharpBehaviour GetProxyBehaviour(UdonBehaviour udonBehaviour)
        {
            return GetProxyBehaviour_Internal(udonBehaviour);
        }

        /// <summary>
        /// Returns if the given UdonBehaviour is an UdonSharpBehaviour
        /// </summary>
        /// <param name="udonBehaviour"></param>
        /// <returns></returns>
        public static bool IsUdonSharpBehaviour(UdonBehaviour udonBehaviour)
        {
            return udonBehaviour.programSource != null && 
                   udonBehaviour.programSource is UdonSharpProgramAsset programAsset && 
                   programAsset.sourceCsScript != null;
        }

        /// <summary>
        /// Gets the UdonSharpBehaviour type from the given behaviour.
        /// If the behaviour is not an UdonSharpBehaviour, returns null.
        /// </summary>
        /// <param name="udonBehaviour"></param>
        /// <returns></returns>
        public static Type GetUdonSharpBehaviourType(UdonBehaviour udonBehaviour)
        {
            if (!IsUdonSharpBehaviour(udonBehaviour))
                return null;

            return ((UdonSharpProgramAsset)udonBehaviour.programSource).GetClass();
        }

        /// <summary>
        /// Gets the C# version of an UdonSharpBehaviour that proxies an UdonBehaviour with the program asset for the matching UdonSharpBehaviour type
        /// </summary>
        /// <param name="udonBehaviour"></param>
        /// <returns></returns>
        private static UdonSharpBehaviour GetProxyBehaviour_Internal(UdonBehaviour udonBehaviour)
        {
            if (udonBehaviour == null)
                throw new ArgumentNullException(nameof(udonBehaviour));

            UdonSharpBehaviour proxyBehaviour = FindProxyBehaviour_Internal(udonBehaviour);

            return proxyBehaviour;
        }
    }
}
