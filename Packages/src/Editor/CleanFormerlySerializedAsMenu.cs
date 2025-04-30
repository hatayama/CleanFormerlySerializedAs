using UnityEngine;
using UnityEditor;
using System.IO;

namespace io.github.hatayama
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
            // Enable if a file or directory exists.
            return (File.Exists(path) && Path.GetExtension(path).ToLower() == ".cs") || Directory.Exists(path);
        }

        [MenuItem(MenuItemPath, false, 1000)]
        private static void ProcessSelectedScript()
        {
            bool userConfirmed = EditorUtility.DisplayDialog(
                "Confirmation", // Dialog title
                "This will remove FormerlySerializedAs attributes.\nIt's recommended to back up your project (e.g., using git) just in case.",
                "OK", 
                "Cancel"
            );

            // If cancel is pressed, do nothing and exit.
            if (!userConfirmed)
            {
                return;
            }

            // --- Original process below ---
            string[] guids = Selection.assetGUIDs;
            if (guids.Length != 1) return; // Already checked in ValidateMenuItem, but just in case.

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            int totalRemovedCount = 0;
            int processedFileCount = 0;

            if (File.Exists(path) && Path.GetExtension(path).ToLower() == ".cs")
            {
                totalRemovedCount = ProcessScript(path);
                processedFileCount = 1;
            }
            else if (Directory.Exists(path))
            {
                // If a directory is selected, recursively search for .cs files within it.
                string[] csFiles = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);
                foreach (string csFile in csFiles)
                {
                    // Execute the process for each file and add the number of deletions.
                    totalRemovedCount += ProcessScript(csFile);
                    processedFileCount++;
                }
            }
            else
            {
                // Basically, it shouldn't come here.
                Debug.LogError("Selected asset is not a C# script file or directory.");
                return;
            }

            // Perform post-processing and display results only if some files have been processed.
            if (processedFileCount > 0)
            {
                 // Refresh AssetDatabase after file changes.
                 AssetDatabase.Refresh();
                 // Update related Prefabs.
                 UpdateRelatedPrefabs();

                 // Display the result dialog.
                 if (totalRemovedCount > 0)
                 {
                     EditorUtility.DisplayDialog("Clean FormerlySerializedAs",
                         $"Processed {processedFileCount} script(s). \nSuccessfully removed {totalRemovedCount} FormerlySerializedAs attributes.",
                         "OK");
                 }
                 else // If attributes were not found.
                 {
                     EditorUtility.DisplayDialog("Clean FormerlySerializedAs",
                         $"Processed {processedFileCount} script(s). \nNo FormerlySerializedAs attributes found.",
                         "OK");
                 }
            }
            else if (Directory.Exists(path)) // If no cs files were found when a directory was selected.
            {
                // Display a dialog indicating that no C# scripts were found.
                EditorUtility.DisplayDialog("Clean FormerlySerializedAs",
                    "No C# scripts found in the selected directory or its subdirectories.",
                    "OK");
            }
        }

        /// <summary>
        /// Processes the specified C# script file and returns the number of FormerlySerializedAs attributes removed.
        /// </summary>
        /// <param name="scriptPath">The path of the script file to process.</param>
        /// <returns>The number of FormerlySerializedAs attributes removed.</returns>
        private static int ProcessScript(string scriptPath)
        {
            var cleaner = new FormerlySerializedAsRemover(); // Uses the other class
            string content = File.ReadAllText(scriptPath);

            var (processedContent, removedCount) = cleaner.RemoveFormerlySerializedAs(content);

            if (removedCount == 0)
            {
                // Even if attributes were not found, the file was processed, so return 0.
                return 0;
            }

            // Write the file only if there were changes.
            System.IO.File.WriteAllText(scriptPath, processedContent);

            // Return the number of removed attributes.
            return removedCount;
        }

        /// <summary>
        /// Updates Prefabs that might be related to scripts where FormerlySerializedAs attributes were removed.
        /// </summary>
        private static void UpdateRelatedPrefabs()
        {
            string[] allPrefabs = AssetDatabase.FindAssets("t:Prefab");

            foreach (string prefabGuid in allPrefabs)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (prefab != null)
                {
                    EditorUtility.SetDirty(prefab);
                }
            }

            AssetDatabase.SaveAssets();
        }
    }
} 