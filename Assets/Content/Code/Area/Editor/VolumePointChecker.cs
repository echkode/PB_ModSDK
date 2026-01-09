using UnityEngine;

namespace Area
{
    using Scene;

    interface VolumePointChecker
    {
        delegate void OnTrigger ();
        void Check (AreaVolumePoint point, Vector3 direction);
        int CurrentMaterialID { get; }
    }

    sealed class TopBottomVolumePointChecker : VolumePointChecker
    {
        public delegate int GetMaterialID ();

        public bool denyAddBlockTop => lastAtTop && lastAtTopCompass == WorldSpace.Compass.Up;
        public bool denyRemoveBlock => lastAtBottom;

        public void Check (AreaVolumePoint point, Vector3 direction)
        {
            if (point == null)
            {
                return;
            }

            var atBottom = !point.spotPresent && point.pointPositionIndex.y >= bb.am.boundsFull.y - 1;
            var atTop = point.pointPositionIndex.y <= 1;
            var (compass, _) = AreaSceneHelper.GetNeighborFromDirection (point, direction);
            var bottom = atBottom != denyRemoveBlock;
            var top = (atTop && compass == WorldSpace.Compass.Up) != denyAddBlockTop;
            var changed = bottom || top;

            lastAtBottom = atBottom;
            lastAtTop = atTop;
            lastAtTopCompass = compass;

            if (!changed)
            {
                return;
            }
            onTrigger?.Invoke ();
        }

        public int CurrentMaterialID => getMaterialID ();

        public TopBottomVolumePointChecker (AreaSceneBlackboard bb, GetMaterialID getMaterialID, VolumePointChecker.OnTrigger onTrigger = null)
        {
            this.bb = bb;
            this.getMaterialID = getMaterialID;
            this.onTrigger = onTrigger;
        }

        readonly AreaSceneBlackboard bb;
        readonly GetMaterialID getMaterialID;
        readonly VolumePointChecker.OnTrigger onTrigger;

        bool lastAtBottom;
        bool lastAtTop;
        int lastAtTopCompass;
    }

    sealed class TerrainPointChecker : VolumePointChecker
    {
        public delegate int GetMaterialID ();

        public enum Operation
        {
            Hover = 0,
            Add,
            Remove,
        }

        public bool denyAddBlock => topBottomChecker.denyAddBlockTop;
        public bool denyRemoveBlock => topBottomChecker.denyRemoveBlock;
        public bool denyTerrainEdit => !allTerrain || lastTerrainCompass != WorldSpace.Compass.Up;

        public Operation operation;

        public void Check(AreaVolumePoint point, Vector3 direction)
        {
            topBottomChecker.Check (point, direction);

            var compass = AreaSceneHelper.GetCompassFromDirection (direction);
            if (compass == lastTerrainCompass && point.spotIndex == lastBlockIndex && !bb.brushChanged)
            {
                if (topBottomTriggered)
                {
                    onTrigger?.Invoke ();
                }
                return;
            }

            lastTerrainCompass = compass;
            lastBlockIndex = point.spotIndex;

            bb.brushChanged = false;
            var isOpAdd = operation == Operation.Add;
            var upSpots = true;
            if (isOpAdd)
            {
                var (ok, pointAdd) = TryGetPointForAdd (point, direction);
                if (!ok)
                {
                    return;
                }
                point = pointAdd;
                upSpots = false;
            }

            allTerrain = true;
            var policy = isOpAdd
                ? FreeSpacePolicy.LookDownPass
                : operation == Operation.Remove
                    ? FreeSpacePolicy.SlopePass
                    : FreeSpacePolicy.Pass;
            var pointsToEdit = AreaSceneModeHelper.CollectPointsInBrush (point);
            var am = bb.am;
            foreach (var pointToEdit in pointsToEdit)
            {
                allTerrain &= AreaSceneHelper.VolumePointAllTerrain (am, pointToEdit, upSpots, policy) != TerrainResult.Error;
                if (!allTerrain)
                {
                    break;
                }
            }
            onTrigger?.Invoke ();
        }

        public int CurrentMaterialID => getMaterialID ();

        static (bool, AreaVolumePoint) TryGetPointForAdd (AreaVolumePoint point, Vector3 direction)
        {
            if (point.pointState == AreaVolumePointState.Full)
            {
                var (_, newPoint) = AreaSceneHelper.GetNeighborFromDirection (point, direction);
                if (newPoint == null)
                {
                    return (false, null);
                }
                point = newPoint;
            }
            return (true, point);
        }

        int GetMaterialIDTopBottom () => getMaterialID ();
        void OnTriggerTopBottom () => topBottomTriggered = true;

        public TerrainPointChecker (AreaSceneBlackboard bb, GetMaterialID getMaterialID, VolumePointChecker.OnTrigger onTrigger = null)
        {
            this.bb = bb;
            this.onTrigger = onTrigger;
            this.getMaterialID = getMaterialID;
            topBottomChecker = new TopBottomVolumePointChecker (bb, GetMaterialIDTopBottom, OnTriggerTopBottom);
        }

        readonly AreaSceneBlackboard bb;
        readonly VolumePointChecker.OnTrigger onTrigger;
        readonly GetMaterialID getMaterialID;
        readonly TopBottomVolumePointChecker topBottomChecker;
        bool topBottomTriggered;

        int lastTerrainCompass;
        int lastBlockIndex = -1;
        bool allTerrain;
    }

    sealed class TerrainOffsetChecker : VolumePointChecker
    {
        public delegate int GetMaterialID ();

        public bool denyTerrainEdit => !pass;

        public void Check(AreaVolumePoint point, Vector3 direction)
        {
            topBottomChecker.Check (point, direction);

            var compass = AreaSceneHelper.GetCompassFromDirection (direction);
            if (compass == lastTerrainCompass && point.spotIndex == lastBlockIndex && !bb.brushChanged)
            {
                return;
            }

            bb.brushChanged = false;
            lastTerrainCompass = compass;
            lastBlockIndex = point.spotIndex;
            pass = false;
            if (!AreaSceneModeHelper.IsEditingVolumeBrushPoint)
            {
                onTrigger?.Invoke ();
                return;
            }
            if (lastTerrainCompass != WorldSpace.Compass.Up)
            {
                onTrigger?.Invoke ();
                return;
            }
            if (topTriggered && point.terrainOffset > 0f)
            {
                onTrigger?.Invoke ();
                return;
            }
            var am = bb.am;
            foreach (var spot in AreaSceneHelper.GetSpotsForBlockFace(am, WorldSpace.Compass.Up, point))
            {
                pass |= am.IsPointTerrain (spot);
                if (pass)
                {
                    break;
                }
            }
            onTrigger?.Invoke ();
        }

        public int CurrentMaterialID => getMaterialID ();

        int GetMaterialIDTopBottom () => getMaterialID ();
        void OnTriggerTopBottom () => topTriggered = topBottomChecker.denyAddBlockTop;

        public TerrainOffsetChecker (AreaSceneBlackboard bb, GetMaterialID getMaterialID, VolumePointChecker.OnTrigger onTrigger = null)
        {
            this.bb = bb;
            this.onTrigger = onTrigger;
            this.getMaterialID = getMaterialID;
            topBottomChecker = new TopBottomVolumePointChecker (bb, GetMaterialIDTopBottom, OnTriggerTopBottom);
        }

        readonly AreaSceneBlackboard bb;
        readonly VolumePointChecker.OnTrigger onTrigger;
        readonly GetMaterialID getMaterialID;
        readonly TopBottomVolumePointChecker topBottomChecker;

        int lastTerrainCompass;
        int lastBlockIndex = -1;

        bool topTriggered;
        bool pass;
    }
}
