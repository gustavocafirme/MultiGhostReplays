using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[assembly: MelonInfo(typeof(MultiGhostReplays.MainMod), "Multi-Ghost Replays", "1.1.0", "Snyviper")]
[assembly: MelonGame("eu.Catze", "Star Drift Evolution")]

namespace MultiGhostReplays
{
    // =========================================================================
    // GHOST CONTROLLER COMPONENT
    // =========================================================================

    /// <summary>
    /// Handles frame interpolation, visual effect generation, and synchronization for individual ghost vehicles.
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

        // Custom Skidmark tracking per wheel position [4]
        private int[] lastSkidIndices = new int[4] { -1, -1, -1, -1 };
        private Vector3[] wheelOffsets = new Vector3[4]
        {
            new Vector3(-0.75f, 0.0f, 1.1f),   // Front Left
            new Vector3(0.75f, 0.0f, 1.1f),    // Front Right
            new Vector3(-0.75f, 0.0f, -1.1f),  // Rear Left
            new Vector3(0.75f, 0.0f, -1.1f)    // Rear Right
        };

        private const int TRACK_GROUND_LAYER_MASK = 1024; // Layer used in CarFX.SetCarFX

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

            // Sync visual states immediately upon component initialization
            MainMod.ApplyTrailStateForGhost(this);
            MainMod.ApplyHeadlightStateForGhost(this);
        }

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
        /// Renders custom tire skidmark meshes based on drift angles and surface raycasting.
        /// </summary>
        private void LateUpdate()
        {
            // Early exit if skidmarks feature is disabled or simulation is paused
            if (!MainMod.showSkidmarks || isPaused)
            {
                for (int i = 0; i < 4; i++) lastSkidIndices[i] = -1;
                return;
            }

            // Fetch and cache Skidmarks singleton reference to prevent repeated property lookups
            Skidmarks skidmarksInstance = MainMod.GetCachedSkidmarksInstance();
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

            // Native drift angle threshold check
            if (driftAngle > 10f)
            {
                float opacity = Mathf.Clamp01((driftAngle - 10f) / 25f);

                for (int i = 0; i < 4; i++)
                {
                    // Start raycast slightly above wheel offset to prevent premature clipping on uneven terrain
                    Vector3 wheelOrigin = transform.TransformPoint(wheelOffsets[i]) + transform.up * 0.3f;

                    // Extended raycast (1.2f) on Layer 1024 for reliable ground detection across off-road meshes
                    if (Physics.Raycast(wheelOrigin, -transform.up, out RaycastHit hit, 1.2f, TRACK_GROUND_LAYER_MASK))
                    {
                        // Retrieve mapped ground type using cached collider lookup
                        int groundType = MainMod.GetGroundTypeFromCollider(hit.collider);

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
        /// Resets playback playback state, frame indices, and skidmark tracking arrays.
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

    // =========================================================================
    // MAIN MOD CLASS
    // =========================================================================

    /// <summary>
    /// Main entry point for the Multi-Ghost Replays mod. Controls ghost spawning, UI rendering, and global state synchronization.
    /// </summary>
    public class MainMod : MelonMod
    {
        public static List<GhostController> activeGhosts = new List<GhostController>();
        public static List<ReplayData> queuedLeaderboardReplays = new List<ReplayData>();

        public static float cachedWorstReplayTime = 1000f;
        public static bool showSkidmarks = true;
        public static bool showHeadlights = true;
        public static bool showTrails = true;

        private bool showModUI = true;
        private static bool isNightTrack = false;
        private bool[] selectedEntries = new bool[10];
        private bool isDownloadingBatch = false;
        private string statusMessage = "Ready";
        private bool wasViewingReplay = false;

        private const int ACTION_STEERING_ANALOG = 0;
        private const int ACTION_RESTART = 12;
        private const int ACTION_PLAY_PAUSE = 17;

        private static FieldInfo timeField;
        private static FieldInfo speedField;
        private static FieldInfo pausedField;
        private static FieldInfo replayField;
        private static FieldInfo carToShowField;
        private static FieldInfo carToShowRbField;
        private static ReplayManager cachedReplayManager;

        private static FieldInfo emissionMaxField;
        private static FieldInfo dirtThresholdField;

        // Caching references for high-frequency runtime operations
        private static Skidmarks cachedSkidmarks;
        private static Dictionary<Collider, int> groundTypeCache = new Dictionary<Collider, int>();

        /// <summary>
        /// Initializes reflection fields and caches game engine references upon loading.
        /// </summary>
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("Multi-Ghost Replays v1.1.0 loaded successfully!");

            Type rmType = typeof(ReplayManager);
            timeField = rmType.GetField("showing_replay_time", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            speedField = rmType.GetField("playbackSpeed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            pausedField = rmType.GetField("isPaused", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            replayField = rmType.GetField("current_showing_replay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            carToShowField = rmType.GetField("car_to_show", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            carToShowRbField = rmType.GetField("car_to_show_rb", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            Type carFxType = typeof(CarFX);
            emissionMaxField = carFxType.GetField("emission_max", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            dirtThresholdField = carFxType.GetField("dirt_particle_threshold", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        /// <summary>
        /// Returns the cached Skidmarks singleton instance, lazily resolving if null.
        /// </summary>
        public static Skidmarks GetCachedSkidmarksInstance()
        {
            if (cachedSkidmarks == null)
            {
                cachedSkidmarks = Skidmarks.instance;
            }
            return cachedSkidmarks;
        }

        /// <summary>
        /// Performs an O(1) lookup to resolve mapped ground type IDs from colliders, avoiding repeated GetComponent allocations.
        /// </summary>
        /// <param name="col">The ground collider hit by the raycast.</param>
        /// <returns>The resolved ground type ID for skidmark coloring.</returns>
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
        /// Recalculates the longest duration across active ghost replays and the native replay to clamp timeline playback.
        /// </summary>
        public static void RecalculateWorstReplayTime()
        {
            float maxTime = 0f;

            foreach (var ghost in activeGhosts)
            {
                if (ghost != null && ghost.replayData != null && ghost.replayData.replay_frame_times != null)
                {
                    float t = ghost.replayData.replay_frame_times[ghost.replayData.replay_frame_times.Length - 1];
                    if (t > maxTime) maxTime = t;
                }
            }

            if (cachedReplayManager == null)
            {
                cachedReplayManager = UnityEngine.Object.FindObjectOfType<ReplayManager>();
            }

            if (cachedReplayManager != null && replayField != null)
            {
                ReplayData nativeReplay = replayField.GetValue(cachedReplayManager) as ReplayData;
                if (nativeReplay != null && nativeReplay.replay_frame_times != null && nativeReplay.replay_frame_times.Length > 0)
                {
                    float nativeTime = nativeReplay.replay_frame_times[nativeReplay.replay_frame_times.Length - 1];
                    if (nativeTime > maxTime) maxTime = nativeTime;
                }
            }

            cachedWorstReplayTime = maxTime > 0f ? maxTime : 1000f;
        }

        /// <summary>
        /// Processes frame updates, user input controls, replay state monitoring, and playback rate adjustments.
        /// </summary>
        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F5))
            {
                showModUI = !showModUI;

                if (!showModUI && GameManager.game_manager != null && GameManager.game_manager.ViewingReplay())
                {
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Locked;
                }
            }

            bool isViewing = GameManager.game_manager != null && GameManager.game_manager.ViewingReplay();

            if (isViewing && showModUI)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            if (wasViewingReplay && !isViewing)
            {
                ClearGhosts();
                ClearAllSkidmarks();
                isNightTrack = false;

                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            wasViewingReplay = isViewing;

            if (isViewing)
            {
                if (activeGhosts.Count > 0)
                {
                    SyncNativeReplayManagerTime();
                }

                if (queuedLeaderboardReplays.Count > 0)
                {
                    foreach (var data in queuedLeaderboardReplays)
                    {
                        SpawnGhostFromData(data);
                    }
                    queuedLeaderboardReplays.Clear();

                    RecalculateWorstReplayTime();
                    MelonCoroutines.Start(ForceInitialSyncReset());
                }
            }

            if (GameManager.player == null || activeGhosts.Count == 0) return;

            if (GameManager.player.GetButtonDown(ACTION_PLAY_PAUSE))
            {
                foreach (var ghost in activeGhosts)
                {
                    if (ghost == null) continue;
                    if (ghost.isPaused && ghost.currentFrame <= 1 && ghost.currentTime <= 0.05f) continue;
                    ghost.isPaused = !ghost.isPaused;
                }
            }

            if (GameManager.player.GetButtonDown(ACTION_RESTART))
            {
                ClearAllSkidmarks();
                foreach (var ghost in activeGhosts)
                {
                    if (ghost != null) ghost.Restart();
                }
            }

            float analogInput = 0f;
            if (!CameraFollow.InFreeFlyMode())
            {
                analogInput = GameManager.player.GetAxis(ACTION_STEERING_ANALOG);
            }

            float deadzone = 0.25f;
            foreach (var ghost in activeGhosts)
            {
                if (ghost == null) continue;

                float baseSpeed = ghost.isPaused ? 0f : 1f;
                if (analogInput < -deadzone)
                {
                    float multiplier = ghost.isPaused ? 2.66666f : 1.33333f;
                    baseSpeed += (analogInput + deadzone) * multiplier;
                }
                else if (analogInput > deadzone)
                {
                    baseSpeed += (analogInput - deadzone) * 1.33333f;
                }

                ghost.playbackSpeed = baseSpeed;
            }
        }

        /// <summary>
        /// Synchronizes the game's internal ReplayManager timeline and playback state with the master ghost controller.
        /// </summary>
        private void SyncNativeReplayManagerTime()
        {
            if (activeGhosts.Count == 0) return;

            if (cachedReplayManager == null)
            {
                cachedReplayManager = UnityEngine.Object.FindObjectOfType<ReplayManager>();
                if (cachedReplayManager == null) return;
            }

            GhostController masterGhost = activeGhosts[0];
            if (masterGhost == null) return;

            if (timeField == null || replayField == null) return;

            ReplayData nativeReplay = replayField.GetValue(cachedReplayManager) as ReplayData;
            Transform carToShow = carToShowField != null ? carToShowField.GetValue(cachedReplayManager) as Transform : null;
            Rigidbody carToShowRb = carToShowRbField != null ? carToShowRbField.GetValue(cachedReplayManager) as Rigidbody : null;

            if (nativeReplay != null && nativeReplay.replay_frame_times != null && nativeReplay.replay_frame_times.Length > 0)
            {
                float nativeSelfTime = nativeReplay.replay_frame_times[nativeReplay.replay_frame_times.Length - 1];
                float clampedNativeTime = Mathf.Min(masterGhost.currentTime, nativeSelfTime);

                timeField.SetValue(cachedReplayManager, clampedNativeTime);

                if (speedField != null) speedField.SetValue(cachedReplayManager, masterGhost.playbackSpeed);
                if (pausedField != null) pausedField.SetValue(cachedReplayManager, masterGhost.isPaused);

                if (masterGhost.currentTime >= nativeSelfTime && (carToShow != null || carToShowRb != null))
                {
                    Vector3 finalPos = nativeReplay.car_positions[nativeReplay.car_positions.Length - 1];
                    Quaternion finalRot = nativeReplay.car_rotations[nativeReplay.car_rotations.Length - 1];

                    if (carToShowRb != null)
                    {
                        carToShowRb.MovePosition(finalPos);
                        carToShowRb.MoveRotation(finalRot);
                    }
                    else if (carToShow != null)
                    {
                        carToShow.position = finalPos;
                        carToShow.rotation = finalRot;
                    }
                }
            }
        }

        /// <summary>
        /// Enforces initial position resets for spawned ghosts at the end of the frame to ensure visual frame alignment.
        /// </summary>
        private IEnumerator ForceInitialSyncReset()
        {
            yield return new WaitForEndOfFrame();
            ClearAllSkidmarks();
            foreach (var ghost in activeGhosts)
            {
                if (ghost != null)
                {
                    ghost.Restart();
                }
            }
            MelonLogger.Msg("[Mod] Initial startup synchronization completed!");
        }

        /// <summary>
        /// Disables native car renderers, lights, and effects to allow multi-ghost instance overrides.
        /// </summary>
        private void SuppressNativeCarEffects()
        {
            CarCosmetics[] allCars = UnityEngine.Object.FindObjectsOfType<CarCosmetics>();

            foreach (var car in allCars)
            {
                if (car == null || car.gameObject == null) continue;

                GhostController ghost = car.GetComponentInParent<GhostController>();

                if (ghost == null)
                {
                    Renderer[] renderers = car.GetComponentsInChildren<Renderer>();
                    foreach (var r in renderers)
                    {
                        if (r.enabled) r.enabled = false;
                    }

                    Light[] lights = car.GetComponentsInChildren<Light>();
                    foreach (var l in lights)
                    {
                        if (l.enabled)
                        {
                            isNightTrack = true;
                            l.enabled = false;
                        }
                    }

                    CarFX carFx = car.GetComponent<CarFX>();
                    if (carFx != null)
                    {
                        if (emissionMaxField != null) emissionMaxField.SetValue(carFx, 0f);
                        if (dirtThresholdField != null) dirtThresholdField.SetValue(carFx, 999999f);

                        carFx.SetTrailState(false);
                    }
                }
            }
        }

        /// <summary>
        /// Applies the global headlight toggle state to a specific ghost instance.
        /// </summary>
        /// <param name="ghost">Target ghost controller.</param>
        public static void ApplyHeadlightStateForGhost(GhostController ghost)
        {
            if (ghost == null || ghost.cachedLights == null) return;

            foreach (var l in ghost.cachedLights)
            {
                if (l != null) l.enabled = isNightTrack && showHeadlights;
            }
        }

        /// <summary>
        /// Updates headlight activation across all currently spawned ghost instances.
        /// </summary>
        private void ApplyHeadlightState()
        {
            foreach (var ghost in activeGhosts)
            {
                ApplyHeadlightStateForGhost(ghost);
            }
        }

        /// <summary>
        /// Configures particle emission variables on the target ghost's CarFX component according to global settings.
        /// </summary>
        /// <param name="ghost">Target ghost controller.</param>
        public static void ApplyTrailStateForGhost(GhostController ghost)
        {
            if (ghost == null || ghost.carFX == null) return;

            float targetEmission = showTrails ? (22f * (float)QualitySettings.GetQualityLevel()) : 0f;
            float targetDirtThreshold = showTrails ? 0.05f : 999999f;

            if (emissionMaxField != null) emissionMaxField.SetValue(ghost.carFX, targetEmission);
            if (dirtThresholdField != null) dirtThresholdField.SetValue(ghost.carFX, targetDirtThreshold);
        }

        /// <summary>
        /// Applies global particle trail emission settings to all active ghosts.
        /// </summary>
        private void ApplyTrailState()
        {
            foreach (var ghost in activeGhosts)
            {
                ApplyTrailStateForGhost(ghost);
            }
        }

        /// <summary>
        /// Clears all generated skidmarks from the global mesh renderer and clears the collider ground-type cache.
        /// </summary>
        public static void ClearAllSkidmarks()
        {
            Skidmarks skidmarksInstance = GetCachedSkidmarksInstance();
            if (skidmarksInstance != null)
            {
                skidmarksInstance.ClearAllSkidmarks();
            }
            groundTypeCache.Clear();
        }

        /// <summary>
        /// Renders the mod's OnGUI overlay interface and selection checkboxes.
        /// </summary>
        public override void OnGUI()
        {
            if (!showModUI) return;

            float topBoxWidth = 280f;
            float topBoxX = (Screen.width - topBoxWidth) / 2f;
            GUI.Box(new Rect(topBoxX, 20, topBoxWidth, 48), "");

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.alignment = TextAnchor.MiddleCenter;
            statusStyle.fontSize = 12;
            statusStyle.normal.textColor = Color.white;

            GUI.Label(new Rect(topBoxX, 22, topBoxWidth, 44), $"Multi-Ghost Mod: {statusMessage}\n<size=10><color=#AAAAAA>Press F5 to toggle UI</color></size>", statusStyle);

            if (GameManager.game_manager != null &&
                (GameManager.game_manager.menu_state == GameManager.MENU_STATE_TRACK_SELECTION_QUICK_RACE ||
                 GameManager.game_manager.menu_state == GameManager.MENU_STATE_DAILY_QUEST) &&
                GameManager.game_manager.replay_car_option == GameManager.REPLAY_LEADERBOARD_SELECT)
            {
                DrawLeaderboardCheckboxes();
            }

            if (GameManager.game_manager != null && GameManager.game_manager.ViewingReplay())
            {
                if (activeGhosts.Count > 0)
                {
                    float specialBoxWidth = 220f;
                    float specialBoxX = (Screen.width - specialBoxWidth) / 2f;

                    float boxHeight = isNightTrack ? 180f : 150f;
                    GUI.Box(new Rect(specialBoxX, 70, specialBoxWidth, boxHeight), "Special Ghosts");

                    float currentY = 95f;

                    if (GUI.Button(new Rect(specialBoxX + 10, currentY, 200, 25), "Spawn Personal Best"))
                    {
                        SpawnPersonalBestGhost();
                    }
                    currentY += 30f;

                    if (isNightTrack)
                    {
                        string headlightLabel = showHeadlights ? "Headlights: ON" : "Headlights: OFF";
                        if (GUI.Button(new Rect(specialBoxX + 10, currentY, 200, 25), headlightLabel))
                        {
                            showHeadlights = !showHeadlights;
                            ApplyHeadlightState();
                        }
                        currentY += 30f;
                    }

                    string trailLabel = showTrails ? "Smoke Trails: ON" : "Smoke Trails: OFF";
                    if (GUI.Button(new Rect(specialBoxX + 10, currentY, 200, 25), trailLabel))
                    {
                        showTrails = !showTrails;
                        ApplyTrailState();
                    }
                    currentY += 30f;

                    string skidmarkLabel = showSkidmarks ? "Skidmarks: ON" : "Skidmarks: OFF";
                    if (GUI.Button(new Rect(specialBoxX + 10, currentY, 200, 25), skidmarkLabel))
                    {
                        showSkidmarks = !showSkidmarks;
                    }
                    currentY += 30f;

                    if (GUI.Button(new Rect(specialBoxX + 10, currentY, 200, 25), "Clear All"))
                    {
                        ClearGhosts();
                        ClearAllSkidmarks();
                    }
                }
            }
        }

        /// <summary>
        /// Renders the custom leaderboard selection menu overlay for downloading multi-replay packages.
        /// </summary>
        private void DrawLeaderboardCheckboxes()
        {
            var steamFuncs = GameManager.game_manager.steam_functions;
            if (steamFuncs == null) return;

            IList entries = GetLeaderboardEntriesList(steamFuncs);
            if (entries == null) return;

            float scaleX = Screen.width / 1920f;
            float scaleY = Screen.height / 1080f;

            float panelWidth = (float)Math.Round(128f * scaleX);
            float panelX = Screen.width - (float)Math.Round(465f * scaleX) - panelWidth;
            float panelY = Screen.height - (float)Math.Round(445f * scaleY);
            float panelHeight = (float)Math.Round(439f * scaleY);

            GUI.Box(new Rect(panelX, panelY, panelWidth, panelHeight), "");

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontSize = (int)Math.Round(16f * scaleX);
            titleStyle.normal.textColor = Color.white;

            float titleOffsetX = (float)Math.Round(12f * scaleX);
            float titleOffsetY = (float)Math.Round(8f * scaleY);
            float titleWidth = panelWidth - ((float)Math.Round(24f * scaleX));
            float titleHeight = (float)Math.Round(38f * scaleY);

            GUI.Label(new Rect(panelX + titleOffsetX, panelY + titleOffsetY, titleWidth, titleHeight), "Replay\nSelection", titleStyle);

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle.fontSize = (int)Math.Round(14f * scaleX);

            float btnOffsetY = (float)Math.Round(54f * scaleY);
            float btnHeight = (float)Math.Round(42f * scaleY);

            if (!isDownloadingBatch && GUI.Button(new Rect(panelX + titleOffsetX, panelY + btnOffsetY, titleWidth, btnHeight), "LOAD\nSELECTED", buttonStyle))
            {
                MelonCoroutines.Start(DownloadAndLaunchSelectedReplays(steamFuncs, entries));
            }

            GUIStyle replayLabelStyle = new GUIStyle(GUI.skin.label);
            replayLabelStyle.alignment = TextAnchor.MiddleLeft;
            replayLabelStyle.fontSize = (int)Math.Round(15f * scaleX);
            replayLabelStyle.normal.textColor = Color.white;

            GUIStyle checkMarkStyle = new GUIStyle(GUI.skin.label);
            checkMarkStyle.alignment = TextAnchor.MiddleCenter;
            checkMarkStyle.fontSize = (int)Math.Round(16f * scaleX);
            checkMarkStyle.fontStyle = FontStyle.Bold;
            checkMarkStyle.normal.textColor = Color.green;

            int entryCount = Mathf.Min(10, entries.Count);
            float startY = panelY + (float)Math.Round(103f * scaleY);
            float rowHeight = (float)Math.Round(33f * scaleY);

            float labelWidth = (float)Math.Round(93f * scaleX);
            float labelHeight = (float)Math.Round(28f * scaleY);

            float toggleOffsetX = (float)Math.Round((115f - 18f) * scaleX);
            float toggleOffsetY = (float)Math.Round(4f * scaleY);
            float toggleSize = (float)Math.Round(20f * scaleX);

            for (int i = 0; i < entryCount; i++)
            {
                float currentY = startY + (i * rowHeight);

                GUI.Label(new Rect(panelX + titleOffsetX, currentY, labelWidth, labelHeight), $"Replay #{i + 1}", replayLabelStyle);

                Rect toggleRect = new Rect(panelX + toggleOffsetX, currentY + toggleOffsetY, toggleSize, toggleSize);

                GUI.Box(toggleRect, "");

                if (selectedEntries[i])
                {
                    GUI.Label(toggleRect, "✓", checkMarkStyle);
                }

                if (GUI.Button(toggleRect, "", GUIStyle.none))
                {
                    selectedEntries[i] = !selectedEntries[i];
                }
            }
        }

        /// <summary>
        /// Coroutine that iterates over selected entries, fetches user-generated replay data via Steam API, and queues them for instantiation.
        /// </summary>
        private IEnumerator DownloadAndLaunchSelectedReplays(SteamFunctions steamFuncs, IList entries)
        {
            isDownloadingBatch = true;
            queuedLeaderboardReplays.Clear();

            int trackID = TrackManager.current_track_id;

            for (int i = 0; i < selectedEntries.Length; i++)
            {
                if (selectedEntries[i] && i < entries.Count)
                {
                    statusMessage = $"Downloading #{i + 1}...";

                    ClearDownloadedUGCBuffer();

                    SteamFunctions.selectedLeaderboardEntry = i;
                    steamFuncs.DownloadUGCForSelectedLeaderboardEntry();

                    float timeout = 4.0f;
                    ReplayData downloaded = null;

                    while (timeout > 0f)
                    {
                        yield return new WaitForSeconds(0.15f);
                        downloaded = SteamFunctions.GetDownloadedUGCReplay(trackID);

                        if (downloaded != null) break;
                        timeout -= 0.15f;
                    }

                    if (downloaded != null)
                    {
                        queuedLeaderboardReplays.Add(downloaded);
                        MelonLogger.Msg($"[Mod] Replay #{i + 1} ({downloaded.playername}) downloaded and queued.");
                    }
                    else
                    {
                        MelonLogger.Warning($"[Mod] Replay #{i + 1} failed or timed out.");
                    }
                }
            }

            if (queuedLeaderboardReplays.Count > 0)
            {
                statusMessage = $"{queuedLeaderboardReplays.Count} Replays Ready!";
                isDownloadingBatch = false;

                GameManager.game_manager.MainMenuButtonPressed(5);
            }
            else
            {
                statusMessage = "Failed to download selections!";
                isDownloadingBatch = false;
            }
        }

        /// <summary>
        /// Clears the cached downloaded UGC replay static buffer field prior to issuing a new download request.
        /// </summary>
        private void ClearDownloadedUGCBuffer()
        {
            FieldInfo field = typeof(SteamFunctions).GetField("downloadedUGCReplay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(null, null);
            }
        }

        /// <summary>
        /// Fetches the raw leaderboard entries list via reflection from SteamFunctions.
        /// </summary>
        private IList GetLeaderboardEntriesList(SteamFunctions steamFuncs)
        {
            FieldInfo field = typeof(SteamFunctions).GetField("leaderboardEntries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null ? field.GetValue(steamFuncs) as IList : null;
        }

        /// <summary>
        /// Instantiates a ghost car GameObject from supplied replay data and attaches the GhostController component.
        /// </summary>
        /// <param name="data">The target replay data object.</param>
        private void SpawnGhostFromData(ReplayData data)
        {
            if (data == null) return;

            Transform carTransform = ReplayManager.CreateReplayCar(data, false, data.car_positions[0], data.car_rotations[0]);
            if (carTransform != null)
            {
                GameObject carGo = carTransform.gameObject;

                GhostController controller = carGo.AddComponent<GhostController>();
                controller.replayData = data;

                if (activeGhosts.Count > 0)
                {
                    GhostController referenceGhost = activeGhosts[0];
                    if (referenceGhost != null)
                    {
                        controller.currentTime = referenceGhost.currentTime;
                        controller.currentFrame = referenceGhost.currentFrame;
                        controller.isPaused = referenceGhost.isPaused;
                        controller.playbackSpeed = referenceGhost.playbackSpeed;
                    }
                }

                activeGhosts.Add(controller);

                SuppressNativeCarEffects();
                RecalculateWorstReplayTime();

                MelonLogger.Msg($"Ghost for {data.playername} spawned synchronized!");
            }
        }

        /// <summary>
        /// Spawns a ghost vehicle corresponding to the player's personal record for the current track.
        /// </summary>
        private void SpawnPersonalBestGhost()
        {
            TrackRecordData trd = GameManager.save_data.GetTrackRecordDataByID(TrackManager.current_track_id);
            if (trd != null && trd.personal_record_replay != null)
            {
                SpawnGhostFromData(trd.personal_record_replay);
            }
            else
            {
                MelonLogger.Warning("[Mod] Personal Best replay not found.");
            }
        }

        /// <summary>
        /// Destroys all active ghost GameObjects, clears references, resets native car effects, and purges scene skidmarks.
        /// </summary>
        private void ClearGhosts()
        {
            CarCosmetics[] allCars = UnityEngine.Object.FindObjectsOfType<CarCosmetics>();
            float defaultEmission = 22f * (float)QualitySettings.GetQualityLevel();

            foreach (var car in allCars)
            {
                if (car == null || car.gameObject == null) continue;

                GhostController ghost = car.GetComponentInParent<GhostController>();
                if (ghost == null)
                {
                    CarFX carFx = car.GetComponent<CarFX>();
                    if (carFx != null)
                    {
                        if (emissionMaxField != null) emissionMaxField.SetValue(carFx, defaultEmission);
                        if (dirtThresholdField != null) dirtThresholdField.SetValue(carFx, 0.05f);
                    }
                }
            }

            foreach (var ghost in activeGhosts)
            {
                if (ghost != null && ghost.gameObject != null)
                {
                    UnityEngine.Object.Destroy(ghost.gameObject);
                }
            }
            activeGhosts.Clear();
            ClearAllSkidmarks();
            isNightTrack = false;
            cachedWorstReplayTime = 1000f;
            MelonLogger.Msg("Ghosts cleared successfully.");
        }
    }
}