
using System;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Serialization.OdinSerializer;
using VRC.Udon.Serialization.OdinSerializer.Utilities;

namespace Anatawa12.UdonSharpMigrationFix
{
    internal class UdonSharpUpgrader
    {

        [MenuItem("VRChat SDK/Udon Sharp/Force Upgrade")]
        internal static void ForceUpgrade()
        {
            UdonSharpEditorCache.Instance.QueueUpgradePass();
        }

        internal static bool NeedsUpgradeScripts()
        {
            return UdonSharpProgramAsset.GetAllUdonSharpPrograms().Any(NeedsUpgradeScript);
        }

        private static bool NeedsUpgradeScript(UdonSharpProgramAsset arg)
        {
            if (!arg.sourceCsScript) return false;
            return arg.sourceCsScript.GetClass() is Type @class && @class.GetFields().Any(NeedsUpgradeChecker.NeedsUpgradeField);
        }

        class NeedsUpgradeChecker
        {
            private const FieldAttributes ConstStaticOrReadOnly =
                (FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.Literal);

            private static bool IsFieldSerializedWithoutOdin(FieldInfo fieldSymbol)
            {
                if ((fieldSymbol.Attributes & ConstStaticOrReadOnly) != 0) return false;

                bool HasAttribute<T>() where T : Attribute => fieldSymbol.GetAttribute<T>() != null;

                if (HasAttribute<NonSerializedAttribute>() && !HasAttribute<OdinSerializeAttribute>()) return false;

                return (fieldSymbol.IsPublic || HasAttribute<SerializeField>()) && !HasAttribute<OdinSerializeAttribute>();
            }

            internal static bool NeedsUpgradeField(FieldInfo firstFieldSymbol)
            {
                //var typeInfo = model.GetTypeInfo(node.Declaration.Type);
                //if (typeInfo.Type == null)
                //{
                //    UdonSharpUtils.LogWarning($"Could not find symbol for {node}");
                //    return fieldDeclaration;
                //}

                //ITypeSymbol rootType = typeInfo.Type;

                //while (rootType.TypeKind == TypeKind.Array)
                //    rootType = ((IArrayTypeSymbol)rootType).ElementType;

                //if (rootType.TypeKind == TypeKind.Error ||
                //    rootType.TypeKind == TypeKind.Unknown)
                //{
                //    UdonSharpUtils.LogWarning(
                //        $"Type {typeInfo.Type} for field '{fieldDeclaration.Declaration}' is invalid");
                //    return fieldDeclaration;
                //}

                //IFieldSymbol firstFieldSymbol = (IFieldSymbol)model.GetDeclaredSymbol(node.Declaration.Variables.First());
                //rootType = firstFieldSymbol.Type;

                // If the field is not serialized or is using Odin already, we don't need to do anything.
                if (!IsFieldSerializedWithoutOdin(firstFieldSymbol))
                    return false;

                // Getting the type may fail if it's a user type that hasn't compiled on the C# side yet. For now we skip it, but we should do a simplified check for jagged arrays
                //if (!TypeSymbol.TryGetSystemType(rootType, out Type systemType))
                //    return fieldDeclaration;
                var systemType = firstFieldSymbol.FieldType;

                // If Unity can serialize the type, we're good
                if (UnitySerializationUtility.GuessIfUnityWillSerialize(systemType))
                    return false;

                // Common type that gets picked up as serialized but shouldn't be
                // todo: Add actual checking for if a type is serializable, which isn't consistent. Unity/System library types in large part are serializable but don't have the System.Serializable tag, but types outside those assemblies need the tag to be serialized.
                if (systemType == typeof(VRCPlayerApi) || systemType == typeof(VRCPlayerApi[]))
                    return false;

                return true;
            }
        }
    }
}
