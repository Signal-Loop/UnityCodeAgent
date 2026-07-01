Currently, the `executing-csharp-scripts-in-unity-editor` skill is failing to properly handle missing assemblies. Create python tools that can be used to evaluate and improve skills.

- use DeepEval framework
- Use DeepEval skill and do internet research how to best approach it, specifically skills evaluation with deepeval
- use python scripts obeying 'project-python' skill
- solution is generic - not for single skill, but can be resused to other skills or tools descriptions.
- skill specific information needed for evals (like test cases) should be located in skill directory in `evals` subfolder. eval configuration like model should be also here, so it can be defined per skill
- eval code and artifacts should be in /evals folder
- model/provider used for tests should be configurable. default should be openrouter with deepseek v4 flash
- evals shoul use existing Copilot service endpoints, so real agent is tested
- configuration should be stored in toml files
- tool calls should be intercepted and mocked, so tests dont modify existing project.

`executing-csharp-scripts-in-unity-editor` failure details:
When there are no additional assemblies added in settings, and for example script uses Image, this error is returned: `error CS0234: The type or namespace name 'UI' does not exist in the namespace 'UnityEngine' (are you missing an assembly reference?)`. To solve this problem, agent uses reflection or load assembly dynamically. This works, but missing assembluy should be added to additional assemmblies in settings instead. This is described in skill, but agent does not follow it.

Goal: Create DeepEval based evals that can be used to improve `executing-csharp-scripts-in-unity-editor` missing assembly behaviour.

example tests:
Create gameobject and add `Image` to it
- it should call execute_csharp_script_in_unity_editor with with `using UnityEngine.UI;` or `UnityEngine.UI.Image` in script, and it should return error about missing assembly. Then agent should call tool `add-assembly-to-unity-editor-settings` with `UnityEngine.UI` as argument. This should end the test with success, and agent interaction should be aborted.

Create gameobject and add `Rigidbody2d` to it
- it should call execute_csharp_script_in_unity_editor with with `Rigidbody2D` in script, and it should return error about missing assembly. Then agent should call tool `add-assembly-to-unity-editor-settings` with `UnityEngine.Physics2DModule` as argument. This should end the test with success, and agent interaction should be aborted.
