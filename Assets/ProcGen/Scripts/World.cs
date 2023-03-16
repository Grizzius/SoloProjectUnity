using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEditor.FilePathAttribute;
using Random = UnityEngine.Random;

public class World : MonoBehaviour
{
    public int chunkSize = 16;
    public float caveNoiseScale = 0.3f;
    public float noiseThreshold = 0.35f;
    public GameObject chunkPrefab;
    public int noiseOffset;
    public int worldHeight;
    Vector3Int offset = new Vector3Int();

    public int viewDistanceInChunk = 6;

    public GameObject player;

    Transform playerTransform;

    public Vector3Int mapSize = new Vector3Int();

    public int generatedMapSize = 4;

    Dictionary<Vector3Int, float> fallOffMap = new Dictionary<Vector3Int, float>();

    Direction[] directions =
    {
        Direction.up,
        Direction.down,
        Direction.left,
        Direction.right,
        Direction.forward,
        Direction.backward
    };

    bool finishedWorldGeneration = false;
    

    Dictionary<Vector3Int, ChunkData> chunkDataDictionnary = new Dictionary<Vector3Int, ChunkData>();
    Dictionary<Vector3Int, ChunkRenderer> chunkDictionnary = new Dictionary<Vector3Int, ChunkRenderer>();

    public void Start()
    {
        offset.x = Random.Range(-1000, 1000);
        offset.y = Random.Range(-1000, 1000);
        offset.z = Random.Range(-1000, 1000);

        StartCoroutine(GenerateWorld(new Vector3Int(0,0,0), 4));
    }

    public void Update()
    {
        if (finishedWorldGeneration && playerTransform != null)
        {
            Vector3Int location = new Vector3Int(Mathf.RoundToInt(playerTransform.position.x / chunkSize), Mathf.RoundToInt(playerTransform.position.y / chunkSize), Mathf.RoundToInt(playerTransform.position.z / chunkSize));

            StartCoroutine(GenerateNewChunk(new Vector3Int(location.x - viewDistanceInChunk / 2, location.y - viewDistanceInChunk / 2, location.z - viewDistanceInChunk / 2), viewDistanceInChunk));
        }
    }
    IEnumerator GenerateWorld(Vector3Int centerPoint, int size)
    {
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                {
                    Vector3Int startPoint = new Vector3Int(centerPoint.x + x, centerPoint.y + y, centerPoint.z + z) * chunkSize;

                    ChunkData data = new ChunkData(chunkSize, this, startPoint);

                    chunkDataDictionnary.Add(data.worldPosition, data);
                }
            }
        }

        foreach (ChunkData data in chunkDataDictionnary.Values)
        {
            if (!chunkDictionnary.ContainsKey(data.worldPosition))
            {
                GenerateVoxel(data);
                yield return new WaitForFixedUpdate();
                BuildChunk(data);
            }
        }
        finishedWorldGeneration = true;
        print("finished generating world");
        SpawnPlayer();
    }

    IEnumerator GenerateNewChunk(Vector3Int centerPoint, int size)
    {
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                {
                    Vector3Int startPoint = new Vector3Int(centerPoint.x + x, centerPoint.y + y, centerPoint.z + z) * chunkSize;

                    if (!chunkDataDictionnary.ContainsKey(startPoint))
                    {
                        ChunkData data = new ChunkData(chunkSize, this, startPoint);

                        //GenerateVoxel(data);
                        chunkDataDictionnary.Add(data.worldPosition, data);
                    }
                }
            }
        }

        Dictionary<Vector3Int, ChunkData> ChunksToBuild = new Dictionary<Vector3Int, ChunkData>();
        Dictionary<Vector3Int, float> chunkDistancesFromPlayer = new Dictionary<Vector3Int, float>();

        bool foundChunkToBuild = false;

        float closestChunkDistance = 9999999f;
        Vector3Int chunkToBuildKey = new Vector3Int();
        

        foreach (ChunkData data in chunkDataDictionnary.Values)
        {
            if (!chunkDictionnary.ContainsKey(data.worldPosition))
            {
                ChunksToBuild.Add(data.worldPosition, data);

                float distance = Vector3.Distance(playerTransform.position, data.worldPosition);

                if(distance < closestChunkDistance)
                {
                    closestChunkDistance = distance;
                    chunkToBuildKey = data.worldPosition;
                    foundChunkToBuild = true;
                }
            }
        }

        if (foundChunkToBuild)
        {
            GenerateVoxel(ChunksToBuild[chunkToBuildKey]);
            yield return new WaitForFixedUpdate();
            BuildChunk(ChunksToBuild[chunkToBuildKey]);
        }
        
    }

    void BuildChunk(ChunkData data)
    {
        MeshData meshData = Chunk.GetChunkMeshData(data);
        GameObject chunkObject = Instantiate(chunkPrefab, data.worldPosition, Quaternion.identity);
        ChunkRenderer chunkRenderer = chunkObject.GetComponent<ChunkRenderer>();
        if (!chunkDictionnary.ContainsKey(data.worldPosition))
        {
            chunkDictionnary.Add(data.worldPosition, chunkRenderer);
        }

        chunkRenderer.InitializeChunk(data);
        chunkRenderer.UpdateChunk(meshData);

        foreach(Direction direction in directions)
        {
            var neightbouringChunkCoordinate = data.worldPosition + direction.GetVector() * chunkSize;
            if (chunkDictionnary.ContainsKey(neightbouringChunkCoordinate))
            {
                var neightbourChunkRenderer = chunkDictionnary[neightbouringChunkCoordinate];
                neightbourChunkRenderer.UpdateChunk();
            }
            
        }
    }

    internal BlockType GetBlockFromChunkCoordinates(ChunkData chunkdata, int x, int y, int z)
    {
        Vector3Int pos = Chunk.ChunkPositionFromBlockCoords(this, x, y, z);
        ChunkData containerChunk = null;

        chunkDataDictionnary.TryGetValue(pos, out containerChunk);

        if (containerChunk == null)
        {
            return BlockType.none;
        }

        Vector3Int blockInChunkCoordinate = Chunk.GetBlockInChunkCoordinate(containerChunk, new Vector3Int(x, y, z));

        return Chunk.GetBlockFromChunkCoordinate(containerChunk, blockInChunkCoordinate);
    }

    private void GenerateVoxel(ChunkData chunkData)
    {
        for (int x = 0; x < chunkData.chunkSize; x++)
        {
            for (int z = 0; z < chunkData.chunkSize; z++)
            {
                for (int y = 0; y < chunkData.chunkSize; y++)
                {
                    BlockType voxelType = BlockType.dirt;

                    //Add Caves
                    float caveNoiseValue = PerlinNoise3D.Perlin3D((chunkData.worldPosition.x + x + offset.x) * caveNoiseScale, (chunkData.worldPosition.y + y + offset.y) * caveNoiseScale, (chunkData.worldPosition.z + z + offset.z) * caveNoiseScale, new Vector3(1, 1, 1) * noiseOffset);

                    float fallOffValue = FallOffMapValue(chunkData.worldPosition.x + x, chunkData.worldPosition.y + y, chunkData.worldPosition.z + z, mapSize);
                    //caveNoiseValue +=  fallOffValue;

                    int currentHeight = chunkData.worldPosition.y + y;

                    if (Mathf.Abs(caveNoiseValue) < noiseThreshold)
                    {
                        voxelType = BlockType.dirt;
                    }
                    else
                    {
                        voxelType = BlockType.air;
                    }



                    Chunk.SetBlock(chunkData, new Vector3Int(x,y,z), voxelType);
                }
            }
        }
    }

    private float FallOffMapValue(int i, int j, int k, Vector3Int size)
    {
        float x = i / (float)size.x / 2 + 1;
        float y = j / (float)size.y / 2 + 1;
        float z = k / (float)size.z / 2 + 1;

        return Evaluate(Mathf.Max(Mathf.Abs(x), Mathf.Abs(y), Mathf.Abs(z)));
    }

    private float Evaluate(float value)
    {
        float a = 3f;
        float b = 2.2f;

        return Mathf.Pow(value, a) / (Mathf.Pow(value, a) + Mathf.Pow(b - b * value, a));
    }

    private Vector3Int FindSpawnChunk()
    {
        Vector3Int spawnCoordinates = new Vector3Int();

        //Pick random generated chunk
        for(int i = 0; i < chunkDataDictionnary.Count; i++)
        {
            ChunkData data = chunkDataDictionnary.ElementAt(Random.Range(0, chunkDataDictionnary.Count)).Value;

            //Find air block
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        BlockType block = Chunk.GetBlockFromChunkCoordinate(data, new Vector3Int(x, y, z));

                        if (block == BlockType.air && Chunk.GetBlockFromChunkCoordinate(data, new Vector3Int(x, y + 1, z)) == BlockType.air && Chunk.GetBlockFromChunkCoordinate(data, new Vector3Int(x, y + 2, z)) == BlockType.air)
                        {
                            spawnCoordinates = new Vector3Int(x, y + 1, z) + data.worldPosition;
                            print("found spawn point");
                            return spawnCoordinates;
                        }
                    }
                }
            }
            print("checked chunk");
        }
        return spawnCoordinates;
    }

    private void SpawnPlayer()
    {
        Vector3Int spawnLocation = FindSpawnChunk();
        print("Spawning player at location " + spawnLocation);
        GameObject spawnedPlayer = Instantiate(player, spawnLocation, Quaternion.identity);
        playerTransform = spawnedPlayer.transform;
    }
}

[Serializable]
public struct noiseLayer
{
    public float noiseScale;
    public float noiseIntensity;
    public float heightOffset;
}