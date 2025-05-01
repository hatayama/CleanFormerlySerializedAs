using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace io.github.hatayama.CleanFormerlySerializedAs
{
    /// <summary>
    /// Provides menu items to trigger the removal of FormerlySerializedAs attributes.
    /// </summary>
    public static class CleanFormerlySerializedAsMenu
    {
        private const string MenuItemPath = "Assets/Clean FormerlySerializedAs";

        [MenuItem(MenuItemPath, true)]
        private static bool ValidateMenuItem()
        {
            string[] guids = Selection.assetGUIDs;
            if (guids.Length != 1) return false;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            // Check if the path is inside the Assets folder before proceeding
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            return (File.Exists(path) && Path.GetExtension(path).ToLower() == ".cs") || Directory.Exists(path);
        }

        [MenuItem(MenuItemPath, false, 1000)]
        private static void ProcessSelectedScript()
        {
            bool userConfirmed = EditorUtility.DisplayDialog(
                "Confirmation",
                "It's recommended to back up your project (e.g., using git) just in case.",
                "OK",
                "Cancel"
            );

            if (!userConfirmed)
            {
                return;
            }

            string[] guids = Selection.assetGUIDs;
            // No need to check guids.Length again, ValidateMenuItem already does.
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);

            // GetTargetScriptPaths already filters for Assets/ folder
            List<string> targetScriptPaths = GetTargetScriptPaths(path);

            if (targetScriptPaths.Count == 0)
            {
                 // Provide feedback if no scripts are found in the selection
                 string selectionType = Directory.Exists(path) ? "directory" : "file";
                 EditorUtility.DisplayDialog("Clean FormerlySerializedAs",
                     $"No C# scripts found in the selected {selectionType} or its subdirectories within the Assets folder.",
                     "OK");
                 return;
            }

            // 1. Find scripts containing the attribute without modifying them
            // Ensure FormerlySerializedAsRemover is available in the project scope
            FormerlySerializedAsRemover checker = new FormerlySerializedAsRemover();
            List<string> scriptsWithAttribute = FindScriptsWithAttribute(targetScriptPaths, checker);

            if (scriptsWithAttribute.Count == 0)
            {
                EditorUtility.DisplayDialog("Clean FormerlySerializedAs",
                    $"Processed {targetScriptPaths.Count} script(s).\nNo FormerlySerializedAs attributes found.",
                    "OK");
                return;
            }

            // 2. Mark related Prefabs as dirty
            int updatedPrefabCount = UpdateRelatedPrefabs(scriptsWithAttribute);

            // 3. Remove the attributes from the identified scripts
            int totalRemovedCount = 0;
            // Reuse the checker instance, assuming it can also perform removal
            foreach (string scriptPath in scriptsWithAttribute)
            {
                totalRemovedCount += RemoveAttributesFromFile(scriptPath, checker);
            }

            // Post-processing: Refresh AssetDatabase if attributes were removed
            if (totalRemovedCount > 0)
            {
                 AssetDatabase.Refresh(); // Refresh only if changes were made
                 Debug.Log("AssetDatabase refreshed after removing FormerlySerializedAs attributes.");
            }


            // Display results
            string message = $"Processed {targetScriptPaths.Count} script(s).\n";
            message += $"Found {scriptsWithAttribute.Count} script(s) with attributes.\n";
            if (totalRemovedCount > 0)
            {
                 message += $"Successfully removed {totalRemovedCount} FormerlySerializedAs attributes.\n";
            } else {
                 message += "No FormerlySerializedAs attributes were removed (check remover logic or file permissions).\n";
            }

            if (updatedPrefabCount > 0)
            {
                message += $"Updated and saved {updatedPrefabCount} related Prefab(s).";
            } else if (updatedPrefabCount == 0 && scriptsWithAttribute.Count > 0) {
                message += "No related Prefabs needed updating or no related Prefabs were found.";
            } else if (scriptsWithAttribute.Count == 0) {
                message += "No related Prefabs needed updating as no scripts with attributes were found.";
            }

            EditorUtility.DisplayDialog("Clean FormerlySerializedAs", message, "OK");
        }

        /// <summary>
        /// Gets a list of C# script paths from the selected asset path, filtering for Assets folder.
        /// </summary>
        private static List<string> GetTargetScriptPaths(string path)
        {
            List<string> scriptPaths = new List<string>();
            if (File.Exists(path) && Path.GetExtension(path).ToLower() == ".cs")
            {
                // Ensure the single selected file is also within Assets
                 if (path.StartsWith("Assets/"))
                 {
                    scriptPaths.Add(path);
                 }
            }
            else if (Directory.Exists(path))
            {
                 // GetFiles already searches recursively. Filter results for Assets/.
                 scriptPaths.AddRange(Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
                                             .Where(p => p.StartsWith("Assets/")));
            }
            return scriptPaths;
        }

        /// <summary>
        /// Finds scripts containing the FormerlySerializedAs attribute. Does not modify files.
        /// </summary>
        private static List<string> FindScriptsWithAttribute(List<string> scriptPaths, FormerlySerializedAsRemover checker)
        {
            List<string> foundScripts = new List<string>();
            foreach (string scriptPath in scriptPaths)
            {
                try
                {
                    string content = File.ReadAllText(scriptPath);
                    // Use the existing RemoveFormerlySerializedAs method to check the count.
                    // We don't need the processed content here, just the count.
                    (_, int potentialRemovedCount) = checker.RemoveFormerlySerializedAs(content);
                    if (potentialRemovedCount > 0)
                    {
                        foundScripts.Add(scriptPath);
                    }
                }
                catch (IOException ex)
                {
                    Debug.LogError($"Error reading script file {scriptPath}: {ex.Message}");
                    // Optionally continue to the next file or rethrow/handle differently
                }
                 catch (System.Exception ex) // Catch other potential exceptions during check
                 {
                    Debug.LogError($"Unexpected error checking script {scriptPath}: {ex.Message}");
                 }
            }
            return foundScripts;
        }


        /// <summary>
        /// Updates Prefabs related to the scripts containing FormerlySerializedAs attributes.
        /// Returns the number of Prefabs that were marked dirty and saved.
        /// </summary>
        private static int UpdateRelatedPrefabs(List<string> scriptsWithAttributePaths)
        {
            if (scriptsWithAttributePaths == null || scriptsWithAttributePaths.Count == 0)
            {
                return 0; // No scripts to check against
            }

            // Use HashSet for efficient lookups
            HashSet<string> scriptPathSet = new HashSet<string>(scriptsWithAttributePaths.Select(p => p.Replace("\\\\", "/"))); // Normalize paths

            string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab");
            int totalPrefabs = allPrefabGuids.Length; // ★ 全プレハブ数を取得
            int processedCount = 0; // ★ 処理済み数をカウント
            int updatedPrefabCount = 0;
            List<Object> dirtyPrefabs = new List<Object>(); // Collect prefabs to mark dirty

            Debug.Log($"Checking {totalPrefabs} prefabs for components using {scriptsWithAttributePaths.Count} modified scripts...");

            try // ★ プログレスバーを確実に閉じるために try-finally を使うんや
            {
                // ★ プログレスバー表示開始
                // 最初はキャンセル不可のバーを表示（0%時点ですぐキャンセルされるのを防ぐ意図もある）
                EditorUtility.DisplayProgressBar("Updating Prefabs", "Starting scan...", 0f);

                foreach (string prefabGuid in allPrefabGuids)
                {
                    processedCount++; // ★ 処理済み数をインクリメント
                    // ★ プログレスバー更新 (タイトル、情報、進捗率) - ここからキャンセル可能にする
                    string info = $"Scanning prefab {processedCount}/{totalPrefabs}";
                    float progress = (float)processedCount / totalPrefabs;
                    // DisplayCancelableProgressBar はループ内で呼ぶ
                    if (EditorUtility.DisplayCancelableProgressBar("Updating Prefabs", info, progress))
                    {
                         // ★ キャンセルボタンが押された場合の処理
                         Debug.LogWarning("Prefab update process cancelled by user.");
                         // finally ブロックで ClearProgressBar が呼ばれるので、ここでは return 0 するだけでよい
                         return 0; // ★ キャンセルされたら 0 を返す
                    }


                    string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid)?.Replace("\\\\", "/"); // Normalize path
                    // Ensure the prefab is within the Assets folder
                    if (string.IsNullOrEmpty(prefabPath) || !prefabPath.StartsWith("Assets/"))
                    {
                        continue;
                    }

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null) {
                         Debug.LogWarning($"Could not load prefab at path: {prefabPath}");
                        continue; // Skip if prefab cannot be loaded
                    }


                    // Get all components, including those on inactive GameObjects within the prefab
                    Component[] components = prefab.GetComponentsInChildren<Component>(true);
                    bool shouldMarkDirty = false;

                    foreach (Component component in components)
                    {
                        if (component == null) continue; // Skip potentially broken components

                        // Check if the component is a MonoBehaviour
                        MonoBehaviour behaviour = component as MonoBehaviour;
                        if (behaviour == null) continue;

                        MonoScript monoScript = MonoScript.FromMonoBehaviour(behaviour);
                        if (monoScript == null) continue; // Skip if script asset is missing

                        string scriptAssetPath = AssetDatabase.GetAssetPath(monoScript)?.Replace("\\\\", "/"); // Normalize path

                        // Check if the script path is in our set of modified scripts
                        if (!string.IsNullOrEmpty(scriptAssetPath) && scriptPathSet.Contains(scriptAssetPath))
                        {
                            shouldMarkDirty = true;
                            break; // Found a relevant component, no need to check others in this prefab
                        }
                    }

                    if (shouldMarkDirty)
                    {
                        dirtyPrefabs.Add(prefab); // Add to list for batch marking
                         updatedPrefabCount++;
                    }
                } // ★ ループ終了
            }
            finally
            {
                 // ★ プログレスバーを閉じる
                 EditorUtility.ClearProgressBar();
            }


            // Mark all collected prefabs as dirty outside the loop
             if (dirtyPrefabs.Count > 0)
             {
                AssetDatabase.StartAssetEditing(); // ★ 編集開始や
                try // finally で確実に StopAssetEditing を呼ぶためやで
                {
                    // Marking prefabs dirty should ideally happen before saving assets.
                    foreach(Object obj in dirtyPrefabs) {
                        EditorUtility.SetDirty(obj);
                    }
                    AssetDatabase.SaveAssets(); // Save all dirty assets
                    Debug.Log($"Marked {updatedPrefabCount} Prefabs dirty and saved assets.");
                }
                finally
                {
                    AssetDatabase.StopAssetEditing(); // ★ 編集終了や
                }
             } else {
                 Debug.Log("No prefabs needed updating.");
             }

            return updatedPrefabCount; // ★ 更新したプレハブ数を返す
        }

        /// <summary>
        /// Removes FormerlySerializedAs attributes from the specified script file using the remover.
        /// </summary>
        /// <returns>The number of attributes removed.</returns>
        private static int RemoveAttributesFromFile(string scriptPath, FormerlySerializedAsRemover remover)
        {
            try
            {
                string originalContent = File.ReadAllText(scriptPath);
                // Call the actual removal method
                (string processedContent, int removedCount) = remover.RemoveFormerlySerializedAs(originalContent);

                // Only write back if changes were actually made
                if (removedCount > 0 && originalContent != processedContent)
                {
                    File.WriteAllText(scriptPath, processedContent);
                    Debug.Log($"Removed {removedCount} attributes from {Path.GetFileName(scriptPath)}");
                    return removedCount;
                } else if (removedCount > 0) {
                     Debug.LogWarning($"Remover reported {removedCount} removals in {Path.GetFileName(scriptPath)}, but content was unchanged. Check remover logic.");
                     return 0; // Report 0 if no change occurred despite count > 0
                }
            }
            catch (IOException ex)
            {
                Debug.LogError($"Error reading/writing script file {scriptPath}: {ex.Message}");
            }
             catch (System.Exception ex) // Catch other potential exceptions during removal
             {
                Debug.LogError($"Unexpected error removing attributes from script {scriptPath}: {ex.Message}");
             }
            return 0; // Return 0 if no attributes were removed or an error occurred
        }
    }
} 