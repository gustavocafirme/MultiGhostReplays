using MelonLoader;
using System;
using System.IO;
using UnityEngine;

namespace MultiGhostReplays
{
    public static class ReplayCacheManager
    {
        private static string BaseCacheFolder => Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "UserData"), "ReplayCache");
        private static string DailyCacheFolder => Path.Combine(BaseCacheFolder, "Daily");

        /// <summary>
        /// Validates if the Daily Challenge cycle has changed based on the global UTC standard (resetting at 07:00 UTC / 04:00 BRT).
        /// Clears the Daily cache folder automatically if a new cycle is detected, supporting players worldwide.
        /// </summary>
        private static void CheckAndClearDailyCacheIfNeeded()
        {
            try
            {
                string markerFile = Path.Combine(DailyCacheFolder, "session_date.txt");

                // Daily server cycle resets at 07:00 UTC (equivalent to 04:00 in Brazil GMT-3).
                // Subtracting 7 hours from current UTC aligns the logical game day cycle globally.
                DateTime utcNow = DateTime.UtcNow;
                DateTime gameServerDay = utcNow.AddHours(-7).Date;
                string expectedDayString = gameServerDay.ToString("yyyy-MM-dd");

                bool clearFolder = false;
                if (Directory.Exists(DailyCacheFolder))
                {
                    if (File.Exists(markerFile))
                    {
                        string savedDayString = File.ReadAllText(markerFile).Trim();
                        if (savedDayString != expectedDayString)
                        {
                            clearFolder = true;
                        }
                    }
                    else
                    {
                        clearFolder = true;
                    }

                    if (clearFolder)
                    {
                        foreach (string file in Directory.GetFiles(DailyCacheFolder))
                        {
                            try { File.Delete(file); } catch { }
                        }
                        MelonLogger.Msg("[Cache] Daily challenges reset (Global UTC rollover detected). Cleared Daily cache folder.");
                    }
                }

                if (!Directory.Exists(DailyCacheFolder))
                {
                    Directory.CreateDirectory(DailyCacheFolder);
                }

                File.WriteAllText(markerFile, expectedDayString);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Cache] Error handling daily cache rollover: {ex.Message}");
            }
        }

        public static string GetTrackFolderPath(int trackId)
        {
            return Path.Combine(BaseCacheFolder, $"Track_{trackId}");
        }

        public static string GetReplayFilePath(int trackId, ulong steamId, int scoreMs)
        {
            if (GameManager.inDailyQuest)
            {
                CheckAndClearDailyCacheIfNeeded();
                return Path.Combine(DailyCacheFolder, $"{steamId}_{scoreMs}.json");
            }

            return Path.Combine(GetTrackFolderPath(trackId), $"{steamId}_{scoreMs}.json");
        }

        public static bool IsReplayCached(int trackId, ulong steamId, int scoreMs)
        {
            return File.Exists(GetReplayFilePath(trackId, steamId, scoreMs));
        }

        public static void InvalidateOutdatedCache(int trackId, ulong steamId, int currentScoreMs)
        {
            string targetFolder;
            if (GameManager.inDailyQuest)
            {
                CheckAndClearDailyCacheIfNeeded();
                targetFolder = DailyCacheFolder;
            }
            else
            {
                targetFolder = GetTrackFolderPath(trackId);
            }

            if (!Directory.Exists(targetFolder)) return;

            string searchPattern = $"{steamId}_*.json";
            string[] files = Directory.GetFiles(targetFolder, searchPattern);

            foreach (string file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string[] parts = fileName.Split('_');
                if (parts.Length == 2 && int.TryParse(parts[1], out int cachedScoreMs))
                {
                    if (cachedScoreMs != currentScoreMs)
                    {
                        try
                        {
                            File.Delete(file);
                            MelonLogger.Msg($"[Cache] Outdated cache file removed: {file}");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"[Cache] Failed to delete file: {ex.Message}");
                        }
                    }
                }
            }
        }

        public static void SaveReplayToCache(int trackId, ulong steamId, int scoreMs, ReplayData replayData)
        {
            try
            {
                if (replayData == null) return;

                string targetFolder;
                if (GameManager.inDailyQuest)
                {
                    CheckAndClearDailyCacheIfNeeded();
                    targetFolder = DailyCacheFolder;
                }
                else
                {
                    targetFolder = GetTrackFolderPath(trackId);
                }

                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                InvalidateOutdatedCache(trackId, steamId, scoreMs);

                string filePath = GetReplayFilePath(trackId, steamId, scoreMs);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // Utilize native game conversion to ReplayDataBinary to guarantee CSD serialization
                ReplayDataBinary binaryData = ConvertToBinaryData(replayData);
                string json = JsonUtility.ToJson(binaryData, true);

                File.WriteAllText(filePath, json);
                string folderType = GameManager.inDailyQuest ? "Daily" : $"Track_{trackId}";
                MelonLogger.Msg($"[Cache] Saved full native JSON replay ({folderType}) ({replayData.car_positions?.Length ?? 0} frames) -> {filePath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Cache] Exception in SaveReplayToCache: {ex}");
            }
        }

        public static ReplayData LoadReplayFromCache(int trackId, ulong steamId, int scoreMs)
        {
            string filePath = GetReplayFilePath(trackId, steamId, scoreMs);
            if (!File.Exists(filePath)) return null;

            try
            {
                string json = File.ReadAllText(filePath);
                ReplayDataBinary binaryData = JsonUtility.FromJson<ReplayDataBinary>(json);

                if (binaryData != null && binaryData.car_positions != null && binaryData.car_positions.Length > 0)
                {
                    ReplayData nativeReplay = ConvertFromBinaryData(binaryData);
                    MelonLogger.Msg($"[Cache] Loaded native JSON replay for '{nativeReplay.playername}' ({nativeReplay.car_positions?.Length ?? 0} frames)");
                    return nativeReplay;
                }

                MelonLogger.Warning($"[Cache] JSON cache exists but position data was empty: {filePath}");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Cache] Exception loading JSON cache: {ex}");
                return null;
            }
        }

        private static ReplayDataBinary ConvertToBinaryData(ReplayData replayData)
        {
            if (GameManager.save_data != null)
            {
                return GameManager.save_data.ConvertReplayDataToBinaryFormat(replayData, false, false, string.Empty);
            }

            // Fallback manual conversion matching game DLL implementation
            ReplayDataBinary binary = new ReplayDataBinary
            {
                car_type = replayData.car_type,
                track_id = replayData.track_id,
                lap_time = replayData.lap_time,
                playername = replayData.playername,
                csd = replayData.csd,
                times = replayData.replay_frame_times,
                steering_angles = replayData.car_steering_angles
            };

            if (replayData.car_positions != null)
            {
                binary.car_positions = new float[replayData.car_positions.Length * 3];
                for (int i = 0; i < replayData.car_positions.Length; i++)
                {
                    binary.car_positions[i * 3] = replayData.car_positions[i].x;
                    binary.car_positions[i * 3 + 1] = replayData.car_positions[i].y;
                    binary.car_positions[i * 3 + 2] = replayData.car_positions[i].z;
                }
            }

            if (replayData.car_rotations != null)
            {
                binary.car_rotations = new float[replayData.car_rotations.Length * 4];
                for (int j = 0; j < replayData.car_rotations.Length; j++)
                {
                    binary.car_rotations[j * 4] = replayData.car_rotations[j].x;
                    binary.car_rotations[j * 4 + 1] = replayData.car_rotations[j].y;
                    binary.car_rotations[j * 4 + 2] = replayData.car_rotations[j].z;
                    binary.car_rotations[j * 4 + 3] = replayData.car_rotations[j].w;
                }
            }

            return binary;
        }

        private static ReplayData ConvertFromBinaryData(ReplayDataBinary binaryData)
        {
            if (GameManager.save_data != null)
            {
                return GameManager.save_data.ConvertReplayDataToJSONFormat(binaryData);
            }

            // Fallback manual conversion matching game DLL implementation
            ReplayData replayData = ScriptableObject.CreateInstance<ReplayData>();
            replayData.car_type = binaryData.car_type;
            replayData.track_id = binaryData.track_id;
            replayData.lap_time = binaryData.lap_time;
            replayData.playername = binaryData.playername;
            replayData.csd = binaryData.csd;

            if (binaryData.car_positions != null)
            {
                replayData.car_positions = new Vector3[binaryData.car_positions.Length / 3];
                for (int i = 0; i < replayData.car_positions.Length; i++)
                {
                    replayData.car_positions[i] = new Vector3(
                        binaryData.car_positions[i * 3],
                        binaryData.car_positions[i * 3 + 1],
                        binaryData.car_positions[i * 3 + 2]
                    );
                }
            }

            if (binaryData.car_rotations != null)
            {
                replayData.car_rotations = new Quaternion[binaryData.car_rotations.Length / 4];
                for (int j = 0; j < replayData.car_rotations.Length; j++)
                {
                    replayData.car_rotations[j] = new Quaternion(
                        binaryData.car_rotations[j * 4],
                        binaryData.car_rotations[j * 4 + 1],
                        binaryData.car_rotations[j * 4 + 2],
                        binaryData.car_rotations[j * 4 + 3]
                    );
                }
            }

            replayData.replay_frame_times = binaryData.times;
            replayData.car_steering_angles = binaryData.steering_angles;

            return replayData;
        }
    }
}