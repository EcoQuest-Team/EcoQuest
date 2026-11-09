using System;
using System.Collections.Generic;
using UnityEngine;

namespace EcoQuest.CityTraffic
{
    public enum TrafficTurnType
    {
        Straight = 0,
        Right = 1,
        Left = 2
    }

    public enum TrafficLightPhaseState
    {
        Green = 0,
        Yellow = 1,
        Red = 2
    }

    [Serializable]
    public class TrafficNode
    {
        public int id;
        public Vector3 position;
        public Vector3 forward;
        public int intersectionIndex = -1;
        public bool isIntersectionEntry;
        public bool isIntersectionExit;
        public List<int> outgoingEdges = new List<int>();
        public List<int> incomingEdges = new List<int>();
    }

    [Serializable]
    public class TrafficEdge
    {
        public int id;
        public int fromNode;
        public int toNode;
        public Vector3[] waypoints;
        public float[] cumulativeLengths;
        public float length;
        public float speedLimit = 5.5f;
        public bool isCurve;
        public bool isIntersectionArc;
        public int intersectionIndex = -1;
        public TrafficTurnType turnType = TrafficTurnType.Straight;
        public int approachDir; // 0=+Z(N), 1=+X(E), 2=-Z(S), 3=-X(W)
        public int exitDir;
        public int approachingCrosswalkIndex = -1;
        public float stopLineDistanceOnEdge = -1f;
        public bool isParkDistrict;
        public bool isRailCrossingApproach;
        public float railCrossingStopLineDist = -1f;

        public void SampleAtDistance(float dist, out Vector3 pos, out Vector3 tangent)
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                pos = Vector3.zero;
                tangent = Vector3.forward;
                return;
            }
            if (waypoints.Length == 1 || length <= 0.0001f)
            {
                pos = waypoints[0];
                tangent = Vector3.forward;
                return;
            }

            float d = Mathf.Clamp(dist, 0f, length);
            int segIndex = 0;
            if (cumulativeLengths != null && cumulativeLengths.Length == waypoints.Length)
            {
                for (int i = 0; i < cumulativeLengths.Length - 1; i++)
                {
                    if (d <= cumulativeLengths[i + 1] || i == cumulativeLengths.Length - 2)
                    {
                        segIndex = i;
                        break;
                    }
                }
                float segStart = cumulativeLengths[segIndex];
                float segLen = Mathf.Max(0.0001f, cumulativeLengths[segIndex + 1] - segStart);
                float t = Mathf.Clamp01((d - segStart) / segLen);
                Vector3 a = waypoints[segIndex];
                Vector3 b = waypoints[segIndex + 1];
                pos = Vector3.Lerp(a, b, t);
                Vector3 diff = b - a;
                diff.y = 0f;
                tangent = diff.sqrMagnitude > 0.00001f ? diff.normalized : Vector3.forward;
            }
            else
            {
                float t = Mathf.Clamp01(d / length);
                pos = Vector3.Lerp(waypoints[0], waypoints[waypoints.Length - 1], t);
                Vector3 diff = waypoints[waypoints.Length - 1] - waypoints[0];
                diff.y = 0f;
                tangent = diff.sqrMagnitude > 0.00001f ? diff.normalized : Vector3.forward;
            }
        }
    }

    [Serializable]
    public class TrafficIntersection
    {
        public int id;
        public string name;
        public Vector3 center;
        public Vector3 size = new Vector3(12f, 4f, 12f);
        public int armCount;
        public bool hasTrafficLight;
        public float signalPhaseOffset;
        public List<int> entryNodes = new List<int>();
        public List<int> exitNodes = new List<int>();
        public List<int> internalEdges = new List<int>();
    }

    [Serializable]
    public class TrafficCrosswalkZone
    {
        public int id;
        public int intersectionIndex = -1;
        public Vector3 armDirection;
        public Vector3 center;
        public Vector3 halfExtents = new Vector3(3.1f, 1.5f, 1.6f);
        public Quaternion rotation = Quaternion.identity;
        public Vector3 stopLinePosition;
        public int approachEdgeId = -1;
        public float stopLineDistAlongEdge;
    }

    [Serializable]
    public class HighwayLane
    {
        public int id;
        public string name;
        public float laneX;
        public float startZ;
        public float endZ;
        public float directionZ; // +1 = Northbound (+Z), -1 = Southbound (-Z)
        public float defaultSpeed = 9.5f;
    }

    [CreateAssetMenu(fileName = "CityTrafficGraph", menuName = "EcoQuest/Traffic/City Traffic Graph")]
    public class CityTrafficGraph : ScriptableObject
    {
        [Header("Road Graph Settings")]
        public bool driveOnRight = true;
        public bool highwayDriveOnRight = true;
        public float laneOffset = 1.5f;
        public float defaultStraightSpeed = 5.5f;
        public float parkDistrictSpeed = 3.5f;
        public float curveSpeed = 3.0f;
        public float stopLineOffsetFromCrosswalk = 2.5f;

        [Header("Traffic Light Cycle (Central 4-Way Intersections)")]
        public float greenDuration = 12.0f;
        public float yellowDuration = 3.0f;
        public float allRedDuration = 2.0f;

        [Header("Baked Graph Data")]
        public List<TrafficNode> nodes = new List<TrafficNode>();
        public List<TrafficEdge> edges = new List<TrafficEdge>();
        public List<TrafficIntersection> intersections = new List<TrafficIntersection>();
        public List<TrafficCrosswalkZone> crosswalkZones = new List<TrafficCrosswalkZone>();
        public List<HighwayLane> highwayLanes = new List<HighwayLane>();

        public struct WaiterEntry
        {
            public int vehicleId;
            public int edgeId;
            public int approachDir;
            public int exitDir;
            public TrafficTurnType turnType;
            public float arrivalTime;
        }

        [NonSerialized] private Dictionary<int, List<WaiterEntry>> _waitQueues;
        [NonSerialized] private Dictionary<int, List<WaiterEntry>> _activeOccupants;

        public void ResetRuntimeState()
        {
            if (_waitQueues == null) _waitQueues = new Dictionary<int, List<WaiterEntry>>(36);
            else _waitQueues.Clear();

            if (_activeOccupants == null) _activeOccupants = new Dictionary<int, List<WaiterEntry>>(36);
            else _activeOccupants.Clear();
        }

        private void EnsureDictionaries()
        {
            if (_waitQueues == null) _waitQueues = new Dictionary<int, List<WaiterEntry>>(36);
            if (_activeOccupants == null) _activeOccupants = new Dictionary<int, List<WaiterEntry>>(36);
        }

        /// <summary>
        /// Evaluates the 4-phase traffic light state (12s Green, 3s Yellow, 2s All-Red, 12s Green, 3s Yellow, 2s All-Red)
        /// for a given travel direction (0=+Z North, 1=+X East, 2=-Z South, 3=-X West).
        /// </summary>
        public TrafficLightPhaseState GetTrafficLightState(int intersectionIdx, int travelDir, float now)
        {
            if (intersectionIdx < 0 || intersectionIdx >= intersections.Count)
                return TrafficLightPhaseState.Green;

            var inter = intersections[intersectionIdx];
            if (!inter.hasTrafficLight)
                return TrafficLightPhaseState.Green;

            float halfCycle = greenDuration + yellowDuration + allRedDuration; // 17s
            float fullCycle = halfCycle * 2f; // 34s
            float t = Mathf.Repeat(Mathf.Max(0f, now) + inter.signalPhaseOffset, fullCycle);

            bool isNorthSouth = (travelDir == 0 || travelDir == 2);
            if (isNorthSouth)
            {
                if (t < greenDuration) return TrafficLightPhaseState.Green;
                if (t < greenDuration + yellowDuration) return TrafficLightPhaseState.Yellow;
                return TrafficLightPhaseState.Red;
            }
            else
            {
                if (t < halfCycle) return TrafficLightPhaseState.Red;
                float ewT = t - halfCycle;
                if (ewT < greenDuration) return TrafficLightPhaseState.Green;
                if (ewT < greenDuration + yellowDuration) return TrafficLightPhaseState.Yellow;
                return TrafficLightPhaseState.Red;
            }
        }

        /// <summary>
        /// Synchronized pedestrian crossing state: pedestrians walking parallel to the green vehicle axis have the WALK phase
        /// while perpendicular vehicle arms are held at RED.
        /// </summary>
        public bool IsPedestrianWalkPhase(int intersectionIdx, Vector3 armDirection, float now)
        {
            if (intersectionIdx < 0 || intersectionIdx >= intersections.Count) return true;
            var inter = intersections[intersectionIdx];
            if (!inter.hasTrafficLight) return true;

            // Crosswalk on North/South arm (armDirection.z != 0) crosses East-West road, so it walks when EW traffic is Red (NS Green)
            bool crossesNorthSouthArm = Mathf.Abs(armDirection.z) > Mathf.Abs(armDirection.x);
            int blockedVehicleDir = crossesNorthSouthArm ? 0 : 1;
            return GetTrafficLightState(intersectionIdx, blockedVehicleDir, now) == TrafficLightPhaseState.Red;
        }

        public void RegisterApproach(int intersectionIdx, int vehicleId, int edgeId, float now)
        {
            if (intersectionIdx < 0 || intersectionIdx >= intersections.Count) return;
            EnsureDictionaries();

            if (!_waitQueues.TryGetValue(intersectionIdx, out var queue))
            {
                queue = new List<WaiterEntry>(4);
                _waitQueues[intersectionIdx] = queue;
            }

            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].vehicleId == vehicleId) return;
            }

            var edge = edges[edgeId];
            queue.Add(new WaiterEntry
            {
                vehicleId = vehicleId,
                edgeId = edgeId,
                approachDir = edge.approachDir,
                exitDir = edge.exitDir,
                turnType = edge.turnType,
                arrivalTime = now
            });
        }

        public bool RequestIntersectionEntry(int intersectionIdx, int vehicleId, int edgeId, float now, float deadlockTimeout = 6.0f, bool allowYellowClearance = false)
        {
            if (intersectionIdx < 0 || intersectionIdx >= intersections.Count) return true;
            var inter = intersections[intersectionIdx];
            // 2-arm 90-degree corners have dedicated inner/outer lanes that never cross
            if (inter.armCount <= 2) return true;

            EnsureDictionaries();
            RegisterApproach(intersectionIdx, vehicleId, edgeId, now);

            if (!_activeOccupants.TryGetValue(intersectionIdx, out var occupants))
            {
                occupants = new List<WaiterEntry>(4);
                _activeOccupants[intersectionIdx] = occupants;
            }

            // Expire stale occupant reservations only after a generous timeout (> 45s) to prevent rare deadlocks without prematurely dropping stopped vehicles
            for (int i = occupants.Count - 1; i >= 0; i--)
            {
                if (now - occupants[i].arrivalTime > 45.0f)
                {
                    occupants.RemoveAt(i);
                }
            }

            // Already occupying
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i].vehicleId == vehicleId) return true;
            }

            // If this intersection is signalized (central 4-way intersections), enforce the 4-phase traffic light
            if (inter.hasTrafficLight && edgeId >= 0 && edgeId < edges.Count)
            {
                var sigState = GetTrafficLightState(intersectionIdx, edges[edgeId].approachDir, now);
                if (sigState == TrafficLightPhaseState.Red || (sigState == TrafficLightPhaseState.Yellow && !allowYellowClearance))
                {
                    return false;
                }
            }

            var queue = _waitQueues[intersectionIdx];
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].vehicleId != vehicleId && now - queue[i].arrivalTime > 24.0f)
                {
                    queue.RemoveAt(i);
                }
            }
            int myIndex = -1;
            WaiterEntry me = default;
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].vehicleId == vehicleId)
                {
                    myIndex = i;
                    me = queue[i];
                    break;
                }
            }
            if (myIndex < 0) return false;

            // Check if any current occupant conflicts with our path or is on the same edge arc
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i].edgeId == edgeId || PathsConflict(me, occupants[i]))
                {
                    return false;
                }
            }

            // Deadlock timeout: if we have been waiting >= deadlockTimeout and no conflicting occupant is inside, grant priority if we are the longest waiter
            float myWait = now - me.arrivalTime;
            if (myWait >= deadlockTimeout)
            {
                bool anyOlderWaiter = false;
                for (int i = 0; i < queue.Count; i++)
                {
                    if (queue[i].vehicleId != vehicleId && queue[i].arrivalTime < me.arrivalTime - 0.01f)
                    {
                        anyOlderWaiter = true;
                        break;
                    }
                }
                if (!anyOlderWaiter)
                {
                    GrantEntry(queue, occupants, myIndex, me, now);
                    return true;
                }
            }

            // Standard right-of-way check against other waiting vehicles
            for (int i = 0; i < queue.Count; i++)
            {
                if (i == myIndex) continue;
                var other = queue[i];
                // At signalized intersections, ignore waiters sitting on a perpendicular RED arm
                if (inter.hasTrafficLight && GetTrafficLightState(intersectionIdx, other.approachDir, now) != TrafficLightPhaseState.Green)
                {
                    continue;
                }
                if (!PathsConflict(me, other)) continue;

                float timeDiff = me.arrivalTime - other.arrivalTime;
                if (timeDiff > 0.25f)
                {
                    // Other arrived noticeably earlier
                    return false;
                }
                if (Mathf.Abs(timeDiff) <= 0.25f)
                {
                    // Left turn yields to oncoming straight/right traffic
                    int oppositeDir = (me.approachDir + 2) % 4;
                    if (me.turnType == TrafficTurnType.Left && other.approachDir == oppositeDir && other.turnType != TrafficTurnType.Left)
                    {
                        return false;
                    }
                    // Otherwise tie-break: yield to vehicle on the right
                    // If my travel direction is approachDir (0=N, 1=E, 2=S, 3=W), the road to my right approaches heading (approachDir + 3) % 4
                    int rightHandTravelDir = (me.approachDir + 3) % 4;
                    if (other.approachDir == rightHandTravelDir)
                    {
                        return false;
                    }
                    // Deterministic fallback tie-break by vehicleId if opposite approaches tie
                    if (other.approachDir == oppositeDir && me.turnType == other.turnType && other.vehicleId < me.vehicleId)
                    {
                        return false;
                    }
                }
            }

            GrantEntry(queue, occupants, myIndex, me, now);
            return true;
        }

        private static void GrantEntry(List<WaiterEntry> queue, List<WaiterEntry> occupants, int queueIndex, WaiterEntry entry, float now)
        {
            queue.RemoveAt(queueIndex);
            entry.arrivalTime = now;
            occupants.Add(entry);
        }

        public void ReleaseIntersection(int intersectionIdx, int vehicleId)
        {
            if (intersectionIdx < 0) return;
            EnsureDictionaries();

            if (_waitQueues.TryGetValue(intersectionIdx, out var queue))
            {
                for (int i = queue.Count - 1; i >= 0; i--)
                {
                    if (queue[i].vehicleId == vehicleId) queue.RemoveAt(i);
                }
            }

            if (_activeOccupants.TryGetValue(intersectionIdx, out var occupants))
            {
                for (int i = occupants.Count - 1; i >= 0; i--)
                {
                    if (occupants[i].vehicleId == vehicleId) occupants.RemoveAt(i);
                }
            }
        }

        public void ReleaseAllForVehicle(int vehicleId)
        {
            if (_waitQueues != null)
            {
                foreach (var kv in _waitQueues)
                {
                    var q = kv.Value;
                    for (int i = q.Count - 1; i >= 0; i--)
                    {
                        if (q[i].vehicleId == vehicleId) q.RemoveAt(i);
                    }
                }
            }
            if (_activeOccupants != null)
            {
                foreach (var kv in _activeOccupants)
                {
                    var o = kv.Value;
                    for (int i = o.Count - 1; i >= 0; i--)
                    {
                        if (o[i].vehicleId == vehicleId) o.RemoveAt(i);
                    }
                }
            }
        }

        public int GetActiveOccupantCount(int intersectionIdx)
        {
            if (_activeOccupants != null && _activeOccupants.TryGetValue(intersectionIdx, out var list))
                return list.Count;
            return 0;
        }

        private static bool PathsConflict(WaiterEntry a, WaiterEntry b)
        {
            if (a.approachDir == b.approachDir) return true;
            if (a.exitDir == b.exitDir) return true;

            int oppA = (a.approachDir + 2) % 4;
            if (b.approachDir == oppA)
            {
                // Opposing straight/right movements do not cross each other
                if (a.turnType != TrafficTurnType.Left && b.turnType != TrafficTurnType.Left)
                    return false;
                return true;
            }

            // Perpendicular approaches: two right turns into different exits do not cross
            if (a.turnType == TrafficTurnType.Right && b.turnType == TrafficTurnType.Right)
                return false;

            return true;
        }
    }
}
