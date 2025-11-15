// Assets/Editor/FindOnEnableMissingBase.cs
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

public class FindOnEnableMissingBase : EditorWindow
{
    [MenuItem("Tools/Find OnEnable Without base")]
    static void Run()
    {
        string[] files = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);
        var onEnableRegex = new Regex(@"\b(public|protected)?\s*override\s+void\s+OnEnable\s*\(\s*\)\s*\{", RegexOptions.Compiled);
        foreach (var f in files)
        {
            string text = File.ReadAllText(f);
            foreach (Match m in onEnableRegex.Matches(text))
            {
                // look ahead for "base.OnEnable(" within next ~200 chars
                int idx = m.Index;
                int lookLen = Mathf.Min(200, text.Length - idx - m.Length);
                string snippet = text.Substring(idx, m.Length + lookLen);
                if (!snippet.Contains("base.OnEnable("))
                {
                    Debug.Log($"Possible missing base.OnEnable in: {f.Replace(Application.dataPath, "Assets")}", AssetDatabase.LoadAssetAtPath<Object>(f.Replace(Application.dataPath, "Assets")));
                }
            }
        }
        Debug.Log("Search complete.");
    }
}
