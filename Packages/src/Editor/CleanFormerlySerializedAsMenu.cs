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

            // 1a. Find paths of scripts containing the attribute
            FormerlySerializedAsRemover checker = new FormerlySerializedAsRemover();
            List<string> scriptsWithAttributePaths = FindScriptsWithAttribute(targetScriptPaths, checker); // * Clarify variable name

            if (scriptsWithAttributePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Clean FormerlySerializedAs",
                    $"Processed {targetScriptPaths.Count} script(s).\nNo scripts containing FormerlySerializedAs attributes found.", // Changed message
                    "OK");
                return;
            }

            // *** 1b. Create a set of Type objects from script paths with attributes ***
            HashSet<Type> baseTypesWithAttribute = new HashSet<Type>();
            foreach (string scriptPath in scriptsWithAttributePaths)
            {
                MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (monoScript != null)
                {
                    Type scriptType = monoScript.GetClass();
                    if (scriptType != null && typeof(MonoBehaviour).IsAssignableFrom(scriptType)) // Also check if it's a MonoBehaviour derivative
                    {
                        baseTypesWithAttribute.Add(scriptType);
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
            // *** Type set creation ends here ***


            // *** 2a. Check related prefabs (argument changed to Type set) ***
            int reserializedPrefabCount = UpdateRelatedPrefabs(baseTypesWithAttribute);

            // *** 2b. Check scene objects (argument changed to Type set) ***
            int updatedSceneCount = UpdateSceneObjects(baseTypesWithAttribute);

            // 3. Remove attributes from scripts (target is the original path list)
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
            }


            // *** Changed to reserialization result message ***
            string message = $"Processed {targetScriptPaths.Count} script(s).\n";
            message += $"Found {scriptsWithAttributePaths.Count} script file(s) with attributes, corresponding to {baseTypesWithAttribute.Count} base class type(s).\n"; // * Message corrected
            if (totalRemovedCount > 0)
            {
                message += $"Successfully removed {totalRemovedCount} FormerlySerializedAs attributes.\n";
            }
            else
            {
                message += "No FormerlySerializedAs attributes were removed.\n"; // Message adjusted
            }

            if (reserializedPrefabCount > 0)
            {
                message += $"Requested reserialization for {reserializedPrefabCount} related Prefab asset(s).\n";
            }
            else if (scriptsWithAttributePaths.Count > 0)
            {
                message += "No related Prefab assets needed reserialization.\n";
            }

            // * Add message for scene update result *
            if (updatedSceneCount > 0)
            {
                message += $"Marked {updatedSceneCount} open scene(s) as dirty.\nPlease save the modified scene(s).";
            }
            else if (scriptsWithAttributePaths.Count > 0)
            {
                // Display message only if there were scripts with attributes
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
            // Argument check
            if (baseTypesWithAttribute == null || baseTypesWithAttribute.Count == 0)
            {
                return 0;
            }

            string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab");
            int totalPrefabs = allPrefabGuids.Length; // * Get total number of prefabs
            int processedCount = 0; // * Count processed items
            List<string> prefabsToReserializePaths = new List<string>();
            
            try // * Use try-finally to ensure the progress bar is closed
            {
                // * Start displaying progress bar
                // Initially display a non-cancelable bar (also intended to prevent immediate cancellation at 0%)
                EditorUtility.DisplayProgressBar("Updating Prefabs", "Starting scan...", 0f);

                foreach (string prefabGuid in allPrefabGuids)
                {
                    processedCount++; // * Increment processed count
                    // * Update progress bar (title, info, progress rate) - make it cancelable from here
                    string info = $"Scanning prefab {processedCount}/{totalPrefabs}";
                    float progress = (float)processedCount / totalPrefabs;
                    // Call DisplayCancelableProgressBar inside the loop
                    if (EditorUtility.DisplayCancelableProgressBar("Updating Prefabs", info, progress))
                    {
                        // * Process when cancel button is pressed
                        Debug.LogWarning("Prefab update process cancelled by user.");
                        // ClearProgressBar will be called in the finally block, so just return 0 here
                        return 0; // * Return 0 if cancelled
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

                    // * Check components in the prefab *
                    Component[] components = prefab.GetComponentsInChildren<Component>(true);
                    bool shouldReserialize = false; // Flag indicating whether to reserialize this prefab

                    foreach (Component component in components)
                    {
                        if (component == null) continue;

                        MonoBehaviour behaviour = component as MonoBehaviour;
                        // No need to check if behaviour is null here, IsComponentTypeMatch will handle it.
                        if (IsComponentTypeMatch(behaviour, baseTypesWithAttribute))
                        {
                            // If a script with attributes (or its child class) is found, this prefab is a reserialization target
                            shouldReserialize = true;
                            break; // This prefab is confirmed, no need to check other components
                        }
                    }

                    // *** If it\'s a reserialization target, add the path to the list ***
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
                // Debug.Log("No prefab assets needed reserialization."); // Removed as final dialog is sufficient
            }

            // *** Return the number of reserialized prefabs (size of the list) ***
            return prefabsToReserializePaths.Count;
        }

        /// <summary>
        /// Updates GameObjects in open scenes that use scripts with FormerlySerializedAs attributes,
        /// skipping any objects that are part of a prefab instance.
        /// Returns the number of scenes marked dirty.
        /// </summary>
        private static int UpdateSceneObjects(HashSet<Type> baseTypesWithAttribute)
        {
            // Argument check
            if (baseTypesWithAttribute == null || baseTypesWithAttribute.Count == 0)
            {
                return 0;
            }

            // * HashSet<string> scriptPathSet is no longer needed *
            // Keep paths of scripts with attributes in a HashSet
            // HashSet<string> scriptPathSet = new HashSet<string>(scriptsWithAttributePaths.Select(p => p.Replace("\\\\", "/")));
            int dirtySceneCount = 0;
            int totalScenes = EditorSceneManager.sceneCount; // * Get total number of scenes

            // * Adjust debug log message as well *
            // Debug.Log($"Checking GameObjects in {totalScenes} open scenes for components deriving from {baseTypesWithAttribute.Count} base types..."); // Removed for cleaner console

            // * Use try-finally to display progress bar *
            try
            {
                // * Initial progress bar display (non-cancelable)
                EditorUtility.DisplayProgressBar("Updating Scene Objects", "Starting scene scan...", 0f);

                // Loop through all open scenes
                for (int i = 0; i < totalScenes; i++) // * Change loop condition to totalScenes *
                {
                    Scene scene = EditorSceneManager.GetSceneAt(i);

                    // * Update progress bar (cancelable from here) *
                    string sceneName = string.IsNullOrEmpty(scene.name) ? $"Untitled Scene {i}" : scene.name;
                    string info = $"Scanning scene {i + 1}/{totalScenes}: {sceneName}";
                    float progress = (float)(i + 1) / totalScenes;
                    if (EditorUtility.DisplayCancelableProgressBar("Updating Scene Objects", info, progress))
                    {
                        // * Cancel process *
                        Debug.LogWarning("Scene object update process cancelled by user.");
                        return 0; // Return 0 if cancelled
                    }

                    // Check if scene is loaded and valid (isDirty check is unnecessary)
                    if (!scene.IsValid() || !scene.isLoaded /*|| scene.isDirty*/)
                    {
                        continue;
                    }

                    bool sceneNeedsMarkingDirty = false; // Flag indicating whether to mark this scene as dirty

                    // Get root GameObjects of the scene
                    GameObject[] rootObjects = scene.GetRootGameObjects();
                    foreach (GameObject rootObject in rootObjects)
                    {
                        // Get all MonoBehaviours in the root and its descendants
                        MonoBehaviour[] behaviours = rootObject.GetComponentsInChildren<MonoBehaviour>(true);
                        foreach (MonoBehaviour behaviour in behaviours)
                        {
                            // No need to check if behaviour is null here, IsComponentTypeMatch will handle it.

                            // *** Confirm that this GameObject is NOT part of a prefab ***
                            if (PrefabUtility.IsPartOfAnyPrefab(behaviour.gameObject))
                            {
                                continue; // Skip if it\'s part of a prefab
                            }

                            if (IsComponentTypeMatch(behaviour, baseTypesWithAttribute))
                            {
                                // Found! This scene needs to be marked dirty
                                sceneNeedsMarkingDirty = true;
                                break; // No need to check other components or descendants of this GameObject
                            }
                        } // End of loop for each MonoBehaviour

                        // If the dirty flag is set for this root object, end the search for the entire scene
                        if (sceneNeedsMarkingDirty)
                        {
                            break;
                        }
                    }

                    // If this scene needs to be marked dirty, do it
                    if (sceneNeedsMarkingDirty)
                    {
                        // MarkSceneDirty should be fine even if the scene is already dirty
                        EditorSceneManager.MarkSceneDirty(scene);
                        dirtySceneCount++;
                    }
                }
            }
            finally
            {
                // * Ensure progress bar is closed *
                EditorUtility.ClearProgressBar();
            }


            if (dirtySceneCount > 0)
            {
                // Debug.Log($"Marked {dirtySceneCount} scene(s) dirty."); // Removed as final dialog is sufficient
            }
            else
            {
                // Debug.Log("No GameObjects in open scenes found using the modified scripts (excluding prefab instances)."); // Removed as final dialog is sufficient
            }

            return dirtySceneCount;
        }

        /// <summary>
        /// Checks if the component\'s type or its base types match any of the types in the provided set.
        /// </summary>
        private static bool IsComponentTypeMatch(MonoBehaviour behaviour, HashSet<Type> baseTypesWithAttribute)
        {
            if (behaviour == null) return false;

            Type componentType = behaviour.GetType();
            foreach (Type baseType in baseTypesWithAttribute)
            {
                if (componentType == baseType || componentType.IsSubclassOf(baseType))
                {
                    return true;
                }
            }
            return false;
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
                    // Debug.Log($"Removed {removedCount} attributes from {Path.GetFileName(scriptPath)}"); // Removed as final dialog is sufficient
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