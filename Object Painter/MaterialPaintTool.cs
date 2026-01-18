using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class MaterialPaintTool : EditorWindow
{
    private Material paintMaterial;
    private float brushSize = 1f;
    private float brushStrength = 1f;
    private bool isPainting = false;
    
    private GameObject lastHitObject;
    private Mesh workingMesh;
    private int[] originalTriangles;
    private int paintSubmeshIndex = 1;
    private HashSet<int> paintedTriangles = new HashSet<int>();

    [MenuItem("Tools/Material Paint Tool")]
    public static void ShowWindow()
    {
        GetWindow<MaterialPaintTool>("Material Paint");
    }

    void OnGUI()
    {
        GUILayout.Label("Material Paint Tool", EditorStyles.boldLabel);
        
        paintMaterial = (Material)EditorGUILayout.ObjectField("Paint Material", paintMaterial, typeof(Material), false);
        brushSize = EditorGUILayout.Slider("Brush Size", brushSize, 0.1f, 5f);
        brushStrength = EditorGUILayout.Slider("Brush Strength", brushStrength, 0.1f, 1f);
        
        if (GUILayout.Button("Clear Paint"))
        {
            ClearPaint();
        }
        
        GUILayout.Space(10);
        GUILayout.Label($"Painted Triangles: {paintedTriangles.Count}", EditorStyles.miniLabel);
    }

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (paintMaterial == null) return;

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        
        Event e = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            Handles.color = new Color(1, 0, 0, 0.5f);
            Handles.DrawWireDisc(hit.point, hit.normal, brushSize);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                isPainting = true;
                InitializeMesh(hit.collider.gameObject);
            }

            if (isPainting && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown) && e.button == 0)
            {
                PaintAtPosition(hit);
                e.Use();
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                isPainting = false;
            }

            sceneView.Repaint();
        }
    }

    void InitializeMesh(GameObject obj)
    {
        if (lastHitObject != obj)
        {
            lastHitObject = obj;
            MeshFilter mf = obj.GetComponent<MeshFilter>();
            MeshRenderer mr = obj.GetComponent<MeshRenderer>();
            
            if (mf != null && mr != null)
            {
                Mesh sourceMesh = mf.sharedMesh;
                workingMesh = Instantiate(sourceMesh);
                workingMesh.name = sourceMesh.name + "_Painted";
                
                string path = "Assets/PaintedMeshes";
                if (!AssetDatabase.IsValidFolder(path))
                    AssetDatabase.CreateFolder("Assets", "PaintedMeshes");
                
                string meshPath = $"{path}/{obj.name}_painted.asset";
                AssetDatabase.CreateAsset(workingMesh, meshPath);
                
                mf.sharedMesh = workingMesh;
                
                Material[] mats = mr.sharedMaterials;
                if (mats.Length == 1)
                {
                    Material[] newMats = new Material[2];
                    newMats[0] = mats[0];
                    newMats[1] = paintMaterial;
                    mr.sharedMaterials = newMats;
                    
                    workingMesh.subMeshCount = 2;
                    originalTriangles = workingMesh.GetTriangles(0);
                    workingMesh.SetTriangles(originalTriangles, 0);
                    workingMesh.SetTriangles(new int[0], 1);
                    
                    paintedTriangles.Clear();
                }
                else if (mats.Length >= 2)
                {
                    originalTriangles = workingMesh.GetTriangles(0);
                    int[] currentPainted = workingMesh.GetTriangles(1);
                    
                    paintedTriangles.Clear();
                    for (int i = 0; i < currentPainted.Length; i += 3)
                    {
                        for (int j = 0; j < originalTriangles.Length; j += 3)
                        {
                            if (originalTriangles[j] == currentPainted[i] &&
                                originalTriangles[j + 1] == currentPainted[i + 1] &&
                                originalTriangles[j + 2] == currentPainted[i + 2])
                            {
                                paintedTriangles.Add(j / 3);
                                break;
                            }
                        }
                    }
                }
                
                EditorUtility.SetDirty(workingMesh);
                EditorUtility.SetDirty(obj);
            }
        }
    }

    void PaintAtPosition(RaycastHit hit)
    {
        if (workingMesh == null) return;

        Vector3[] vertices = workingMesh.vertices;
        Transform objTransform = lastHitObject.transform;
        
        List<int> trianglesToPaint = new List<int>();
        
        for (int i = 0; i < originalTriangles.Length; i += 3)
        {
            int idx0 = originalTriangles[i];
            int idx1 = originalTriangles[i + 1];
            int idx2 = originalTriangles[i + 2];
            
            Vector3 v0 = objTransform.TransformPoint(vertices[idx0]);
            Vector3 v1 = objTransform.TransformPoint(vertices[idx1]);
            Vector3 v2 = objTransform.TransformPoint(vertices[idx2]);
            
            Vector3 center = (v0 + v1 + v2) / 3f;
            float distance = Vector3.Distance(center, hit.point);
            
            if (distance < brushSize)
            {
                int triIndex = i / 3;
                if (!paintedTriangles.Contains(triIndex))
                {
                    trianglesToPaint.Add(idx0);
                    trianglesToPaint.Add(idx1);
                    trianglesToPaint.Add(idx2);
                    paintedTriangles.Add(triIndex);
                }
            }
        }
        
        if (trianglesToPaint.Count > 0)
        {
            List<int> currentPaintTris = new List<int>(workingMesh.GetTriangles(1));
            currentPaintTris.AddRange(trianglesToPaint);
            workingMesh.SetTriangles(currentPaintTris.ToArray(), 1);
            
            List<int> baseTris = new List<int>();
            for (int i = 0; i < originalTriangles.Length; i += 3)
            {
                int triIndex = i / 3;
                if (!paintedTriangles.Contains(triIndex))
                {
                    baseTris.Add(originalTriangles[i]);
                    baseTris.Add(originalTriangles[i + 1]);
                    baseTris.Add(originalTriangles[i + 2]);
                }
            }
            workingMesh.SetTriangles(baseTris.ToArray(), 0);
            
            EditorUtility.SetDirty(workingMesh);
            EditorUtility.SetDirty(lastHitObject);
        }
    }

    void ClearPaint()
    {
        if (workingMesh != null && lastHitObject != null)
        {
            workingMesh.SetTriangles(originalTriangles, 0);
            workingMesh.SetTriangles(new int[0], 1);
            paintedTriangles.Clear();
            
            MeshRenderer mr = lastHitObject.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterials.Length > 1)
            {
                Material[] newMats = new Material[1];
                newMats[0] = mr.sharedMaterials[0];
                mr.sharedMaterials = newMats;
                workingMesh.subMeshCount = 1;
            }
            
            EditorUtility.SetDirty(workingMesh);
            EditorUtility.SetDirty(lastHitObject);
        }
    }
}