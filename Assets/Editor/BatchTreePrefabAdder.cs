using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class BatchTreePrefabAdder : EditorWindow
{
    private Terrain targetTerrain;
    private readonly List<GameObject> prefabs = new List<GameObject>();
    private Vector2 scroll;

    [MenuItem("Tools/Batch Tree Prefab Adder")]
    public static void Open()
    {
        GetWindow<BatchTreePrefabAdder>("Batch Tree Prefabs");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Batch Add Tree Prefabs", EditorStyles.boldLabel);
        targetTerrain = (Terrain)EditorGUILayout.ObjectField(
            "Target Terrain", targetTerrain, typeof(Terrain), true);

        if (targetTerrain == null && Selection.activeGameObject != null)
        {
            if (GUILayout.Button("Use Selected Terrain"))
                targetTerrain = Selection.activeGameObject.GetComponent<Terrain>();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Select multiple Prefab assets in the Project window and drag them into the box below. Only the tree prototype list will change; no trees will be placed.",
            MessageType.Info);

        Rect dropRect = GUILayoutUtility.GetRect(0, 75, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "Drop tree prefabs here (multi-select supported)");
        Event evt = Event.current;
        if (dropRect.Contains(evt.mousePosition) &&
            (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object obj in DragAndDrop.objectReferences)
                {
                    GameObject go = obj as GameObject;
                    if (go != null && EditorUtility.IsPersistent(go) && !prefabs.Contains(go))
                        prefabs.Add(go);
                }
            }
            evt.Use();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Selected prefabs: " + prefabs.Count);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(220));
        for (int i = prefabs.Count - 1; i >= 0; i--)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.ObjectField(prefabs[i], typeof(GameObject), false);
            if (GUILayout.Button("Remove", GUILayout.Width(70))) prefabs.RemoveAt(i);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear List")) prefabs.Clear();
        EditorGUI.BeginDisabledGroup(targetTerrain == null || prefabs.Count == 0);
        if (GUILayout.Button("Add to Terrain Tree List")) AddTrees();
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private void AddTrees()
    {
        if (targetTerrain == null || targetTerrain.terrainData == null)
        {
            EditorUtility.DisplayDialog("Missing Terrain Data", "Select a Terrain with valid Terrain Data.", "OK");
            return;
        }

        TerrainData data = targetTerrain.terrainData;
        List<TreePrototype> prototypes = new List<TreePrototype>(data.treePrototypes);
        HashSet<GameObject> existing = new HashSet<GameObject>();
        foreach (TreePrototype prototype in prototypes)
            if (prototype.prefab != null) existing.Add(prototype.prefab);

        int count = 0;
        foreach (GameObject prefab in prefabs)
        {
            if (prefab == null || existing.Contains(prefab)) continue;
            prototypes.Add(new TreePrototype { prefab = prefab, bendFactor = 0f });
            existing.Add(prefab);
            count++;
        }

        if (count == 0)
        {
            EditorUtility.DisplayDialog("No Changes", "All selected tree prefabs are already present.", "OK");
            return;
        }

        Undo.RegisterCompleteObjectUndo(data, "Batch Add Tree Prefabs");
        data.treePrototypes = prototypes.ToArray();
        data.RefreshPrototypes();
        EditorUtility.SetDirty(data);
        EditorUtility.DisplayDialog("Finished", "Added " + count + " tree prefabs to the Terrain tree list. No trees were placed.", "OK");
    }
}
