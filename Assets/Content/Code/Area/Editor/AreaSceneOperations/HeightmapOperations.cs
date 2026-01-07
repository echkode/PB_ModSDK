using System;
using System.Collections.Generic;

using UnityEngine;

namespace Area
{
    public static class Heightmap
    {
        public sealed class Spec
        {
            public AreaManager AreaManager;
            public int SizeX;
            public int SizeZ;
            public int MaxDepth;
            public int[,] Heightfield;
            public List<AreaVolumePoint> Points;
            public Vector3Int Bounds;
            public StoreHeightmapInfo Store;
        }

        public delegate void StoreHeightmapInfo (AreaManager am, int x, int z, int depthProcessed, byte slopeInfo, byte roadInfo);
        public delegate void ProcessDepthValues (Spec spec);

        public const string standardHeightmapFileName = "heightmap.png";

        public static void CalculateStandardHeightmapValues (Spec spec)
        {
            for (var x = 0; x < spec.SizeX; x += 1)
            {
                for (var z = 0; z < spec.SizeZ; z += 1)
                {
                    var depth = heightfield[x, z];
                    var sizeY = spec.AreaManager.boundsFull.y;
                    var slopeInfo = byte.MinValue;
                    var roadInfo = byte.MinValue;

                    // Try to hit a surface point
                    var posIndex = new Vector3Int (x, 0, z);
                    var pointIndex = AreaUtility.GetIndexFromVolumePosition (posIndex, spec.AreaManager.boundsFull, skipBoundsCheck: true);
                    var pointCurrent = spec.AreaManager.points[pointIndex];
                    for (var iteration = 0; iteration <= sizeY; iteration += 1)
                    {
                        if (pointCurrent == null)
                        {
                            break;
                        }

                        var pointStateCurrent = pointCurrent.pointState;
                        if (pointStateCurrent == AreaVolumePointState.Full)
                        {
                            var pointAboveStartEmpty = pointCurrent.pointsWithSurroundingSpots[3];
                            if (pointAboveStartEmpty == null)
                            {
                                continue;
                            }
                            if (pointAboveStartEmpty.terrainOffset.RoughlyEqual (terrainOffsetTopRamp))
                            {
                                slopeInfo = byte.MaxValue;
                            }
                            if (pointCurrent.road)
                            {
                                roadInfo = byte.MaxValue;
                            }
                            break;
                        }

                        // Get point below
                        pointCurrent = pointCurrent.pointsInSpot[4];
                    }
                    spec.Store (spec.AreaManager, x, z, depth, slopeInfo, roadInfo);
                }
            }
        }

        public static void Create (AreaManager am, ProcessDepthValues processDepthValues, string filePath)
        {
            var sizeX = am.boundsFull.x;
            var sizeZ = am.boundsFull.z;
            if (heightfield == null || (heightfield.GetLength (0) != sizeX && heightfield.GetLength (1) != sizeZ))
            {
                heightfield = new int[sizeX, sizeZ];
            }

            ProceduralMeshUtilities.CollectSurfacePoints (am, heightfield);

            am.heightfieldPalette.Clear ();
            colorValues.Clear ();

            colorArray = new Color32[heightfield.Length];

            var sizeY = am.boundsFull.y;
            var maxDepth = Mathf.Min (sizeY - 1, byte.MaxValue);
            maxDepthScaled = maxDepth * 10;

            // Points on north (sizeZ - 1) and east (sizeX - 1) borders don't have spots so exclude them.
            var spec = new Spec ()
            {
                AreaManager = am,
                SizeX = sizeX - 1,
                SizeZ = sizeZ - 1,
                MaxDepth = maxDepth,
                Heightfield = heightfield,
                Points = am.points,
                Bounds = am.boundsFull,
                Store = StoreHeightmapInfoInternal,
            };
            processDepthValues (spec);
            am.heightfieldPalette.Sort ((x, y) => x.depth.CompareTo (y.depth));

            var zMax = sizeZ - 1;
            for (var x = 0; x < sizeX; x += 1)
            {
                var index = zMax * am.boundsFull.z + x;
                colorArray[index] = Color.black;
            }
            var xMax = sizeX - 1;
            for (var z = 0; z < sizeZ; z += 1)
            {
                var index = z * am.boundsFull.z + xMax;
                colorArray[index] = Color.black;
            }

            textureDepthmap = new Texture2D (sizeX, sizeZ, TextureFormat.RGB24, false);
            textureDepthmap.name = am.areaName + "_heightmap";
            textureDepthmap.SetPixels32 (colorArray);
            textureDepthmap.Apply ();
            textureDepthmap.filterMode = FilterMode.Point;
            textureDepthmap.wrapMode = TextureWrapMode.Clamp;

            try
            {
                var png = textureDepthmap.EncodeToPNG ();
                System.IO.File.WriteAllBytes (filePath, png);
            }
            catch (Exception e)
            {
                Debug.LogWarningFormat ("Area manager | Encountered an exception while saving heightmap {0}\n{1}", filePath, e);
            }
        }

        public static void ImportHeightFromTexture (AreaManager am, string filePath)
        {
            if (!LoadHeightmapFromFile (am, filePath))
            {
                return;
            }

            var sizeX = am.boundsFull.x;
            var sizeY = am.boundsFull.y;
            var sizeZ = am.boundsFull.z;

            // To ensure that the new terrain matches the existing area segments without any visible seams,
            // don't remove any terrain offsets on the border spots.
            const int fringe = 1;

            colorArray = textureDepthmap.GetPixels32 ();
            var maxDepthScaled = Mathf.Min ((sizeY - 1) * 10, byte.MaxValue);
            // The points on the north (sizeZ - 1) and east (sizeX - 1) borders don't have spots. Shrink the
            // bounds by one to exclude those points.
            var lastX = sizeX - 1;
            var lastZ = sizeZ - 1;

            for (var y = 0; y < sizeY; y += 1)
            {
                for (var z = 0; z < lastZ; z += 1)
                {
                    for (var x = 0; x < lastX; x += 1)
                    {
                        var colorIndex = z * sizeX + x;
                        var color = colorArray[colorIndex];
                        var depthSample = color.r;
                        var depthRestored = Mathf.RoundToInt (Mathf.Clamp (maxDepthScaled - depthSample, 0, maxDepthScaled) * 0.1f);
                        var posIndex = new Vector3Int (x, y, z);
                        var index = AreaUtility.GetIndexFromVolumePosition (posIndex, am.boundsFull, skipBoundsCheck: true);
                        var spot = am.points[index];

                        if (x > fringe && x < lastX - fringe && z > fringe && z < lastZ - fringe)
                        {
                            spot.terrainOffset = 0f;
                        }

                        if (y < depthRestored)
                        {
                            AreaSceneHelper.ClearSpot (spot);
                        }
                        else if (y == depthRestored)
                        {
                            AreaSceneHelper.ChangeSpotToTerrain (spot);
                        }
                        else
                        {
                            AreaSceneHelper.ChangeSpotToInterior (spot);
                        }
                        am.RemovePropPlacement (index);
                    }
                }
            }

            am.RebuildEverything ();

            // Terrain undergoes a smoothing process that may cause a slope to go through an empty or interior
            // spot. The tileset is 0 for these spots and they're displayed with the fallback tileset. Change
            // the tileset to terrain so they are displayed correctly.

            var fixup = false;
            for (var y = 0; y < sizeY; y += 1)
            {
                for (var z = 0; z < lastZ; z += 1)
                {
                    for (var x = 0; x < lastX; x += 1)
                    {
                        var posIndex = new Vector3Int (x, y, z);
                        var index = AreaUtility.GetIndexFromVolumePosition (posIndex, am.boundsFull, skipBoundsCheck: true);
                        var spot = am.points[index];
                        if (spot.pointState == AreaVolumePointState.Full
                            && spot.spotConfiguration != TilesetUtility.configurationFull
                            && spot.blockTileset != AreaTilesetHelper.idOfTerrain)
                        {
                            spot.blockTileset = AreaTilesetHelper.idOfTerrain;
                            fixup = true;
                            continue;
                        }
                        if (spot.pointState == AreaVolumePointState.Empty
                            && spot.spotConfiguration != TilesetUtility.configurationEmpty
                            && spot.blockTileset != AreaTilesetHelper.idOfTerrain)
                        {
                            spot.blockTileset = AreaTilesetHelper.idOfTerrain;
                            fixup = true;
                        }
                    }
                }
            }
            if (fixup)
            {
                am.RebuildEverything ();
            }
        }

        public static void ImportRampsFromTexture (AreaManager am, string filePath)
        {
            if (!LoadHeightmapFromFile (am, filePath))
            {
                return;
            }

            var sizeX = am.boundsFull.x;
            var sizeY = am.boundsFull.y;
            var sizeZ = am.boundsFull.z;

            colorArray = textureDepthmap.GetPixels32 ();
            int slopeAdditionsFound = 0;
            int slopeRemovalsFound = 0;

            // Apply slopes once all volume changes are applied
            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    var colorIndex = z * sizeX + x;
                    var color = colorArray[colorIndex];
                    bool slopeAdditionDesired = color.g >= (byte)255;
                    bool slopeRemovalDesired = color.g > (byte)117 && color.g < (byte)137;
                    bool slopeChangeDesired = slopeAdditionDesired || slopeRemovalDesired;

                    if (!slopeChangeDesired)
                        continue;

                    var posIndex = new Vector3Int (x, 0, z);
                    var index = AreaUtility.GetIndexFromVolumePosition (posIndex, am.boundsFull, skipBoundsCheck: true);
                    var pointCurrent = am.points[index];
                    int iteration = 0;

                    while (true)
                    {
                        if (pointCurrent == null)
                            break;

                        var pointStateCurrent = pointCurrent.pointState;
                        if (pointStateCurrent == AreaVolumePointState.Full)
                        {
                            am.TrySettingSlope (pointCurrent, slopeAdditionDesired, false);

                            if (slopeAdditionDesired)
                                slopeAdditionsFound += 1;

                            if (slopeRemovalDesired)
                                slopeRemovalsFound += 1;

                            break;
                        }

                        // Get point below
                        pointCurrent = pointCurrent.pointsInSpot[4];
                        iteration += 1;

                        if (iteration > sizeY)
                            break;
                    }
                }
            }

            am.RebuildEverything ();

            Debug.Log ($"Slope import completed. Slope additions requested (G=255): {slopeAdditionsFound} | Slope removals requested (G=127): {slopeRemovalsFound}");
        }

        public static void ImportRoadsFromTexture(AreaManager am, string filePath)
        {
            if (!LoadHeightmapFromFile (am, filePath))
            {
                return;
            }
            var sizeX = am.boundsFull.x;
            var sizeY = am.boundsFull.y;
            var sizeZ = am.boundsFull.z;

            colorArray = textureDepthmap.GetPixels32 ();
            List<AreaVolumePoint> pointsModified = new List<AreaVolumePoint> ();

            int roadAdditions = 0;
            int roadRemovals = 0;

            // Apply slopes once all volume changes are applied
            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    var colorIndex = z * sizeX + x;
                    var color = colorArray[colorIndex];
                    bool roadDesired = color.b == (byte)255;

                    var posIndex = new Vector3Int (x, 0, z);
                    var index = AreaUtility.GetIndexFromVolumePosition (posIndex, am.boundsFull, skipBoundsCheck: true);
                    var pointCurrent = am.points[index];
                    int iteration = 0;

                    while (true)
                    {
                        if (pointCurrent == null)
                            break;

                        var pointStateCurrent = pointCurrent.pointState;
                        if (pointStateCurrent == AreaVolumePointState.Full)
                        {
                            bool mismatch = pointCurrent.road != roadDesired;
                            if (mismatch)
                            {
                                pointCurrent.road = roadDesired;
                                pointsModified.Add (pointCurrent);

                                if (roadDesired)
                                    roadAdditions += 1;
                                else
                                    roadRemovals += 1;
                            }
                            else if (roadDesired)
                                pointsModified.Add (pointCurrent);

                            break;
                        }

                        // Get point below
                        pointCurrent = pointCurrent.pointsInSpot[4];
                        iteration += 1;

                        if (iteration > sizeY)
                            break;
                    }
                }
            }

            for (int i = 0; i < pointsModified.Count; ++i)
                am.UpdateRoadConfigurations (pointsModified[i], AreaManager.roadSubtype);

            am.RebuildEverything ();

            Debug.Log ($"Road import completed. Road additions requested (B=255): {roadAdditions} | Road removals: {roadRemovals} | Total points modified: {pointsModified.Count}");

        }

        public static void ImportPropsFromTexture (AreaManager am, string filePath)
        {
            var sizeX = am.boundsFull.x;
            var sizeZ = am.boundsFull.z;
            var sizeXDouble = sizeX * 2;
            var sizeZDouble = sizeZ * 2;

            if (!System.IO.File.Exists (filePath))
            {
                Debug.LogWarning ("Area manager | File doesn't exist: " + filePath);
                return;
            }

            try
            {
                var pngBytes = System.IO.File.ReadAllBytes (filePath);
                textureMaskVegetation = new Texture2D (sizeXDouble, sizeZDouble, TextureFormat.RGB24, false, false);
                textureMaskVegetation.name = am.areaName + "_mask_vegetation";
                textureMaskVegetation.filterMode = FilterMode.Point;
                textureMaskVegetation.wrapMode = TextureWrapMode.Clamp;
                textureMaskVegetation.LoadImage (pngBytes);
            }
            catch (Exception e)
            {
                Debug.LogWarningFormat ($"Area manager | Encountered an exception while loading vegetation mask {filePath}\n{0}", e);
            }

            if (textureMaskVegetation.width != sizeXDouble || textureMaskVegetation.height != sizeZDouble)
            {
                Debug.LogError ($"Area manager | Unexpected heightmap resolution {textureMaskVegetation.width}x{textureMaskVegetation.height} (expected {sizeXDouble}x{sizeZDouble}) at {filePath}\n{0}");
                return;
            }

            var colorArray = textureMaskVegetation.GetPixels ();
            var gridSizeHalf = TilesetUtility.blockAssetSize * 0.5f;
            var dualGridOffset = new Vector3 (-gridSizeHalf, 0f, -gridSizeHalf) * 0.5f;

            var offsetDiag1 = new Vector3 (0.27f, 0f, 0.27f);
            var offsetDiag2 = new Vector3 (-0.27f, 0f, 0.27f);
            var offsetDiag3 = new Vector3 (-0.27f, 0f, -0.27f);
            var offsetDiag4 = new Vector3 (0.27f, 0f, -0.27f);

            var colorLink = Color.white.WithAlpha (0.1f);
            var spotRaycastHitOffset = new Vector3 (-1.5f, 1.5f, -1.5f);
            var volumePos = am.GetHolderColliders ().position;

            for (int x = 0; x < sizeXDouble; x++)
            {
                for (int z = 0; z < sizeZDouble; z++)
                {
                    var colorIndex = z * sizeXDouble + x;
                    var color = colorArray[colorIndex];
                    var posRaycast = new Vector3 (x * gridSizeHalf, 25f, z * gridSizeHalf) + dualGridOffset;

                    var posGround = AreaUtility.GroundPoint (posRaycast);
                    if (posGround.y > 0f)
                    {
                        // Debug.Log ($"Failed to find position for pixel {x}x{z}");
                        continue;
                    }

                    var hex = UtilityColor.ToHexRGB (color);
                    var group = am.GetPropGroupFromHex (hex);
                    if (group == null)
                        continue;

                    var height = group.debugHeight;
                    var colorFaded = color.WithAlpha (0.5f);
                    var colorDark = Color.Lerp (color, Color.black, 0.5f);

                    var posGroundShifted = posGround + spotRaycastHitOffset;
                    int indexForSpot = AreaUtility.GetIndexFromWorldPosition (posGroundShifted, volumePos, am.boundsFull);
                    if (!indexForSpot.IsValidIndex (am.points))
                        continue;

                    var point = am.points[indexForSpot];
                    if (point.blockTileset != AreaTilesetHelper.idOfTerrain)
                        continue;

                    Debug.DrawLine (posGround, point.pointPositionLocal, colorLink, 15f);

                    Debug.DrawLine (posGround, posGround + Vector3.up * (height * 0.5f), colorDark, 15f);
                    Debug.DrawLine (posGround + Vector3.up * (height * 0.5f), posGround + Vector3.up * height, color, 15f);
                    Debug.DrawLine (posGround + offsetDiag1, posGround + offsetDiag3, colorFaded, 15f);
                    Debug.DrawLine (posGround + offsetDiag2, posGround + offsetDiag4, colorFaded, 15f);

                    int propSelectionID = group.propsIDs.GetRandomEntry ();
                    AreaPlacementProp placement = new AreaPlacementProp ();
                    AreaPropPrototypeData prototype = AreaAssetHelper.GetPropPrototype (propSelectionID);

                    if (prototype == null)
                        continue;

                    var posLocal = posGroundShifted - point.pointPositionLocal;
                    var pointIndex = point.spotIndex;

                    placement.id = propSelectionID;
                    placement.pivotIndex = pointIndex;
                    placement.rotation = 0;
                    placement.flipped = false;
                    placement.offsetX = posLocal.x;
                    placement.offsetZ = posLocal.z;
                    placement.hsbPrimary = Constants.defaultHSBOffset;
                    placement.hsbSecondary = Constants.defaultHSBOffset;

                    if (am.IsPropPlacementValid (placement, point, prototype, false))
                    {
                        if (!am.indexesOccupiedByProps.ContainsKey (pointIndex))
                            am.indexesOccupiedByProps.Add (pointIndex, new List<AreaPlacementProp> ());

                        am.indexesOccupiedByProps[pointIndex].Add (placement);
                        am.placementsProps.Add (placement);

                        am.ExecutePropPlacement (placement);
                        am.SnapPropRotation (placement);
                    }
                }
            }
        }

        public static void SetRampsEverywhere (AreaManager am, string filePath, AreaManager.SlopeProximityCheck proximityCheck)
        {
            bool IsPointOnSurface (AreaVolumePoint point)
            {
                if (point == null || point.pointState != AreaVolumePointState.Full)
                    return false;

                var pointAbove = point.pointsWithSurroundingSpots[3];
                if (pointAbove == null || pointAbove.pointState != AreaVolumePointState.Empty)
                    return false;

                return true;
            }

            for (int i = 0, limit = am.points.Count; i < limit; ++i)
            {
                var point = am.points[i];
                if (!IsPointOnSurface (point))
                    continue;

                am.TrySettingSlope (point, true, false, proximityCheck);
            }

            if (am.rampImportOnGeneration)
            {
                ImportRampsFromTexture (am, filePath);
                return;
            }
            am.RebuildEverything ();
        }

        static void StoreHeightmapInfoInternal(AreaManager am, int x, int z, int depthProcessed, byte slopeInfo, byte roadInfo)
        {
            var depthScaled = (byte)Mathf.Max (maxDepthScaled - depthProcessed * 10, minDepthScaled);
            var color = new Color32 (depthScaled, slopeInfo, roadInfo, byte.MaxValue);
            var colorIndex = z * am.boundsFull.z + x;

            colorArray[colorIndex] = color;
            if (!colorValues.Add (depthScaled))
            {
                return;
            }
            am.heightfieldPalette.Add (new AreaManager.DepthColorLink ()
            {
                color = color,
                depth = depthProcessed,
                depthScaled = depthScaled,
            });
        }

        static bool LoadHeightmapFromFile (AreaManager am, string filePath)
        {
            if (!System.IO.File.Exists (filePath))
            {
                Debug.LogWarning ("Area manager | File doesn't exist: " + filePath);
                return false;
            }

            var sizeX = am.boundsFull.x;
            var sizeZ = am.boundsFull.z;
            try
            {
                var pngBytes = System.IO.File.ReadAllBytes (filePath);
                textureDepthmap = new Texture2D (sizeX, sizeZ, TextureFormat.RGB24, false, false)
                {
                    name = am.areaName + "_heightmap",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                textureDepthmap.LoadImage (pngBytes);
            }
            catch (Exception e)
            {
                Debug.LogWarningFormat ("Area manager | Encountered an exception while loading heightmap {0}\n{1}", filePath, e);
                return false;
            }

            if (textureDepthmap.width != sizeX || textureDepthmap.height != sizeZ)
            {
                Debug.LogErrorFormat
                (
                    "Area manager | Unexpected heightmap resolution {0}x{1} (expected {2}x{3}) at {4}",
                    textureDepthmap.width,
                    textureDepthmap.height,
                    sizeX,
                    sizeZ,
                    filePath
                );
                return false;
            }
            return true;
        }

        static int[,] heightfield;
        static Texture2D textureDepthmap;
        static Texture2D textureMaskVegetation;

        static readonly HashSet<int> colorValues = new HashSet<int> ();
        static Color32[] colorArray;
        static int maxDepthScaled;

        const byte minDepthScaled = 10;
        const float terrainOffsetTopRamp = -1f / 3f;
    }
}
