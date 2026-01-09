using System.Collections.Generic;

using UnityEngine;

namespace Area
{
    using Scene;

    sealed class AreaSceneRoadCurveMode : AreaSceneMode
    {
        public EditingMode EditingMode => EditingMode.RoadCurves;

        public AreaSceneModePanel Panel { get; }

        public void OnDisable () => Panel.OnDisable ();
        public void OnDestroy () { }

        public int LayerMask => AreaSceneCamera.environmentLayerMask;

        public bool Hover (Event e, RaycastHit hitInfo)
        {
            if (!AreaSceneModeHelper.DisplaySpotCursor (bb, hitInfo))
            {
                return false;
            }
            var (eventType, button) = AreaSceneModeHelper.ResolveEvent (e);
            switch (eventType)
            {
                case EventType.MouseDown:
                    EditRoadCurves (bb.am, bb.lastSpotHovered.spotIndex, button, e.shift);
                    return true;
            }
            return false;
        }

        public void OnHoverEnd () => bb.gizmos.cursor.HideCursor ();

        public bool HandleSceneUserInput (Event e) => false;

        public void DrawSceneMarkup (Event e, System.Action repaint)
        {
            AreaSceneModeHelper.TryRebuildTerrain (bb);
        }

        public static AreaSceneMode CreateInstance (AreaSceneBlackboard bb) => new AreaSceneRoadCurveMode (bb);

		// The road curve tool matches against a library of patterns to detect if we can switch blocks to a smooth turn
		// the pattern spec includes the road configuration type and the rotation of several blocks
		// if all match, we replace them with whatever the pattern specifies
		static void EditRoadCurves (AreaManager am, int spotIndex, KeyCode mouseButton, bool shift)
		{
            if (mouseButton != KeyCode.Mouse0 && mouseButton != KeyCode.Mouse1)
            {
                return;
            }

			var startingPoint = am.points[spotIndex];
            //Debug.Log($"{roadDataStarting?.configType??RoadConfigType.Empty} {roadDataStarting.usedRotation} {startingPoint.pointPositionIndex}");
            var pointList = new List<(AreaVolumePoint pt, AreaRoadData data)>();
            //Index 0 is the center point; 1-4 are in rotation order
            AddPoint (pointList, startingPoint);
            AddPoint (pointList, startingPoint.pointsInSpot[WorldSpace.Compass.East]);
            AddPoint (pointList, startingPoint.pointsWithSurroundingSpots[WorldSpace.Compass.South]);
            AddPoint (pointList, startingPoint.pointsWithSurroundingSpots[WorldSpace.Compass.West]);
            AddPoint (pointList, startingPoint.pointsInSpot[WorldSpace.Compass.North]);


            //go through patterns
            foreach (var curveTemplate in roadCurveData)
            {
                //Try all four rotations of each pattern
                for (var r = 0; r < 4; r += 1)
                {
                    var allReqsMet = true;
                    //Go through individual spot requirements of a pattern
                    for (var i = 0; i < curveTemplate.DataCount && allReqsMet; i += 1)
                    {
                        var spotData = curveTemplate.GetData(i);
                        if (!spotData.reqActive)
                        {
                            continue;
                        }

                        var rotatedNeighbourIndex = spotData.neighbourIndex == 0
                            ? 0
                            : (spotData.neighbourIndex + 3 + r) % 4 + 1;
                        if (rotatedNeighbourIndex < 0 || rotatedNeighbourIndex >= pointList.Count)
                        {
                            allReqsMet = false;
                            break;
                        }

                        var pointInfo = pointList[rotatedNeighbourIndex];
                        if (pointInfo.pt == null)
                        {
                            allReqsMet = false;
                            break;
                        }

                        var reqMet = pointInfo.data.configType == spotData.reqConfigType && (pointInfo.data.usedRotation + r) % 4 == spotData.reqRotation;
                        allReqsMet &= reqMet;
                    }

                    if (!allReqsMet)
                    {
                        continue;
                    }

                    var anyModified = false;
                    //Apply modifications
                    for (var i = 0; i < curveTemplate.DataCount; i += 1)
                    {
                        var spotData = curveTemplate.GetData(i);
                        var rotatedNeighbourIndex = spotData.neighbourIndex == 0?0:((spotData.neighbourIndex-1+4+r)%4+1);
                        var pointInfo = pointList[rotatedNeighbourIndex];

                        if (!spotData.reqActive || !spotData.editActive)
                        {
                            continue;
                        }

                        bool modified;
                        if (mouseButton == KeyCode.Mouse0)
                        {
                            var rotationVal = (byte)((pointInfo.data.usedRotation + spotData.rotationShift + 4) % 4);
                            modified = pointInfo.pt.blockGroup != spotData.group
                                       || pointInfo.pt.blockRotation != rotationVal
                                       || pointInfo.pt.blockFlippedHorizontally != spotData.flip;

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
                        if (modified)
                        {
                           am.RebuildBlock (pointInfo.pt);
                        }
                    }

                    if (anyModified)
                    {
                        return;
                    }
                }
            }
        }

        static void AddPoint (List<(AreaVolumePoint pt, AreaRoadData data)> pointList, AreaVolumePoint pt)
        {
            if (pt == null)
            {
                pointList.Add ((null, null));
                return;
            }
            pointList.Add ((pt, AreaSceneHelper.GetRoadDataForPoint (pt)));
        }

        AreaSceneRoadCurveMode (AreaSceneBlackboard bb)
        {
            this.bb = bb;
            Panel = new AreaSceneRoadCurveModePanel ();
        }

        readonly AreaSceneBlackboard bb;
        static readonly AreaRoadCurveData[] roadCurveData = new[]
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

        class AreaRoadCurveData
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
    }
}
