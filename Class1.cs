using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[assembly: MelonInfo(typeof(MultiGhostReplays.MainMod), "Multi-Ghost Replays", "1.0.0", "Snyviper")]
[assembly: MelonGame("eu.Catze", "Star Drift Evolution")]

namespace MultiGhostReplays
{
    // =========================================================================
    // GHOST CONTROLLER COMPONENT
    // =========================================================================

    /// <summary>
    /// Manages playback interpolation, frame timing, and transforms for an individual ghost vehicle.
    /// </summary>
    public class GhostController : MonoBehaviour
    {
        public ReplayData replayData;
        public Rigidbody rb;
        public CarCosmetics carCosmetics;

        public float currentTime = 0f;
        public int currentFrame = 0;
        public bool isPaused = false;
        public float playbackSpeed = 1f;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            carCosmetics = GetComponent<CarCosmetics>();

            // Apply global ghost car transparency settings
            if (carCosmetics != null)
            {
                CarCosmetics.MakeCarTransparent(gameObject, GameManager.ghostCarTransparency);
            }
        }

        private void FixedUpdate()
        {
            if (replayData == null || rb == null) return;

            float selfTime = replayData.replay_frame_times[replayData.replay_frame_times.Length - 1];
            float worstTime = MainMod.GetWorstReplayTime();

            // Advance playback time based on current speed scale, clamped to the longest active ghost duration
            currentTime = Mathf.Clamp(currentTime + Time.fixedDeltaTime * playbackSpeed, 0f, worstTime);

            if (currentTime >= worstTime && playbackSpeed >= 0f)
            {
                isPaused = true;
            }

            float clampedReplayTime = Mathf.Min(currentTime, selfTime);

            // Locate active frame index matching playback time
            currentFrame = 0;
            while (currentFrame < replayData.replay_frame_times.Length - 1 &&
                   clampedReplayTime > replayData.replay_frame_times[currentFrame + 1])
            {
                currentFrame++;
            }

            // Lock ghost to final frame if replay completed
            if (clampedReplayTime >= selfTime)
            {
                Vector3 finalPos = replayData.car_positions[replayData.car_positions.Length - 1];
                Quaternion finalRot = replayData.car_rotations[replayData.car_rotations.Length - 1];

                rb.MoveRotation(finalRot);
                rb.MovePosition(finalPos);
                return;
            }

            // Interpolate position and rotation between current and next recorded frames
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
        /// Resets ghost state and moves vehicle back to starting position.
        /// </summary>
        public void Restart()
        {
            currentTime = 0f;
            currentFrame = 0;
            isPaused = false;

            if (replayData != null && rb != null)
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
    /// Handles replay downloading, multi-ghost lifetime management, input controls, and IMGUI rendering.
    /// </summary>
    public class MainMod : MelonMod
    {
        // --- PUBLIC DATA & MOD STATE ---
        public static List<GhostController> activeGhosts = new List<GhostController>();
        public static List<ReplayData> queuedLeaderboardReplays = new List<ReplayData>();

        private bool showModUI = true;
        private bool[] selectedEntries = new bool[10];
        private bool isDownloadingBatch = false;
        private string statusMessage = "Ready";
        private bool wasViewingReplay = false;

        // --- REWIRTED / INPUT ENGINE ACTION MAPPINGS ---
        private const int ACTION_STEERING_ANALOG = 0;
        private const int ACTION_RESTART = 12;
        private const int ACTION_PLAY_PAUSE = 17;

        // --- REFLECTION CACHE ---
        private static FieldInfo timeField;
        private static FieldInfo speedField;
        private static FieldInfo pausedField;
        private static FieldInfo replayField;
        private static FieldInfo carToShowField;
        private static FieldInfo carToShowRbField;
        private static ReplayManager cachedReplayManager;

        /// <summary>
        /// Mod initialization lifecycle method. Caches reflection metadata once at startup.
        /// </summary>
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("Multi-Ghost Replays v1.0.0 loaded successfully!");

            // Pre-cache reflection handles to avoid runtime overhead during playback loops
            Type rmType = typeof(ReplayManager);
            timeField = rmType.GetField("showing_replay_time", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            speedField = rmType.GetField("playbackSpeed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            pausedField = rmType.GetField("isPaused", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            replayField = rmType.GetField("current_showing_replay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            carToShowField = rmType.GetField("car_to_show", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            carToShowRbField = rmType.GetField("car_to_show_rb", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        /// <summary>
        /// Evaluates all active ghost tracks to determine the longest total replay duration.
        /// </summary>
        public static float GetWorstReplayTime()
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

            ReplayManager rm = UnityEngine.Object.FindObjectOfType<ReplayManager>();
            if (rm != null)
            {
                FieldInfo field = typeof(ReplayManager).GetField("current_showing_replay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (field != null)
                {
                    ReplayData nativeReplay = field.GetValue(rm) as ReplayData;
                    if (nativeReplay != null && nativeReplay.replay_frame_times != null && nativeReplay.replay_frame_times.Length > 0)
                    {
                        float nativeTime = nativeReplay.replay_frame_times[nativeReplay.replay_frame_times.Length - 1];
                        if (nativeTime > maxTime) maxTime = nativeTime;
                    }
                }
            }

            return maxTime > 0f ? maxTime : 1000f;
        }

        // =========================================================================
        // UPDATE & PLAYBACK CONTROL LOOPS
        // =========================================================================

        public override void OnUpdate()
        {
            // Toggle Mod UI visibility via F5
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

            // Retain un-locked cursor when viewing replay alongside mod interface
            if (isViewing && showModUI)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            // Cleanup ghosts and reset cursor state upon exiting replay mode
            if (wasViewingReplay && !isViewing)
            {
                ClearGhosts();

                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            wasViewingReplay = isViewing;

            if (isViewing)
            {
                if (activeGhosts.Count > 0)
                {
                    SuppressNativeCarEffects();
                    SyncNativeReplayManagerTime();
                }

                // Instantiate queued ghosts upon entering replay mode
                if (queuedLeaderboardReplays.Count > 0)
                {
                    foreach (var data in queuedLeaderboardReplays)
                    {
                        SpawnGhostFromData(data);
                    }
                    queuedLeaderboardReplays.Clear();

                    MelonCoroutines.Start(ForceInitialSyncReset());
                }
            }

            if (GameManager.player == null || activeGhosts.Count == 0) return;

            // Handle global play / pause toggling
            if (GameManager.player.GetButtonDown(ACTION_PLAY_PAUSE))
            {
                foreach (var ghost in activeGhosts)
                {
                    if (ghost == null) continue;
                    if (ghost.isPaused && ghost.currentFrame <= 1 && ghost.currentTime <= 0.05f) continue;
                    ghost.isPaused = !ghost.isPaused;
                }
            }

            // Handle global restart command
            if (GameManager.player.GetButtonDown(ACTION_RESTART))
            {
                foreach (var ghost in activeGhosts)
                {
                    if (ghost != null) ghost.Restart();
                }
            }

            // Evaluate analog steering input to dynamically scrub or scale playback speed
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
        /// Synchronizes native game ReplayManager state with the master ghost timeline.
        /// </summary>
        private void SyncNativeReplayManagerTime()
        {
            if (activeGhosts.Count == 0) return;

            // Reuse cached ReplayManager instance to eliminate search overhead
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

                // Lock native vehicle transform once its personal replay time concludes
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
        /// Forces initial frame alignment after spawning new ghosts.
        /// </summary>
        private IEnumerator ForceInitialSyncReset()
        {
            yield return new WaitForEndOfFrame();
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
        /// Suppresses renderers, particle emissions, and trail effects on native non-ghost cars.
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

                    ParticleSystem[] particles = car.GetComponentsInChildren<ParticleSystem>();
                    foreach (var p in particles)
                    {
                        if (p.isPlaying) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }

                    TrailRenderer[] trails = car.GetComponentsInChildren<TrailRenderer>();
                    foreach (var t in trails)
                    {
                        if (t.enabled)
                        {
                            t.enabled = false;
                            t.Clear();
                        }
                    }
                }
            }
        }

        // =========================================================================
        // USER INTERFACE & IMGUI RENDERING
        // =========================================================================

        public override void OnGUI()
        {
            if (!showModUI) return;

            float topBoxWidth = 280f;
            float topBoxX = (Screen.width - topBoxWidth) / 2f;
            GUI.Box(new Rect(topBoxX, 20, topBoxWidth, 48), "");

            // Top status message style
            GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.alignment = TextAnchor.MiddleCenter;
            statusStyle.fontSize = 12;
            statusStyle.normal.textColor = Color.white;

            GUI.Label(new Rect(topBoxX, 22, topBoxWidth, 44), $"Multi-Ghost Mod: {statusMessage}\n<size=10><color=#AAAAAA>Press F5 to toggle UI</color></size>", statusStyle);

            // Render selection panel when leaderboards are active
            if (GameManager.game_manager != null &&
                (GameManager.game_manager.menu_state == GameManager.MENU_STATE_TRACK_SELECTION_QUICK_RACE ||
                 GameManager.game_manager.menu_state == GameManager.MENU_STATE_DAILY_QUEST) &&
                GameManager.game_manager.replay_car_option == GameManager.REPLAY_LEADERBOARD_SELECT)
            {
                DrawLeaderboardCheckboxes();
            }

            if (GameManager.game_manager != null && GameManager.game_manager.ViewingReplay())
            {
                // Render "Special Ghosts" panel only when ghosts are loaded
                if (activeGhosts.Count > 0)
                {
                    float specialBoxWidth = 220f;
                    float specialBoxX = (Screen.width - specialBoxWidth) / 2f;
                    GUI.Box(new Rect(specialBoxX, 70, specialBoxWidth, 110), "Special Ghosts");

                    if (GUI.Button(new Rect(specialBoxX + 10, 100, 200, 30), "Spawn Personal Best"))
                    {
                        SpawnPersonalBestGhost();
                    }

                    if (GUI.Button(new Rect(specialBoxX + 10, 140, 200, 30), "Clear All"))
                    {
                        ClearGhosts();
                    }
                }
            }
        }

        /// <summary>
        /// Renders resolution-scaled IMGUI selection panel with custom scalable checkboxes.
        /// </summary>
        private void DrawLeaderboardCheckboxes()
        {
            var steamFuncs = GameManager.game_manager.steam_functions;
            if (steamFuncs == null) return;

            IList entries = GetLeaderboardEntriesList(steamFuncs);
            if (entries == null) return;

            // Compute resolution scale factors relative to 1080p (1920x1080) baseline
            float scaleX = Screen.width / 1920f;
            float scaleY = Screen.height / 1080f;

            float panelWidth = (float)Math.Round(128f * scaleX);
            float panelX = Screen.width - (float)Math.Round(465f * scaleX) - panelWidth;
            float panelY = Screen.height - (float)Math.Round(445f * scaleY);
            float panelHeight = (float)Math.Round(439f * scaleY);

            GUI.Box(new Rect(panelX, panelY, panelWidth, panelHeight), "");

            // 1. Title Header Style ("Replay Selection")
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontSize = (int)Math.Round(16f * scaleX);
            titleStyle.normal.textColor = Color.white;

            float titleOffsetX = (float)Math.Round(12f * scaleX);
            float titleOffsetY = (float)Math.Round(8f * scaleY);
            float titleWidth = panelWidth - ((float)Math.Round(24f * scaleX));
            float titleHeight = (float)Math.Round(38f * scaleY);

            GUI.Label(new Rect(panelX + titleOffsetX, panelY + titleOffsetY, titleWidth, titleHeight), "Replay\nSelection", titleStyle);

            // 2. Action Button Style ("LOAD SELECTED")
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle.fontSize = (int)Math.Round(14f * scaleX);

            float btnOffsetY = (float)Math.Round(54f * scaleY);
            float btnHeight = (float)Math.Round(42f * scaleY);

            if (!isDownloadingBatch && GUI.Button(new Rect(panelX + titleOffsetX, panelY + btnOffsetY, titleWidth, btnHeight), "LOAD\nSELECTED", buttonStyle))
            {
                MelonCoroutines.Start(DownloadAndLaunchSelectedReplays(steamFuncs, entries));
            }

            // 3. Row Label Style ("Replay #X")
            GUIStyle replayLabelStyle = new GUIStyle(GUI.skin.label);
            replayLabelStyle.alignment = TextAnchor.MiddleLeft;
            replayLabelStyle.fontSize = (int)Math.Round(15f * scaleX);
            replayLabelStyle.normal.textColor = Color.white;

            // 4. Custom Scalable Checkmark Style
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

                // Render replay label
                GUI.Label(new Rect(panelX + titleOffsetX, currentY, labelWidth, labelHeight), $"Replay #{i + 1}", replayLabelStyle);

                // Render custom scalable checkbox element
                Rect toggleRect = new Rect(panelX + toggleOffsetX, currentY + toggleOffsetY, toggleSize, toggleSize);

                GUI.Box(toggleRect, "");

                if (selectedEntries[i])
                {
                    GUI.Label(toggleRect, "✓", checkMarkStyle);
                }

                // Invisible button layer to capture click events
                if (GUI.Button(toggleRect, "", GUIStyle.none))
                {
                    selectedEntries[i] = !selectedEntries[i];
                }
            }
        }

        // =========================================================================
        // STEAM UGC & REPLAY MANAGEMENT
        // =========================================================================

        /// <summary>
        /// Asynchronously downloads checked leaderboard entries and launches replay mode.
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
        /// Clears static Steam UGC buffer field to prepare for consecutive downloads.
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
        /// Retrieves active leaderboard entries list via reflection.
        /// </summary>
        private IList GetLeaderboardEntriesList(SteamFunctions steamFuncs)
        {
            FieldInfo field = typeof(SteamFunctions).GetField("leaderboardEntries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null ? field.GetValue(steamFuncs) as IList : null;
        }

        /// <summary>
        /// Instantiates ghost vehicle, disables unnecessary colliders for performance, and attaches GhostController.
        /// </summary>
        private void SpawnGhostFromData(ReplayData data)
        {
            if (data == null) return;

            Transform carTransform = ReplayManager.CreateReplayCar(data, false, data.car_positions[0], data.car_rotations[0]);
            if (carTransform != null)
            {
                GameObject carGo = carTransform.gameObject;

                // Disable colliders to reduce physics calculation load when handling 10+ active ghosts
                foreach (var col in carGo.GetComponentsInChildren<Collider>())
                {
                    col.enabled = false;
                }

                GhostController controller = carGo.AddComponent<GhostController>();
                controller.replayData = data;

                // Sync newly spawned ghost playback parameters with existing master ghost
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

                // Apply initial native effect suppression upon spawning
                SuppressNativeCarEffects();
                MelonLogger.Msg($"Ghost for {data.playername} spawned synchronized!");
            }
        }

        /// <summary>
        /// Spawns ghost corresponding to the local player's Personal Best track record.
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
        /// Destroys all active ghost GameObjects and clears state lists.
        /// </summary>
        private void ClearGhosts()
        {
            foreach (var ghost in activeGhosts)
            {
                if (ghost != null && ghost.gameObject != null)
                {
                    UnityEngine.Object.Destroy(ghost.gameObject);
                }
            }
            activeGhosts.Clear();
            MelonLogger.Msg("Ghosts cleared successfully.");
        }
    }
}