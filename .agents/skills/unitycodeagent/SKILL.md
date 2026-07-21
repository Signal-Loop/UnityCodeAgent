---
name: unitycodeagent
description: 'Use when an agent must work in Unity, build games, inspect or modify Unity Editor state with UnityCodeAgent: scene, GameObject, component, prefab, asset, ScriptableObject, or Editor automation changes; Unity console log checks; Unity EditMode or PlayMode test runs; or creating/updating favourite Editor scripts. Do not use for generic C# source editing, plain file reads/writes, or non-Unity Editor tasks.'
---

# Executing C# Scripts in Unity Editor

## Table of Contents

1. Available Tools
2. Core Principles
3. Usage Workflow
4. Debugging Loop
5. Script Context and APIs
6. Common Scripting Patterns

---

## Available Tools

This skill coordinates three tools within one workflow. Use the tool that matches the task, and combine them when the task crosses from source edits to Editor automation or test verification.

| Tool                                    | Purpose                                                 | When to Use                                                                                                                                                                                        |
| --------------------------------------- | ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `execute_csharp_script_in_unity_editor` | Executes C# code in the live Unity Editor (Roslyn)      | Modifying scenes, GameObjects, Components, Prefabs, ScriptableObjects, or running Editor automation. Script output and logs are returned directly in the tool result.                              |
| `read_unity_console_logs`               | Reads recent entries from the Unity Editor Console      | Diagnosing compilation errors, runtime exceptions, unexpected Editor state, asset imports, and other Editor activity.                                                                            |
| `run_unity_tests`                       | Runs EditMode or PlayMode Unity tests via TestRunnerApi | Verifying Unity behavior through the Test Framework, optionally narrowed to fully qualified test names.                                                                                          |

---

## Execute C# Script Tool Contract

Use `execute_csharp_script_in_unity_editor` for live Unity Editor work:

- Modify scene objects, GameObjects, Components, Transforms, UI elements, Prefabs, ScriptableObjects, or other assets through Unity APIs.
- Query current Editor, scene, prefab, or asset state, such as listing objects or reading component values.
- Batch-process or automate Editor tasks.
- Compute values with Unity math and physics APIs such as `Mathf`, `Vector3`, `Quaternion`, and `Physics` when the result depends on Unity behavior.

Package installation and ProjectSettings changes require dedicated project tooling or a narrower explicit workflow.

The tool uses Roslyn in the Unity Editor, has access to loaded Unity/project assemblies, captures `Debug.Log()`, `Debug.LogError()`, and the final evaluated expression, and marks the active scene dirty after successful execution in edit mode.

---

## Core Principles

- **Pre-Imported Namespaces:** `System`, `System.Collections.Generic`, `System.Linq`, `UnityEngine`, `UnityEditor` are always available. Do **not** redeclare them with `using`.
- **Explicit Usings for Others:** Declare any namespace not in the pre-imported list (e.g., `using UnityEngine.UI;`).
- **Specificity:** Prefer fully qualified names (e.g., `UnityEngine.GameObject`, `UnityEditor.AssetDatabase`) to avoid ambiguity.
- **Clarity & Error Handling:** Comment non-obvious logic. Wrap risky operations in `try-catch` and log errors with context.
- **Object class ambiguity:** Always use `UnityEngine.Object` when referring to Unity objects to avoid confusion with `System.Object`.

---

## Usage Workflow

Use the applicable steps in this order for each task. Skip only the steps that do not apply.

1. **Analyze Requirements:** Understand what needs to be created, modified, or queried. Identify target GameObjects, Components, assets, source files, and expected outcome.
2. **Analyze Unity Context:** Inspect the hierarchy, existing components, and current asset state when needed. Identify edge cases such as missing objects, duplicate components, or invalid asset paths.
3. **Plan the Action:** Determine the Unity APIs and assets involved, and separate Editor automation from source-file work and test verification.
4. **Execute the Action:** Use the appropriate Unity API patterns and Core Principles for the selected operation.
5. **Fix and Retry:** If execution, compilation, or tests fail, apply the Debugging Loop before reporting completion.

---

## Debugging Loop

When a script execution returns errors or unexpected results:

1. Read the error message and stack trace returned directly in the tool result.
2. If the error suggests a compilation failure (e.g., type not found, missing member), use the console details to identify and fix the source file, then re-execute.
3. Otherwise, identify the root cause from the script output: missing object, bad asset path, wrong API usage, or null reference.
4. Fix the script and re-execute.
5. Repeat until the tool result confirms success with no errors.

---

## Script Context and APIs

### Pre-Imported Namespaces (do NOT add `using` for these)

- `System`
- `System.Collections.Generic`
- `System.Linq`
- `UnityEngine`
- `UnityEditor`

### Commonly Needed Explicit Usings

```csharp
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using TMPro;
```

### Loaded Assemblies

Only loaded assemblies are available.

#### Assemblies included by default:

- System.Core,
- UnityEditor.CoreModule,
- UnityEngine.CoreModule,
- Assembly-CSharp,
- Assembly-CSharp-Editor

#### Loading Additional Assemblies:

When encountering errors about missing types or namespaces, like `error CS0234: The type or namespace name 'UI' does not exist in the namespace 'UnityEngine' (are you missing an assembly reference?)`:

1. Identify the required assembly and namespace for the API you are trying to use.
2. Use the following code to add an assembly to the settings:
```csharp
SignalLoop.UnityCodeAgent.Settings.UnityCodeAgentSettings.Instance.AddToolAssembly("UnityEngine.Physics2DModule");
```

### Key API Reference

| Task                         | API                                                                                       |
| ---------------------------- | ----------------------------------------------------------------------------------------- |
| Find active scene GameObject | `UnityEngine.GameObject.Find("Name")`                                                     |
| Find all objects of type     | `UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None)`                       |
| Load asset from path         | `UnityEditor.AssetDatabase.LoadAssetAtPath<T>("Assets/...")`                              |
| Save all dirty assets        | `UnityEditor.AssetDatabase.SaveAssets()`                                                  |
| Refresh asset database       | `UnityEditor.AssetDatabase.Refresh()`                                                     |
| Mark object dirty            | `UnityEditor.EditorUtility.SetDirty(obj)`                                                 |
| Open scene                   | `UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MyScene.unity")` |
| Save open scene              | `UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes()`                         |

---

## Common Scripting Patterns

### Safe Get-or-Add Component

```csharp
T GetOrAddComponent<T>(UnityEngine.GameObject go) where T : UnityEngine.Component {
    var c = go.GetComponent<T>();
    if (c == null) c = go.AddComponent<T>();
    return c;
}
```

### Safe Create-or-Get GameObject

```csharp
UnityEngine.GameObject CreateOrGetGameObject(string name, UnityEngine.Transform parent = null) {
    var existing = UnityEngine.GameObject.Find(name);
    if (existing != null) return existing;
    var go = new UnityEngine.GameObject(name);
    if (parent != null) go.transform.SetParent(parent, false);
    return go;
}
```

### Creating and Parenting Objects

```csharp
var parent = UnityEngine.GameObject.Find("Canvas");
if (parent == null) { UnityEngine.Debug.LogError("Canvas not found"); return; }

var panel = CreateOrGetGameObject("HUD_Panel", parent.transform);
using UnityEngine.UI;
var image = GetOrAddComponent<Image>(panel);
image.color = new UnityEngine.Color(0f, 0f, 0f, 0.5f);
UnityEngine.Debug.Log("HUD_Panel configured");
```

### Modifying Prefab Assets

```csharp
string path = "Assets/Prefabs/MyPrefab.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
if (root == null) { UnityEngine.Debug.LogError("Prefab not found: " + path); return; }

try {
    var rb = GetOrAddComponent<UnityEngine.Rigidbody2D>(root);
    rb.gravityScale = 0f;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    UnityEngine.Debug.Log("Prefab saved: " + path);
} finally {
    UnityEditor.PrefabUtility.UnloadPrefabContents(root);
}
```

### Querying Scene State

```csharp
var allObjects = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(UnityEngine.FindObjectsSortMode.None);
foreach (var go in allObjects) {
    UnityEngine.Debug.Log($"{go.name} | Layer: {UnityEngine.LayerMask.LayerToName(go.layer)} | Active: {go.activeSelf}");
}
```

### Creating a ScriptableObject Asset

```csharp
var so = UnityEngine.ScriptableObject.CreateInstance<MyScriptableObjectType>();
so.someField = 42;
UnityEditor.AssetDatabase.CreateAsset(so, "Assets/Data/MyAsset.asset");
UnityEditor.AssetDatabase.SaveAssets();
UnityEngine.Debug.Log("ScriptableObject created at Assets/Data/MyAsset.asset");
```

### Deleting a GameObject

Store the name before destroying, since the object becomes invalid immediately after.

```csharp
var target = UnityEngine.GameObject.Find("ToDestroy");
if (target != null) {
    string name = target.name;
    UnityEngine.Object.DestroyImmediate(target);
    UnityEngine.Debug.Log("Destroyed: " + name);
} else {
    UnityEngine.Debug.Log("Object not found — nothing to destroy");
}
```
