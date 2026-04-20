using UnityEngine;

namespace G3MagnetBoots
{
    internal struct HullTarget
    {
        public Part part;
        public Rigidbody rigidbody;
        public Collider collider;
        public Vector3 hitPoint;
        public Vector3 hitNormal;
        public float hitDistance;

        // debug
        public Vector3 debugSphereOrigin;
        public float debugSphereRadius;
        public bool debugOriginInsideAny;

        public bool IsValid()
        {
            return this.part != null
                && this.collider != null
                && this.part.FindModuleImplementing<ModuleG3NoAttach>() == null // cannot attach to parts with G3NoAttachModule, added to blacklist parts from config
                && (G3MagnetBootsDifficultySettings.Current.magbootsAsteroidsEnabled || this.part.FindModuleImplementing<ModuleAsteroid>() == null);
        }
    }

    internal static class HullTargeting
    {
        public static readonly int HullMask = (LayerUtil.DefaultEquivalent | 0x8000 | 0x80000) & ~(0x20000); // exclude layer 17 EVA
        private static readonly Collider[] _overlaps = new Collider[16];
        private static readonly RaycastHit[] _hits = new RaycastHit[32];
        private static SphereCollider _probeSphere;
        private static Transform _probeXform;
        private static void EnsureProbeSphere()
        {
            if (_probeSphere != null) return;

            var go = new GameObject("G3MagBoots_SpherecastProbe");
            go.hideFlags = HideFlags.HideAndDontSave;
            _probeXform = go.transform;

            _probeSphere = go.AddComponent<SphereCollider>();
            _probeSphere.isTrigger = true;
        }

        static Vector3 ResolveSpherecastOrigin(KerbalEVA kerbal, Vector3 origin, float radius, int mask)
        {
            int count = Physics.OverlapSphereNonAlloc(origin, radius, _overlaps, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = _overlaps[i];
                if (!c) continue;
                int layer = c.gameObject.layer;
            }

            EnsureProbeSphere();

            if (count == 0) return origin;

            _probeXform.position = origin;
            _probeXform.rotation = Quaternion.identity;
            _probeSphere.radius = radius;

            Vector3 correction = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                var other = _overlaps[i];
                if (!other ) continue;

                if (kerbal != null && other.transform.IsChildOf(kerbal.transform))
                    continue;

                if (Physics.ComputePenetration(
                    _probeSphere, _probeXform.position, _probeXform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out var dir, out var dist))
                {
                    // move probe out of collider
                    correction += dir * dist;
                }
            }

            if (correction.sqrMagnitude > 0f)
                origin += correction + correction.normalized * 0.002f;

            return origin;
        }

        internal static bool TryAcquireHullSpherecast(KerbalEVA kerbal, bool doProbeRay, float upOffset, float sphereRadius, float castLength, float engageRadius, out HullTarget target)
        {
            target = default;
            if (kerbal == null || kerbal.footPivot == null) return false;

            Vector3 footPos = kerbal.footPivot.position;
            Vector3 up = kerbal.transform.up;
            Vector3 origin = footPos + up * upOffset;
            origin = ResolveSpherecastOrigin(kerbal, origin, sphereRadius, HullMask);

            bool originInsideAny = Physics.CheckSphere(kerbal.footPivot.position + (kerbal.transform.up * upOffset), sphereRadius * 0.98f, HullTargeting.HullMask, QueryTriggerInteraction.Ignore);

            int count = Physics.SphereCastNonAlloc(origin, sphereRadius, -up, _hits, castLength, HullMask, QueryTriggerInteraction.Ignore);
            if (count <= 0) return false;

            float bestDist = float.PositiveInfinity;
            HullTarget best = default;

            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null) continue;

                Part hitPart = hit.collider.GetComponentInParent<Part>();
                if (hitPart == null) continue;

                // Ignore self
                if (hitPart == kerbal.part) continue;

                // Skip disallowed parts (same rules as HullTarget.IsValid)
                if (hitPart.FindModuleImplementing<ModuleG3NoAttach>() != null) continue;
                if (!G3MagnetBootsDifficultySettings.Current.magbootsAsteroidsEnabled &&
                    hitPart.FindModuleImplementing<ModuleAsteroid>() != null) continue;


                bool originInsideThis = false;
                if (hit.collider != null)
                {
                    // If ClosestPoint(origin) is NOT origin, origin is outside (for convex; mesh colliders can be imperfect but still useful)
                    Vector3 cpO = hit.collider.ClosestPoint(origin);
                    originInsideThis = (cpO - origin).sqrMagnitude < 1e-8f;
                }


                // Select candidate point based on proximity to the foot
                Vector3 closestPoint = hit.collider.ClosestPoint(footPos);
                float dist = Vector3.Distance(footPos, closestPoint);
                if (dist > engageRadius) continue;

                // Cast a  ray from just off the surface back toward it.
                Vector3 point = closestPoint;
                Vector3 normal = hit.normal.normalized; // fallback if probe fails

                if (doProbeRay)
                {
                    Vector3 footDirToClosestPoint = (footPos - closestPoint).normalized;
                    if (Physics.Raycast(point + footDirToClosestPoint * 0.20f, -footDirToClosestPoint, out RaycastHit nHit, 0.1f, HullMask, QueryTriggerInteraction.Ignore) && nHit.collider == hit.collider)
                    {
                        point = nHit.point;
                        normal = nHit.normal.normalized;
                    }
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best.part = hitPart;
                    best.rigidbody = hitPart.rb;
                    best.collider = hit.collider;
                    best.hitPoint = point;
                    best.hitNormal = normal;
                    best.hitDistance = dist;

                    best.debugSphereOrigin = origin;
                    best.debugSphereRadius = sphereRadius;
                    best.debugOriginInsideAny = originInsideAny;
                }
            }

            if (best.part == null) return false;

            target = best;
            return true;
        }

        internal static float GetRelativeSpeedToHullPoint(this in HullTarget target, Part part)
        {
            if (part?.rb == null) return float.PositiveInfinity;
            Vector3 surfV = (target.rigidbody != null) ? target.rigidbody.GetPointVelocity(target.hitPoint) : Vector3.zero;
            return (part.rb.velocity - surfV).magnitude;
        }

        internal static Vector3 GetSurfacePointVelocity(this in HullTarget target)
        {
            if (target.rigidbody == null) return Vector3.zero;
            return target.rigidbody.GetPointVelocity(target.hitPoint);
        }
    }
}
