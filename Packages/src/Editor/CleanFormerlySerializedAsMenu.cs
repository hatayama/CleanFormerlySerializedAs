using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;

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
            Debug.Log("[hatayama] ProcessSelectedScript");

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

            // 1a. 属性を含むスクリプトの「パス」を探す
            FormerlySerializedAsRemover checker = new FormerlySerializedAsRemover();
            List<string> scriptsWithAttributePaths = FindScriptsWithAttribute(targetScriptPaths, checker); // ★ 変数名明確化

            if (scriptsWithAttributePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Clean FormerlySerializedAs",
                    $"Processed {targetScriptPaths.Count} script(s).\nNo scripts containing FormerlySerializedAs attributes found.", // メッセージ変更
                    "OK");
                return;
            }

            // ★★★ 1b. 属性を持つスクリプトパスから Type オブジェクトのセットを作成 ★★★
            HashSet<Type> baseTypesWithAttribute = new HashSet<Type>();
            foreach (string scriptPath in scriptsWithAttributePaths)
            {
                MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (monoScript != null)
                {
                    Type scriptType = monoScript.GetClass();
                    if (scriptType != null && typeof(MonoBehaviour).IsAssignableFrom(scriptType)) // MonoBehaviour 派生かどうかも一応チェック
                    {
                        baseTypesWithAttribute.Add(scriptType);
                        Debug.Log($"[Type Check] Found base type with attribute: {scriptType.FullName}");
                    }
                    else if (scriptType == null)
                    {
                        Debug.LogWarning($"Could not get class type from MonoScript at path: {scriptPath}");
                    }
                }
                else
                {
                    Debug.LogWarning($"Could not load MonoScript at path: {scriptPath}");
                }
            }

            if (baseTypesWithAttribute.Count == 0)
            {
                EditorUtility.DisplayDialog("Clean FormerlySerializedAs",
                    $"Processed {scriptsWithAttributePaths.Count} script(s) with attributes, but failed to get their class types.",
                    "OK");
                return;
            }
            // ★★★ Type セット作成ここまで ★★★


            // ★★★ 2a. 関連プレハブチェック (引数を Type セットに変更) ★★★
            int reserializedPrefabCount = UpdateRelatedPrefabs(baseTypesWithAttribute);

            // ★★★ 2b. シーンオブジェクトチェック (引数を Type セットに変更) ★★★
            int updatedSceneCount = UpdateSceneObjects(baseTypesWithAttribute);

            // 3. 属性をスクリプトから削除 (対象は元のパスリスト)
            int totalRemovedCount = 0;
            // Reuse the checker instance, assuming it can also perform removal
            foreach (string scriptPath in scriptsWithAttributePaths)
            {
                totalRemovedCount += RemoveAttributesFromFile(scriptPath, checker);
            }

            // Post-processing: Refresh AssetDatabase if attributes were removed
            if (totalRemovedCount > 0)
            {
                AssetDatabase.Refresh(); // Refresh only if changes were made
                Debug.Log("AssetDatabase refreshed after removing FormerlySerializedAs attributes.");
            }


            // ★★★ 再シリアライズ結果のメッセージに変更 ★★★
            string message = $"Processed {targetScriptPaths.Count} script(s).\n";
            message += $"Found {scriptsWithAttributePaths.Count} script file(s) with attributes, corresponding to {baseTypesWithAttribute.Count} base class type(s).\n"; // ★ メッセージ修正
            if (totalRemovedCount > 0)
            {
                message += $"Successfully removed {totalRemovedCount} FormerlySerializedAs attributes.\n";
            }
            else
            {
                message += "No FormerlySerializedAs attributes were removed.\n"; // メッセージ調整
            }

            if (reserializedPrefabCount > 0)
            {
                message += $"Requested reserialization for {reserializedPrefabCount} related Prefab asset(s).\n";
            }
            else if (scriptsWithAttributePaths.Count > 0)
            {
                message += "No related Prefab assets needed reserialization.\n";
            }

            // ★ シーン更新結果のメッセージを追加 ★
            if (updatedSceneCount > 0)
            {
                message += $"Marked {updatedSceneCount} open scene(s) as dirty.\nPlease save the modified scene(s).";
            }
            else if (scriptsWithAttributePaths.Count > 0)
            {
                // 属性持ちスクリプトがあった場合のみメッセージ表示
                message += "No open scenes needed updating.";
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
        private static int UpdateRelatedPrefabs(HashSet<Type> baseTypesWithAttribute)
        {
            Debug.Log("[hatayama] UpdateRelatedPrefabs (Type check)"); // ログ変更
            // 引数チェック
            if (baseTypesWithAttribute == null || baseTypesWithAttribute.Count == 0)
            {
                return 0;
            }

            // ★ HashSet<string> scriptPathSet は不要になる ★
            // Use HashSet for efficient lookups
            // HashSet<string> scriptPathSet = new HashSet<string>(scriptsWithAttributePaths.Select(p => p.Replace("\\", "/"))); // Normalize paths
            // foreach (string scriptPath in scriptsWithAttributePaths)
            // {
            //     Debug.Log($"[hatayama] {scriptPath}");
            // }

            string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab");
            int totalPrefabs = allPrefabGuids.Length; // ★ 全プレハブ数を取得
            int processedCount = 0; // ★ 処理済み数をカウント
            List<string> prefabsToReserializePaths = new List<string>();

            // ★ デバッグログのメッセージも調整 ★
            Debug.Log($"Checking {totalPrefabs} prefabs for components deriving from {baseTypesWithAttribute.Count} base types...");

            Debug.Log("[hatayama] ProcessSelectedScript 2");
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
                    if (prefab == null)
                    {
                        Debug.LogWarning($"Could not load prefab at path: {prefabPath}");
                        continue;
                    }

                    Debug.Log($"[hatayama] prefabPath 3 {prefabPath}");

                    // ★ プレハブ内のコンポーネントをチェック ★
                    Component[] components = prefab.GetComponentsInChildren<Component>(true);
                    bool shouldReserialize = false; // このプレハブを再シリアライズするかのフラグ

                    foreach (Component component in components)
                    {
                        if (component == null) continue;

                        MonoBehaviour behaviour = component as MonoBehaviour;
                        if (behaviour == null) continue;

                        // ★★★ 型チェックロジックに変更 ★★★
                        Type componentType = behaviour.GetType(); // コンポーネントの型を取得
                        bool typeMatchFound = false; // マッチしたかのフラグ

                        // 属性を持つ基本クラスの型セットをループ
                        foreach (Type baseType in baseTypesWithAttribute)
                        {
                            // コンポーネントの型が基本クラスと同じか、そのサブクラスか？
                            if (componentType == baseType || componentType.IsSubclassOf(baseType))
                            {
                                typeMatchFound = true;
                                break;
                            }
                        }

                        // ★★★ 型がマッチした場合のみ、再シリアライズ対象とする ★★★
                        if (typeMatchFound)
                        {
                            // 属性持ちスクリプト(またはその子クラス)が見つかったら、このプレハブは再シリアライズ対象や
                            shouldReserialize = true;
                            // 以前のデバッグログは型チェックのログに含めたので、ここはシンプルに
                            // Debug.Log($"Marking prefab asset for reserialization: {prefabPath}");
                            break; // このプレハブは対象確定なので、他のコンポーネントは見なくてええ
                        }
                    }

                    // ★★★ 再シリアライズ対象なら、パスをリストに追加 ★★★
                    if (shouldReserialize)
                    {
                        prefabsToReserializePaths.Add(prefabPath);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }


            if (prefabsToReserializePaths.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                AssetDatabase.ForceReserializeAssets(prefabsToReserializePaths);
                AssetDatabase.StopAssetEditing();
            }
            else
            {
                Debug.Log("No prefab assets needed reserialization.");
            }

            // ★★★ 再シリアライズしたプレハブ数を返す (リストのサイズ) ★★★
            return prefabsToReserializePaths.Count;
        }

        /// <summary>
        /// Updates GameObjects in open scenes that use scripts with FormerlySerializedAs attributes,
        /// skipping any objects that are part of a prefab instance.
        /// Returns the number of scenes marked dirty.
        /// </summary>
        private static int UpdateSceneObjects(HashSet<Type> baseTypesWithAttribute)
        {
            // 引数チェック
            if (baseTypesWithAttribute == null || baseTypesWithAttribute.Count == 0)
            {
                return 0;
            }

            // ★ HashSet<string> scriptPathSet は不要になる ★
            // 属性持ちスクリプトのパスを HashSet にしておくで
            // HashSet<string> scriptPathSet = new HashSet<string>(scriptsWithAttributePaths.Select(p => p.Replace("\\", "/")));
            int dirtySceneCount = 0;
            int totalScenes = EditorSceneManager.sceneCount; // ★ 総シーン数を取得

            // ★ デバッグログのメッセージも調整 ★
            Debug.Log($"Checking GameObjects in {totalScenes} open scenes for components deriving from {baseTypesWithAttribute.Count} base types...");

            // ★ プログレスバー表示のために try-finally を使うで
            try
            {
                // ★ プログレスバー初期表示 (キャンセル不可)
                EditorUtility.DisplayProgressBar("Updating Scene Objects", "Starting scene scan...", 0f);

                // 開いている全シーンをループ
                for (int i = 0; i < totalScenes; i++) // ★ ループ条件を totalScenes に変更
                {
                    Scene scene = EditorSceneManager.GetSceneAt(i);

                    // ★ プログレスバー更新 (ここからキャンセル可能) ★
                    string sceneName = string.IsNullOrEmpty(scene.name) ? $"Untitled Scene {i}" : scene.name;
                    string info = $"Scanning scene {i + 1}/{totalScenes}: {sceneName}";
                    float progress = (float)(i + 1) / totalScenes;
                    if (EditorUtility.DisplayCancelableProgressBar("Updating Scene Objects", info, progress))
                    {
                        // ★ キャンセル処理 ★
                        Debug.LogWarning("Scene object update process cancelled by user.");
                        return 0; // キャンセルされたら 0 を返す
                    }

                    // シーンがロードされてて有効か確認 (isDirtyチェックは不要)
                    if (!scene.IsValid() || !scene.isLoaded /*|| scene.isDirty*/)
                    {
                        continue;
                    }

                    bool sceneNeedsMarkingDirty = false; // このシーンをダーティにするかのフラグ

                    // シーンのルートGameObjectを取得
                    GameObject[] rootObjects = scene.GetRootGameObjects();
                    foreach (GameObject rootObject in rootObjects)
                    {
                        // ルートとその子孫の MonoBehaviour を全取得
                        MonoBehaviour[] behaviours = rootObject.GetComponentsInChildren<MonoBehaviour>(true);
                        foreach (MonoBehaviour behaviour in behaviours)
                        {
                            if (behaviour == null) continue;

                            // ★★★ このGameObjectがプレハブの一部「ではない」ことを確認 ★★★
                            if (PrefabUtility.IsPartOfAnyPrefab(behaviour.gameObject))
                            {
                                continue; // プレハブの一部ならスキップや
                            }

                            // ★★★ 型チェックロジックに変更 ★★★
                            Type componentType = behaviour.GetType(); // コンポーネントの型を取得
                            bool typeMatchFound = false; // マッチしたかのフラグ

                            // 属性を持つ基本クラスの型セットをループ
                            foreach (Type baseType in baseTypesWithAttribute)
                            {
                                // コンポーネントの型が基本クラスと同じか、そのサブクラスか？
                                if (componentType == baseType || componentType.IsSubclassOf(baseType))
                                {
                                    // 型がマッチした！
                                    typeMatchFound = true;
                                    Debug.Log(
                                        $"[Type Check] Match found: Component type '{componentType.FullName}' matches or inherits from base type '{baseType.FullName}' on GameObject '{behaviour.gameObject.name}' in scene '{scene.name}'.");
                                    break; // このコンポーネントについては、これ以上基本クラスをチェック不要
                                }
                            }

                            // ★★★ 型がマッチした場合のみ、シーンをダーティにする ★★★
                            if (typeMatchFound)
                            {
                                // 見つかった！このシーンはダーティにする必要がある
                                sceneNeedsMarkingDirty = true;
                                // 以前のデバッグログは型チェックのログに含めたので、ここはシンプルに
                                // Debug.Log($"Marking scene dirty due to component on {behaviour.gameObject.name}.");
                                break; // この GameObject の他のコンポーネントや子孫は見なくてええ
                            }
                        } // MonoBehaviour ごとのループ終了

                        // このルートオブジェクト以下でダーティフラグが立ったら、シーン全体の探索も終了
                        if (sceneNeedsMarkingDirty)
                        {
                            break;
                        }
                    }

                    // このシーンをダーティにする必要があればマークする
                    if (sceneNeedsMarkingDirty)
                    {
                        // MarkSceneDirty はシーンがすでにダーティでも問題ないはずや
                        EditorSceneManager.MarkSceneDirty(scene);
                        dirtySceneCount++;
                    }
                }
            }
            finally
            {
                // ★ プログレスバーを確実に閉じる ★
                EditorUtility.ClearProgressBar();
            }


            if (dirtySceneCount > 0)
            {
                Debug.Log($"Marked {dirtySceneCount} scene(s) dirty.");
            }
            else
            {
                Debug.Log("No GameObjects in open scenes found using the modified scripts (excluding prefab instances).");
            }

            return dirtySceneCount;
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
                }
                else if (removedCount > 0)
                {
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