using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Settings
{
    internal static class UnityCodeAgentToolAssembliesEditorDrawer
    {
        private const string NoAssembliesAvailableLabel = "(No assemblies available)";

        private static readonly AdvancedDropdownState AssemblyDropdownState = new AdvancedDropdownState();
        private static string[] _availableAssemblyNames = Array.Empty<string>();

        public static void Draw(UnityCodeAgentSettings settings, SerializedObject serializedObject, Action repaint)
        {
            RefreshAvailableAssemblies(settings);

            EditorGUILayout.LabelField("Execute C# Scripts");
            EditorGUILayout.HelpBox(
                "These assemblies are loaded for C# script execution. Default assemblies are always included; add project or package assemblies when scripts need their APIs.",
                MessageType.Info);

            EditorGUILayout.LabelField("Default Assemblies");
            DrawDefaultAssemblies();

            EditorGUILayout.LabelField("Additional Assemblies");

            EditorGUI.indentLevel++;
            DrawAssemblySelector(settings, serializedObject, repaint);
            EditorGUILayout.Space(4f);
            DrawAdditionalAssemblies(settings, serializedObject);
            EditorGUI.indentLevel--;
        }

        private static void DrawDefaultAssemblies()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.indentLevel++;
                foreach (string assemblyName in UnityCodeAgentSettings.DefaultToolAssemblyNames)
                {
                    EditorGUILayout.LabelField("- " + assemblyName);
                }

                EditorGUI.indentLevel--;
            }
        }

        private static void DrawAssemblySelector(
            UnityCodeAgentSettings settings,
            SerializedObject serializedObject,
            Action repaint)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Add Assembly", GUILayout.Width(100f));
                using (new EditorGUI.DisabledScope(!HasAvailableAssemblies()))
                {
                    if (EditorGUILayout.DropdownButton(new GUIContent(GetAssemblyDropdownLabel()), FocusType.Keyboard))
                    {
                        Rect buttonRect = GUILayoutUtility.GetLastRect();
                        new AssemblySearchableDropdown(
                            AssemblyDropdownState,
                            _availableAssemblyNames,
                            assemblyName => SelectAssembly(settings, serializedObject, assemblyName, repaint)).Show(buttonRect);
                    }
                }
            }
        }

        private static void DrawAdditionalAssemblies(UnityCodeAgentSettings settings, SerializedObject serializedObject)
        {
            if (settings.AdditionalToolAssemblyNames == null || settings.AdditionalToolAssemblyNames.Count == 0)
            {
                EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
                return;
            }

            List<string> assembliesToRemove = null;
            foreach (string assemblyName in settings.AdditionalToolAssemblyNames)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("- " + assemblyName);
                    if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                    {
                        assembliesToRemove ??= new List<string>();
                        assembliesToRemove.Add(assemblyName);
                    }
                }
            }

            if (assembliesToRemove == null)
            {
                return;
            }

            serializedObject.ApplyModifiedProperties();
            foreach (string assemblyName in assembliesToRemove)
            {
                settings.RemoveToolAssembly(assemblyName);
            }

            RefreshAvailableAssemblies(settings);
            serializedObject.UpdateIfRequiredOrScript();
        }

        private static bool SelectAssembly(
            UnityCodeAgentSettings settings,
            SerializedObject serializedObject,
            string assemblyName,
            Action repaint)
        {
            if (string.IsNullOrWhiteSpace(assemblyName) || IsPlaceholderAssemblyName(assemblyName))
            {
                return false;
            }

            serializedObject.ApplyModifiedProperties();
            var success = settings.AddToolAssembly(assemblyName);
            if (!success)
            {
                return false;
            }

            RefreshAvailableAssemblies(settings);
            serializedObject.UpdateIfRequiredOrScript();
            repaint?.Invoke();
            return true;
        }

        private static void RefreshAvailableAssemblies(UnityCodeAgentSettings settings)
        {
            var existingNames = new HashSet<string>(UnityCodeAgentSettings.DefaultToolAssemblyNames, StringComparer.Ordinal);
            if (settings.AdditionalToolAssemblyNames != null)
            {
                foreach (string name in settings.AdditionalToolAssemblyNames)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        existingNames.Add(name);
                    }
                }
            }

            _availableAssemblyNames = AppDomain.CurrentDomain.GetAssemblies()
                .Select(GetAssemblyName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !existingNames.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (_availableAssemblyNames.Length == 0)
            {
                _availableAssemblyNames = new[] { NoAssembliesAvailableLabel };
            }
        }

        private static bool HasAvailableAssemblies()
        {
            return _availableAssemblyNames != null &&
                _availableAssemblyNames.Length > 0 &&
                !IsPlaceholderAssemblyName(_availableAssemblyNames[0]);
        }

        private static string GetAssemblyDropdownLabel()
            => HasAvailableAssemblies() ? "Select Assembly..." : NoAssembliesAvailableLabel;

        private static bool IsPlaceholderAssemblyName(string assemblyName)
            => string.Equals(assemblyName, NoAssembliesAvailableLabel, StringComparison.Ordinal);

        private static string GetAssemblyName(Assembly assembly)
            => assembly?.GetName().Name;

        private sealed class AssemblySearchableDropdown : AdvancedDropdown
        {
            private readonly string[] _assemblyNames;
            private readonly Func<string, bool> _onSelected;

            public AssemblySearchableDropdown(
                AdvancedDropdownState state,
                string[] assemblyNames,
                Func<string, bool> onSelected)
                : base(state)
            {
                _assemblyNames = assemblyNames ?? Array.Empty<string>();
                _onSelected = onSelected;
                minimumSize = new Vector2(300f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Assemblies");
                for (var index = 0; index < _assemblyNames.Length; index++)
                {
                    root.AddChild(new AdvancedDropdownItem(_assemblyNames[index])
                    {
                        id = index,
                    });
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                _onSelected?.Invoke(item.name);
            }
        }
    }
}
