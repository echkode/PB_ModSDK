using System;
using System.Collections.Generic;

using UnityEngine;

namespace Area
{
    partial class AreaManager
    {
        #if UNITY_EDITOR && PB_MODSDK

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

        public static bool ignoreUnresolvedTilesetOnLoad;

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

        #endif
    }
}
