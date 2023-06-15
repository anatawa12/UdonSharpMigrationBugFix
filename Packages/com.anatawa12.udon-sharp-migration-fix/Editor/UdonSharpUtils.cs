
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UdonSharp
{
    internal static class UdonSharpUtils
    {
        private static volatile Dictionary<Type, Type> _inheritedTypeMap;

        [ThreadStatic]
        private static Dictionary<Type, Type> _userTypeToUdonTypeCache;

        public static void Log(object message)
        {
            Debug.Log($"[<color=#0c824c>UdonSharp Migration Bug Fix</color>] {message}");
        }
        
        public static void Log(object message, UnityEngine.Object context)
        {
            Debug.Log($"[<color=#0c824c>UdonSharp Migration Bug Fix</color>] {message}", context);
        }
        
        public static void LogWarning(object message)
        {
            Debug.LogWarning($"[<color=#FF00FF>UdonSharp Migration Bug Fix</color>] {message}");
        }
        
        public static void LogWarning(object message, UnityEngine.Object context)
        {
            Debug.LogWarning($"[<color=#FF00FF>UdonSharp Migration Bug Fix</color>] {message}", context);
        }
        
        public static void LogError(object message)
        {
            Debug.LogError($"[<color=#FF00FF>UdonSharp Migration Bug Fix</color>] {message}");
        }
        
        public static void LogError(object message, UnityEngine.Object context)
        {
            Debug.LogError($"[<color=#FF00FF>UdonSharp Migration Bug Fix</color>] {message}", context);
        }

        internal static string[] GetProjectDefines(bool editorBuild)
        {
            List<string> defines = new List<string>();

            foreach (string define in UnityEditor.EditorUserBuildSettings.activeScriptCompilationDefines)
            {
                if (!editorBuild)
                    if (define.StartsWith("UNITY_EDITOR"))
                        continue;

                defines.Add(define);
            }

            defines.Add("COMPILER_UDONSHARP");

            return defines.ToArray();
        }

        public static bool DoesUnityProjectHaveCompileErrors()
        {
            Type logEntryType = typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");
            MethodInfo getLinesAndModeMethod = logEntryType.GetMethod("GetLinesAndModeFromEntryInternal", BindingFlags.Public | BindingFlags.Static);
            
            bool hasCompileError = false;
            
            int logEntryCount = (int)logEntryType.GetMethod("StartGettingEntries", BindingFlags.Public | BindingFlags.Static).Invoke(null, Array.Empty<object>());

            try
            {
                object[] getLinesParams = { 0, 1, 0, "" };

                for (int i = 0; i < logEntryCount; ++i)
                {
                    getLinesParams[0] = i;
                    getLinesAndModeMethod.Invoke(null, getLinesParams);

                    int mode = (int)getLinesParams[2];

                    // 1 << 11 == ConsoleWindow.Mode.ScriptCompileError
                    if ((mode & (1 << 11)) != 0)
                    {
                        hasCompileError = true;
                        break;
                    }
                }
            }
            finally
            {
                logEntryType.GetMethod("EndGettingEntries").Invoke(null, Array.Empty<object>());
            }

            return hasCompileError;
        }

        internal static void SetDirty(UnityEngine.Object obj)
        {
            EditorUtility.SetDirty(obj);
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }

        private static PropertyInfo _getLoadedAssembliesProp;
    }
}
