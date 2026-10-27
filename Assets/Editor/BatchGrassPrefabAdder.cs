using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-adds mesh grass/detail prefabs to a Terrain's Paint Details list.
/// Does not paint or place any grass instances.
/// Place this file in Assets/Editor/.
/// </summary>
public class BatchGrassPrefabAdder : EditorWindow
{
    private Terrain targetTerrain;
    private readonly List<GameObject> prefabs = new List<GameObject>();
    private Vector2 scroll;

    [MenuItem("Tools/Batch Grass Prefab Adder")]
    private static void Open()
    {
        var window = GetWindow<BatchGrassPrefabAdder>("Batch Grass Adder");
        window.minSize = new Vector2(370, 320);
        window.UseSelectedTerrain();
    }

    private void UseSelectedTerrain()
    {
        GameObject active = Selection.activeGameObject;
        if (active != null)
            targetTerrain = active.GetComponent<Terrain>();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Batch Add Grass / Detail Meshes", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Adds prefab types to Terrain > Paint Details only. No grass is painted. Make sure your Terrain Data is an independent copy first.", MessageType.Info);
        targetTerrain = (Terrain)EditorGUILayout.ObjectField("Target Terrain", targetTerrain, typeof(Terrain), true);
        if (GUILayout.Button("Use Selected Terrain")) UseSelectedTerrain();

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Drag grass prefabs here (multiple selection supported):", EditorStyles.label);
        Rect area = GUILayoutUtility.GetRect(0, 65, GUILayout.ExpandWidth(true));
        GUI.Box(area, "Drop grass prefabs here");
        Event evt = Event.current;
        if (area.Contains(evt.mousePosition) && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object item in DragAndDrop.objectReferences)
                    AddCandidate(item);
            }
            evt.Use();
        }
        if (GUILayout.Button("Add Selected Project Prefabs"))
            foreach (Object item in Selection.objects) AddCandidate(item);

        GUILayout.Space(5);
        EditorGUILayout.LabelField("Queued prefabs: " + prefabs.Count);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(120));
        for (int i = prefabs.Count - 1; i >= 0; i--)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.ObjectField(prefabs[i], typeof(GameObject), false);
            if (GUILayout.Button("Remove", GUILayout.Width(65))) prefabs.RemoveAt(i);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button("Clear List")) prefabs.Clear();

        EditorGUI.BeginDisabledGroup(targetTerrain == null || prefabs.Count == 0);
        if (GUILayout.Button("Add to Terrain Detail List", GUILayout.Height(34))) AddAll();
        EditorGUI.EndDisabledGroup();
    }

    private void AddCandidate(Object obj)
    {
        GameObject go = obj as GameObject;
        if (go == null) return;
        // Only persistent project assets, not scene instances.
        if (!EditorUtility.IsPersistent(go)) return;
        if (go.GetComponentInChildren<MeshFilter>(true) == null ||
            go.GetComponentInChildren<MeshRenderer>(true) == null)
        {
            Debug.LogWarning("Skipped (no MeshFilter/MeshRenderer): " + go.name);
            return;
        }
        if (!prefabs.Contains(go)) prefabs.Add(go);
        Repaint();
    }

    private void AddAll()
    {
        if (targetTerrain == null || targetTerrain.terrainData == null)
        {
            EditorUtility.DisplayDialog("Missing Terrain Data", "Assign a Terrain with valid Terrain Data.", "OK");
            return;
        }

        TerrainData data = targetTerrain.terrainData;
        List<DetailPrototype> details = new List<DetailPrototype>(data.detailPrototypes);
        int added = 0, skipped = 0;
        foreach (GameObject prefab in prefabs)
        {
            if (prefab == null) { skipped++; continue; }
            bool exists = details.Exists(d => d.prototype == prefab);
            if (exists) { skipped++; continue; }

            DetailPrototype detail = new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                renderMode = DetailRenderMode.VertexLit,
                useInstancing = true,
                minWidth = 1f,
                maxWidth = 1f,
                minHeight = 1f,
                maxHeight = 1f,
                healthyColor = Color.white,
                dryColor = Color.white,
                noiseSpread = 0.1f
            };
            details.Add(detail);
            added++;
        }

        if (added > 0)
        {
            Undo.RegisterCompleteObjectUndo(data, "Batch Add Grass Detail Prefabs");
            data.detailPrototypes = details.ToArray();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }
        EditorUtility.DisplayDialog("Finished", "Added: " + added + "\nSkipped (duplicates/invalid): " + skipped + "\nNo grass was painted.", "OK");
    }
}
