using UnityEngine;

namespace MultiGhostReplays
{
    /// <summary>
    /// Handles frame interpolation, visual effect generation (skidmarks, smoke, headlights), 
    /// and playback synchronization for individual ghost vehicles.
    /// </summary>
    public class GhostController : MonoBehaviour
    {
        public ReplayData replayData;
        public Rigidbody rb;
        public CarCosmetics carCosmetics;
        public CarFX carFX;
        public Light[] cachedLights;
        public WheelCollider[] cachedWheels;

        public float currentTime = 0f;
        public int currentFrame = 0;
        public bool isPaused = false;
        public float playbackSpeed = 1f;

        private int cachedFrameCount = 0;
        private float cachedSelfTime = 0f;

        // Custom Skidmark tracking index array per wheel position [0: FL, 1: FR, 2: RL, 3: RR]
        private int[] lastSkidIndices = new int[4] { -1, -1, -1, -1 };

        // Relative local offsets for wheel contact points
        private Vector3[] wheelOffsets = new Vector3[4]
        {
            new Vector3(-0.75f, 0.0f, 1.1f),   // Front Left
            new Vector3(0.75f, 0.0f, 1.1f),    // Front Right
            new Vector3(-0.75f, 0.0f, -1.1f),  // Rear Left
            new Vector3(0.75f, 0.0f, -1.1f)    // Rear Right
        };

        private const int TRACK_GROUND_LAYER_MASK = 1024; // Physics layer mask reserved for track ground colliders

        /// <summary>
        /// Initializes component references, caches replay frame parameters, applies transparency, 
        /// and synchronizes initial visual FX states upon instantiation.
        /// </summary>
        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            carCosmetics = GetComponent<CarCosmetics>();
            carFX = GetComponent<CarFX>();

            cachedLights = GetComponentsInChildren<Light>(true);
            cachedWheels = GetComponentsInChildren<WheelCollider>(true);

            if (replayData != null && replayData.replay_frame_times != null && replayData.replay_frame_times.Length > 0)
            {
                cachedFrameCount = replayData.replay_frame_times.Length;
                cachedSelfTime = replayData.replay_frame_times[cachedFrameCount - 1];
            }

            if (carCosmetics != null)
            {
                CarCosmetics.MakeCarTransparent(gameObject, GameManager.ghostCarTransparency);
            }

            // Immediately apply active global mod settings for headlights and smoke trails
            MainMod.ApplyTrailStateForGhost(this);
            MainMod.ApplyHeadlightStateForGhost(this);
        }

        /// <summary>
        /// Advances the ghost replay timeline and linearly interpolates/spheric-interpolates transform positions between recorded frames.
        /// </summary>
        private void FixedUpdate()
        {
            if (replayData == null || rb == null || cachedFrameCount == 0) return;

            float worstTime = MainMod.cachedWorstReplayTime;

            currentTime = Mathf.Clamp(currentTime + Time.fixedDeltaTime * playbackSpeed, 0f, worstTime);

            if (currentTime >= worstTime && playbackSpeed >= 0f)
            {
                isPaused = true;
            }

            float clampedReplayTime = Mathf.Min(currentTime, cachedSelfTime);

            if (currentFrame >= cachedFrameCount - 1 || clampedReplayTime < replayData.replay_frame_times[currentFrame])
            {
                currentFrame = 0;
            }

            while (currentFrame < cachedFrameCount - 1 &&
                   clampedReplayTime > replayData.replay_frame_times[currentFrame + 1])
            {
                currentFrame++;
            }

            if (clampedReplayTime >= cachedSelfTime)
            {
                rb.MovePosition(replayData.car_positions[cachedFrameCount - 1]);
                rb.MoveRotation(replayData.car_rotations[cachedFrameCount - 1]);
                return;
            }

            float currentFrameTime = replayData.replay_frame_times[currentFrame];
            float nextFrameTime = replayData.replay_frame_times[currentFrame + 1];
            float frameDelta = nextFrameTime - currentFrameTime;

            if (frameDelta > 0f)
            {
                float t = (clampedReplayTime - currentFrameTime) / frameDelta;

                Vector3 targetPos = Vector3.Lerp(replayData.car_positions[currentFrame], replayData.car_positions[currentFrame + 1], t);
                Quaternion targetRot = Quaternion.Slerp(replayData.car_rotations[currentFrame], replayData.car_rotations[currentFrame + 1], t);

                rb.MovePosition(targetPos);
                rb.MoveRotation(targetRot);
            }
        }

        /// <summary>
        /// Evaluates vehicle lateral velocity vectors and generates tire skidmarks when exceeding the native drift angle threshold (10deg).
        /// </summary>
        private void LateUpdate()
        {
            // Early exit if skidmarks feature is disabled globally or simulation is paused
            if (!MainMod.showSkidmarks || isPaused)
            {
                for (int i = 0; i < 4; i++) lastSkidIndices[i] = -1;
                return;
            }

            // Fetch cached Skidmarks singleton instance to prevent costly UnityEngine.Object property accesses
            Skidmarks skidmarksInstance = SkidmarkManager.GetInstance();
            if (skidmarksInstance == null)
            {
                for (int i = 0; i < 4; i++) lastSkidIndices[i] = -1;
                return;
            }

            Vector3 velocity = rb.velocity;
            float speed = velocity.magnitude;

            if (speed < 1f)
            {
                for (int i = 0; i < 4; i++) lastSkidIndices[i] = -1;
                return;
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            velocity.y = 0f;

            float driftAngle = Vector3.Angle(velocity, forward);

            // Native game drift angle threshold (10 degrees)
            if (driftAngle > 10f)
            {
                float opacity = Mathf.Clamp01((driftAngle - 10f) / 25f);

                for (int i = 0; i < 4; i++)
                {
                    // Elevate raycast origin slightly (+0.3f) to accommodate uneven ground colliders on off-road tracks
                    Vector3 wheelOrigin = transform.TransformPoint(wheelOffsets[i]) + transform.up * 0.3f;

                    // Execute extended raycast (1.2f) filtered by ground layer mask 1024
                    if (Physics.Raycast(wheelOrigin, -transform.up, out RaycastHit hit, 1.2f, TRACK_GROUND_LAYER_MASK))
                    {
                        // Resolve surface type ID via O(1) dictionary cache
                        int groundType = SkidmarkManager.GetGroundTypeFromCollider(hit.collider);

                        lastSkidIndices[i] = skidmarksInstance.AddSkidMark(hit.point, hit.normal, opacity, lastSkidIndices[i], 1f, groundType);
                    }
                    else
                    {
                        lastSkidIndices[i] = -1;
                    }
                }
            }
            else
            {
                for (int i = 0; i < 4; i++) lastSkidIndices[i] = -1;
            }
        }

        /// <summary>
        /// Resets ghost timeline positioning, playback speed, frame indices, and skidmark continuity trackers.
        /// </summary>
        public void Restart()
        {
            currentTime = 0f;
            currentFrame = 0;
            isPaused = false;

            for (int i = 0; i < 4; i++) lastSkidIndices[i] = -1;

            if (replayData != null && rb != null && replayData.car_positions.Length > 0)
            {
                rb.position = replayData.car_positions[0];
                rb.rotation = replayData.car_rotations[0];
            }
        }
    }
}