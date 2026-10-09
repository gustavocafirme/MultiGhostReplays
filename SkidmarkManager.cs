using System.Collections.Generic;
using UnityEngine;

namespace MultiGhostReplays
{
    /// <summary>
    /// Utility class providing performance caching for Skidmarks singleton references and track surface types.
    /// </summary>
    public static class SkidmarkManager
    {
        private static Skidmarks cachedSkidmarks;
        private static Dictionary<Collider, int> groundTypeCache = new Dictionary<Collider, int>();

        /// <summary>
        /// Returns the cached Skidmarks instance, lazily resolving it if null.
        /// </summary>
        public static Skidmarks GetInstance()
        {
            if (cachedSkidmarks == null)
            {
                cachedSkidmarks = Skidmarks.instance;
            }
            return cachedSkidmarks;
        }

        /// <summary>
        /// Performs an O(1) dictionary lookup to resolve ground type IDs from colliders, avoiding repeated GetComponent calls.
        /// </summary>
        /// <param name="col">The ground collider hit by tire raycasts.</param>
        /// <returns>The resolved ground type integer ID.</returns>
        public static int GetGroundTypeFromCollider(Collider col)
        {
            if (col == null) return 0;

            if (groundTypeCache.TryGetValue(col, out int cachedType))
            {
                return cachedType;
            }

            int groundType = 0;
            GroundTypeChanger gtc = col.GetComponent<GroundTypeChanger>();
            if (gtc != null)
            {
                groundType = gtc.ground_type;
                if (groundType == 8) groundType = 4;
                else if (groundType == 9) groundType = 8;
            }

            groundTypeCache[col] = groundType;
            return groundType;
        }

        /// <summary>
        /// Clears all active skidmarks rendered in the scene and purges the collider cache.
        /// </summary>
        public static void ClearAll()
        {
            Skidmarks instance = GetInstance();
            if (instance != null)
            {
                instance.ClearAllSkidmarks();
            }
            groundTypeCache.Clear();
        }
    }
}