using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndlessTerrain : MonoBehaviour {

    const float viewerMoveThresholdForChunkUpdate = 25f;
    const float sqrViewerMoveThresholdForChunkUpdate = viewerMoveThresholdForChunkUpdate * viewerMoveThresholdForChunkUpdate;
    const float colliderGenerationDistanceThreshold = 5;

    public int colliderLODIndex;
    public LODInfo[] detailLevels;
    public static float maxViewDst;

    public Transform viewer;
    public Material mapMaterial;

    public static Vector2 viewerPositon;
    Vector2 viewerPositionOld;
    static MapGenerator mapGenrator;
    int chunkSize;
    int chunksVisibleInViewDst;

    Dictionary<Vector2, TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();
    static List<TerrainChunk> terrainChunkVisibleLastUpdate = new List<TerrainChunk>();

    void Start()
    {
        mapGenrator = FindObjectOfType<MapGenerator>();

        maxViewDst = detailLevels[detailLevels.Length - 1].visibleDstThreshold;
        chunkSize = mapGenrator.mapChunkSize - 1;
        chunksVisibleInViewDst = Mathf.RoundToInt(maxViewDst / chunkSize);

        UpdateVisibleChunks();
    }

    private void Update()
    {
        viewerPositon = new Vector2(viewer.position.x, viewer.position.z) / mapGenrator.terrainData.uniformScale;

        if (viewerPositon != viewerPositionOld) {
            foreach(TerrainChunk chunk in terrainChunkVisibleLastUpdate) {
                chunk.UpdateCollisionMesh();
            }
        }


        if ((viewerPositionOld - viewerPositon).sqrMagnitude > sqrViewerMoveThresholdForChunkUpdate) {
            viewerPositionOld = viewerPositon;
            UpdateVisibleChunks();
        }
    }

    void UpdateVisibleChunks()
    {
        for (int i = 0; i <terrainChunkVisibleLastUpdate.Count; i++)
        {
            terrainChunkVisibleLastUpdate[i].SetVisible(false);
        }
        terrainChunkVisibleLastUpdate.Clear();

        int currentChunkCoordX = Mathf.RoundToInt(viewerPositon.x / chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPositon.y / chunkSize);

        for (int yOffset = -chunksVisibleInViewDst; yOffset <= chunksVisibleInViewDst; yOffset++)
        {
            for(int xOffset = -chunksVisibleInViewDst; xOffset <= chunksVisibleInViewDst; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);

                if (terrainChunkDictionary.ContainsKey(viewedChunkCoord)) {
                    terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
                } else {
                    terrainChunkDictionary.Add(viewedChunkCoord, new TerrainChunk(viewedChunkCoord, chunkSize, detailLevels, colliderLODIndex, transform, mapMaterial));
                }
            }
        }
    }

    public class TerrainChunk
    {
        GameObject meshObject;
        Vector2 position;
        Bounds bounds;
        
        MeshRenderer meshRenderer;
        MeshFilter meshFilter;
        MeshCollider meshCollider;

        LODInfo[] detailLevels;
        LODMesh[] lodMeshes;
        int colliderLODIndex;

        MapData mapData;
        bool mapDataRecived;
        int previousLODIndex = -1;
        bool hasSetCollider;

        public TerrainChunk(Vector2 coord, int size, LODInfo[] detailLevels, int colliderLODIndex,Transform parent, Material material)
        {
            this.detailLevels = detailLevels;
            this.colliderLODIndex = colliderLODIndex;

            position = coord * size;
            bounds = new Bounds(position, Vector2.one * size);
            Vector3 positionV3 = new Vector3(position.x, 0, position.y);

            meshObject = new GameObject("Terrain Chunk");
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshFilter = meshObject.AddComponent<MeshFilter>();
            meshCollider = meshObject.AddComponent<MeshCollider>();
            meshRenderer.material = material;

            meshObject.transform.position = positionV3 * mapGenrator.terrainData.uniformScale;
            meshObject.transform.parent = parent;
            meshObject.transform.localScale = Vector3.one * mapGenrator.terrainData.uniformScale;

            SetVisible(false);

            lodMeshes = new LODMesh[detailLevels.Length];
            for(int i = 0; i < detailLevels.Length; i++) {
                lodMeshes[i] = new LODMesh(detailLevels[i].lod);
                lodMeshes[i].updateCallback += UpdateTerrainChunk;
                if (i == colliderLODIndex) {
                    lodMeshes[i].updateCallback += UpdateCollisionMesh;
                }
            }

            mapGenrator.RequestMapData(position, OnMapDataRecived);
        }

        void OnMapDataRecived(MapData mapData) {
            this.mapData = mapData;
            mapDataRecived = true;

            UpdateTerrainChunk();
        }

        public void UpdateTerrainChunk() {
            if (mapDataRecived) {
                float viewerDstFromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(viewerPositon));
                bool visible = viewerDstFromNearestEdge <= maxViewDst;

                if (visible) {
                    int lodIndex = 0;

                    for (int i = 0; i < detailLevels.Length - 1; i++) {
                        if (viewerDstFromNearestEdge > detailLevels[i].visibleDstThreshold) {
                            lodIndex = i + 1;
                        } else {
                            break;
                        }
                    }

                    if (lodIndex != previousLODIndex) {
                        LODMesh lodMesh = lodMeshes[lodIndex];
                        if (lodMesh.hasMesh) {
                            previousLODIndex = lodIndex;
                            meshFilter.mesh = lodMesh.mesh;
                            //meshCollider.sharedMesh = collisionLODMesh.mesh;
                        } else if (!lodMesh.hasRequestMesh) {
                            lodMesh.RequestMesh(mapData);
                        }
                    }

                    terrainChunkVisibleLastUpdate.Add(this);
                }

                SetVisible(visible);
            } 
        }

        public void UpdateCollisionMesh() {
            if(!hasSetCollider) {
                float sqrDstFromViewerToEdge = bounds.SqrDistance(viewerPositon);

                if (sqrDstFromViewerToEdge < detailLevels[colliderLODIndex].sqrVisibleDstThreshold) {
                    if (!lodMeshes[colliderLODIndex].hasRequestMesh) {
                        lodMeshes[colliderLODIndex].RequestMesh(mapData);
                    }
                }

                if (sqrDstFromViewerToEdge < colliderGenerationDistanceThreshold * colliderGenerationDistanceThreshold) {
                    if (lodMeshes[colliderLODIndex].hasMesh) {
                        meshCollider.sharedMesh = lodMeshes[colliderLODIndex].mesh;
                        hasSetCollider = true;
                    }
                }
            }
        }

        public void SetVisible(bool visible)
        {
            meshObject.SetActive(visible);
        }

        public bool IsVisible()
        {
            return meshObject.activeSelf;
        }

    }

    class LODMesh {

        public Mesh mesh;
        public bool hasRequestMesh;
        public bool hasMesh;
        int lod;
        public event System.Action updateCallback;

        public LODMesh(int lod) {
            this.lod = lod;
        }

        void OnMeshDataRecived(MeshData meshData) {
            mesh = meshData.CreateMesh();
            hasMesh = true;

            updateCallback();
        }

        public void RequestMesh(MapData mapData) {
            hasRequestMesh = true;
            mapGenrator.RequestMeshData(mapData, lod, OnMeshDataRecived);
        }
    }

    [System.Serializable]
    public struct LODInfo {
        [Range(0,MeshGenerator.numSupportedLODs-1)]
        public int lod;
        public float visibleDstThreshold;
        public bool useForCollider;

        public float sqrVisibleDstThreshold {
            get {
                return visibleDstThreshold * visibleDstThreshold;
            }
        }
    }
}
