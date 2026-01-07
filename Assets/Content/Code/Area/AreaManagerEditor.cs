using System;
using System.Collections.Generic;

using UnityEngine;

using CustomRendering;
using PhantomBrigade.Data;

namespace Area
{
    partial class AreaManager
    {
        #if UNITY_EDITOR && PB_MODSDK

        public enum EditingVolumeBrush
        {
            Point,
            Square2x2,
            Square3x3,
            Circle3x3,
            Circle5x5
        }

        public enum RoadConfigType
        {
            Empty,
            Full,
            Straight,
            InCorner,
            OutCorner,
            BiDiagonal
        }

        public enum RoadEditingOperation
        {
            None = 0,
            Add = 1,
            Remove = 2,
            FloodFill = 3,
            SubtypeNext = 10,
            SubtypePrev = 11
        }

        public enum RoadSubtype
        {
            GrassDirt = 0,
            GrassCurb = 10,
            ConcreteCurb = 20,
            TileCurb = 30,
        }

        public enum SlopeProximityCheck
        {
            None,
            LateralSingle,
            LateralDouble
        }

        public class AreaRoadData
        {
            public bool[] configurationAsArray = new bool[4];
            public byte usedGroup = 0;
            public byte usedRotation = 0;
            public RoadConfigType configType;

            public AreaRoadData (RoadConfigType configType, bool a, bool b, bool c, bool d, byte usedGroup, byte usedRotation)
            {
                this.configurationAsArray = new bool[] { a, b, c, d };
                this.usedGroup = usedGroup;
                this.usedRotation = usedRotation;
                this.configType = configType;
            }
        }

        public class AreaRoadCurveData
        {
            public struct SpotData
            {
                public bool reqActive;

                public int neighbourIndex;
                public RoadConfigType reqConfigType;
                public byte reqRotation;

                public bool editActive;

                public byte group;
                public byte rotationShift;
                public bool flip;

                public SpotData(int neighbourIndex, RoadConfigType reqConfigType, byte reqRotation, byte group, byte rotationShift, bool flip)
                {
                    this.reqActive = true;
                    this.editActive = true;

                    this.neighbourIndex = neighbourIndex;
                    this.reqConfigType = reqConfigType;
                    this.reqRotation = reqRotation;

                    this.group = group;
                    this.rotationShift = rotationShift;
                    this.flip = flip;
                }

                public SpotData(int neighbourIndex, RoadConfigType reqConfigType, byte reqRotation)
                {
                    this.reqActive = true;
                    this.editActive = false;

                    this.neighbourIndex = neighbourIndex;
                    this.reqConfigType = reqConfigType;
                    this.reqRotation = reqRotation;

                    this.group = 0;
                    this.rotationShift = 0;
                    this.flip = false;
                }
            }

            public SpotData spot1;
            public SpotData spot2;
            public SpotData spot3;

            public AreaRoadCurveData(SpotData spot1)
            {
                this.spot1 = spot1;
            }

            public AreaRoadCurveData(SpotData spot1, SpotData spot2)
            {
                this.spot1 = spot1;
                this.spot2 = spot2;
            }

            public AreaRoadCurveData(SpotData spot1, SpotData spot2, SpotData spot3)
            {
                this.spot1 = spot1;
                this.spot2 = spot2;
                this.spot3 = spot3;
            }

            public SpotData GetData(int i)
            {
                switch(i)
                {
                    case 0:	return spot1;
                    case 1:	return spot2;
                    case 2:	return spot3;
                }

                return default(SpotData);
            }

            public int DataCount => 3;
        }

        public struct DepthColorLink
        {
            public Color color;
            public int depth;
            public int depthScaled;
        }

        public void DrawHighlightSpot (AreaVolumePoint point)
        {
            DrawHighlightBox (point.instancePosition, Color.red, 15f);
        }

        public void DrawHighlightBox (Vector3 pos, Color col, float duration, float size = 1.5f)
        {
            var ct1 = pos + new Vector3 (size, size, size);
            var ct2 = pos + new Vector3 (-size, size, size);
            var ct3 = pos + new Vector3 (-size, size, -size);
            var ct4 = pos + new Vector3 (size, size, -size);

            var cb1 = pos + new Vector3 (size, -size, size);
            var cb2 = pos + new Vector3 (-size, -size, size);
            var cb3 = pos + new Vector3 (-size, -size, -size);
            var cb4 = pos + new Vector3 (size, -size, -size);

            Debug.DrawLine (ct1, ct2, col, duration);
            Debug.DrawLine (ct2, ct3, col, duration);
            Debug.DrawLine (ct3, ct4, col, duration);
            Debug.DrawLine (ct4, ct1, col, duration);

            Debug.DrawLine (cb1, cb2, col, duration);
            Debug.DrawLine (cb2, cb3, col, duration);
            Debug.DrawLine (cb3, cb4, col, duration);
            Debug.DrawLine (cb4, cb1, col, duration);

            Debug.DrawLine (ct1, cb1, col, duration);
            Debug.DrawLine (ct2, cb2, col, duration);
            Debug.DrawLine (ct3, cb3, col, duration);
            Debug.DrawLine (ct4, cb4, col, duration);
        }

        public void ApplyPropVisibilityEverywhere (int cutoffLayer)
        {
            // This is the counterpart for props to hiding/showing blocks during layer editing.
            // Any props above layer will be hidden, those on or below layer will be shown.

            var hideStop = boundsFull.x * boundsFull.z * cutoffLayer;
            for (var i = 0; i < points.Count; i += 1)
            {
                if (!indexesOccupiedByProps.TryGetValue (i, out var placements))
                {
                    continue;
                }

                var visible = i >= hideStop;
                var halfValues = visible ? propVisible : propInvisible;
                foreach (var placement in placements)
                {
                    if (AreaAssetHelper.propsHiddenWithECS.Contains (placement.prototype.id))
                    {
                        placement.UpdateVisibilityWithECS (visible, componentTypeModel);
                        continue;
                    }
                    placement.UpdateVisibility(halfValues);
                }
            }
        }
        public static List<AreaVolumePoint> CollectPointsInBrush (AreaVolumePoint pointStart, EditingVolumeBrush brush)
        {
            pointsToEdit.Clear ();
            pointsToEdit.Add (pointStart);

            if (brush == EditingVolumeBrush.Circle3x3 || brush == EditingVolumeBrush.Square3x3)
            {
                // X+ : east
                if (pointStart.pointsInSpot[1] != null)
                    pointsToEdit.Add (pointStart.pointsInSpot[1]);

                // Z+ : north
                if (pointStart.pointsInSpot[2] != null)
                    pointsToEdit.Add (pointStart.pointsInSpot[2]);

                // X- : west
                if (pointStart.pointsWithSurroundingSpots[6] != null)
                    pointsToEdit.Add (pointStart.pointsWithSurroundingSpots[6]);

                // Z- : south
                if (pointStart.pointsWithSurroundingSpots[5] != null)
                    pointsToEdit.Add (pointStart.pointsWithSurroundingSpots[5]);
            }

            if (brush == EditingVolumeBrush.Square3x3)
            {
                // X+ & Z+ : northeast
                if (pointStart.pointsInSpot[3] != null)
                    pointsToEdit.Add (pointStart.pointsInSpot[3]);

                // X- & Z+ : northwest
                var nw = pointStart.pointsWithSurroundingSpots[6]?.pointsInSpot[2];
                if (nw != null)
                    pointsToEdit.Add (nw);

                // X- & Z- : southwest
                if (pointStart.pointsWithSurroundingSpots[4] != null)
                    pointsToEdit.Add (pointStart.pointsWithSurroundingSpots[4]);

                // X+ & Z- : southeast
                var se = pointStart.pointsInSpot[1]?.pointsWithSurroundingSpots[5];
                if (se != null)
                    pointsToEdit.Add (se);
            }

            if (brush == EditingVolumeBrush.Square2x2)
            {
                // X- : west
                if (pointStart.pointsWithSurroundingSpots[6] != null)
                    pointsToEdit.Add (pointStart.pointsWithSurroundingSpots[6]);

                // Z- : south
                if (pointStart.pointsWithSurroundingSpots[5] != null)
                    pointsToEdit.Add (pointStart.pointsWithSurroundingSpots[5]);

                // X- & Z- : southwest
                if (pointStart.pointsWithSurroundingSpots[4] != null)
                    pointsToEdit.Add (pointStart.pointsWithSurroundingSpots[4]);
            }

            pointsToEdit.Sort (OrderPointsToEditByIndex);

            return pointsToEdit;
        }

        public void TrySettingSlope
        (
            AreaVolumePoint pointStartFull,
            bool slopeDesired,
            bool selectiveUpdates = true,
            SlopeProximityCheck proximityCheck = SlopeProximityCheck.None,
            bool log = false
        )
        {
            var pointAboveStartEmpty = pointStartFull?.pointsWithSurroundingSpots[3];

            bool IsPointUsable (AreaVolumePoint tstPoint, AreaVolumePointState stateExpected, bool log)
            {
                if (tstPoint == null || tstPoint.pointState != stateExpected)
                    return false;

                int xMax = boundsFull.x - 1;
                int zMax = boundsFull.z - 1;

                // Only iterate over top points in neighbor list
                for (int p = 0; p < 4; ++p)
                {
                    var pt = tstPoint.pointsWithSurroundingSpots[p];
                    if (pt == null)
                        return false;

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

            if (!IsPointUsable (pointStartFull, AreaVolumePointState.Full, log) || !IsPointUsable (pointAboveStartEmpty, AreaVolumePointState.Empty, log))
            {
                if (log)
                {
                    Debug.Log ($"Point {pointStartFull.ToLog ()} (origin, expected full) or {pointAboveStartEmpty.ToLog ()} (above origin, expected empty) is not compatible with ramps");

                    if (pointStartFull != null)
                        DebugExtensions.DrawCube (pointStartFull.pointPositionLocal, Vector3.one, Color.yellow, 1f);

                    if (pointAboveStartEmpty != null)
                        DebugExtensions.DrawCube (pointAboveStartEmpty.pointPositionLocal, Vector3.one, Color.yellow, 1f);
                }

                return;
            }

            // Debug.Log ($"Slope set to {slopeDesired} | Corners allowed: {cornersAllowed} | Point start: {pointStartFull.ToLog ()} | Point above: {pointAboveStartEmpty.ToLog ()}");

            slopePointNeighbors.Clear ();
            slopePointsAffected.Clear ();

            // X+
            var neighborXPos = pointStartFull.pointsInSpot[1];
            slopePointNeighbors.Add (neighborXPos);

            // Z+
            var neighborZPos = pointStartFull.pointsInSpot[2];
            slopePointNeighbors.Add (neighborZPos);

            // X-
            var neighborXNeg = pointStartFull.pointsWithSurroundingSpots[6];
            slopePointNeighbors.Add (neighborXNeg);

            // Z-
            var neighborZNeg = pointStartFull.pointsWithSurroundingSpots[5];
            slopePointNeighbors.Add (neighborZNeg);

            if (proximityCheck == SlopeProximityCheck.LateralSingle || proximityCheck == SlopeProximityCheck.LateralDouble)
            {
                slopePointNeighborsLeft.Clear ();
                slopePointNeighborsRight.Clear ();

                slopePointNeighborsRight.Add (neighborXPos?.pointsInSpot[2]);
                slopePointNeighborsLeft.Add (neighborXPos?.pointsWithSurroundingSpots[5]);

                slopePointNeighborsRight.Add (neighborZPos?.pointsInSpot[1]);
                slopePointNeighborsLeft.Add (neighborZPos?.pointsWithSurroundingSpots[6]);

                slopePointNeighborsRight.Add (neighborXNeg?.pointsWithSurroundingSpots[5]);
                slopePointNeighborsLeft.Add (neighborXNeg?.pointsInSpot[2]);

                slopePointNeighborsRight.Add (neighborZNeg?.pointsInSpot[1]);
                slopePointNeighborsLeft.Add (neighborZNeg?.pointsWithSurroundingSpots[6]);

                if (proximityCheck == SlopeProximityCheck.LateralDouble)
                {
                    slopePointNeighborsLeft2.Clear ();
                    slopePointNeighborsRight2.Clear ();

                    slopePointNeighborsRight2.Add (slopePointNeighborsRight[0]?.pointsInSpot[2]);
                    slopePointNeighborsLeft2.Add (slopePointNeighborsLeft[0]?.pointsWithSurroundingSpots[5]);

                    slopePointNeighborsRight2.Add (slopePointNeighborsRight[1]?.pointsInSpot[1]);
                    slopePointNeighborsLeft2.Add (slopePointNeighborsLeft[1]?.pointsWithSurroundingSpots[6]);

                    slopePointNeighborsRight2.Add (slopePointNeighborsRight[2]?.pointsWithSurroundingSpots[5]);
                    slopePointNeighborsLeft2.Add (slopePointNeighborsLeft[2]?.pointsInSpot[2]);

                    slopePointNeighborsRight2.Add (slopePointNeighborsRight[3]?.pointsInSpot[1]);
                    slopePointNeighborsLeft2.Add (slopePointNeighborsLeft[3]?.pointsWithSurroundingSpots[6]);
                }
            }

            for (int i = 0, limit = slopePointNeighbors.Count; i < limit; ++i)
            {
                // For each of these horizontal neighbors, find the point under them
                // A horizontal neighbor must be empty, a point under them must be full
                var pointNeighbor = slopePointNeighbors[i];
                var pointNeighborUnder = pointNeighbor?.pointsInSpot[4];

                // Validate each point for state, proximity to edges, missing spots or wrong tilesets
                if (!IsPointUsable (pointNeighbor, AreaVolumePointState.Empty, log) || !IsPointUsable (pointNeighborUnder, AreaVolumePointState.Full, log))
                {
                    if (log)
                    {
                        Debug.Log ($"Point {pointNeighbor.ToLog ()} (neighbor {i}, expected empty) or {pointNeighborUnder.ToLog ()} (under it, expected full) is not compatible with ramps");

                        if (pointNeighbor != null)
                            DebugExtensions.DrawCube (pointNeighbor.pointPositionLocal, Vector3.one, Color.red, 1f);

                        if (pointNeighborUnder != null)
                            DebugExtensions.DrawCube (pointNeighborUnder.pointPositionLocal, Vector3.one, Color.red, 1f);
                    }
                    continue;
                }

                if (proximityCheck == SlopeProximityCheck.LateralSingle || proximityCheck == SlopeProximityCheck.LateralDouble)
                {
                    var pointNeighborLeft = slopePointNeighborsLeft[i];
                    var pointNeighborLeftUnder = pointNeighborLeft?.pointsInSpot[4];

                    if (!IsPointUsable (pointNeighborLeft, AreaVolumePointState.Empty, log) || !IsPointUsable (pointNeighborLeftUnder, AreaVolumePointState.Full, log))
                    {
                        if (log)
                        {
                            Debug.Log ($"Point {pointNeighborLeft.ToLog ()} (neighbor left {i}, expected empty) or {pointNeighborLeftUnder.ToLog ()} (under it, expected full) is not compatible with ramps");

                            if (pointNeighborLeft != null)
                                DebugExtensions.DrawCube (pointNeighborLeft.pointPositionLocal, Vector3.one, Color.red, 1f);

                            if (pointNeighborLeftUnder != null)
                                DebugExtensions.DrawCube (pointNeighborLeftUnder.pointPositionLocal, Vector3.one, Color.red, 1f);
                        }
                        continue;
                    }

                    var pointNeighborRight = slopePointNeighborsRight[i];
                    var pointNeighborRightUnder = pointNeighborRight?.pointsInSpot[4];

                    if (!IsPointUsable (pointNeighborRight, AreaVolumePointState.Empty, log) || !IsPointUsable (pointNeighborRightUnder, AreaVolumePointState.Full, log))
                    {
                        if (log)
                        {
                            Debug.Log ($"Point {pointNeighborRight.ToLog ()} (neighbor right {i}, expected empty) or {pointNeighborRightUnder.ToLog ()} (under it, expected full) is not compatible with ramps");

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
                        var pointNeighborLeft2Under = pointNeighborLeft2?.pointsInSpot[4];

                        if (!IsPointUsable (pointNeighborLeft2, AreaVolumePointState.Empty, log) || !IsPointUsable (pointNeighborLeft2Under, AreaVolumePointState.Full, log))
                            continue;

                        var pointNeighborRight2 = slopePointNeighborsRight2[i];
                        var pointNeighborRight2Under = pointNeighborRight2?.pointsInSpot[4];

                        if (!IsPointUsable (pointNeighborRight2, AreaVolumePointState.Empty, log) || !IsPointUsable (pointNeighborRight2Under, AreaVolumePointState.Full, log))
                            continue;
                    }
                }

                // At this point we're ready to apply changes
                if (slopeDesired)
                {
                    pointNeighbor.terrainOffset = 1f / 3f;
                    pointAboveStartEmpty.terrainOffset = -1f / 3f;
                }
                else
                {
                    pointNeighbor.terrainOffset = 0f;
                    pointAboveStartEmpty.terrainOffset = 0f;
                }

                slopePointsAffected.Add (pointAboveStartEmpty);
            }

            if (slopePointsAffected.Count > 0)
            {
                slopePointsAffected.Add (pointAboveStartEmpty);

                bool terrainModified = true;
                for (int i = 0; i < slopePointsAffected.Count; ++i)
                {
                    AreaVolumePoint point = slopePointsAffected[i];

                    for (int s = 0; s < 8; ++s)
                    {
                        AreaVolumePoint pointWithNeighbourSpot = point.pointsWithSurroundingSpots[s];
                        if (pointWithNeighbourSpot == null)
                            continue;

                        if (pointWithNeighbourSpot.blockTileset == AreaTilesetHelper.idOfTerrain)
                        {
                            pointWithNeighbourSpot.blockFlippedHorizontally = false;
                            pointWithNeighbourSpot.blockRotation = 0;
                            pointWithNeighbourSpot.blockGroup = 0;
                            pointWithNeighbourSpot.blockSubtype = 0;
                        }

                        if (selectiveUpdates)
                        {
                            UpdateSpotAtIndex (pointWithNeighbourSpot.spotIndex, false);
                            RebuildBlock (pointWithNeighbourSpot, false);
                            RebuildCollisionForPoint (pointWithNeighbourSpot);
                        }
                    }

                    if (selectiveUpdates)
                        UpdateDamageAroundIndex (pointStartFull.spotIndex);
                }

                if (selectiveUpdates)
                {
                    var sceneHelper = CombatSceneHelper.ins;
                    sceneHelper.terrain.Rebuild (true);
                }
            }
        }

		// The road curve tool matches against a library of patterns to detect if we can switch blocks to a smooth turn
		// the pattern spec includes the road configuration type and the rotation of several blocks
		// if all match, we replace them with whatever the pattern specifies
		public void EditRoadCurves (int spotIndex, KeyCode mouseButton, bool shift)
		{
			AreaVolumePoint startingPoint = points[spotIndex];

			var roadDataStarting = GetRoadDataForPoint(startingPoint);
			var roadCurveData = GetRoadCurveData();

			if(mouseButton == KeyCode.Mouse0 || mouseButton == KeyCode.Mouse1)
			{
				//Debug.Log($"{roadDataStarting?.configType??RoadConfigType.Empty} {roadDataStarting.usedRotation} {startingPoint.pointPositionIndex}");

				var pointList = new List<(AreaVolumePoint pt, AreaRoadData data)>();

				void AddPoint(AreaVolumePoint pt)
				{
					if(pt == null)
						pointList.Add((null, null));
					else
						pointList.Add((pt, GetRoadDataForPoint(pt)));
				}

				//Index 0 is the center point; 1-4 are in rotation order
				AddPoint(startingPoint);
				AddPoint(startingPoint.pointsInSpot[1]);
				AddPoint(startingPoint.pointsWithSurroundingSpots[5]);
				AddPoint(startingPoint.pointsWithSurroundingSpots[6]);
				AddPoint(startingPoint.pointsInSpot[2]);


				//go through patterns
				foreach(var curveTemplate in roadCurveData)
				{
					//Try all four rotations of each pattern
					for(int r = 0;r < 4;++r)
					{
						bool allReqsMet = true;
						//Go through individual spot requirements of a pattern
						for(int i = 0;i < curveTemplate.DataCount;++i)
						{
							var spotData = curveTemplate.GetData(i);
							if(!spotData.reqActive)
								continue;

							var rotatedNeighbourIndex = spotData.neighbourIndex == 0?0:((spotData.neighbourIndex-1+4+r)%4+1);

							if(rotatedNeighbourIndex < 0 || rotatedNeighbourIndex >= pointList.Count)
							{
								allReqsMet = false;
								break;
							}

							var pointInfo = pointList[rotatedNeighbourIndex];
							if(pointInfo.pt == null)
							{
								allReqsMet = false;
								break;
							}

							var reqMet = pointInfo.data.configType == spotData.reqConfigType && (pointInfo.data.usedRotation + r)%4 == spotData.reqRotation;

							allReqsMet &= reqMet;

							if(!allReqsMet)
								break;
						}

						if(!allReqsMet)
							continue;

						bool anyModified = false;
						//Apply modifications
						for(int i = 0;i < curveTemplate.DataCount;++i)
						{
							var spotData = curveTemplate.GetData(i);
							var rotatedNeighbourIndex = spotData.neighbourIndex == 0?0:((spotData.neighbourIndex-1+4+r)%4+1);
							var pointInfo = pointList[rotatedNeighbourIndex];

							if(!spotData.reqActive || !spotData.editActive)
								continue;

							bool modified = false;
							if(mouseButton == KeyCode.Mouse0)
							{
								var rotationVal = (byte)((pointInfo.data.usedRotation + spotData.rotationShift + 4) % 4);

								modified = (pointInfo.pt.blockGroup != spotData.group || pointInfo.pt.blockRotation != rotationVal || pointInfo.pt.blockFlippedHorizontally != spotData.flip);

								pointInfo.pt.blockGroup = spotData.group;
								pointInfo.pt.blockRotation = rotationVal;
								pointInfo.pt.blockFlippedHorizontally = spotData.flip;
							}
							else
							{
								pointInfo.pt.blockGroup = pointInfo.data.usedGroup;
								pointInfo.pt.blockRotation = pointInfo.data.usedRotation;
								pointInfo.pt.blockFlippedHorizontally = false;
								modified = true;
							}

							anyModified |= modified;

							if(modified)
								RebuildBlock (pointInfo.pt, false);
						}

						if(anyModified)
							goto donePatternMatching;
					}
				}

				donePatternMatching:;
			}
		}

        public void UpdateRoadConfigurations (AreaVolumePoint pointStart, RoadSubtype subType)
        {
            if (pointStart.pointState != AreaVolumePointState.Full)
            {
                Debug.Log ("AM (I) | EditRoad | Selected starting point is not full, aborting");
                return;
            }

            for (int i = 0; i < 4; ++i)
            {
                AreaVolumePoint pointAbove = pointStart.pointsWithSurroundingSpots[i];
                if (pointAbove == null)
                {
                    Debug.Log ("AM (I) | EditRoad | One of the points (" + i + ") above the starting one is null, aborting");
                    return;
                }

                if (pointAbove.spotConfiguration != (byte)15)
                {
                    Debug.Log ("AM (I) | EditRoad | One of the points (" + i + ") above has non-00001111 configuration, aborting");
                    return;
                }

                AreaRoadData data = GetRoadDataForPoint(pointAbove);
                if(data == null)
                {
                    Debug.Log ("AM (I) | EditRoad | One of the spots above (" + i + ") has a configuration that could not be found.");
                    return;
                }

                if (pointAbove.blockTileset != AreaTilesetHelper.database.tilesetRoad.id || pointAbove.blockGroup != data.usedGroup || pointAbove.blockRotation != data.usedRotation)
                {
                    // Debug.Log ("AM (I) | EditRoad | Updating block " + pointAbove.spotIndex + " using road " + dataIndex);
                    pointAbove.blockTileset = AreaTilesetHelper.database.tilesetRoad.id;
                    pointAbove.blockGroup = data.usedGroup;
                    pointAbove.blockSubtype = (byte)(int)subType;
                    pointAbove.blockFlippedHorizontally = false;
                    pointAbove.blockRotation = data.usedRotation;
                    RebuildBlock (pointAbove, false);
                }
            }
        }

        public DataBlockAreaPropGroup GetPropGroupFromHex (string hex)
        {
            if (string.IsNullOrEmpty (hex))
                return null;

            if (propImportOverrides)
            {
                if (string.Equals (hex, propImportOverrideRedHex, StringComparison.InvariantCultureIgnoreCase))
                {
                    DataLinkerCombatBiomes.data.propGroups.TryGetValue (propImportOverrideRed, out var group);
                    return group;
                }
                if (string.Equals (hex, propImportOverrideYellowHex, StringComparison.InvariantCultureIgnoreCase))
                {
                    DataLinkerCombatBiomes.data.propGroups.TryGetValue (propImportOverrideYellow, out var group);
                    return group;
                }
                if (string.Equals (hex, propImportOverrideGreenHex, StringComparison.InvariantCultureIgnoreCase))
                {
                    DataLinkerCombatBiomes.data.propGroups.TryGetValue (propImportOverrideGreen, out var group);
                    return group;
                }
            }

            DataLinkerCombatBiomes.data.propGroupsByColor.TryGetValue (hex, out var groupFromColor);
            return groupFromColor;
        }

        public void RemoveRampsEverywhere ()
        {
            for (int i = 0; i < points.Count; ++i)
            {
                var point = points[i];
                if (point != null)
                    point.terrainOffset = 0f;
            }

            RebuildEverything ();
        }

        public void EditRoad (int indexStart, RoadEditingOperation operation)
        {
            var pointStart = points[indexStart];
            CollectPointsInBrush (pointStart, editingVolumeBrush);
            EditRoadPoints (operation);
        }

        public void EditRoadPoints (List<AreaVolumePoint> roadPoints, RoadEditingOperation operation)
        {
            pointsToEdit.Clear ();
            pointsToEdit.AddRange (roadPoints);
            EditRoadPoints (operation);
        }

        void EditRoadPoints (RoadEditingOperation operation)
        {
            var terrainModified = false;
            var roadAdded = operation == RoadEditingOperation.Add;
            var roadRemoved = operation == RoadEditingOperation.Remove;
            var roadFloodFill = operation == RoadEditingOperation.FloodFill;
            var roadSubtypeNext = operation == RoadEditingOperation.SubtypeNext;
            var roadSubtypePrev = operation == RoadEditingOperation.SubtypePrev;

            if (roadAdded || roadRemoved)
            {
                for (var i = 0; i < pointsToEdit.Count; i += 1)
                {
                    var point = pointsToEdit[i];
                    if (point.pointState != AreaVolumePointState.Full)
                    {
                        Debug.Log ("AM (I) | EditRoad | One of the core points (" + i + ") is not full, aborting");
                        return;
                    }

                    for (var a = 0; a < 4; a += 1)
                    {
                        var pointAbove = point.pointsWithSurroundingSpots[a];
                        if (pointAbove == null)
                        {
                            Debug.Log ("AM (I) | EditRoad | One of the surface points (" + a + ") above the starting one is null, aborting");
                            return;
                        }

                        if (pointAbove.spotConfiguration != AreaNavUtility.configFloor)
                        {
                            Debug.Log ("AM (I) | EditRoad | One of the surface points (" + a + ") above has non-00001111 configuration, aborting");
                            return;
                        }
                    }

                    if (!terrainModified)
                    {
                        for (var s = 0; s < 8; s += 1)
                        {
                            AreaVolumePoint pointWithNeighbourSpot = point.pointsWithSurroundingSpots[s];
                            if (pointWithNeighbourSpot == null)
                            {
                                continue;
                            }
                            if (pointWithNeighbourSpot.blockTileset == AreaTilesetHelper.idOfTerrain)
                            {
                                terrainModified = true;
                                break;
                            }
                        }
                    }
                }
            }

            if (roadAdded)
            {
                for (var i = 0; i < pointsToEdit.Count; i += 1)
                {
                    pointsToEdit[i].road = true;
                }
                for (var i = 0; i < pointsToEdit.Count; i += 1)
                {
                    UpdateRoadConfigurations (pointsToEdit[i], roadSubtype);
                }
            }
            else if (roadRemoved)
            {
                for (var i = 0; i < pointsToEdit.Count; i += 1)
                {
                    pointsToEdit[i].road = false;
                }
                for (var i = 0; i < pointsToEdit.Count; i += 1)
                {
                    UpdateRoadConfigurations (pointsToEdit[i], roadSubtype);
                }
            }
            else if (roadFloodFill)
            {
                FloodFillRoadSubtype (pointsToEdit, roadSubtype);
                terrainModified = true;
            }
            else if (roadSubtypeNext || roadSubtypePrev)
            {
                var roadSubtypeInt = (int)roadSubtype;
                roadSubtypeInt += roadSubtypeNext ? 10 : -10;
                if (roadSubtypeInt > 30)
                {
                    roadSubtypeInt = 0;
                }
                else if (roadSubtypeInt < 0)
                {
                    roadSubtypeInt = 30;
                }
                roadSubtype = (RoadSubtype)roadSubtypeInt;
            }

            if (terrainModified)
            {
                var sceneHelper = CombatSceneHelper.ins;
                sceneHelper.terrain.Rebuild (true);
            }
        }

		void FloodFillRoadSubtype(List<AreaVolumePoint> startPoints, RoadSubtype roadSubtype)
		{
			Queue<AreaVolumePoint> candidates = new Queue<AreaVolumePoint>();
			HashSet<AreaVolumePoint> processedSet = new HashSet<AreaVolumePoint>();
			List<AreaVolumePoint> resultList = new List<AreaVolumePoint>();

			foreach (var pt in startPoints)
				candidates.Enqueue(pt);

			while(candidates.Count > 0)
			{
				var pt = candidates.Dequeue();

				if(pt == null || !pt.road || processedSet.Contains(pt))
					continue;

				resultList.Add(pt);
				processedSet.Add(pt);

				candidates.Enqueue(pt.pointsInSpot[1]);
				candidates.Enqueue(pt.pointsWithSurroundingSpots[6]);
				candidates.Enqueue(pt.pointsInSpot[2]);
				candidates.Enqueue(pt.pointsWithSurroundingSpots[5]);
			}

			foreach (var pt in resultList)
			{
				for (int i = 0; i < 4; ++i)
				{
					AreaVolumePoint pointAbove = pt.pointsWithSurroundingSpots[i];
					if (pointAbove == null)
						break;

					if (pointAbove.spotConfiguration != (byte)15)
						break;

					pointAbove.blockSubtype = (byte)(int)roadSubtype;
					RebuildBlock (pointAbove, false);
				}
			}
		}

        AreaRoadCurveData[] GetRoadCurveData()
		{
			if(cachedRoadCurveData == null)
			{
				cachedRoadCurveData = new[]
				{
					//90 degree turns (inner)
					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 2, 8, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.Straight, 2),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.Straight, 1)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 2, 5, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.Straight, 2),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.Straight, 1)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 2, 8, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.Straight, 2),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.InCorner, 1)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 2, 5, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.Straight, 2),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.InCorner, 1)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 2, 8, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.InCorner, 3),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.InCorner, 1)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 2, 5, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.InCorner, 3),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.InCorner, 1)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 2, 8, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.InCorner, 3),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.Straight, 1)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 2, 5, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.InCorner, 3),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.Straight, 1)),

					//90 degree turns (outer)
					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 0, 7, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.Straight, 0),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.Straight, 3)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 0, 6, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.Straight, 0),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.Straight, 3)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 0, 7, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.OutCorner, 1),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.OutCorner, 3)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 0, 6, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.OutCorner, 1),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.OutCorner, 3)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 0, 7, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.Straight, 0),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.OutCorner, 3)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 0, 6, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.Straight, 0),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.OutCorner, 3)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 0, 7, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.OutCorner, 1),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.Straight, 3)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 0, 6, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.OutCorner, 1),
						new AreaRoadCurveData.SpotData(4, RoadConfigType.Straight, 3)),

					//45 degree turns, from the reference of the straight edge
					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.Straight, 2, 13, 2, true),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.InCorner, 3, 12, 1, true)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.Straight, 0, 13, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.InCorner, 0, 12, 0, false)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.Straight, 2, 10, 0, false),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.OutCorner, 2, 11, 0, false)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(0, RoadConfigType.Straight, 0, 10, 2, true),
						new AreaRoadCurveData.SpotData(1, RoadConfigType.OutCorner, 1, 11, 1, true)),

					//45 degree turns, from the reference of the corner
					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(3, RoadConfigType.Straight, 2, 13, 2, true),
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 3, 12, 1, true)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(3, RoadConfigType.Straight, 0, 13, 0, false),
						new AreaRoadCurveData.SpotData(0, RoadConfigType.InCorner, 0, 12, 0, false)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(3, RoadConfigType.Straight, 2, 10, 0, false),
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 2, 11, 0, false)),

					new AreaRoadCurveData(
						new AreaRoadCurveData.SpotData(3, RoadConfigType.Straight, 0, 10, 2, true),
						new AreaRoadCurveData.SpotData(0, RoadConfigType.OutCorner, 1, 11, 1, true)),
				};
			}

			return cachedRoadCurveData;
		}

		AreaRoadData[] GetRoadData()
		{
			if (cachedRoadData == null)
			{
				cachedRoadData = new AreaRoadData[16];

				// no road (plain terrain)
				cachedRoadData[0] = new AreaRoadData (RoadConfigType.Empty, false, false, false, false, 1, 0);

				// road in a single corner (inner turn edge)
				cachedRoadData[1] = new AreaRoadData (RoadConfigType.InCorner, true, false, false, false, 4, 0);
				cachedRoadData[2] = new AreaRoadData (RoadConfigType.InCorner, false, true, false, false, 4, 1);
				cachedRoadData[3] = new AreaRoadData (RoadConfigType.InCorner, false, false, true, false, 4, 3);
				cachedRoadData[4] = new AreaRoadData (RoadConfigType.InCorner, false, false, false, true, 4, 2);

				// road in two corners (straight road edge)
				cachedRoadData[5] = new AreaRoadData (RoadConfigType.Straight, true, true, false, false, 2, 0);
				cachedRoadData[6] = new AreaRoadData (RoadConfigType.Straight, true, false, true, false, 2, 3);
				cachedRoadData[7] = new AreaRoadData (RoadConfigType.Straight, false, false, true, true, 2, 2);
				cachedRoadData[8] = new AreaRoadData (RoadConfigType.Straight, false, true, false, true, 2, 1);

				// road in two corners (diagonal passage)
				cachedRoadData[9] = new AreaRoadData (RoadConfigType.BiDiagonal, true, false, true, false, 9, 0);
				cachedRoadData[10] = new AreaRoadData (RoadConfigType.BiDiagonal, false, true, false, true, 9, 1);

				// road in three corners (outer turn edge)
				cachedRoadData[11] = new AreaRoadData (RoadConfigType.OutCorner, true, false, true, true, 3, 3);
				cachedRoadData[12] = new AreaRoadData (RoadConfigType.OutCorner,false, true, true, true, 3, 2);
				cachedRoadData[13] = new AreaRoadData (RoadConfigType.OutCorner,true, true, false, true, 3, 1);
				cachedRoadData[14] = new AreaRoadData (RoadConfigType.OutCorner,true, true, true, false, 3, 0);

				// road in all corners (plain asphalt)
				cachedRoadData[15] = new AreaRoadData (RoadConfigType.Full, true, true, true, true, 0, 0);
			}

			return cachedRoadData;
		}

		AreaRoadData GetRoadDataForPoint(AreaVolumePoint pointAbove)
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

        static int OrderPointsToEditByIndex (AreaVolumePoint x, AreaVolumePoint y) => x.spotIndex.CompareTo (y.spotIndex);

        public const string standardPropMaskVegetationFileName = "mask_vegetation.png";

        public static EditingVolumeBrush editingVolumeBrush = EditingVolumeBrush.Point;
        public static bool ignoreUnresolvedTilesetOnLoad;
        public static RoadSubtype roadSubtype = RoadSubtype.GrassDirt;

        public AreaClipboard clipboard =  new AreaClipboard();
        public Vector3Int clipboardOrigin;
        public Vector3Int clipboardBoundsRequested;
        public Vector3Int targetOrigin;
        public bool transferVolume = true;
        public bool transferProps;
        public bool rampImportOnGeneration = false;
        public bool propImportOverrides = false;
        public string propImportOverrideRed = "l3_tall_fir";
        public string propImportOverrideYellow = "l2_mid_fir_mix";
        public string propImportOverrideGreen = "l1_low_fir_mix";

        [NonSerialized]
        public readonly List<DepthColorLink> heightfieldPalette = new List<DepthColorLink> ();

        [NonSerialized]
        public bool debugPasteDrawHighlights;

        static readonly HalfVector4 propVisible = new HalfVector4(1f, 0f, 1f, 1f);
        static readonly HalfVector4 propInvisible = new HalfVector4(0f, 1f, 1f, 1f);
        static readonly List<AreaVolumePoint> pointsToEdit = new List<AreaVolumePoint> ();
        static readonly string propImportOverrideRedHex = UtilityColor.ToHexRGB (new Color (1f, 0f, 0f));
        static readonly string propImportOverrideYellowHex = UtilityColor.ToHexRGB (new Color (1f, 1f, 0f));
        static readonly string propImportOverrideGreenHex = UtilityColor.ToHexRGB (new Color (0f, 1f, 0f));
        static AreaRoadData[] cachedRoadData;
        static AreaRoadCurveData[] cachedRoadCurveData;

        readonly List<AreaVolumePoint> slopePointNeighbors = new List<AreaVolumePoint> ();
        readonly List<AreaVolumePoint> slopePointNeighborsLeft = new List<AreaVolumePoint> ();
        readonly List<AreaVolumePoint> slopePointNeighborsRight = new List<AreaVolumePoint> ();
        readonly List<AreaVolumePoint> slopePointNeighborsLeft2 = new List<AreaVolumePoint> ();
        readonly List<AreaVolumePoint> slopePointNeighborsRight2 = new List<AreaVolumePoint> ();
        readonly List<AreaVolumePoint> slopePointsAffected = new List<AreaVolumePoint> ();

        #endif
    }
}
