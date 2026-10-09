using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MultiGhostReplays
{
    /// <summary>
    /// Main entry point for the Multi-Ghost Replays mod (v1.1.1).
    /// Handles mod lifecycle, input shortcuts, ghost spawning orchestration, and reflection binding.
    /// </summary>
    public class MainMod : MelonMod
    {
        public static List<GhostController> activeGhosts = new List<GhostController>();
        public static List<ReplayData> queuedLeaderboardReplays = new List<ReplayData>();

        public static float cachedWorstReplayTime = 1000f;
        public static bool showSkidmarks = true;
        public static bool showHeadlights = true;
        public static bool showTrails = true;

        public static string statusMessage = "Ready";
        private bool showModUI = true;
        private static bool isNightTrack = false;
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

        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("Multi-Ghost Replays v1.1.1 loaded successfully!");

            Type rmType = typeof(ReplayManager);
            timeField = rmType.GetField("showing_replay_time", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            speedField = rmType.GetField("playbackSpeed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            pausedField = rmType.GetField("isPaused", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            replayField = rmType.GetField("current_showing_replay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            carToShowField = rmType.GetField("car_to_show", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            carToShowRbField = rmType.GetField("car_to_show_rb", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            Type carFxType = typeof(CarFX);
            emissionMaxField = carFxType.GetField("emission_max", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            dirtThresholdField = carFxType.GetField("dirt_particle_threshold", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        /// <summary>
        /// Evaluates total replay duration across all active ghosts and native replay instances to establish timeline clamping.
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
        /// Core loop processing UI toggles, replay exit cleanup triggers, input actions, and custom playback rate modifications.
        /// </summary>
        public override void OnUpdate()
        {
            // Toggle UI overlay visibility via F5 key
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

            // Detect transition out of replay mode to automatically purge active ghosts and scene skidmarks
            if (wasViewingReplay && !isViewing)
            {
                ClearGhosts();
                SkidmarkManager.ClearAll();
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

                // Process pending batch downloads queued from leaderboard UI
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
                SkidmarkManager.ClearAll();
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
        /// Mirrors master ghost timeline values into the native ReplayManager instance via reflection.
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
        /// Coroutine forcing an initial sync reset and skidmark clear at the end of frame following ghost spawn.
        /// </summary>
        private IEnumerator ForceInitialSyncReset()
        {
            yield return new WaitForEndOfFrame();
            SkidmarkManager.ClearAll();
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
        /// Suppresses visual renderers and effects on native car instances to prevent visual overlapping with multi-ghost cars.
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
        /// Applies global headlight toggle state to a specific target ghost controller.
        /// </summary>
        public static void ApplyHeadlightStateForGhost(GhostController ghost)
        {
            if (ghost == null || ghost.cachedLights == null) return;

            foreach (var l in ghost.cachedLights)
            {
                if (l != null) l.enabled = isNightTrack && showHeadlights;
            }
        }

        /// <summary>
        /// Updates headlight activation state across all active ghost instances.
        /// </summary>
        private void ApplyHeadlightState()
        {
            foreach (var ghost in activeGhosts)
            {
                ApplyHeadlightStateForGhost(ghost);
            }
        }

        /// <summary>
        /// Configures particle emission settings on a target ghost's CarFX component based on user preferences.
        /// </summary>
        public static void ApplyTrailStateForGhost(GhostController ghost)
        {
            if (ghost == null || ghost.carFX == null) return;

            float targetEmission = showTrails ? (22f * (float)QualitySettings.GetQualityLevel()) : 0f;
            float targetDirtThreshold = showTrails ? 0.05f : 999999f;

            if (emissionMaxField != null) emissionMaxField.SetValue(ghost.carFX, targetEmission);
            if (dirtThresholdField != null) dirtThresholdField.SetValue(ghost.carFX, targetDirtThreshold);
        }

        /// <summary>
        /// Updates particle trail emission settings across all active ghosts.
        /// </summary>
        private void ApplyTrailState()
        {
            foreach (var ghost in activeGhosts)
            {
                ApplyTrailStateForGhost(ghost);
            }
        }

        /// <summary>
        /// Renders OnGUI interface overlays for replay control and multi-ghost selection menus.
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
                LeaderboardUI.DrawLeaderboardCheckboxes();
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
                        SkidmarkManager.ClearAll();
                    }
                }
            }
        }

        /// <summary>
        /// Spawns a ghost vehicle instance from given ReplayData and attaches the GhostController component.
        /// </summary>
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
        /// Fetches local personal best track record replay data and spawns it as an active ghost vehicle.
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
        /// Destroys all active ghost GameObjects, restores native car visual FX values, and purges all scene skidmarks.
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
            SkidmarkManager.ClearAll();
            isNightTrack = false;
            cachedWorstReplayTime = 1000f;
            MelonLogger.Msg("Ghosts cleared successfully.");
        }
    }
}