using System.Collections.Generic;

using PhantomBrigade.Data;

using Sirenix.OdinInspector.Editor;

using UnityEditor;
using UnityEngine;

namespace Area
{
    using Scene;

    static class AreaSceneHelper
    {
        public delegate bool TryChangeBlock (AreaVolumePoint point, bool log);

        public static readonly int[] volumeTilesetIDs = { 10, 20, 30, 40, 50, };
        public static readonly byte[] spotNeighborMasks =
        {
            0x02, // up sw
            0x01, // up s
            0x04, // up w
            0x08, // up
            0x20, // sw
            0x10, // s
            0x40, // w
            0x80, // self
        };

        public const string unresolvedTilesetName = "unresolved";
        public const float rampOffset = 1f / TilesetUtility.blockAssetSize;

        public static void SaveSelectedLevel ()
        {
            var dataSelected = DataMultiLinkerCombatArea.selectedArea;
            // XXX not sure why this load is here.
            dataSelected.LoadLevelContentFromScene ();
            DataMultiLinkerCombatArea.unsavedChangesPossible = true;
            DataMultiLinkerCombatArea.SaveDataIsolated (dataSelected.key);
        }

        public static void ReturnToAreaDB ()
        {
            ToggleSegmentVisiblity (true);
            ToggleFieldsVisibility (true);
            var obj = DataMultiLinkerUtility.FindLinkerAsInterface (typeof (DataContainerCombatArea));
            if (obj != null)
            {
                obj.SelectObject ();
            }
        }

        public static void DrawUIOutline (Rect rect)
        {
            var start = new Vector3 (rect.x, rect.y, 0f);
            var p1 = new Vector3 (rect.x + rect.width, start.y, 0f);
            var p2 = new Vector3 (rect.x + rect.width, start.y + rect.height, 0f);
            var p3 = new Vector3 (rect.x, start.y + rect.height, 0f);
            var hc = Handles.color;
            Handles.color = Color.yellow;
            Handles.DrawPolyLine (start, p1, p2, p3, start);
            Handles.color = hc;
        }

        public static bool OverlapsUI (AreaSceneBlackboard bb, Vector3 worldPoint)
        {
            var placementPositionUI = HandleUtility.WorldToGUIPoint (worldPoint);
            if (bb.showLevelInfo && bb.sceneTitlePanelScreenRect.Contains (placementPositionUI))
            {
                return true;
            }
            if (bb.sceneModePanelScreenRect.Contains (placementPositionUI))
            {
                return true;
            }
            if (bb.sceneModePanelHintsScreenRect.Contains (placementPositionUI))
            {
                return true;
            }
            return bb.showModeToolbar && bb.toolbarScreenRect.Contains (placementPositionUI);
        }

        public static Color GetColorFromMain (AreaVolumePoint point)
        {
            if (point == null)
            {
                return Colors.Offset.Neutral;
            }
            if (point.indestructibleIndirect)
            {
                return Colors.Volume.MainIndestructibleIndr;
            }
            if (AreaManager.IsPointIndestructible (point, false, true, true, false, false))
            {
                return Colors.Volume.MainIndestructibleHard;
            }
            if (!point.destructible)
            {
                return Colors.Volume.MainIndestructibleFlag;
            }
            if (point.destructionUntracked)
            {
                return Colors.Volume.MainDestructibleUntracked;
            }

            return Colors.Volume.MainDestructible;
        }

        public static Color GetColorFromNeighbor (AreaVolumePoint point)
        {
            if (point == null)
            {
                return Colors.Offset.Neutral;
            }
            if (AreaManager.IsPointIndestructible (point, false, true, true, false, false))
            {
                return Colors.Volume.NeighborPrimaryIndestructibleHard;
            }
            if (!point.destructible)
            {
                return Colors.Volume.NeighborPrimaryIndestructibleFlag;
            }
            if (point.destructionUntracked)
            {
                return Colors.Volume.NeighborPrimaryDestructibleUntracked;
            }

            return Colors.Volume.NeighborPrimaryDestructible;
        }

        public static string GetPointConfigurationDisplayString (byte configuration)
        {
            var asStr = TilesetUtility.GetStringFromConfiguration (configuration);
            return asStr.Insert (4, "_");
        }

        public static string GetTilesetDisplayName (AreaTileset tileset)
        {
            if (tileset == null)
            {
                return "null";
            }
            if (tileset.name.StartsWith (tilesetPrefix))
            {
                return tileset.name.Substring (tilesetPrefix.Length);
            }
            return tileset.name;
        }

        public static string GetTilesetDisplayName (int tilesetID)
        {
            if (AreaTilesetHelper.database.tilesets.TryGetValue (tilesetID, out var tileset))
            {
                return GetTilesetDisplayName (tileset);
            }
            return unresolvedTilesetName;
        }

        public static (int Point, int Spot) ResolveIndexesFromHit (RaycastHit hitInfo, Vector3Int bounds, bool log = false)
        {
            // A word about the math in this function to get the spot index. The spot related to a point is to the northeast and down.
            // The box collider that's hit is centered on the point. A box collider is a cube and the corners of this cube are the spots
            // surrounding the point. As mentioned before the spot associated with the point is the bottom northeast corner of the box
            // collider.
            //
            // Subtracting the point (center of box collider) from the point on the collider that was hit gives you a direction vector.
            // The signs of the components of this direction vector tell you which octant of the cube was hit. Each octant corresponds
            // to a corner and that corner is a spot. We don't deal with spots directly but rather the point that's related to the spot.
            // So we use the signs to move each component one unit in the appropriate direction to arrive at the point for a given spot.
            //
            // Signed components  -->  offset  -->  corner
            // +x, -y, +z  -->   0, 0, 0  -->  bottom northeast (this is the same as the point)
            // +x, +y, +z  -->   0,-1, 0  -->  top northeast
            // -x, -y, +z  -->  -1, 0, 0  -->  bottom northwest
            // -x, +y, +z  -->  -1,-1, 0  -->  top northwest
            // -x, -y, -z  -->  -1, 0,-1  -->  bottom southwest
            // -x, +y, -z  -->  -1,-1,-1  -->  top southwest
            // +x, -y, -z  -->   0, 0,-1  -->  bottom southeast
            // +x, +y, -z  -->   0,-1,-1  -->  top southeast
            //
            // You can see you need two functions to resolve a sign on a component to an offset value.
            // For x and z
            //   + yields 0
            //   - yields -1
            // For y
            //   + yields -1
            //   - yields 0
            //
            // We're not done with the signs of the offset components yet because GetIndexFromWorldPosition subtracts
            // the offset which means we have to invert the offset.

            var pointWorldPosition = hitInfo.collider.bounds.center;
            var hitPoint = hitInfo.point;
            var pointIndex = AreaUtility.GetIndexFromWorldPosition (pointWorldPosition, Vector3.zero, bounds);
            var offset = hitPoint - pointWorldPosition;
            var offsetX = Mathf.Sign (offset.x) - 1f;
            var offsetY = Mathf.Sign (-offset.y) - 1f;
            var offsetZ = Mathf.Sign (offset.z) - 1f;
            offset = new Vector3 (offsetX, -offsetY, offsetZ) * -TilesetUtility.blockAssetHalfSize;
            var spotIndex = AreaUtility.GetIndexFromWorldPosition (pointWorldPosition, offset, bounds);

            if (log)
            {
                Debug.LogFormat ("Hit: {0} | point: {1} {2} | spot: {3} {4}", hitPoint, pointIndex, pointWorldPosition, spotIndex, offset);
            }

            return (pointIndex, spotIndex);
        }

        public static bool VolumePointHasTerrainOrRoadTilesets (AreaManager am, AreaVolumePoint avp)
        {
            // New blocks can't be put on top of terrain or road tilesets without problems. This function
            // checks an existing volume point to see if it has terrain or road tilesets. A volume point
            // straddles four spots with the volume point itself being the northeast spot of the four.

            return SpotHasTerrainOrRoadTileset (am, avp)
                || SpotHasTerrainOrRoadTileset (am, avp.pointsWithSurroundingSpots[WorldSpace.Compass.West])
                || SpotHasTerrainOrRoadTileset (am, avp.pointsWithSurroundingSpots[WorldSpace.Compass.South])
                || SpotHasTerrainOrRoadTileset (am, avp.pointsWithSurroundingSpots[WorldSpace.Compass.SouthWest]);
        }

        public static bool SpotHasTerrainOrRoadTileset(AreaManager am, AreaVolumePoint spot)
        {
            if (spot == null)
            {
                return false;
            }
            if (!spot.spotPresent)
            {
                return false;
            }
            if (spot.spotConfiguration == TilesetUtility.configurationEmpty)
            {
                return false;
            }
            return am.IsPointTerrain(spot) || spot.blockTileset == AreaTilesetHelper.idOfRoad;
        }

        public static TerrainResult VolumePointAllTerrain (AreaManager am, AreaVolumePoint point, bool upSpots, FreeSpacePolicy freeSpacePolicy)
        {
            var start = upSpots ? WorldSpace.Compass.UpSouthWest : WorldSpace.Compass.SouthWest;
            var stop = upSpots ? WorldSpace.Compass.Up : WorldSpace.Compass.SpotSelf;
            for (var i = start; i <= stop; i += 1)
            {
                var spot = point.pointsWithSurroundingSpots[i];
                if (spot == null)
                {
                    return TerrainResult.Error;
                }
                if (!spot.spotPresent)
                {
                    return TerrainResult.Error;
                }
                if (spot.pointState == AreaVolumePointState.FullDestroyed)
                {
                    continue;
                }
                if (IsFreeSpace(spot))
                {
                    return CheckFreeSpacePolicy (am, point, spot, i, upSpots, freeSpacePolicy, true);
                }
                if (!am.IsPointTerrain (spot))
                {
                    return TerrainResult.Error;
                }
                if ((spot.spotConfiguration & TilesetUtility.configurationFloor) != TilesetUtility.configurationFloor)
                {
                    return CheckFreeSpacePolicy (am, point, spot, i, upSpots, freeSpacePolicy, false);
                }
            }

            return TerrainResult.Terrain;
        }

        static TerrainResult CheckFreeSpacePolicy (AreaManager am, AreaVolumePoint point, AreaVolumePoint spot, int neighborIndex, bool upSpots, FreeSpacePolicy freeSpacePolicy, bool isFreeSpace)
        {
            if (freeSpacePolicy == FreeSpacePolicy.Error)
            {
                return TerrainResult.Error;
            }
            if (freeSpacePolicy == FreeSpacePolicy.Pass)
            {
                return TerrainResult.FreeSpace;
            }
            if (freeSpacePolicy == FreeSpacePolicy.SlopePass)
            {
                if (isFreeSpace)
                {
                    return TerrainResult.FreeSpace;
                }
                return TerrainResult.Terrain;
            }
            var neighborDown = upSpots
                ? point.pointsWithSurroundingSpots[neighborIndex + WorldSpace.Compass.SouthWest]
                : spot.pointsInSpot[WorldSpace.Compass.Down];
            if (neighborDown == null
                || !neighborDown.spotPresent
                || IsFreeSpace (neighborDown)
                || !am.IsPointTerrain (neighborDown))
            {
                return TerrainResult.Error;
            }
            return TerrainResult.FreeSpace;
        }

        public static bool IsEnclosed (AreaManager am, AreaVolumePoint spot)
        {
            var bounds = am.boundsFull;
            var points = am.points;
            foreach (var offset in cardinalNeighborOffsets)
            {
                var location = spot.pointPositionIndex + offset;
                var index = AreaUtility.GetIndexFromVolumePosition (location, bounds);
                if (index == AreaUtility.invalidIndex)
                {
                    continue;
                }
                var neighbor = points[index];
                if (neighbor == null)
                {
                    continue;
                }
                if (neighbor.pointState == AreaVolumePointState.Full)
                {
                    continue;
                }
                if (neighbor.spotConfiguration == TilesetUtility.configurationEmpty)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsFreeSpace (AreaVolumePoint point) =>
            point.pointState == AreaVolumePointState.Empty
            && point.spotConfiguration == TilesetUtility.configurationEmpty;

        public static bool IsSpotInterior (AreaVolumePoint spot) =>
            spot != null
            && spot.spotPresent
            && spot.pointState == AreaVolumePointState.Full
            && spot.spotConfiguration == TilesetUtility.configurationFull
            && spot.blockTileset == 0;

        public static bool IsSpotRoad (AreaVolumePoint spot) => spot != null && spot.blockTileset == AreaTilesetHelper.idOfRoad;

        public static void ClearSpot (AreaVolumePoint spot)
        {
            spot.pointState = AreaVolumePointState.Empty;
            spot.spotConfiguration = TilesetUtility.configurationEmpty;
            spot.spotConfigurationWithDamage = spot.spotConfiguration;
            spot.road = false;
            spot.terrainOffset = 0f;
            ClearTileData (spot);
        }

        public static void ClearTileData (AreaVolumePoint spot)
        {
            spot.blockFlippedHorizontally = false;
            spot.blockRotation = 0;
            spot.blockGroup = 0;
            spot.blockSubtype = 0;
            spot.blockTileset = 0;
            spot.customization = TilesetVertexProperties.defaults;
        }

        public static void ChangeSpotToTerrain (AreaVolumePoint spot)
        {
            spot.pointState = AreaVolumePointState.Empty;
            spot.road = false;
            ClearTileData (spot);
            spot.blockTileset = AreaTilesetHelper.idOfTerrain;
        }

        public static void ChangeSpotToInterior (AreaVolumePoint spot)
        {
            spot.pointState = AreaVolumePointState.Full;
            spot.spotConfiguration = TilesetUtility.configurationFull;
            spot.spotConfigurationWithDamage = spot.spotConfiguration;
            spot.road = false;
            ClearTileData (spot);
        }

        public static bool TryChangeBlocks (List<AreaVolumePoint> pointsToEdit, TryChangeBlock tryChange, bool log)
        {
            var result = false;
            for (var i = 0; i < pointsToEdit.Count; i += 1)
            {
                var point = pointsToEdit[i];
                // Can't remember why, but it's best to leave destroyed points alone
                if (point.pointState == AreaVolumePointState.FullDestroyed)
                {
                    return false;
                }
                result |= tryChange (point, log);
            }
            return result;
        }

        public static AreaRoadData GetRoadDataForPoint(AreaVolumePoint pointAbove)
        {
            var roadData = GetRoadData();

            bool[] config = new bool[4];
            for (int a = 0; a < 4; ++a)
            {
                AreaVolumePoint pointInRoadConfiguration = pointAbove.pointsInSpot[a + 4];
                config[a] = pointInRoadConfiguration?.road??false;
            }

            int dataIndex = -1;
            for (int a = 0; a < roadData.Length; ++a)
            {
                bool[] candidate = roadData[a].configurationAsArray;
                bool equal = true;
                for (int b = 0; b < 4; ++b)
                {
                    if (config[b] != candidate[b])
                    {
                        equal = false;
                        break;
                    }
                }

                if (equal)
                {
                    dataIndex = a;
                    break;
                }
            }

            if (dataIndex <= -1)
                return null;

            return roadData[dataIndex];
        }

        static AreaRoadData[] GetRoadData ()
        {
            if (cachedRoadData != null)
            {
                return cachedRoadData;
            }

            cachedRoadData = new[]
            {
                // no road (plain terrain)
                new AreaRoadData (RoadConfigType.Empty, false, false, false, false, 1, 0),

                // road in a single corner (inner turn edge)
                new AreaRoadData (RoadConfigType.InCorner, true, false, false, false, 4, 0),
                new AreaRoadData (RoadConfigType.InCorner, false, true, false, false, 4, 1),
                new AreaRoadData (RoadConfigType.InCorner, false, false, true, false, 4, 3),
                new AreaRoadData (RoadConfigType.InCorner, false, false, false, true, 4, 2),

                // road in two corners (straight road edge)
                new AreaRoadData (RoadConfigType.Straight, true, true, false, false, 2, 0),
                new AreaRoadData (RoadConfigType.Straight, true, false, true, false, 2, 3),
                new AreaRoadData (RoadConfigType.Straight, false, false, true, true, 2, 2),
                new AreaRoadData (RoadConfigType.Straight, false, true, false, true, 2, 1),

                // road in two corners (diagonal passage)
                new AreaRoadData (RoadConfigType.BiDiagonal, true, false, true, false, 9, 0),
                new AreaRoadData (RoadConfigType.BiDiagonal, false, true, false, true, 9, 1),

                // road in three corners (outer turn edge)
                new AreaRoadData (RoadConfigType.OutCorner, true, false, true, true, 3, 3),
                new AreaRoadData (RoadConfigType.OutCorner, false, true, true, true, 3, 2),
                new AreaRoadData (RoadConfigType.OutCorner, true, true, false, true, 3, 1),
                new AreaRoadData (RoadConfigType.OutCorner, true, true, true, false, 3, 0),

                // road in all corners (plain asphalt)
                new AreaRoadData (RoadConfigType.Full, true, true, true, true, 0, 0),
            };
            return cachedRoadData;
        }

        public static void UpdateRoadConfigurations (AreaManager am, AreaVolumePoint pointStart, RoadSubtype subType)
        {
            if (pointStart.pointState != AreaVolumePointState.Full)
            {
                Debug.Log ("AM (I) | EditRoad | Selected starting point is not full, aborting");
                return;
            }

            for (var i = 0; i < 4; i += 1)
            {
                var pointAbove = pointStart.pointsWithSurroundingSpots[i];
                if (pointAbove == null)
                {
                    Debug.Log ("AM (I) | EditRoad | One of the points (" + i + ") above the starting one is null, aborting");
                    return;
                }

                if (pointAbove.spotConfiguration != TilesetUtility.configurationFloor)
                {
                    Debug.Log ("AM (I) | EditRoad | One of the points (" + i + ") above has non-00001111 configuration, aborting");
                    return;
                }

                var data = GetRoadDataForPoint (pointAbove);
                if (data == null)
                {
                    Debug.Log ("AM (I) | EditRoad | One of the spots above (" + i + ") has a configuration that could not be found.");
                    return;
                }

                if (pointAbove.blockTileset != AreaTilesetHelper.database.tilesetRoad.id
                    || pointAbove.blockGroup != data.usedGroup
                    || pointAbove.blockRotation != data.usedRotation)
                {
                    // Debug.Log ("AM (I) | EditRoad | Updating block " + pointAbove.spotIndex + " using road " + dataIndex);
                    pointAbove.blockTileset = AreaTilesetHelper.database.tilesetRoad.id;
                    pointAbove.blockGroup = data.usedGroup;
                    pointAbove.blockSubtype = (byte)(int)subType;
                    pointAbove.blockFlippedHorizontally = false;
                    pointAbove.blockRotation = data.usedRotation;
                    am.RebuildBlock (pointAbove);
                }
            }
        }

        public static void TrySettingSlope
        (
            AreaManager am,
            AreaVolumePoint pointStartFull,
            bool slopeDesired,
            bool selectiveUpdates = true,
            SlopeProximityCheck proximityCheck = SlopeProximityCheck.None,
            bool log = false
        )
        {
            var pointAboveStartEmpty = pointStartFull?.pointsWithSurroundingSpots[WorldSpace.Compass.Up];
            if (!IsPointUsable (am, pointStartFull, AreaVolumePointState.Full, log)
                || !IsPointUsable (am, pointAboveStartEmpty, AreaVolumePointState.Empty, log))
            {
                if (log)
                {
                    Debug.LogFormat
                    (
                        "Point {0} (origin, expected full) or {1} (above origin, expected empty) is not compatible with ramps",
                        pointStartFull.ToLog (),
                        pointAboveStartEmpty.ToLog ()
                    );

                    if (pointStartFull != null)
                    {
                        DebugExtensions.DrawCube (pointStartFull.pointPositionLocal, Vector3.one, Color.yellow, 1f);
                    }
                    if (pointAboveStartEmpty != null)
                    {
                        DebugExtensions.DrawCube (pointAboveStartEmpty.pointPositionLocal, Vector3.one, Color.yellow, 1f);
                    }
                }
                return;
            }

            // Debug.Log ($"Slope set to {slopeDesired} | Corners allowed: {cornersAllowed} | Point start: {pointStartFull.ToLog ()} | Point above: {pointAboveStartEmpty.ToLog ()}");

            slopePointNeighbors.Clear ();
            slopePointsAffected.Clear ();

            var neighborXPos = pointStartFull.pointsInSpot[WorldSpace.Compass.East];
            var neighborZPos = pointStartFull.pointsInSpot[WorldSpace.Compass.North];
            var neighborXNeg = pointStartFull.pointsWithSurroundingSpots[WorldSpace.Compass.West];
            var neighborZNeg = pointStartFull.pointsWithSurroundingSpots[WorldSpace.Compass.South];
            slopePointNeighbors.Add (neighborXPos);
            slopePointNeighbors.Add (neighborZPos);
            slopePointNeighbors.Add (neighborXNeg);
            slopePointNeighbors.Add (neighborZNeg);

            if (proximityCheck == SlopeProximityCheck.LateralSingle || proximityCheck == SlopeProximityCheck.LateralDouble)
            {
                slopePointNeighborsLeft.Clear ();
                slopePointNeighborsRight.Clear ();

                slopePointNeighborsRight.Add (neighborXPos?.pointsInSpot[WorldSpace.Compass.North]);
                slopePointNeighborsLeft.Add (neighborXPos?.pointsWithSurroundingSpots[WorldSpace.Compass.South]);

                slopePointNeighborsRight.Add (neighborZPos?.pointsInSpot[WorldSpace.Compass.East]);
                slopePointNeighborsLeft.Add (neighborZPos?.pointsWithSurroundingSpots[WorldSpace.Compass.West]);

                slopePointNeighborsRight.Add (neighborXNeg?.pointsWithSurroundingSpots[WorldSpace.Compass.South]);
                slopePointNeighborsLeft.Add (neighborXNeg?.pointsInSpot[WorldSpace.Compass.North]);

                slopePointNeighborsRight.Add (neighborZNeg?.pointsInSpot[WorldSpace.Compass.East]);
                slopePointNeighborsLeft.Add (neighborZNeg?.pointsWithSurroundingSpots[WorldSpace.Compass.West]);

                if (proximityCheck == SlopeProximityCheck.LateralDouble)
                {
                    slopePointNeighborsLeft2.Clear ();
                    slopePointNeighborsRight2.Clear ();

                    slopePointNeighborsRight2.Add (slopePointNeighborsRight[0]?.pointsInSpot[WorldSpace.Compass.North]);
                    slopePointNeighborsLeft2.Add (slopePointNeighborsLeft[0]?.pointsWithSurroundingSpots[WorldSpace.Compass.South]);

                    slopePointNeighborsRight2.Add (slopePointNeighborsRight[1]?.pointsInSpot[WorldSpace.Compass.East]);
                    slopePointNeighborsLeft2.Add (slopePointNeighborsLeft[1]?.pointsWithSurroundingSpots[WorldSpace.Compass.West]);

                    slopePointNeighborsRight2.Add (slopePointNeighborsRight[2]?.pointsWithSurroundingSpots[WorldSpace.Compass.South]);
                    slopePointNeighborsLeft2.Add (slopePointNeighborsLeft[2]?.pointsInSpot[WorldSpace.Compass.North]);

                    slopePointNeighborsRight2.Add (slopePointNeighborsRight[3]?.pointsInSpot[WorldSpace.Compass.East]);
                    slopePointNeighborsLeft2.Add (slopePointNeighborsLeft[3]?.pointsWithSurroundingSpots[WorldSpace.Compass.West]);
                }
            }

            for (var i = 0; i < slopePointNeighbors.Count; i += 1)
            {
                // For each of these horizontal neighbors, find the point under them
                // A horizontal neighbor must be empty, a point under them must be full
                var pointNeighbor = slopePointNeighbors[i];
                var pointNeighborUnder = pointNeighbor?.pointsInSpot[WorldSpace.Compass.Down];

                // Validate each point for state, proximity to edges, missing spots or wrong tilesets
                if (!IsPointUsable (am, pointNeighbor, AreaVolumePointState.Empty, log)
                    || !IsPointUsable (am, pointNeighborUnder, AreaVolumePointState.Full, log))
                {
                    if (log)
                    {
                        Debug.LogFormat
                        (
                            "Point {0} (neighbor {1}, expected empty) or {2} (under it, expected full) is not compatible with ramps",
                            pointNeighbor.ToLog (),
                            i,
                            pointNeighborUnder.ToLog ()
                        );

                        if (pointNeighbor != null)
                        {
                            DebugExtensions.DrawCube (pointNeighbor.pointPositionLocal, Vector3.one, Color.red, 1f);
                        }
                        if (pointNeighborUnder != null)
                        {
                            DebugExtensions.DrawCube (pointNeighborUnder.pointPositionLocal, Vector3.one, Color.red, 1f);
                        }
                    }
                    continue;
                }

                if (proximityCheck == SlopeProximityCheck.LateralSingle || proximityCheck == SlopeProximityCheck.LateralDouble)
                {
                    var pointNeighborLeft = slopePointNeighborsLeft[i];
                    var pointNeighborLeftUnder = pointNeighborLeft?.pointsInSpot[WorldSpace.Compass.Down];

                    if (!IsPointUsable (am, pointNeighborLeft, AreaVolumePointState.Empty, log)
                        || !IsPointUsable (am, pointNeighborLeftUnder, AreaVolumePointState.Full, log))
                    {
                        if (log)
                        {
                            Debug.LogFormat
                            (
                                "Point {0} (neighbor left {1}, expected empty) or {2} (under it, expected full) is not compatible with ramps",
                                pointNeighborLeft.ToLog (),
                                i,
                                pointNeighborLeftUnder.ToLog ()
                            );

                            if (pointNeighborLeft != null)
                            {
                                DebugExtensions.DrawCube (pointNeighborLeft.pointPositionLocal, Vector3.one, Color.red, 1f);
                            }
                            if (pointNeighborLeftUnder != null)
                            {
                                DebugExtensions.DrawCube (pointNeighborLeftUnder.pointPositionLocal, Vector3.one, Color.red, 1f);
                            }
                        }
                        continue;
                    }

                    var pointNeighborRight = slopePointNeighborsRight[i];
                    var pointNeighborRightUnder = pointNeighborRight?.pointsInSpot[WorldSpace.Compass.Down];

                    if (!IsPointUsable (am, pointNeighborRight, AreaVolumePointState.Empty, log)
                        || !IsPointUsable (am, pointNeighborRightUnder, AreaVolumePointState.Full, log))
                    {
                        if (log)
                        {
                            Debug.LogFormat
                            (
                                "Point {0} (neighbor right {1}, expected empty) or {2} (under it, expected full) is not compatible with ramps",
                                pointNeighborRight.ToLog (),
                                i,
                                pointNeighborRightUnder.ToLog ()
                            );

                            if (pointNeighborRight != null)
                                DebugExtensions.DrawCube (pointNeighborRight.pointPositionLocal, Vector3.one, Color.red, 1f);

                            if (pointNeighborRightUnder != null)
                                DebugExtensions.DrawCube (pointNeighborRightUnder.pointPositionLocal, Vector3.one, Color.red, 1f);
                        }

                        continue;
                    }

                    if (proximityCheck == SlopeProximityCheck.LateralDouble)
                    {
                        var pointNeighborLeft2 = slopePointNeighborsLeft2[i];
                        var pointNeighborLeft2Under = pointNeighborLeft2?.pointsInSpot[WorldSpace.Compass.Down];
                        if (!IsPointUsable (am, pointNeighborLeft2, AreaVolumePointState.Empty, log)
                            || !IsPointUsable (am, pointNeighborLeft2Under, AreaVolumePointState.Full, log))
                        {
                            continue;
                        }

                        var pointNeighborRight2 = slopePointNeighborsRight2[i];
                        var pointNeighborRight2Under = pointNeighborRight2?.pointsInSpot[WorldSpace.Compass.Down];
                        if (!IsPointUsable (am, pointNeighborRight2, AreaVolumePointState.Empty, log)
                            || !IsPointUsable (am, pointNeighborRight2Under, AreaVolumePointState.Full, log))
                        {
                            continue;
                        }
                    }
                }

                // At this point we're ready to apply changes
                if (slopeDesired)
                {
                    pointNeighbor.terrainOffset = rampOffset;
                    pointAboveStartEmpty.terrainOffset = -rampOffset;
                }
                else
                {
                    pointNeighbor.terrainOffset = 0f;
                    pointAboveStartEmpty.terrainOffset = 0f;
                }

                slopePointsAffected.Add (pointAboveStartEmpty);
            }

            if (slopePointsAffected.Count <= 0)
            {
                return;
            }

            slopePointsAffected.Add (pointAboveStartEmpty);

            var terrainModified = true;
            for (var i = 0; i < slopePointsAffected.Count; i += 1)
            {
                var point = slopePointsAffected[i];
                for (var s = 0; s < point.pointsWithSurroundingSpots.Length; s += 1)
                {
                    var pointWithNeighbourSpot = point.pointsWithSurroundingSpots[s];
                    if (pointWithNeighbourSpot == null)
                    {
                        continue;
                    }
                    if (pointWithNeighbourSpot.blockTileset == AreaTilesetHelper.idOfTerrain)
                    {
                        pointWithNeighbourSpot.blockFlippedHorizontally = false;
                        pointWithNeighbourSpot.blockRotation = 0;
                        pointWithNeighbourSpot.blockGroup = 0;
                        pointWithNeighbourSpot.blockSubtype = 0;
                    }
                    if (selectiveUpdates)
                    {
                        am.UpdateSpotAtIndex (pointWithNeighbourSpot.spotIndex, false);
                        am.RebuildBlock (pointWithNeighbourSpot);
                        am.RebuildCollisionForPoint (pointWithNeighbourSpot);
                    }
                }

                if (selectiveUpdates)
                {
                    am.UpdateDamageAroundIndex (pointStartFull.spotIndex);
                }
            }

            if (selectiveUpdates)
            {
                var sceneHelper = CombatSceneHelper.ins;
                sceneHelper.terrain.Rebuild (true);
            }
        }

        static bool IsPointUsable (AreaManager am, AreaVolumePoint tstPoint, AreaVolumePointState stateExpected, bool log)
        {
            if (tstPoint == null || tstPoint.pointState != stateExpected)
            {
                return false;
            }

            var xMax = am.boundsFull.x - 1;
            var zMax = am.boundsFull.z - 1;
            // Only iterate over top points in neighbor list
            for (var p = WorldSpace.Compass.UpSouthWest; p < WorldSpace.Compass.SouthWest; p += 1)
            {
                var pt = tstPoint.pointsWithSurroundingSpots[p];
                if (pt == null)
                {
                    return false;
                }
                // Skip cases where any spot in the surrounding 2x2x2 volume is not all terrain
                if (pt.pointState != AreaVolumePointState.Empty && pt.blockTileset != 0 && pt.blockTileset != AreaTilesetHelper.idOfTerrain)
                {
                    if (log)
                    {
                        DebugExtensions.DrawCube (pt.pointPositionLocal, Vector3.one, Color.red, 1f);
                        Debug.DrawLine (pt.pointPositionLocal, tstPoint.pointPositionLocal, Color.red, 1f);
                    }
                    return false;
                }
                if (pt.pointPositionIndex.x <= 0 || pt.pointPositionIndex.z <= 0)
                {
                    if (log)
                    {
                        DebugExtensions.DrawCube (pt.pointPositionLocal, Vector3.one, Color.red, 1f);
                        Debug.DrawLine (pt.pointPositionLocal, tstPoint.pointPositionLocal, Color.red, 1f);
                    }
                    return false;
                }
                if (pt.pointPositionIndex.x >= xMax || pt.pointPositionIndex.z >= zMax)
                {
                    if (log)
                    {
                        DebugExtensions.DrawCube (pt.pointPositionLocal, Vector3.one, Color.red, 1f);
                        Debug.DrawLine (pt.pointPositionLocal, tstPoint.pointPositionLocal, Color.red, 1f);
                    }
                    return false;
                }
            }

            return true;
        }

        public static int GetCompassFromDirection (Vector3 direction)
        {
            var vertical = Vector3.Dot (Vector3.up, direction);
            if (vertical > dot45)
            {
                return WorldSpace.Compass.Up;
            }
            if (vertical < -dot45)
            {
                return WorldSpace.Compass.Down;
            }

            var ns = Vector3.Dot (Vector3.forward, direction);
            if (ns > dot45)
            {
                return WorldSpace.Compass.North;
            }
            if (ns < -dot45)
            {
                return WorldSpace.Compass.South;
            }

            var ew = Vector3.Dot (Vector3.right, direction);
            return ew > 0 ? WorldSpace.Compass.East : WorldSpace.Compass.West;
        }

        static (AVPArray, int) GetNeighborIndexFromCompass (int compass)
        {
            if (compass < 1)
            {
                return (AVPArray.Invalid, -1);
            }
            if (compass >= avpArrayLookup.Length)
            {
                return (AVPArray.Invalid, -1);
            }
            return (avpArrayLookup[compass], compass);
        }

        static AreaVolumePoint GetNeighborFromCompass (AreaVolumePoint avp, int compass)
        {
            var (avpArray, neighbor) = GetNeighborIndexFromCompass (compass);
            if (neighbor == -1)
            {
                return null;
            }
            return avpArray == AVPArray.Point ? avp.pointsInSpot[neighbor] : avp.pointsWithSurroundingSpots[neighbor];
        }

        public static (int, AreaVolumePoint) GetNeighborFromDirection (AreaVolumePoint avp, Vector3 direction)
        {
            var compass = GetCompassFromDirection (direction);
            return (compass, GetNeighborFromCompass (avp, compass));
        }

        public static AreaVolumePoint[] GetSpotsForBlockFace (AreaManager am, int face, AreaVolumePoint point)
        {
            // A point is the center of a block. A block is a cube with a side length of WorldSpace.BlockSize and shares
            // the same center position as the instanceCollider in AreaVolumePoint. This center position is
            // AreaVolumePoint.pointPositionLocal and that position is used as the point's location.
            //
            // A block has eight spots, one at each of its corners. Each spot may have a tile. The "skin" of the block is
            // formed from parts of the tiles of the eight spots.
            //
            // A spot is the center position of a cube of the same size as a block. This center position is
            // AreaVolumePoint.instancePosition. Each corner of the spot cube is a point.
            //
            // Every point has an associated spot. This associated spot is the down northeast corner of the block. Conversely,
            // every spot has an associated point which is the up southwest corner of the spot cube.
            //
            // A block can be seen as a composite of 8 smaller cubes which are octants of the spots at the block's corners.
            // Conversely, a spot can be seen as a composite of 8 smaller cubes which are octants of the points at the spot
            // cube's corners. Either way, the result is that a tile at a spot is shared by the surrounding 8 points.
            //
            // A face on the block is composed of the four spots at the face corners.

            System.Array.Clear (blockFaceSpots, 0, blockFaceSpots.Length);
            if (point == null)
            {
                return blockFaceSpots;
            }
            var bounds = am.boundsFull;
            var points = am.points;
            var offsets = blockFaceSpotOffsets[face];
            for (var i = 0; i < offsets.Length; i += 1)
            {
                var location = point.pointPositionIndex + offsets[i];
                var index = AreaUtility.GetIndexFromVolumePosition (location, bounds);
                if (index == AreaUtility.invalidIndex)
                {
                    continue;
                }
                blockFaceSpots[i] = points[index];
            }
            return blockFaceSpots;
        }

        public static (string, AreaVolumePoint)[] GetSpotsForBlockFaceWithLabels (AreaManager am, int face, AreaVolumePoint point)
        {
            // We're given a point but we want spots because tileset info is associated with spots, not points. The point is at the
            // center of the block with the face in question. Surrounding the point are 8 spots which can be thought of as cubes of
            // the same size as the block. These 8 spots share a common corner at the point. So the block face intersects 4 of the
            // spot cubes. It's those spot cubes that we're looking for.
            //
            // Due to the way that points and spots are related, a spot cube is simply a cube that's offset from a point by one-half
            // of the block size in each direction for X and Z, and down one-half of the block size in the Y direction (world space).
            // That means the point being passed in is the northeast down spot of itself.
            //
            // A block is aligned with the XYZ axes in world space so the four cardinal directions plus up and down can be used
            // to label each face of the block. A face is composed of the four spots at each corner of the face. Each spot
            // can be labeled by one of four compass directions: northeast (NE), northwest (NW), southwest (SW), southeast (SE).
            // These directions are in the plane of the face with the top edge of the facing being the up edge of the block for
            // the north, west, south and east faces of the block and the north edge of the block for the up and down faces.

            System.Array.Clear (blockFaceSpotsWithLabels, 0, blockFaceSpotsWithLabels.Length);
            if (point == null)
            {
                return blockFaceSpotsWithLabels;
            }

            var spots = GetSpotsForBlockFace (am, face, point);
            var labels = CubeFaceQuadrant.Labels[face];
            for (var i = 0; i < blockFaceSpotsWithLabels.Length; i += 1)
            {
                blockFaceSpotsWithLabels[i] = (labels[i], spots[i]);
            }
            return blockFaceSpotsWithLabels;
        }

        public static string GetCompassDisplayStringForFace (int compass) =>
            compass == WorldSpace.Compass.Up
                ? "Top"
                : compass == WorldSpace.Compass.Down
                    ? "Bottom"
                    : compass.ToString ();

        public static void ToggleSegmentVisiblity (bool visible)
        {
            var sceneHelper = CombatSceneHelper.ins;
            if (sceneHelper == null)
            {
                return;
            }

            if (visible)
            {
                sceneHelper.segmentHelper.gameObject.SetActive (true);
                return;
            }
            sceneHelper.segmentHelper.gameObject.SetActive (false);
        }

        public static void ToggleFieldsVisibility (bool visible)
        {
            var sceneHelper = CombatSceneHelper.ins;
            if (sceneHelper == null)
            {
                return;
            }

            if (visible)
            {
                sceneHelper.fieldHelper.gameObject.SetActive (true);
                return;
            }
            sceneHelper.fieldHelper.gameObject.SetActive (false);
        }

        public static bool ValidateBounds (Vector3Int bounds) =>
            bounds.x > BoundsSpace.BottomValue.x
            && bounds.z > BoundsSpace.BottomValue.z
            && bounds.y > BoundsSpace.BottomValue.y;

        public static string FormatBounds (Vector3Int bounds) => string.Format ("{0} x {1} x {2}", bounds.x, bounds.z, bounds.y);

        public static string SpotNeighborDisplay (int spotNeighborIndex, bool compact = false)
        {
            if (spotNeighborDisplayMap.TryGetValue (spotNeighborIndex, out var display))
            {
                return compact ? display.Compact : display.LongForm;
            }
            return "<error>";
        }

        // The AreaVolumePoint class keeps its neighboring points in two different array.
        // The pointsInSpot array holds neighbors to the north, east and down in world space. This is Point.
        // The pointsWithSurroundingSpots array holds neighbors to the south, west and up in world space. This is Spot.
        enum AVPArray
        {
            Invalid = -1,
            Point,
            Spot,
        }

        const float dot45 = 0.7071067f;
        const string tilesetPrefix = "tileset_";

        static readonly AreaVolumePoint[] blockFaceSpots = new AreaVolumePoint[CubeFaceQuadrant.Count];
        static readonly (string, AreaVolumePoint)[] blockFaceSpotsWithLabels = new (string, AreaVolumePoint)[CubeFaceQuadrant.Count];

        static readonly AVPArray[] avpArrayLookup = new[]
        {
            AVPArray.Invalid,
            AVPArray.Point,
            AVPArray.Point,
            AVPArray.Spot,
            AVPArray.Point,
            AVPArray.Spot,
            AVPArray.Spot,
        };

        static readonly Dictionary<int, Vector3Int[]> blockFaceSpotOffsets = new Dictionary<int, Vector3Int[]> ()
        {
            [WorldSpace.Compass.Up] = new[]
            {
                new Vector3Int(0, -1, 0),
                new Vector3Int(-1, -1, 0),
                new Vector3Int(-1, -1, -1),
                new Vector3Int(0, -1, -1),
            },
            [WorldSpace.Compass.Down] = new[]
            {
                new Vector3Int(-1, 0, 0),
                new Vector3Int(0, 0, 0),
                new Vector3Int(0, 0, -1),
                new Vector3Int(-1, 0, -1),
            },
            [WorldSpace.Compass.North] = new[]
            {
                new Vector3Int(-1, -1, 0),
                new Vector3Int(0, -1, 0),
                new Vector3Int(0, 0, 0),
                new Vector3Int(-1, 0, 0),
            },
            [WorldSpace.Compass.West] = new[]
            {
                new Vector3Int(-1, -1, -1),
                new Vector3Int(-1, -1, 0),
                new Vector3Int(-1, 0, 0),
                new Vector3Int(-1, 0, -1),
            },
            [WorldSpace.Compass.East] = new[]
            {
                new Vector3Int(0, -1, 0),
                new Vector3Int(0, -1, -1),
                new Vector3Int(0, 0, -1),
                new Vector3Int(0, 0, 0),
            },
            [WorldSpace.Compass.South] = new[]
            {
                new Vector3Int(0, -1, -1),
                new Vector3Int(-1, -1, -1),
                new Vector3Int(-1, 0, -1),
                new Vector3Int(0, 0, -1),
            },
        };

        static class CubeFaceQuadrant
        {
            // Quadrants are listed in counterclockwise order starting from the northeast corner.
            // See GetSpotsForBlockFaceWithLabels() details about how faces are oriented on a block and
            // how quadrants are oriented on a face.

            public const int NorthEast = 0;
            public const int NorthWest = NorthEast + 1;
            public const int SouthWest = NorthWest + 1;
            public const int SouthEast = SouthWest + 1;
            public const int Count = SouthEast + 1;

            public static readonly Dictionary<int, List<string>> Labels = new Dictionary<int, List<string>> ()
            {
                [WorldSpace.Compass.Up] = new List<string> () { "NE", "NW", "SW", "SE", },
                [WorldSpace.Compass.Down] = new List<string> () { "NW", "SW", "SE", "NE", },
                [WorldSpace.Compass.North] = new List<string> () { "UE", "UW", "DW", "DE", },
                [WorldSpace.Compass.West] = new List<string> () { "US", "UN", "DN", "DS" },
                [WorldSpace.Compass.South] = new List<string> () { "UE", "UW", "DW", "DE", },
                [WorldSpace.Compass.East] = new List<string> () { "UN", "US", "DS", "DN", },
            };
        }

        static readonly Dictionary<int, (string Compact, string LongForm)> spotNeighborDisplayMap = new Dictionary<int, (string, string)> ()
        {
            [WorldSpace.Compass.UpSouthWest] = ("Up SW", "Up SouthWest"),
            [WorldSpace.Compass.UpSouth] = ("Up S", "Up South"),
            [WorldSpace.Compass.UpWest] = ("Up W", "Up West"),
            [WorldSpace.Compass.Up] = ("Up", "Up"),
            [WorldSpace.Compass.SouthWest] = ("SW", "SouthWest"),
            [WorldSpace.Compass.South] = ("S", "South"),
            [WorldSpace.Compass.West] = ("W", "West"),
            [WorldSpace.Compass.SpotSelf] = ("Self", "Self"),
        };

        static readonly Vector3Int[] cardinalNeighborOffsets =
        {
            new Vector3Int (0, 0, 1),
            new Vector3Int (-1, 0, 0),
            new Vector3Int (1, 0, 0),
            new Vector3Int (0, 0, -1),
        };

        static readonly List<AreaVolumePoint> slopePointNeighbors = new List<AreaVolumePoint> ();
        static readonly List<AreaVolumePoint> slopePointNeighborsLeft = new List<AreaVolumePoint> ();
        static readonly List<AreaVolumePoint> slopePointNeighborsRight = new List<AreaVolumePoint> ();
        static readonly List<AreaVolumePoint> slopePointNeighborsLeft2 = new List<AreaVolumePoint> ();
        static readonly List<AreaVolumePoint> slopePointNeighborsRight2 = new List<AreaVolumePoint> ();
        static readonly List<AreaVolumePoint> slopePointsAffected = new List<AreaVolumePoint> ();

        static AreaRoadData[] cachedRoadData;
    }

    abstract class SelfDrawnGUI
    {
        public virtual void Draw ()
        {
            tree ??= PropertyTree.Create (this);
            tree.Draw (false);
        }

        public virtual void OnDisable ()
        {
            tree?.Dispose ();
            tree = null;
        }

        PropertyTree tree;
    }

    #if COMPILE_UNUSED
    class UnusedCode
    {
        void SaveColorPaletteProps()
        {
            UtilitiesYAML.SaveDataToFile ("Configs/LevelEditor", "colorPaletteProps.yaml", colorPalette);
        }

        void LoadColorPaletteProps()
        {
            var list = UtilitiesYAML.LoadDataFromFile<List<PaletteEntry>>("Configs/LevelEditor/colorPaletteProps.yaml");

            if (list != null)
            {
                colorPalette.Clear();
                colorPalette.AddRange(list);
            }
            else
            {
                Debug.LogWarning($"Could not load color palette for props");
            }
        }

        static readonly List<PaletteEntry> colorPalette = new List<PaletteEntry>();

        class PaletteEntry
        {
            public int tilesetId;
            public string tilesetDescription;
            public HSBColor primaryColor;
            public HSBColor secondaryColor;

            public PaletteEntry()
            {
                tilesetId = 0;
                tilesetDescription = "";
                primaryColor = HSBColor.FromColor(Color.gray);
                secondaryColor = HSBColor.FromColor(Color.gray);
            }

            public PaletteEntry(PaletteEntry other)
            {
                tilesetId = other.tilesetId;
                tilesetDescription = other.tilesetDescription;
                primaryColor = other.primaryColor;
                secondaryColor = other.secondaryColor;
            }
        }

        void GetPointsInColumnRecursive (List<AreaVolumePoint> results, AreaVolumePoint currentPoint, bool up)
        {
            if (results == null || currentPoint == null)
                return;

            AreaVolumePoint nextPoint = null;
            if (!results.Contains (currentPoint))
                results.Add (currentPoint);

            if (up)
            {
                AreaVolumePoint pointAbove = currentPoint.pointsWithSurroundingSpots[3];
                if (pointAbove != null && pointAbove.spotConfiguration != 0)
                    nextPoint = pointAbove;
            }
            else
            {
                AreaVolumePoint pointBelow = currentPoint.pointsInSpot[4];
                if (pointBelow != null && pointBelow.spotConfiguration != 0)
                    nextPoint = pointBelow;
            }

            if (nextPoint != null)
                GetPointsInColumnRecursive (results, nextPoint, up);
        }

        // void GetPointsAtHeight (List<AreaVolumePoint> results, AreaVolumePoint startingPoint)
        // {
        //     if (results == null || startingPoint == null)
        //         return;
        //
        //     var am = bb.am;
        //     int height = startingPoint.pointPositionIndex.y;
        //     for (int i = 0; i < am.points.Count; ++i)
        //     {
        //         AreaVolumePoint point = am.points[i];
        //         if (point.pointPositionIndex.y == height)
        //             results.Add (point);
        //     }
        // }

        void GetConnectedPointsAtHeight (List<AreaVolumePoint> results, AreaVolumePoint startingPoint)
        {
            if (results == null || startingPoint == null)
                return;

            bool allowFloor = startingPoint.spotConfiguration == (byte)15;

            results.Add (startingPoint);
            GetPointsInFloorRecursively (results, startingPoint, AreaUtility.Direction.XNeg, allowFloor);
            GetPointsInFloorRecursively (results, startingPoint, AreaUtility.Direction.ZNeg, allowFloor);
            GetPointsInFloorRecursively (results, startingPoint, AreaUtility.Direction.XPos, allowFloor);
            GetPointsInFloorRecursively (results, startingPoint, AreaUtility.Direction.ZPos, allowFloor);
        }

        void GetPointsInFloorRecursively (List<AreaVolumePoint> results, AreaVolumePoint currentPoint, AreaUtility.Direction directionOfPreviousPoint, bool allowFloor)
        {
            if (results == null || currentPoint == null)
                return;

            AreaVolumePoint pointXPos = null;
            bool allowFloorFurtherOnXPos = false;
            if (directionOfPreviousPoint != AreaUtility.Direction.XPos)
                pointXPos = FillFromCandidate (results, currentPoint.pointsInSpot[1], AreaUtility.Direction.XPos, allowFloor, out allowFloorFurtherOnXPos);

            AreaVolumePoint pointZPos = null;
            bool allowFloorFurtherOnZPos = false;
            if (directionOfPreviousPoint != AreaUtility.Direction.ZPos)
                pointZPos = FillFromCandidate (results, currentPoint.pointsInSpot[2], AreaUtility.Direction.ZPos, allowFloor, out allowFloorFurtherOnZPos);

            AreaVolumePoint pointXNeg = null;
            bool allowFloorFurtherOnXNeg = false;
            if (directionOfPreviousPoint != AreaUtility.Direction.XNeg)
                pointXNeg = FillFromCandidate (results, currentPoint.pointsWithSurroundingSpots[6], AreaUtility.Direction.XNeg, allowFloor, out allowFloorFurtherOnXNeg);

            AreaVolumePoint pointZNeg = null;
            bool allowFloorFurtherOnZNeg = false;
            if (directionOfPreviousPoint != AreaUtility.Direction.ZNeg)
                pointZNeg = FillFromCandidate (results, currentPoint.pointsWithSurroundingSpots[5], AreaUtility.Direction.ZNeg, allowFloor, out allowFloorFurtherOnZNeg);

            if (pointXPos != null)
                GetPointsInFloorRecursively (results, pointXPos, AreaUtility.Direction.XNeg, allowFloorFurtherOnXPos);

            if (pointZPos != null)
                GetPointsInFloorRecursively (results, pointZPos, AreaUtility.Direction.ZNeg, allowFloorFurtherOnZPos);

            if (pointXNeg != null)
                GetPointsInFloorRecursively (results, pointXNeg, AreaUtility.Direction.XPos, allowFloorFurtherOnXNeg);

            if (pointZNeg != null)
                GetPointsInFloorRecursively (results, pointZNeg, AreaUtility.Direction.ZPos, allowFloorFurtherOnZNeg);
        }

        AreaVolumePoint FillFromCandidate (List<AreaVolumePoint> results, AreaVolumePoint candidate, AreaUtility.Direction direction, bool allowFloor, out bool allowFloorFurther)
        {
            if (candidate == null)
            {
                allowFloorFurther = false;
                return null;
            }

            if (candidate.spotConfiguration != 15 && allowFloor)
                allowFloorFurther = false;
            else
                allowFloorFurther = allowFloor;

            if (candidate.spotConfiguration == 15 && !allowFloor)
                return null;

            if (candidate.spotConfiguration != 0 && !results.Contains (candidate))
            {
                results.Add (candidate);

                // We will null the candidate, preventing further recursive traversal
                // from that point on, if a point is an outer wall

                if (direction == AreaUtility.Direction.XPos)
                {
                    for (int i = 0; i < AreaNavUtility.maskForFloorTermination_XPos.Length; ++i)
                    {
                        if (candidate.spotConfiguration == AreaNavUtility.maskForFloorTermination_XPos[i])
                            return null;
                    }
                }
                else if (direction == AreaUtility.Direction.ZPos)
                {
                    for (int i = 0; i < AreaNavUtility.maskForFloorTermination_ZPos.Length; ++i)
                    {
                        if (candidate.spotConfiguration == AreaNavUtility.maskForFloorTermination_ZPos[i])
                            return null;
                    }
                }
                else if (direction == AreaUtility.Direction.XNeg)
                {
                    for (int i = 0; i < AreaNavUtility.maskForFloorTermination_XNeg.Length; ++i)
                    {
                        if (candidate.spotConfiguration == AreaNavUtility.maskForFloorTermination_XNeg[i])
                            return null;
                    }
                }
                else if (direction == AreaUtility.Direction.ZNeg)
                {
                    for (int i = 0; i < AreaNavUtility.maskForFloorTermination_ZNeg.Length; ++i)
                    {
                        if (candidate.spotConfiguration == AreaNavUtility.maskForFloorTermination_ZNeg[i])
                            return null;
                    }
                }

                return candidate;
            }

            return null;
        }
    }
    #endif
}
