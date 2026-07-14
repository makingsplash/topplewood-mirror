using UnityEngine;
using UnityEngine.Rendering;

namespace SGS.Infrastructure.Tools.ProceduralMeshes
{
    public class GridGenerator
    {
        public static GameObject GenerateProceduralGrid(Transform parent, float sizeX, float sizeZ, float resolution)
        {
            GameObject grid = new GameObject("Grid");
            MeshFilter filter = grid.AddComponent<MeshFilter>();
            
            grid.AddComponent<MeshRenderer>();
            
            Mesh mesh = new Mesh();
            mesh.name = "Grid";
            mesh.indexFormat = IndexFormat.UInt32;
            int resolutionX = Mathf.CeilToInt(sizeX / resolution);
            int resolutionZ = Mathf.CeilToInt(sizeZ / resolution);
            int verticesCountX = resolutionX + 1;
            int verticesCountZ = resolutionZ + 1;

            Vector3[] vertices = new Vector3[verticesCountX * verticesCountZ];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[6 * resolutionX * resolutionZ];
            
            float stepX = sizeX / resolutionX;
            float stepZ = sizeZ / resolutionZ;

            int index = 0;
            for (int z = 0; z <= resolutionZ; z++)
            {
                for (int x = 0; x <= resolutionX; x++)
                {
                    float posX = x * stepX - sizeX / 2f;
                    float posZ = z * stepZ - sizeZ / 2f;
                    
                    vertices[index] = new Vector3(posX, 0f, posZ);
                    uv[index] = new Vector2(x / (float)resolutionX, z / (float)resolutionZ);
                    
                    index++;
                }
            }
            
            int triangleIndex = 0;
            
            for (int z = 0; z < resolutionZ; z++)
            {
                for (int x = 0; x < resolutionX; x++)
                {
                    int vertexIndex = x + z * verticesCountX;
                    
                    triangles[triangleIndex + 0] = vertexIndex;
                    triangles[triangleIndex + 1] = vertexIndex + verticesCountX;
                    triangles[triangleIndex + 2] = vertexIndex + 1;
                    
                    triangles[triangleIndex + 3] = vertexIndex + 1;
                    triangles[triangleIndex + 4] = vertexIndex + verticesCountX;
                    triangles[triangleIndex + 5] = vertexIndex + verticesCountX + 1;
                    
                    triangleIndex += 6;
                }
            }
            
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            filter.mesh = mesh;
            grid.transform.parent = parent;

            return grid;
        }
    }
}