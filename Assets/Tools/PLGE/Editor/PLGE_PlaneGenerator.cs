using System.Collections;
using System.Collections.Generic;
using System;

using UnityEngine;
using UnityEditor;

public class PLGE_PlaneGenerator : EditorWindow
{
    public int chunks = 1;

    public Vector2Int numberOfFaces = new Vector2Int(1, 1);

    // [Tooltip("How many faces you want in your mesh in addition to the dimensions. Range is 1 - 50")]
    public int meshResolution = 10;

    GameObject waterGroup;
    Material mat;

    [MenuItem("Tools/Plague/Square Plane")]
    public static void ShowWindow()
    {
        //Show existing window instance. If one doesn't exist, make one.
        EditorWindow.GetWindow(typeof(PLGE_PlaneGenerator));
    }
    
    private void OnGUI() 
    {
        // EditorGUILayout.HelpBox("How many chunks do you want the mesh to split into (1 means it is only the single mesh)", MessageType.Info);
        chunks = EditorGUILayout.IntField(new GUIContent("Chunks", null, "How many chunks do you want the mesh to split into (1 means it is only the single mesh)"), chunks);

        EditorGUILayout.Space();

        mat = (Material)EditorGUILayout.ObjectField(mat, typeof(Material), false);

        EditorGUILayout.Space();

        numberOfFaces = EditorGUILayout.Vector2IntField(new GUIContent("Number of Faces", null, "The Base Dimensions for the mesh"), numberOfFaces);

        EditorGUILayout.Space();

        meshResolution = EditorGUILayout.IntSlider("Mesh Resolution", meshResolution, 1, 50);

        GUILayout.FlexibleSpace();

        if(GUILayout.Button("Build Plane"))
        {
            InitChunks();
        }
    }
    
    void InitChunks()
    {
        DestroyImmediate(GameObject.Find("Plague Chunks"));

        waterGroup = new GameObject("Plague Chunks");

        Mesh chunkMesh;

        try
        {
            for (int x = 0; x < chunks; x++)
            {
                for (int z = 0; z < chunks; z++)
                {
                    GameObject chunk = new GameObject($"Chunk {(z + chunks * x)}");
                    chunk.transform.parent = waterGroup.transform;
                    chunk.transform.position = new Vector3(x * (numberOfFaces.x / chunks) * chunks, 0, z * (numberOfFaces.y / chunks) * chunks) - new Vector3(numberOfFaces.x / 2, 0, numberOfFaces.y / 2);

                    chunk.AddComponent<MeshRenderer>();
                    chunk.AddComponent<MeshFilter>();

                    try
                    {
                        chunk.GetComponent<MeshFilter>().mesh = chunkMesh = GenerateMesh();
                        chunkMesh.name = "Plague_Chunk";

                        chunk.GetComponent<MeshRenderer>().material = mat;
                    }
                    catch(Exception e)
                    {
                        Debug.LogWarning(e);
                    }
                }
            }
        }
        catch(Exception e)
        {
            Debug.LogWarning(e);
        }
    }

    public Mesh GenerateMesh()
    {
        Mesh mesh = new Mesh();

        Vector3[] vertices = new Vector3[(numberOfFaces.x + 1) * (numberOfFaces.y + 1) * (meshResolution * meshResolution) / chunks];
        Vector2[] uv = new Vector2[vertices.Length];
        Vector4[] tangents = new Vector4[vertices.Length];
        Vector4 tangent = new Vector4(1f, 0f, 0f, -1f);
        for (int i = 0, z = 0; z <= meshResolution; z++) 
        {
            for (int x = 0; x <= meshResolution; x++, i++) 
            {
                vertices[i] = (new Vector3(x * numberOfFaces.x / (meshResolution), 0, z * numberOfFaces.y / (meshResolution))) /* - new Vector3((numberOfFaces.x / 2), 0, (numberOfFaces.y / 2)) */;
                uv[i] = new Vector2((float)x, (float)z) / meshResolution;
                tangents[i] = tangent;
            }
        }
        
        int[] triangles = new int[(meshResolution * meshResolution * 6)];
        for (int ti = 0, vi = 0, y = 0; y < meshResolution; y++, vi++) 
        {
            for (int x = 0; x < meshResolution; x++, ti += 6, vi++) 
            {
                triangles[ti] = vi;
                triangles[ti + 3] = triangles[ti + 2] = vi + 1;
                triangles[ti + 4] = triangles[ti + 1] = vi + (meshResolution) + 1;
                triangles[ti + 5] = vi + (meshResolution) + 2;
            }
        }

        // Set the current mesh filter to use our generated mesh
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.tangents = tangents;
        mesh.RecalculateNormals();

        return mesh;
    }
}