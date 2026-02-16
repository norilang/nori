#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Nori
{
    /// <summary>
    /// Handles drag-and-drop of .nori files onto the Hierarchy window.
    /// Creates a GameObject with UdonBehaviour pointing to the companion .asset.
    /// </summary>
    [InitializeOnLoad]
    public static class NoriDragHandler
    {
        // Cached reflection lookups
        private static Type _udonBehaviourType;
        private static Type _programSourceType;
        private static bool _cacheInitialized;

        static NoriDragHandler()
        {
            DragAndDrop.AddDropHandler(OnHierarchyDrop);
        }

        private static void EnsureCache()
        {
            if (_cacheInitialized) return;
            _cacheInitialized = true;

            _udonBehaviourType = Type.GetType(
                "VRC.Udon.UdonBehaviour, VRC.Udon");
            _programSourceType = Type.GetType(
                "VRC.Udon.AbstractUdonProgramSource, VRC.Udon");
        }

        private static DragAndDropVisualMode OnHierarchyDrop(
            int dropTargetInstanceID,
            HierarchyDropFlags dropMode,
            Transform parentForDraggedObjects,
            bool perform)
        {
            // Check if any dragged object is a .nori file
            var draggedObjects = DragAndDrop.objectReferences;
            if (draggedObjects == null || draggedObjects.Length == 0)
                return DragAndDropVisualMode.None;

            string noriPath = null;
            foreach (var obj in draggedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".nori"))
                {
                    noriPath = path;
                    break;
                }
            }

            if (noriPath == null)
                return DragAndDropVisualMode.None;

            if (!perform)
                return DragAndDropVisualMode.Copy;

            // Find the companion .asset
            string companionPath = NoriImporter.GetCompanionAssetPath(noriPath);
            EnsureCache();

            if (_udonBehaviourType == null || _programSourceType == null)
            {
                Debug.LogWarning("[Nori] VRChat SDK not found. Cannot create UdonBehaviour.");
                return DragAndDropVisualMode.Rejected;
            }

            var companionAsset = AssetDatabase.LoadAssetAtPath(companionPath, _programSourceType);
            if (companionAsset == null)
            {
                Debug.LogWarning($"[Nori] Companion asset not found at '{companionPath}'. " +
                                 "Try recompiling the .nori file first.");
                return DragAndDropVisualMode.Rejected;
            }

            // Determine target: existing GameObject or create new one
            GameObject targetGo = null;
            if (dropTargetInstanceID != 0)
            {
                targetGo = EditorUtility.InstanceIDToObject(dropTargetInstanceID) as GameObject;
            }

            if (targetGo == null)
            {
                string displayName = System.IO.Path.GetFileNameWithoutExtension(noriPath);
                targetGo = new GameObject(displayName);
                Undo.RegisterCreatedObjectUndo(targetGo, "Create Nori GameObject");

                if (parentForDraggedObjects != null)
                    targetGo.transform.SetParent(parentForDraggedObjects);
            }

            // Add UdonBehaviour component via reflection
            var udonBehaviour = Undo.AddComponent(targetGo, _udonBehaviourType);

            // Set programSource field via SerializedObject
            var so = new SerializedObject(udonBehaviour);
            var programSourceProp = so.FindProperty("programSource");
            if (programSourceProp != null)
            {
                programSourceProp.objectReferenceValue = companionAsset;
                so.ApplyModifiedProperties();
            }

            Selection.activeGameObject = targetGo;
            return DragAndDropVisualMode.Copy;
        }
    }
}
#endif
